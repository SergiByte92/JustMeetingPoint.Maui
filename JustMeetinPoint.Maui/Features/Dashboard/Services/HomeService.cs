using JustMeetingPoint.Maui.NetUtils;
using JustMeetinPoint.Maui.Features.Auth.Services;

namespace JustMeetinPoint.Maui.Features.Dashboard.Services;

public sealed class HomeService : IHomeService
{
    private readonly IAuthService _authService;

    private enum MainMenuOption
    {
        CreateGroup = 1,
        JoinGroup = 2,
        GetHomeData = 3,
        GetProfileData = 4
    }

    public HomeService(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<string> GetUsernameAsync()
    {
        return await Task.Run(() =>
        {
            var socket = _authService.CurrentSocket;

            if (socket is null || !socket.Connected)
                throw new InvalidOperationException("No hay socket autenticado activo.");

            SocketTools.sendInt(socket, (int)MainMenuOption.GetHomeData);

            return SocketTools.receiveString(socket);
        });
    }
}