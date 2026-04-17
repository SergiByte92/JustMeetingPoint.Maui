using System.Text.RegularExpressions;
using System.Windows.Input;
using JustMeetinPoint.Maui.Features.Auth.Dtos;
using JustMeetinPoint.Maui.Features.Auth.Services;

namespace JustMeetinPoint.Maui.Features.Auth.ViewModels;

public class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _authService;

    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    private string _message = string.Empty;
    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand LoginCommand { get; }
    public ICommand GoToRegisterCommand { get; }

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
        LoginCommand = new Command(async () => await LoginAsync());
        GoToRegisterCommand = new Command(async () => await GoToRegisterAsync());
    }

    private async Task LoginAsync()
    {
        if (IsBusy)
            return;

        Message = string.Empty;

        if (string.IsNullOrWhiteSpace(Email) ||
            string.IsNullOrWhiteSpace(Password))
        {
            Message = "Todos los campos son obligatorios.";
            return;
        }

        if (!IsValidEmail(Email))
        {
            Message = "Introduce un correo electrónico válido.";
            return;
        }

        try
        {
            IsBusy = true;

            var request = new LoginRequestDto
            {
                Email = Email,
                Password = Password
            };

            var response = await _authService.LoginAsync(request);

            Message = response.Message;

            if (response.Success)
            {
                await Shell.Current.GoToAsync("///main/home");
            }
        }
        catch (Exception ex)
        {
            Message = $"Error inesperado: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GoToRegisterAsync()
    {
        await Shell.Current.GoToAsync("///register");
    }

    private bool IsValidEmail(string email)
    {
        return Regex.IsMatch(
            email,
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.IgnoreCase);
    }
}