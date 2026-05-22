using System.Windows.Controls;
using System.Windows.Input;
using SFTracker.ViewModels;

namespace SFTracker.Views;

public partial class LoginView : Page
{
    public LoginView(LoginViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void TokenBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ((LoginViewModel)DataContext).LoginCommand.Execute(null);
    }
}
