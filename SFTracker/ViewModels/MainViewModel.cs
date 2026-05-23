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

    private GameWatcher? _gameWatcher;

    public MainViewModel(ApiService api)
    {
        _api = api;
        _skipConfirm = AuthService.LoadSkipConfirm();
        _autoSync    = AuthService.LoadAutoSync();
        _autoStart   = StartupService.IsEnabled();
        SyncCommand     = new RelayCommand(async _ => await SyncAsync(),     _ => SelectedWorld != null && !IsBusy);
        UploadCommand   = new RelayCommand(async _ => await ConfirmAndUploadAsync(), _ => SelectedWorld != null && !IsBusy);
        DownloadCommand = new RelayCommand(async _ => await ConfirmAndDownloadAsync(), _ => SelectedWorld != null && !IsBusy);
        LogoutCommand   = new RelayCommand(_ => Logout());
        RefreshCommand  = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
        OpenSaveFolderCommand = new RelayCommand(_ => OpenSaveFolder());
    }

    public void AttachGameWatcher(GameWatcher watcher)
    {
        _gameWatcher = watcher;
        _gameWatcher.GameClosed += OnGameClosed;
    }

    public void DetachGameWatcher()
    {
        if (_gameWatcher != null)
            _gameWatcher.GameClosed -= OnGameClosed;
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
        set
        {
            Set(ref _autoSync, value);
            AuthService.SaveAutoSync(value);
            AutoSyncChanged?.Invoke(value);
            if (value) MaybeAskAutoStart();
        }
    }

    public event Action<bool>? AutoSyncChanged;

    private bool _autoStart;
    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            Set(ref _autoStart, value);
            if (value) StartupService.Enable();
            else StartupService.Disable();
        }
    }

    private void MaybeAskAutoStart()
    {
        if (AuthService.WasAutoStartAsked()) return;
        AuthService.MarkAutoStartAsked();

        var result = MessageBox.Show(
            "Додати SFTracker до автозапуску Windows?\n\nЦе дозволить авто-синку працювати без ручного запуску програми.",
            "Автозапуск",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        AutoStart = result == MessageBoxResult.Yes;
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
                IsPremium = me.IsPremium;
                Username = me.Username ?? "";
                var usedMb = me.StorageUsed / (1024.0 * 1024.0);
                var limitMb = me.StorageLimit / (1024.0 * 1024.0);
                StorageInfo = $"{usedMb:F0} MB / {limitMb:F0} MB";
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
        var f = FindBestLocalSave(CloudMeta?.SessionName, SelectedWorld?.Name);
        if (f == null) { LocalSaveInfo = "Сейвів немає"; return; }
        var tag = f.Name.Contains("autosave", StringComparison.OrdinalIgnoreCase) ? " [auto]" : "";
        var pt = FormatPlayTime(SaveParser.ReadPlayTimeSec(f.FullName));
        LocalSaveInfo = $"{f.Name}{tag}\n{f.Length / 1024} KB · {pt}\n{f.LastWriteTime:dd.MM HH:mm}";
    }

    private void UpdateCloudInfo()
    {
        if (CloudMeta == null || !CloudMeta.Exists) { CloudSaveInfo = "Порожньо"; return; }
        var date = CloudMeta.UpdatedAt != null && DateTime.TryParse(CloudMeta.UpdatedAt,
            null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(dt, "FLE Standard Time").ToString("dd.MM HH:mm")
            : "";
        var pt = FormatPlayTime(CloudMeta.PlayTimeSec);
        CloudSaveInfo = $"{CloudMeta.SessionName ?? CloudMeta.Filename}\n{CloudMeta.Size / 1024} KB · {pt}\n{date}";
    }

    private void UpdateSyncDirection()
    {
        var local = FindBestLocalSave(CloudMeta?.SessionName, SelectedWorld?.Name);
        if (local == null) { SyncDirection = "↓ Sync"; return; }
        if (CloudMeta == null || !CloudMeta.Exists) { SyncDirection = "↑ Sync"; return; }

        var (cmp, _) = ComparePlayTime(local, CloudMeta);
        SyncDirection = cmp > 0 ? "↑ Sync" : cmp < 0 ? "↓ Sync" : "✓ Sync";
    }

    // Smart sync по play time
    private async Task SyncAsync()
    {
        var local = FindBestLocalSave(CloudMeta?.SessionName, SelectedWorld?.Name);

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
        var local = FindBestLocalSave(CloudMeta?.SessionName, SelectedWorld?.Name);
        if (local == null) { StatusText = "Локальний сейв не знайдено"; return; }
        var (_, reason) = CloudMeta?.Exists == true
            ? ComparePlayTime(local, CloudMeta!)
            : (1, "Хмара порожня");
        if (!Confirm(true, local, reason)) return;
        await UploadAsync();
    }

    private async Task ConfirmAndDownloadAsync()
    {
        var local = FindBestLocalSave(CloudMeta?.SessionName, SelectedWorld?.Name);
        var (_, reason) = (local != null && CloudMeta?.Exists == true)
            ? ComparePlayTime(local, CloudMeta!)
            : (-1, "Локального сейву немає");
        if (!Confirm(false, local, reason)) return;
        await DownloadAsync();
    }

    private bool Confirm(bool isUpload, FileInfo? local, string reason)
    {
        if (SkipConfirm) return true;

        var localPt  = local != null ? FormatPlayTime(SaveParser.ReadPlayTimeSec(local.FullName)) : "—";
        var cloudPt  = CloudMeta?.Exists == true ? FormatPlayTime(CloudMeta.PlayTimeSec) : "—";
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
            what       = $"Хмарний сейв ({cloudPt} награно)\n→ перезапише локальний ({localPt})";
            overwriting = $"Буде записано як {SelectedWorld?.Name}.sav";
        }

        var msg = $"[{worldName}] {action}\n\n{what}\n\n{reason}\n\n{overwriting}";
        var result = MessageBox.Show(msg, "Підтвердження", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.OK;
    }

    // Повертає (1 = local новіший, -1 = cloud новіший, 0 = однаково) + опис причини
    private static (int, string) ComparePlayTime(FileInfo local, SaveMetadata cloud)
    {
        var localPt = SaveParser.ReadPlayTimeSec(local.FullName);
        var cloudPt = cloud.PlayTimeSec;

        // Обидва мають play time — порівнюємо
        if (localPt > 0 && cloudPt > 0)
        {
            if (localPt > cloudPt)
                return (1, $"Локальний +{FormatPlayTime(localPt - cloudPt)} більше награного часу");
            if (cloudPt > localPt)
                return (-1, $"Хмарний +{FormatPlayTime(cloudPt - localPt)} більше награного часу");
            return (0, $"Однаковий play time: {FormatPlayTime(localPt)}");
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

    private static string FormatPlayTime(int sec)
    {
        if (sec <= 0) return "—";
        var h = sec / 3600;
        var m = (sec % 3600) / 60;
        return h > 0 ? $"{h}г {m}хв" : $"{m}хв";
    }

    private async Task UploadAsync()
    {
        var f = FindBestLocalSave(CloudMeta?.SessionName, SelectedWorld?.Name);
        if (f == null) { StatusText = "Локальний сейв не знайдено"; return; }

        var standardName = $"{SelectedWorld!.Name}.sav";
        IsBusy = true; ProgressVisible = true; Progress = 0;
        StatusText = $"Upload: {standardName}...";
        try
        {
            var ok = await _api.UploadSaveAsync(SelectedWorld.Id, f.FullName, standardName,
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

        // Завжди кладемо як WorldName.sav — перезаписуємо (качаємо тільки коли хмара новіша)
        var targetPath = Path.Combine(dir, $"{SelectedWorld!.Name}.sav");
        IsBusy = true; ProgressVisible = true; Progress = 0;
        StatusText = "Download з сервера...";
        try
        {
            var path = await _api.DownloadSaveAsync(SelectedWorld.Id, targetPath,
                new Progress<double>(p => Progress = p * 100));
            StatusText = path != null ? $"Скачано ✓ → {Path.GetFileName(path)}" : "Помилка download";
            RefreshLocalInfo();
            UpdateSyncDirection();
        }
        finally { IsBusy = false; ProgressVisible = false; }
    }

    private async void OnGameClosed()
    {
        if (!AutoSync) return;
        if (SelectedWorld == null || CloudMeta == null || !CloudMeta.Exists) return;

        // Чекаємо поки гра допише сейв на диск
        await Task.Delay(3000);

        // Беремо найновіший .sav файл
        var allSaves = GetAllSaveFiles();
        var recentSave = allSaves.MaxBy(f => f.LastWriteTime);
        if (recentSave == null) return;

        // Читаємо session name з хедера і перевіряємо що це наш світ
        var sessionName = SaveParser.ReadSessionName(recentSave.FullName);
        if (!string.Equals(sessionName, CloudMeta.SessionName, StringComparison.OrdinalIgnoreCase)) return;

        // Порівнюємо play time — синхронізуємо тільки якщо є щось нове
        var localPt = SaveParser.ReadPlayTimeSec(recentSave.FullName);
        if (localPt <= CloudMeta.PlayTimeSec) return;

        // Диспатч на UI thread
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (IsBusy) return;
            IsBusy = true; ProgressVisible = true; Progress = 0;
            StatusText = "Автосинк: завантаження...";
            try
            {
                var ok = await _api.UploadSaveAsync(SelectedWorld.Id, recentSave.FullName,
                    $"{SelectedWorld.Name}.sav",
                    new Progress<double>(p => Progress = p * 100));
                StatusText = ok ? $"Автосинк ✓ ({FormatPlayTime(localPt)})" : "Автосинк: помилка завантаження";
                if (ok) await LoadWorldMetaAsync();
            }
            finally { IsBusy = false; ProgressVisible = false; }
        });
    }

    private void Logout()
    {
        DetachGameWatcher();
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
    // 1. Звичайний .sav з sessionName в хедері (точне співпадіння)
    // 2. Autosave з sessionName в хедері
    // 3. Fallback: звичайний .sav з назвою світу в імені файлу
    // 4. Fallback: autosave з назвою світу в імені файлу
    // 5. Будь-який звичайний .sav
    // 6. Будь-який autosave
    private static FileInfo? FindBestLocalSave(string? sessionName, string? worldName = null)
    {
        var all = GetAllSaveFiles();
        if (all.Count == 0) return null;

        bool IsAuto(FileInfo f) => f.Name.Contains("autosave", StringComparison.OrdinalIgnoreCase);
        bool MatchesSession(FileInfo f) => !string.IsNullOrEmpty(sessionName) &&
            string.Equals(SaveParser.ReadSessionName(f.FullName), sessionName, StringComparison.OrdinalIgnoreCase);
        bool MatchesWorld(FileInfo f) => !string.IsNullOrEmpty(worldName) &&
            f.Name.Contains(worldName, StringComparison.OrdinalIgnoreCase);

        return all.Where(f => !IsAuto(f) && MatchesSession(f)).MaxBy(f => f.LastWriteTime)
            ?? all.Where(f =>  IsAuto(f) && MatchesSession(f)).MaxBy(f => f.LastWriteTime)
            ?? all.Where(f => !IsAuto(f) && MatchesWorld(f)).MaxBy(f => f.LastWriteTime)
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
