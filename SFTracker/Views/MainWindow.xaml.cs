using System.Reflection;
using System.Windows;
using System.Windows.Input;
using SFTracker.Models;
using SFTracker.Services;
using SFTracker.ViewModels;

namespace SFTracker.Views;

public partial class MainWindow : Window
{
    private readonly ApiService _api = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => _trayIcon?.Dispose();
    }

    private void InitTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "SF Tracker",
            Icon = new System.Drawing.Icon(
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico")),
            Visible = false,
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Відкрити", null, (_, _) => ShowWindow());
        menu.Items.Add("Вийти", null, (_, _) => { _forceClose = true; Close(); });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _trayIcon!.Visible = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose && AuthService.LoadRunInBackground())
        {
            e.Cancel = true;
            Hide();
            _trayIcon!.Visible = true;
            _trayIcon.ShowBalloonTip(1500, "SF Tracker", "Працює у фоні", System.Windows.Forms.ToolTipIcon.None);
            return;
        }
        base.OnClosing(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitTrayIcon();
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
        var vm = new LoginViewModel(_api);
        vm.LoginSucceeded += _ => NavigateToMain();
        ContentFrame.Navigate(new LoginView(vm));
    }

    private void NavigateToMain()
    {
        var vm = new MainViewModel(_api);
        vm.LoggedOut += () => { _forceClose = false; NavigateToLogin(); };
        var page = new MainPage(vm, _api);
        ContentFrame.Navigate(page);
        _ = vm.LoadAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var api = new ApiService();
            var info = await api.GetVersionAsync();
            var mode = UpdateService.GetUpdateMode(info);

            if (mode == UpdateMode.Force)
            {
                await ForceUpdateAsync(info!);
            }
            else if (mode == UpdateMode.Optional)
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version!;
                var result = MessageBox.Show(
                    $"Доступне оновлення {info!.Version}\n(поточна {current.Major}.{current.Minor}.{current.Build})\n\nВстановити зараз?",
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
            MessageBox.Show("Помилка завантаження оновлення. Спробуй пізніше.", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
