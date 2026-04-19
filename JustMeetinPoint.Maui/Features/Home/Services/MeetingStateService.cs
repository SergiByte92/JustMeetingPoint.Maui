using JustMeetinPoint.Maui.Features.Home.Models;

namespace JustMeetinPoint.Maui.Features.Home.Services;

public class MeetingStateService : IMeetingStateService
{
    public MeetingResultModel? CurrentResult { get; set; }

    public void Clear()
    {
        CurrentResult = null;
    }
}