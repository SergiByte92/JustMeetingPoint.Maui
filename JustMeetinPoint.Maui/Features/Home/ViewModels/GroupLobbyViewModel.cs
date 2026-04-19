using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustMeetinPoint.Maui.Features.Home.Models;
using JustMeetinPoint.Maui.Features.Home.Services;

namespace JustMeetinPoint.Maui.Features.Home.ViewModels;

[QueryProperty(nameof(GroupCode), "groupCode")]
[QueryProperty(nameof(IsCurrentUserHostRaw), "isCurrentUserHost")]
public partial class GroupLobbyViewModel : ObservableObject
{
    private readonly IGroupService _groupService;
    private readonly IMeetingStateService _meetingStateService;

    public GroupLobbyViewModel(
        IGroupService groupService,
        IMeetingStateService meetingStateService)
    {
        _groupService = groupService;
        _meetingStateService = meetingStateService;
    }

    [ObservableProperty] private string groupCode = string.Empty;
    [ObservableProperty] private int memberCount;
    [ObservableProperty] private bool hasStarted;
    [ObservableProperty] private bool isCurrentUserHost;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;

    public string IsCurrentUserHostRaw
    {
        set
        {
            if (bool.TryParse(value, out bool parsed))
            {
                IsCurrentUserHost = parsed;
                OnPropertyChanged(nameof(CanStartGroup));
            }
        }
    }

    public bool CanStartGroup => IsCurrentUserHost && !HasStarted;

    public string StatusText => HasStarted
        ? "El grupo ya ha iniciado."
        : "Esperando a más participantes...";

    public string ParticipantsText => $"{MemberCount} participante{(MemberCount == 1 ? "" : "s")} conectado{(MemberCount == 1 ? "" : "s")}";

    partial void OnGroupCodeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            MainThread.BeginInvokeOnMainThread(async () => await LoadLobbyAsync());
    }

    partial void OnMemberCountChanged(int value) => OnPropertyChanged(nameof(ParticipantsText));

    partial void OnHasStartedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanStartGroup));
    }

    [RelayCommand]
    private async Task LoadLobbyAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(GroupCode)) return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var lobby = await _groupService.RefreshLobbyAsync(GroupCode, IsCurrentUserHost);

            MemberCount = lobby.MemberCount;
            HasStarted = lobby.HasStarted;

            if (HasStarted)
            {
                await SendCurrentLocationAndNavigateToMapAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al cargar el lobby: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadLobbyAsync();

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy || !CanStartGroup)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            bool started = await _groupService.StartGroupAsync(GroupCode, IsCurrentUserHost);

            if (!started)
            {
                ErrorMessage = "No se pudo iniciar el grupo.";
                return;
            }

            HasStarted = true;

            await SendCurrentLocationAndNavigateToMapAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al iniciar el grupo: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LeaveGroupAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(GroupCode)) return;

        try
        {
            IsBusy = true;
            await _groupService.LeaveGroupAsync(GroupCode);
            await Shell.Current.GoToAsync("//main/groups");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al salir del grupo: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendCurrentLocationAndNavigateToMapAsync()
    {
        Location? location = await Geolocation.Default.GetLastKnownLocationAsync();

        if (location == null)
        {
            location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best));
        }

        if (location == null)
            throw new InvalidOperationException("No se pudo obtener la ubicación actual.");

        MeetingResultModel? result = await _groupService.SendLocationAndWaitResultAsync(
            GroupCode,
            location.Latitude,
            location.Longitude);

        if (result == null)
            throw new InvalidOperationException("No se recibió resultado del servidor.");

        _meetingStateService.CurrentResult = result;

        await Shell.Current.GoToAsync("//main/map");
    }
}