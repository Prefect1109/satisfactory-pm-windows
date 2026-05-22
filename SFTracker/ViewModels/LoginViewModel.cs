using System.Windows.Input;
using SFTracker.Services;

namespace SFTracker.ViewModels;

public class LoginViewModel : ViewModelBase
{
    private readonly ApiService _api;

    public LoginViewModel(ApiService api)
    {
        _api = api;
        LoginCommand = new RelayCommand(async _ => await LoginAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(ConnectToken));
    }

    private string _connectToken = "";
    public string ConnectToken
    {
        get => _connectToken;
        set { Set(ref _connectToken, value); CommandManager.InvalidateRequerySuggested(); }
    }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    private string _errorText = "";
    public string ErrorText { get => _errorText; set => Set(ref _errorText, value); }

    public ICommand LoginCommand { get; }

    public event Action<string>? LoginSucceeded;

    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorText = "";
        try
        {
            var token = await _api.LoginAsync(ConnectToken.Trim());
            if (token == null)
            {
                ErrorText = "Невірний токен. Отримай його у боті /connect";
                return;
            }
            AuthService.SaveToken(token);
            _api.SetToken(token);
            LoginSucceeded?.Invoke(token);
        }
        finally { IsBusy = false; }
    }
}
