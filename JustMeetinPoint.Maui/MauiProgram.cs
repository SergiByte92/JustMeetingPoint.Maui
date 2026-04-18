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

        builder.Services.AddSingleton<IAuthService, SocketAuthService>();
        builder.Services.AddSingleton<IGroupService, GroupService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<GroupsViewModel>();
        builder.Services.AddTransient<GroupLobbyViewModel>();

        builder.Services.AddTransient<LoginView>();
        builder.Services.AddTransient<GroupsView>();
        builder.Services.AddTransient<GroupLobbyView>();

        return builder.Build();
    }
}