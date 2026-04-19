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
        if (IsBusy || string.IsNullOrWhiteSpace(GroupCode))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            Console.WriteLine("[Lobby] LoadLobbyAsync");

            var lobby = await _groupService.RefreshLobbyAsync(GroupCode, IsCurrentUserHost);

            MemberCount = lobby.MemberCount;
            HasStarted = lobby.HasStarted;

            Console.WriteLine($"[Lobby] MemberCount={MemberCount}, HasStarted={HasStarted}");

            if (HasStarted)
            {
                await SendCurrentLocationAndNavigateToMapAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al cargar el lobby: {ex.Message}";
            Console.WriteLine($"[Lobby] Error en LoadLobbyAsync: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadLobbyAsync();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy || !CanStartGroup)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            Console.WriteLine("[Lobby] StartAsync iniciado");

            bool started = await _groupService.StartGroupAsync(GroupCode, IsCurrentUserHost);

            Console.WriteLine($"[Lobby] StartGroupAsync => {started}");

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
            Console.WriteLine($"[Lobby] Error en StartAsync: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LeaveGroupAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(GroupCode))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            await _groupService.LeaveGroupAsync(GroupCode);
            await Shell.Current.GoToAsync("//main/groups");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al salir del grupo: {ex.Message}";
            Console.WriteLine($"[Lobby] Error en LeaveGroupAsync: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendCurrentLocationAndNavigateToMapAsync()
    {
        Console.WriteLine("[Lobby] Entrando en SendCurrentLocationAndNavigateToMapAsync");

        var permission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        Console.WriteLine($"[Lobby] Permiso ubicación inicial: {permission}");

        if (permission != PermissionStatus.Granted)
        {
            permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            Console.WriteLine($"[Lobby] Permiso ubicación tras pedirlo: {permission}");
        }

        if (permission != PermissionStatus.Granted)
            throw new InvalidOperationException("Permiso de ubicación denegado.");

        Location? location = await Geolocation.Default.GetLastKnownLocationAsync();
        Console.WriteLine($"[Lobby] LastKnownLocation null? {location == null}");

        if (location == null)
        {
            location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best));

            Console.WriteLine($"[Lobby] GetLocationAsync null? {location == null}");
        }

        if (location == null)
            throw new InvalidOperationException("No se pudo obtener la ubicación actual.");

        Console.WriteLine($"[Lobby] Ubicación obtenida: {location.Latitude}, {location.Longitude}");

        MeetingResultModel? result = await _groupService.SendLocationAndWaitResultAsync(
            GroupCode,
            location.Latitude,
            location.Longitude);

        Console.WriteLine($"[Lobby] Resultado null? {result == null}");

        if (result == null)
            throw new InvalidOperationException("No se recibió resultado del servidor.");

        Console.WriteLine($"[Lobby] Resultado recibido: {result.Latitude}, {result.Longitude}, {result.DurationSeconds}");

        _meetingStateService.CurrentResult = result;

        await Shell.Current.GoToAsync("//main/map");
    }
}