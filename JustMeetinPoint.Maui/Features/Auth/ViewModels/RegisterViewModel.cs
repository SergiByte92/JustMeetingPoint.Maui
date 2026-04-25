using System.Text.RegularExpressions;
using System.Windows.Input;
using JustMeetinPoint.Maui.Features.Auth.Dtos;
using JustMeetinPoint.Maui.Features.Auth.Services;
using JustMeetingPoint.Maui.NetUtils;


namespace JustMeetinPoint.Maui.Features.Auth.ViewModels;

public class RegisterViewModel : BaseViewModel
{
    private readonly IAuthService _authService;

    private string _username = string.Empty;
    public string Username //update censurar nombres 
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    private DateTime _birthDate = new DateTime(2000, 1, 1);
    public DateTime BirthDate
    {
        get => _birthDate;
        set => SetProperty(ref _birthDate, value);
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    private string _repeatPassword = string.Empty;
    public string RepeatPassword
    {
        get => _repeatPassword;
        set => SetProperty(ref _repeatPassword, value);
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

    public ICommand RegisterCommand { get; }

    public RegisterViewModel(IAuthService authService)
    {
        _authService = authService;
        RegisterCommand = new Command(async () => await RegisterAsync());
    }

    private async Task RegisterAsync()
    {
        if (IsBusy)
            return;

        Message = string.Empty;

        if (string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Email) ||
            string.IsNullOrWhiteSpace(Password) ||
            string.IsNullOrWhiteSpace(RepeatPassword))
        {
            Message = "Todos los campos son obligatorios.";
            return;
        }

        if (!IsValidEmail(Email))
        {
            Message = "Introduce un correo electrónico válido.";
            return;
        }

        if (Password != RepeatPassword)
        {
            Message = "Las contraseñas no coinciden.";
            return;
        }

        try
        {
            IsBusy = true;

            var request = new RegisterRequestDto
            {
                Username = Username,
                Email = Email,
                Password = Password,
                BirthDate = BirthDate
            };

            var response = await _authService.RegisterAsync(request);

            Message = response.Message;

            if (response.Success)
            {
                await Shell.Current.GoToAsync("//login");
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

    private bool IsValidEmail(string email)
    {
        return Regex.IsMatch(
            email,
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.IgnoreCase);
    }


}