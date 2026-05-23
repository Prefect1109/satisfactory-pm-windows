using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using SFTracker.Models;
using SFTracker.Services;
using SFTracker.ViewModels;

namespace SFTracker.Views;

public partial class MainWindow : Window
{
    private readonly ApiService _api = new();
    private readonly GameWatcher _gameWatcher = new();
    private NotifyIcon? _trayIcon;
    private MainViewModel? _currentVm;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await CheckForUpdateAsync();

        var token = AuthService.LoadToken();
        if (token != null)
        {
            _api.SetToken(token);
            NavigateToMain();
        }
        else
        {
            NavigateToLogin();
        }
    }

    private void NavigateToLogin()
    {
        _currentVm?.DetachGameWatcher();
        _currentVm = null;

        var vm = new LoginViewModel(_api);
        vm.LoginSucceeded += _ => NavigateToMain();
        ContentFrame.Navigate(new LoginView(vm));
    }

    private void NavigateToMain()
    {
        var vm = new MainViewModel(_api);
        vm.LoggedOut += NavigateToLogin;
        vm.AutoSyncChanged += OnAutoSyncChanged;
        vm.AttachGameWatcher(_gameWatcher);
        _currentVm = vm;

        var page = new MainPage(vm);
        ContentFrame.Navigate(page);
        _ = vm.LoadAsync();

        if (vm.AutoSync) EnsureTrayIcon();
    }

    private void OnAutoSyncChanged(bool enabled)
    {
        if (enabled) EnsureTrayIcon();
        else DestroyTrayIcon();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null) return;

        _trayIcon = new NotifyIcon
        {
            Text = "SF Tracker — Auto-Sync активний",
            Visible = true,
        };

        try
        {
            var uri = new Uri("pack://application:,,,/Assets/icon.ico");
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream != null) _trayIcon.Icon = new Icon(stream);
            else _trayIcon.Icon = SystemIcons.Application;
        }
        catch { _trayIcon.Icon = SystemIcons.Application; }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Показати", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Вийти", null, (_, _) => ForceClose());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void DestroyTrayIcon()
    {
        if (_trayIcon == null) return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ForceClose()
    {
        DestroyTrayIcon();
        _gameWatcher.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_currentVm?.AutoSync == true)
        {
            // Auto-sync увімкнено — ховаємося в трей замість закриття
            e.Cancel = true;
            Hide();

            _trayIcon?.ShowBalloonTip(
                3000,
                "SF Tracker",
                "Працює у фоні. Подвійний клік щоб відкрити.",
                ToolTipIcon.Info);
        }
        else
        {
            _gameWatcher.Dispose();
            DestroyTrayIcon();
        }
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var api = new ApiService();
            var info = await api.GetVersionAsync();
            if (info == null || !UpdateService.IsNewer(info.Version)) return;

            if (info.ForceUpdate)
            {
                await ForceUpdateAsync(info);
            }
            else
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version!;
                var result = System.Windows.MessageBox.Show(
                    $"Доступне оновлення {info.Version}\n(поточна {current.Major}.{current.Minor}.{current.Build})\n\nВстановити зараз?",
                    "Оновлення",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                    await ForceUpdateAsync(info);
            }
        }
        catch { /* не блокуємо запуск */ }
    }

    private async Task ForceUpdateAsync(VersionInfo info)
    {
        var win = new UpdateWindow();
        win.Owner = this;
        win.Show();

        var ok = await UpdateService.DownloadUpdateAsync(info.Url,
            new Progress<double>(p => win.SetProgress(p)));

        win.Close();

        if (ok)
            UpdateService.ApplyUpdateAndRestart();
        else
            System.Windows.MessageBox.Show("Помилка завантаження оновлення. Спробуй пізніше.", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
