using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustMeetinPoint.Maui.Features.Dashboard.Services;

namespace JustMeetinPoint.Maui.Features.Dashboard.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IHomeService _homeService;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    public HomeViewModel(IHomeService homeService)
    {
        _homeService = homeService;
    }

    public string GreetingText =>
        string.IsNullOrWhiteSpace(Username)
            ? "Hola"
            : $"Hola, {Username}";

    public string SubtitleText =>
        "Encuentra un sitio que os venga bien a todos.";

    public string MainCtaTitle =>
        "¿Listos para vuestra próxima quedada?";

    public string MainCtaSubtitle =>
        "Comparte las ubicaciones de los participantes y deja que JMP proponga un punto cómodo para todos.";

    public string PrimaryButtonText =>
        "Crear  quedada";

    public string QuickActionTitle =>
        "Acciones rápidas";

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnUsernameChanged(string value)
    {
        OnPropertyChanged(nameof(GreetingText));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            Username = await _homeService.GetUsernameAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = "No se pudieron cargar los datos de inicio.";
            Console.WriteLine($"[HomeViewModel] Error LoadAsync: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoToActiveGroupAsync()
    {
        await Shell.Current.GoToAsync("//main/groups");
    }

    [RelayCommand]
    private async Task GoToCreateGroupAsync()
    {
        await Shell.Current.GoToAsync("//main/groups/create");
    }

    [RelayCommand]
    private async Task GoToGroupsAsync()
    {
        await Shell.Current.GoToAsync("//main/groups");
    }
}