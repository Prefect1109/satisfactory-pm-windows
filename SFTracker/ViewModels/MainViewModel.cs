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
        _skipConfirm = AuthService.LoadSkipConfirm();
        _autoSync = AuthService.LoadAutoSync();
        SyncCommand     = new RelayCommand(async _ => await SyncAsync(),     _ => SelectedWorld != null && !IsBusy);
        UploadCommand   = new RelayCommand(async _ => await ConfirmAndUploadAsync(), _ => SelectedWorld != null && !IsBusy);
        DownloadCommand = new RelayCommand(async _ => await ConfirmAndDownloadAsync(), _ => SelectedWorld != null && !IsBusy);
        LogoutCommand   = new RelayCommand(_ => Logout());
        RefreshCommand  = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
        OpenSaveFolderCommand = new RelayCommand(_ => OpenSaveFolder());
    }

    private bool _isPremium;
    public bool IsPremium { get => _isPremium; set => Set(ref _isPremium, value); }

    private string _username = "";
    public string Username { get => _username; set => Set(ref _username, value); }

    private bool _skipConfirm;
    public bool SkipConfirm
    {
        get => _skipConfirm;
        set { Set(ref _skipConfirm, value); AuthService.SaveSkipConfirm(value); }
    }

    private bool _autoSync;
    public bool AutoSync
    {
        get => _autoSync;
        set { Set(ref _autoSync, value); AuthService.SaveAutoSync(value); }
    }

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
                if (value != null) AuthService.SaveLastWorld(value.InviteCode);
                _ = LoadWorldMetaAsync();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private SaveMetadata? _cloudMeta;
    public SaveMetadata? CloudMeta { get => _cloudMeta; set { Set(ref _cloudMeta, value); UpdateSyncDirection(); } }

    // Local save fields
    private string _localPlayTime = "—";
    public string LocalPlayTime { get => _localPlayTime; set => Set(ref _localPlayTime, value); }
    private string _localSaveDate = "";
    public string LocalSaveDate { get => _localSaveDate; set => Set(ref _localSaveDate, value); }
    private string _localFileName = "";
    public string LocalFileName { get => _localFileName; set => Set(ref _localFileName, value); }
    private bool _localExists;
    public bool LocalExists { get => _localExists; set => Set(ref _localExists, value); }

    // Cloud save fields
    private string _cloudPlayTime = "—";
    public string CloudPlayTime { get => _cloudPlayTime; set => Set(ref _cloudPlayTime, value); }
    private string _cloudSaveDate = "";
    public string CloudSaveDate { get => _cloudSaveDate; set => Set(ref _cloudSaveDate, value); }
    private string _cloudFileName = "";
    public string CloudFileName { get => _cloudFileName; set => Set(ref _cloudFileName, value); }
    private bool _cloudExists;
    public bool CloudExists { get => _cloudExists; set => Set(ref _cloudExists, value); }

    // Sync reason hint (shown between sync button and manual buttons)
    private string _syncReason = "";
    public string SyncReason { get => _syncReason; set => Set(ref _syncReason, value); }

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
                IsPremium = me.IsPremium;
                Username = me.Username ?? "";
                var usedMb = me.StorageUsed / (1024.0 * 1024.0);
                var limitMb = me.StorageLimit / (1024.0 * 1024.0);
                StorageInfo = $"{usedMb:F0} MB / {limitMb:F0} MB";
            }

            var worlds = await _api.GetWorldsAsync();
            Worlds.Clear();
            foreach (var w in worlds) Worlds.Add(w);

            var lastCode = AuthService.LoadLastWorld();
            SelectedWorld = Worlds.FirstOrDefault(w => w.InviteCode == lastCode) ?? Worlds.FirstOrDefault();
            StatusText = "";

            if (AutoSync && SelectedWorld != null)
                await SyncAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task LoadWorldMetaAsync()
    {
        if (SelectedWorld == null) return;
        CloudMeta = await _api.GetSaveMetadataAsync(SelectedWorld.InviteCode);
        RefreshLocalInfo();
        UpdateCloudInfo();
    }

    private void RefreshLocalInfo()
    {
        var f = FindBestLocalSave(SelectedWorld?.Name, CloudMeta?.SessionName);
        if (f == null)
        {
            LocalExists = false; LocalPlayTime = "—"; LocalSaveDate = ""; LocalFileName = "";
            return;
        }
        var isAuto = f.Name.Contains("autosave", StringComparison.OrdinalIgnoreCase);
        LocalExists   = true;
        LocalPlayTime = SaveParser.FormatPlayTime(SaveParser.ReadPlayTimeSec(f.FullName));
        LocalSaveDate = f.LastWriteTime.ToString("dd.MM HH:mm");
        LocalFileName = Path.GetFileNameWithoutExtension(f.Name) + (isAuto ? " [auto]" : "");
    }

    private void UpdateCloudInfo()
    {
        if (CloudMeta == null || !CloudMeta.Exists)
        {
            CloudExists = false; CloudPlayTime = "—"; CloudSaveDate = ""; CloudFileName = "";
            return;
        }
        CloudExists   = true;
        CloudPlayTime = SaveParser.FormatPlayTime(CloudMeta.PlayTimeSec);
        CloudSaveDate = CloudMeta.UpdatedAt?.Replace("T", " ") ?? "";
        CloudFileName = CloudMeta.SessionName ?? CloudMeta.Filename ?? "";
    }

    private void UpdateSyncDirection()
    {
        var local = FindBestLocalSave(SelectedWorld?.Name, CloudMeta?.SessionName);
        if (local == null) { SyncDirection = "↓ Sync"; SyncReason = "Локального сейву немає"; return; }
        if (CloudMeta == null || !CloudMeta.Exists) { SyncDirection = "↑ Sync"; SyncReason = "Хмара порожня"; return; }

        var (cmp, reason) = ComparePlayTime(local, CloudMeta);
        SyncDirection = cmp > 0 ? "↑ Sync" : cmp < 0 ? "↓ Sync" : "✓ Sync";
        SyncReason = reason;
    }

    private async Task SyncAsync()
    {
        var local = FindBestLocalSave(SelectedWorld?.Name, CloudMeta?.SessionName);

        if (local == null && (CloudMeta == null || !CloudMeta.Exists))
        { StatusText = "Немає сейвів ні локально, ні в хмарі"; return; }

        if (local == null) { await ConfirmAndDownloadAsync(); return; }
        if (CloudMeta == null || !CloudMeta.Exists) { await ConfirmAndUploadAsync(); return; }

        var (cmp, reason) = ComparePlayTime(local, CloudMeta);

        if (cmp == 0) { StatusText = "Вже синхронізовано ✓"; return; }

        bool isUpload = cmp > 0;
        if (!Confirm(isUpload, local, reason)) return;

        if (isUpload) await UploadAsync();
        else await DownloadAsync();
    }

    private async Task ConfirmAndUploadAsync()
    {
        var local = FindBestLocalSave(SelectedWorld?.Name, CloudMeta?.SessionName);
        if (local == null) { StatusText = "Локальний сейв не знайдено"; return; }
        var (_, reason) = CloudMeta?.Exists == true
            ? ComparePlayTime(local, CloudMeta!)
            : (1, "Хмара порожня");
        if (!Confirm(true, local, reason)) return;
        await UploadAsync();
    }

    private async Task ConfirmAndDownloadAsync()
    {
        var local = FindBestLocalSave(SelectedWorld?.Name, CloudMeta?.SessionName);
        var (_, reason) = (local != null && CloudMeta?.Exists == true)
            ? ComparePlayTime(local, CloudMeta!)
            : (-1, "Локального сейву немає");
        if (!Confirm(false, local, reason)) return;
        await DownloadAsync();
    }

    private bool Confirm(bool isUpload, FileInfo? local, string reason)
    {
        if (SkipConfirm) return true;

        var localPt  = local != null ? SaveParser.FormatPlayTime(SaveParser.ReadPlayTimeSec(local.FullName)) : "—";
        var cloudPt  = CloudMeta?.Exists == true ? SaveParser.FormatPlayTime(CloudMeta.PlayTimeSec) : "—";
        var worldName = SelectedWorld?.Name ?? "";

        string action, what, overwriting;
        if (isUpload)
        {
            action     = "⬆ UPLOAD";
            what       = $"Локальний сейв ({localPt} награно)\n→ перезапише хмарний ({cloudPt})";
            overwriting = "Хмарний сейв буде перезаписано.";
        }
        else
        {
            action     = "⬇ DOWNLOAD";
            what       = $"Хмарний сейв ({cloudPt} награно)\n→ збережеться поруч з локальним";
            overwriting = "Локальний файл НЕ видаляється — новий ляже поруч.";
        }

        var msg = $"[{worldName}] {action}\n\n{what}\n\n{reason}\n\n{overwriting}";
        var result = MessageBox.Show(msg, "Підтвердження", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.OK;
    }

    // Повертає (1 = local новіший, -1 = cloud новіший, 0 = однаково) + опис причини
    internal static (int, string) ComparePlayTime(FileInfo local, SaveMetadata cloud)
    {
        var localPt = SaveParser.ReadPlayTimeSec(local.FullName);
        var cloudPt = cloud.PlayTimeSec;

        // Обидва мають play time — порівнюємо
        if (localPt > 0 && cloudPt > 0)
        {
            if (localPt > cloudPt)
                return (1, $"Локальний +{SaveParser.FormatPlayTime(localPt - cloudPt)} більше награного часу");
            if (cloudPt > localPt)
                return (-1, $"Хмарний +{SaveParser.FormatPlayTime(cloudPt - localPt)} більше награного часу");
            return (0, $"Однаковий play time: {SaveParser.FormatPlayTime(localPt)}");
        }

        // Fallback на дату якщо play time недоступний (dedicated server header = 0)
        if (!DateTime.TryParse(cloud.UpdatedAt, out var cloudDate))
            return (1, "Fallback: хмара без дати → upload");

        if (local.LastWriteTime > cloudDate)
            return (1, "Fallback: локальний новіший за датою");
        if (cloudDate > local.LastWriteTime)
            return (-1, "Fallback: хмарний новіший за датою");
        return (0, "Однаково");
    }

    private async Task UploadAsync()
    {
        var f = FindBestLocalSave(SelectedWorld?.Name, CloudMeta?.SessionName);
        if (f == null) { StatusText = "Локальний сейв не знайдено"; return; }

        IsBusy = true; ProgressVisible = true; Progress = 0;
        StatusText = $"Upload: {f.Name}...";
        try
        {
            var ok = await _api.UploadSaveAsync(SelectedWorld!.InviteCode, f.FullName,
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
            // uniqueName: false — перезаписуємо: ім'я = session_name.sav (canonical)
            var path = await _api.DownloadSaveAsync(SelectedWorld!.InviteCode, dir,
                new Progress<double>(p => Progress = p * 100),
                uniqueName: false);
            if (path != null)
            {
                StatusText = $"Скачано ✓ → {System.IO.Path.GetFileName(path)}";
                _lastDownloadedPath = path;
            }
            else
            {
                StatusText = "Помилка download";
            }
            // Оновлюємо CloudMeta і local одночасно щоб порівняння було актуальним
            await LoadWorldMetaAsync();
        }
        finally { IsBusy = false; ProgressVisible = false; }
    }

    private string? _lastDownloadedPath;

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

    // Шукає найкращий сейв для цього світу.
    // Пріоритет: точний match по sessionName (canonical) → match по worldName → будь-який звичайний
    private static FileInfo? FindBestLocalSave(string? worldName, string? sessionName = null)
    {
        var all = GetAllSaveFiles();
        if (all.Count == 0) return null;

        bool IsAuto(FileInfo f) => f.Name.Contains("autosave", StringComparison.OrdinalIgnoreCase);

        // 1. Точний match по canonical session name (session_name.sav)
        if (!string.IsNullOrEmpty(sessionName))
        {
            var exact = all.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f.Name), sessionName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Partial match по sessionName (якщо autosave або з суфіксом)
            var partial = all.Where(f => !IsAuto(f) &&
                f.Name.Contains(sessionName, StringComparison.OrdinalIgnoreCase)).MaxBy(f => f.LastWriteTime);
            if (partial != null) return partial;
        }

        // 2. Match по worldName
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
        // Custom folder: scan directly (flat), no subdirectory requirement
        var custom = AuthService.LoadCustomSaveFolder();
        if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom))
        {
            return Directory.EnumerateFiles(custom, "*.sav", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToList();
        }

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
