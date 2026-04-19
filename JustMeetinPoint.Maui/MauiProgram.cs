using CommunityToolkit.Maui;
using JustMeetinPoint.Maui.Features.Auth.Services;
using JustMeetinPoint.Maui.Features.Auth.ViewModels;
using JustMeetinPoint.Maui.Features.Auth.Views;
using JustMeetinPoint.Maui.Features.Home.Services;
using JustMeetinPoint.Maui.Features.Home.ViewModels;
using JustMeetinPoint.Maui.Features.Home.Views;

namespace JustMeetinPoint.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        // ── SERVICIOS (Singleton: viven toda la sesión) ──────────────────────
        // IAuthService gestiona la conexión autenticada (socket de sesión).
        // Debe ser Singleton para que el socket persista entre navegaciones.
        builder.Services.AddSingleton<IAuthService, SocketAuthService>();

        // IGroupService depende de IAuthService para reutilizar el socket.
        // También Singleton porque comparte estado de sesión.
        builder.Services.AddSingleton<IGroupService, GroupService>();

        // ── VIEWMODELS (Transient: nueva instancia cada vez que se navega) ───
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();   // ← AÑADIDO
        builder.Services.AddTransient<GroupsViewModel>();
        builder.Services.AddTransient<GroupLobbyViewModel>();

        // ── VIEWS (Transient: ligadas a su ViewModel) ────────────────────────
        builder.Services.AddTransient<LoginView>();
        builder.Services.AddTransient<RegisterView>();        // ← AÑADIDO
        builder.Services.AddTransient<GroupsView>();
        builder.Services.AddTransient<GroupLobbyView>();

        // NOTA: HomeView, MapView y ProfileView no necesitan registro en DI
        // mientras no reciban ViewModel por constructor. Si en el futuro
        // añades ViewModels a estas pantallas, regístralas aquí.

        return builder.Build();
    }
}