using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using SFTracker.Services;
using MessageBox = System.Windows.MessageBox;

namespace SFTracker.Views;

public partial class SettingsWindow : Window
{
    private readonly ApiService _api;

    public SettingsWindow(ApiService api)
    {
        InitializeComponent();
        _api = api;
        LoadSettings();
    }

    private void LoadSettings()
    {
        AutoSyncCheck.IsChecked = AuthService.LoadAutoSync();

        var customPath = AuthService.LoadCustomSaveFolder();
        SavePathBox.Text = customPath ?? "";
        PathHint.Text = string.IsNullOrEmpty(customPath)
            ? "Порожньо — автоматичний пошук"
            : customPath;

        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        VersionLabel.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AuthService.SaveAutoSync(AutoSyncCheck.IsChecked == true);
        var path = SavePathBox.Text.Trim();
        AuthService.SaveCustomSaveFolder(string.IsNullOrEmpty(path) ? null : path);
        Close();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Виберіть папку з сейвами Satisfactory",
            UseDescriptionForTitle = true,
        };
        var current = SavePathBox.Text.Trim();
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
            dlg.InitialDirectory = current;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SavePathBox.Text = dlg.SelectedPath;
            PathHint.Text = dlg.SelectedPath;
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        CheckUpdateBtn.Content = "...";
        try
        {
            var info = await _api.GetVersionAsync();
            var mode = UpdateService.GetUpdateMode(info);
            if (mode == UpdateMode.None)
                MessageBox.Show("У вас актуальна версія.", "Оновлення", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show($"Доступна нова версія: {info!.Version}\nЗакрийте налаштування і перезапустіть застосунок.", "Оновлення", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
            CheckUpdateBtn.Content = "Перевірити";
        }
    }

    private void Support_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "mailto:prefect.t@icloud.com?subject=SF%20Tracker%20Support",
            UseShellExecute = true,
        });
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
