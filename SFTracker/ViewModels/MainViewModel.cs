using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SFTracker.Models;
using SFTracker.Services;

namespace SFTracker.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly ApiService _api;

    public MainViewModel(ApiService api)
    {
        _api = api;
        SyncCommand   = new RelayCommand(async _ => await SyncAsync(),     _ => SelectedWorld != null && !IsBusy);
        UploadCommand = new RelayCommand(async _ => await UploadAsync(),   _ => SelectedWorld != null && !IsBusy);
        DownloadCommand = new RelayCommand(async _ => await DownloadAsync(), _ => SelectedWorld != null && !IsBusy);
        LogoutCommand = new RelayCommand(_ => Logout());
        RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
        OpenSaveFolderCommand = new RelayCommand(_ => OpenSaveFolder());
    }

    private string _username = "";
    public string Username { get => _username; set => Set(ref _username, value); }

    private bool _isPremium;
    public bool IsPremium { get => _isPremium; set => Set(ref _isPremium, value); }

    private string _storageInfo = "";
    public string StorageInfo { get => _storageInfo; set => Set(ref _storageInfo, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { Set(ref _isBusy, value); OnPropertyChanged(nameof(IsNotBusy)); CommandManager.InvalidateRequerySuggested(); }
    }
    public bool IsNotBusy => !_isBusy;

    private string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private bool _progressVisible;
    public bool ProgressVisible { get => _progressVisible; set => Set(ref _progressVisible, value); }

    private World? _selectedWorld;
    public World? SelectedWorld
    {
        get => _selectedWorld;
        set
        {
            if (Set(ref _selectedWorld, value))
            {
                if (value != null) AuthService.SaveLastWorld(value.Id);
                _ = LoadWorldMetaAsync();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private SaveMetadata? _cloudMeta;
    public SaveMetadata? CloudMeta { get => _cloudMeta; set { Set(ref _cloudMeta, value); UpdateSyncDirection(); } }

    private string _localSaveInfo = "Не знайдено";
    public string LocalSaveInfo { get => _localSaveInfo; set => Set(ref _localSaveInfo, value); }

    private string _cloudSaveInfo = "Порожньо";
    public string CloudSaveInfo { get => _cloudSaveInfo; set => Set(ref _cloudSaveInfo, value); }

    // "↑ Upload", "↓ Download", "✓ Sync"
    private string _syncDirection = "⇅ Sync";
    public string SyncDirection { get => _syncDirection; set => Set(ref _syncDirection, value); }

    public ObservableCollection<World> Worlds { get; } = [];

    public ICommand SyncCommand { get; }
    public ICommand UploadCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenSaveFolderCommand { get; }

    public event Action? LoggedOut;

    public async Task LoadAsync()
    {
        IsBusy = true;
        StatusText = "Завантаження...";
        try
        {
            var me = await _api.GetMeAsync();
            if (me != null)
            {
                Username = me.Username;
                IsPremium = me.IsPremium;
                var usedMb = me.StorageUsed / 1024 / 1024;
                var limitMb = me.StorageLimit / 1024 / 1024;
                StorageInfo = $"{usedMb} / {limitMb} MB";
            }

            var worlds = await _api.GetWorldsAsync();
            Worlds.Clear();
            foreach (var w in worlds) Worlds.Add(w);

            var lastId = AuthService.LoadLastWorld();
            SelectedWorld = Worlds.FirstOrDefault(w => w.Id == lastId) ?? Worlds.FirstOrDefault();
            StatusText = "";
        }
        finally { IsBusy = false; }
    }

    private async Task LoadWorldMetaAsync()
    {
        if (SelectedWorld == null) return;
        CloudMeta = await _api.GetSaveMetadataAsync(SelectedWorld.Id);
        RefreshLocalInfo();
        UpdateCloudInfo();
    }

    private void RefreshLocalInfo()
    {
        var f = FindBestLocalSave(SelectedWorld?.Name);
        if (f == null) { LocalSaveInfo = "Сейвів немає"; return; }
        var tag = f.Name.Contains("autosave", StringComparison.OrdinalIgnoreCase) ? " [auto]" : "";
        LocalSaveInfo = $"{f.Name}{tag}\n{f.Length / 1024} KB · {f.LastWriteTime:dd.MM HH:mm}";
    }

    private void UpdateCloudInfo()
    {
        if (CloudMeta == null) { CloudSaveInfo = "Порожньо"; return; }
        var date = CloudMeta.UploadedAt?.Length >= 16 ? CloudMeta.UploadedAt[..16].Replace("T", " ") : CloudMeta.UploadedAt;
        CloudSaveInfo = $"{CloudMeta.Filename}\n{CloudMeta.Size / 1024} KB · {date}";
    }

    private void UpdateSyncDirection()
    {
        var local = FindBestLocalSave(SelectedWorld?.Name);
        if (local == null || CloudMeta == null) { SyncDirection = "⇅ Sync"; return; }

        if (!DateTime.TryParse(CloudMeta.UploadedAt, out var cloudDate))
        { SyncDirection = "⇅ Sync"; return; }

        SyncDirection = local.LastWriteTime > cloudDate ? "↑ Sync" : "↓ Sync";
    }

    // Smart sync: новіший перемагає
    private async Task SyncAsync()
    {
        var local = FindBestLocalSave(SelectedWorld?.Name);

        if (local == null && CloudMeta == null)
        { StatusText = "Немає ні локального, ні хмарного сейву"; return; }

        if (local == null) { await DownloadAsync(); return; }
        if (CloudMeta == null) { await UploadAsync(); return; }

        if (!DateTime.TryParse(CloudMeta.UploadedAt, out var cloudDate))
        { await UploadAsync(); return; }

        if (local.LastWriteTime > cloudDate)
            await UploadAsync();
        else if (cloudDate > local.LastWriteTime)
            await DownloadAsync();
        else
            StatusText = "Вже синхронізовано ✓";
    }

    private async Task UploadAsync()
    {
        var f = FindBestLocalSave(SelectedWorld?.Name);
        if (f == null) { StatusText = "Локальний сейв не знайдено"; return; }

        IsBusy = true; ProgressVisible = true; Progress = 0;
        StatusText = $"Upload: {f.Name}...";
        try
        {
            var ok = await _api.UploadSaveAsync(SelectedWorld!.Id, f.FullName,
                new Progress<double>(p => Progress = p * 100));
            StatusText = ok ? "Завантажено на сервер ✓" : "Помилка upload";
            if (ok) await LoadWorldMetaAsync();
        }
        finally { IsBusy = false; ProgressVisible = false; }
    }

    private async Task DownloadAsync()
    {
        var dir = FindSavesDirectory();
        if (dir == null) { StatusText = "Папку сейвів не знайдено"; return; }

        IsBusy = true; ProgressVisible = true; Progress = 0;
        StatusText = "Download з сервера...";
        try
        {
            var path = await _api.DownloadSaveAsync(SelectedWorld!.Id, dir,
                new Progress<double>(p => Progress = p * 100));
            StatusText = path != null ? "Скачано ✓" : "Помилка download";
            RefreshLocalInfo();
            UpdateSyncDirection();
        }
        finally { IsBusy = false; ProgressVisible = false; }
    }

    private void Logout()
    {
        AuthService.ClearToken();
        _api.ClearToken();
        LoggedOut?.Invoke();
    }

    private void OpenSaveFolder()
    {
        var dir = FindSavesDirectory();
        if (dir != null && Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
    }

    // Шукає найкращий сейв для цього світу:
    // 1. Звичайний .sav з назвою світу
    // 2. Autosave з назвою світу
    // 3. Будь-який звичайний .sav
    // 4. Будь-який autosave
    private static FileInfo? FindBestLocalSave(string? worldName)
    {
        var all = GetAllSaveFiles();
        if (all.Count == 0) return null;

        bool IsAuto(FileInfo f) => f.Name.Contains("autosave", StringComparison.OrdinalIgnoreCase);
        bool MatchesWorld(FileInfo f) => !string.IsNullOrEmpty(worldName) &&
            f.Name.Contains(worldName, StringComparison.OrdinalIgnoreCase);

        return all.Where(f => !IsAuto(f) && MatchesWorld(f)).MaxBy(f => f.LastWriteTime)
            ?? all.Where(f =>  IsAuto(f) && MatchesWorld(f)).MaxBy(f => f.LastWriteTime)
            ?? all.Where(f => !IsAuto(f)).MaxBy(f => f.LastWriteTime)
            ?? all.MaxBy(f => f.LastWriteTime);
    }

    // Папка для download — та де вже є сейви, або перша підпапка SaveGames
    private static string? FindSavesDirectory()
    {
        var root = GetSaveGamesRoot();
        if (root == null) return null;

        // Тільки підпапки — ніколи не сам SaveGames root
        var subdirs = Directory.EnumerateDirectories(root).ToList();

        // Підпапка де є будь-який .sav (включно з autosave) — найновіший
        var best = subdirs
            .Where(d => Directory.EnumerateFiles(d, "*.sav").Any())
            .OrderByDescending(d => Directory.EnumerateFiles(d, "*.sav")
                .Select(f => new FileInfo(f).LastWriteTime).DefaultIfEmpty().Max())
            .FirstOrDefault();

        return best ?? subdirs.FirstOrDefault();
    }

    private static List<FileInfo> GetAllSaveFiles()
    {
        var root = GetSaveGamesRoot();
        if (root == null) return [];

        return Directory.EnumerateDirectories(root)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.sav"))
            .Select(f => new FileInfo(f))
            .ToList();
    }

    private static string? GetSaveGamesRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(local, "FactoryGame", "Saved", "SaveGames");
        return Directory.Exists(path) ? path : null;
    }
}

public class RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null) : ICommand
{
    private readonly Func<object?, Task> _executeAsync = executeAsync;
    private readonly Predicate<object?>? _canExecute = canExecute;
    private bool _isRunning;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        : this(p => { execute(p); return Task.CompletedTask; }, canExecute) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (_isRunning) return;
        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try { await _executeAsync(parameter); }
        finally { _isRunning = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
