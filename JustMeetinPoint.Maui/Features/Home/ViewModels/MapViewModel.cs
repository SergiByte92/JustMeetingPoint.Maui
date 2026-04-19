using CommunityToolkit.Mvvm.ComponentModel;
using JustMeetinPoint.Maui.Features.Home.Services;

namespace JustMeetinPoint.Maui.Features.Home.ViewModels;

public partial class MapViewModel : ObservableObject
{
    private readonly IMeetingStateService _meetingStateService;

    public MapViewModel(IMeetingStateService meetingStateService)
    {
        _meetingStateService = meetingStateService;

        if (_meetingStateService.CurrentResult != null)
        {
            Latitude = _meetingStateService.CurrentResult.Latitude;
            Longitude = _meetingStateService.CurrentResult.Longitude;
            DurationSeconds = _meetingStateService.CurrentResult.DurationSeconds;
        }
    }

    [ObservableProperty] private double latitude;
    [ObservableProperty] private double longitude;
    [ObservableProperty] private int durationSeconds;
}