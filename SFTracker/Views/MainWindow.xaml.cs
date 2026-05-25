using System.Windows;
using System.Windows.Input;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
        var page = new MainPage(vm, _api);
        ContentFrame.Navigate(page);
        _ = vm.LoadAsync();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
