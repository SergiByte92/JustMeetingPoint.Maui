using JustMeetinPoint.Maui.Features.Auth.ViewModels;

namespace JustMeetinPoint.Maui.Features.Auth.Views;

public partial class RegisterView : ContentPage
{
    // ✅ CORRECTO: el ViewModel llega por inyección de dependencias.
    // MAUI resuelve automáticamente RegisterViewModel desde el contenedor DI,
    // que a su vez inyecta IAuthService (el Singleton real, no una instancia nueva).
    //
    // ❌ ANTES (incorrecto):
    //   BindingContext = new RegisterViewModel(new SocketAuthService());
    //   → Creaba un SocketAuthService distinto al Singleton registrado.
    //   → El socket de registro era diferente al de login: estado inconsistente.
    public RegisterView(RegisterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override bool OnBackButtonPressed()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Shell.Current.GoToAsync("//login");
        });

        return true;
    }
}