using System.Collections.ObjectModel;
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
        UploadCommand = new RelayCommand(async _ => await UploadAsync(), _ => SelectedWorld != null && !IsBusy);
        DownloadCommand = new RelayCommand(async _ => await DownloadAsync(), _ => SelectedWorld != null && !IsBusy);
        LogoutCommand = new RelayCommand(_ => Logout());
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsBusy);
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
        set
        {
            Set(ref _isBusy, value);
            OnPropertyChanged(nameof(IsNotBusy));
            CommandManager.InvalidateRequerySuggested();
        }
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
                AuthService.SaveLastWorld(value?.Id ?? 0);
                _ = LoadWorldMetaAsync();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private SaveMetadata? _cloudMeta;
    public SaveMetadata? CloudMeta { get => _cloudMeta; set => Set(ref _cloudMeta, value); }

    private string _localSaveInfo = "Не знайдено";
    public string LocalSaveInfo { get => _localSaveInfo; set => Set(ref _localSaveInfo, value); }

    private string _syncStatus = "";
    public string SyncStatus { get => _syncStatus; set => Set(ref _syncStatus, value); }

    public ObservableCollection<World> Worlds { get; } = [];

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
                StorageInfo = $"{me.StorageUsed / 1024 / 1024} MB / {me.StorageLimit / 1024 / 1024} MB";
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

    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    private async Task LoadWorldMetaAsync()
    {
        if (SelectedWorld == null) return;
        CloudMeta = await _api.GetSaveMetadataAsync(SelectedWorld.Id);
        UpdateLocalSaveInfo();
        UpdateSyncStatus();
    }

    private void UpdateLocalSaveInfo()
    {
        var path = GetLocalSavePath();
        if (path == null) { LocalSaveInfo = "Папку сейвів не знайдено"; return; }

        var files = Directory.GetFiles(path, "*.sav")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        if (files == null) { LocalSaveInfo = "Сейвів немає"; return; }
        LocalSaveInfo = $"{files.Name} ({files.Length / 1024} KB) · {files.LastWriteTime:dd.MM HH:mm}";
    }

    private void UpdateSyncStatus()
    {
        if (CloudMeta == null) { SyncStatus = "Хмара порожня"; return; }
        SyncStatus = $"Хмара: {CloudMeta.Filename} · {CloudMeta.UploadedAt?[..16]}";
    }

    private async Task UploadAsync()
    {
        var path = GetLocalSavePath();
        if (path == null) { StatusText = "Папку сейвів не знайдено"; return; }

        var latest = Directory.GetFiles(path, "*.sav")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        if (latest == null) { StatusText = "Немає локального сейву"; return; }

        IsBusy = true;
        ProgressVisible = true;
        Progress = 0;
        StatusText = "Завантаження на сервер...";
        try
        {
            var ok = await _api.UploadSaveAsync(SelectedWorld!.Id, latest.FullName,
                new Progress<double>(p => { Progress = p * 100; }));
            StatusText = ok ? "Завантажено!" : "Помилка завантаження";
            if (ok) await LoadWorldMetaAsync();
        }
        finally { IsBusy = false; ProgressVisible = false; }
    }

    private async Task DownloadAsync()
    {
        var path = GetLocalSavePath();
        if (path == null) { StatusText = "Папку сейвів не знайдено"; return; }

        IsBusy = true;
        ProgressVisible = true;
        Progress = 0;
        StatusText = "Завантаження з сервера...";
        try
        {
            var result = await _api.DownloadSaveAsync(SelectedWorld!.Id, path,
                new Progress<double>(p => { Progress = p * 100; }));
            StatusText = result != null ? "Скачано!" : "Помилка скачування";
            UpdateLocalSaveInfo();
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
        var path = GetLocalSavePath();
        if (path != null && Directory.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", path);
    }

    private static string? GetLocalSavePath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(local, "FactoryGame", "Saved", "SaveGames", "common");
        if (Directory.Exists(path)) return path;

        // fallback — старий шлях
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        path = Path.Combine(appdata, "..", "Local", "FactoryGame", "Saved", "SaveGames");
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
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
