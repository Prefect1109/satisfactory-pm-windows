using System.Windows.Controls;
using SFTracker.ViewModels;

namespace SFTracker.Views;

public partial class MainPage : Page
{
    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
