using System.Windows;

namespace SFTracker.Views;

public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
    }

    public void SetProgress(double fraction)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateProgress.Value = fraction * 100;
            PercentLabel.Text = $"{(int)(fraction * 100)}%";
        });
    }
}
