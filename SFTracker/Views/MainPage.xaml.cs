using System.Windows;
using System.Windows.Controls;
using SFTracker.Services;
using SFTracker.ViewModels;

namespace SFTracker.Views;

public partial class MainPage : Page
{
    private readonly MainViewModel _vm;
    private readonly ApiService _api;

    public MainPage(MainViewModel vm, ApiService api)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
        _api = api;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_api) { Owner = Window.GetWindow(this) };
        win.ShowDialog();
        // Перезавантажуємо якщо змінилась папка сейвів
        _ = _vm.LoadAsync();
    }
}
