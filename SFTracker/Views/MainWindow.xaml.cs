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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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
        var vm = new LoginViewModel(_api);
        vm.LoginSucceeded += _ => NavigateToMain();
        ContentFrame.Navigate(new LoginView(vm));
    }

    private void NavigateToMain()
    {
        var vm = new MainViewModel(_api);
        vm.LoggedOut += NavigateToLogin;
        var page = new MainPage(vm);
        ContentFrame.Navigate(page);
        _ = vm.LoadAsync();
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
                var result = MessageBox.Show(
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
