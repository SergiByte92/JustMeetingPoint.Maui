using JustMeetinPoint.Maui.Features.Home.Models;

namespace JustMeetinPoint.Maui.Features.Home.Services;

public interface IMeetingStateService
{
    MeetingResultModel? CurrentResult { get; set; }
    void Clear();
}