using JustMeetinPoint.Maui.Features.Home.Models;

namespace JustMeetinPoint.Maui.Features.Home.Services;

public interface IGroupService
{
    Task<GroupLobbyModel> CreateGroupAsync();
    Task<GroupLobbyModel> JoinGroupAsync(string groupCode);
    Task<GroupLobbyModel> RefreshLobbyAsync(string groupCode, bool isCurrentUserHost);
    Task LeaveGroupAsync(string groupCode);

    Task<bool> StartGroupAsync(string groupCode, bool isCurrentUserHost);
    Task<MeetingResultModel?> SendLocationAndWaitResultAsync(string groupCode, double latitude, double longitude);
}