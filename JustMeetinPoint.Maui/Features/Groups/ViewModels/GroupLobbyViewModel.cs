using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustMeetinPoint.Maui.Features.Groups.Services;
using JustMeetinPoint.Maui.Features.Map.Models;
using JustMeetinPoint.Maui.Features.Shared.Services;

namespace JustMeetinPoint.Maui.Features.Groups.ViewModels;

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
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string StatusText => HasStarted
        ? "Calculando punto de encuentro..."
        : "Esperando a más participantes...";

    public string ParticipantsText =>
        $"{MemberCount} participante{(MemberCount == 1 ? "" : "s")} conectado{(MemberCount == 1 ? "" : "s")}";

    public int CurrentStep => HasStarted ? 3 : 2;
    public bool IsCalculating => HasStarted;

    public string Step1Color => CurrentStep >= 1 ? "#1F5FBF" : "#D9DEE5";
    public string Step2Color => CurrentStep >= 2 ? "#1F5FBF" : "#D9DEE5";
    public string Step3Color => CurrentStep >= 3 ? "#1F5FBF" : "#D9DEE5";

    public string LobbyTitle => HasStarted ? "Calculando punto de encuentro" : "Sala del grupo";

    public string LobbySubtitle => HasStarted
        ? "Estamos calculando la mejor opción para todos."
        : "Comparte el código mientras llegan los participantes.";

    public string PrimaryStatusText => HasStarted
        ? "Cálculo en curso"
        : ParticipantsText;

    public string SecondaryStatusText => HasStarted
        ? "Esto puede tardar unos segundos."
        : "Esperando a más participantes...";

    partial void OnGroupCodeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Console.WriteLine($"[LobbyVM] GroupCode recibido: {value}. Host={IsCurrentUserHost}");
            MainThread.BeginInvokeOnMainThread(async () => await LoadLobbyAsync());
        }
    }

    partial void OnMemberCountChanged(int value)
    {
        OnPropertyChanged(nameof(ParticipantsText));
        OnPropertyChanged(nameof(PrimaryStatusText));
    }

    partial void OnHasStartedChanged(bool value)
    {
        Console.WriteLine($"[LobbyVM] HasStarted cambió a {value}. GroupCode={GroupCode}. Host={IsCurrentUserHost}");

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanStartGroup));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsCalculating));
        OnPropertyChanged(nameof(Step1Color));
        OnPropertyChanged(nameof(Step2Color));
        OnPropertyChanged(nameof(Step3Color));
        OnPropertyChanged(nameof(LobbyTitle));
        OnPropertyChanged(nameof(LobbySubtitle));
        OnPropertyChanged(nameof(PrimaryStatusText));
        OnPropertyChanged(nameof(SecondaryStatusText));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
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

            Console.WriteLine($"[LobbyVM] LoadLobbyAsync -> Group={GroupCode}, Host={IsCurrentUserHost}");

            var lobby = await _groupService.RefreshLobbyAsync(GroupCode, IsCurrentUserHost);

            MemberCount = lobby.MemberCount;
            HasStarted = lobby.HasStarted;

            Console.WriteLine($"[LobbyVM] RefreshLobbyAsync -> MemberCount={MemberCount}, HasStarted={HasStarted}, Host={IsCurrentUserHost}");

            if (HasStarted)
            {
                Console.WriteLine($"[LobbyVM] HasStarted=true, entrando a SendCurrentLocationAndNavigateToMapAsync. Group={GroupCode}, Host={IsCurrentUserHost}");
                await SendCurrentLocationAndNavigateToMapAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al cargar el lobby: {ex.Message}";
            Console.WriteLine($"[LobbyVM] Error en LoadLobbyAsync: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Console.WriteLine($"[LobbyVM] RefreshAsync manual. Group={GroupCode}, Host={IsCurrentUserHost}");
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

            Console.WriteLine($"[LobbyVM] StartAsync -> Group={GroupCode}, Host={IsCurrentUserHost}");

            bool started = await _groupService.StartGroupAsync(GroupCode, IsCurrentUserHost);

            Console.WriteLine($"[LobbyVM] StartGroupAsync => {started}");

            if (!started)
            {
                ErrorMessage = "No se pudo iniciar el grupo.";
                return;
            }

            HasStarted = true;

            Console.WriteLine($"[LobbyVM] StartAsync OK, entrando a SendCurrentLocationAndNavigateToMapAsync. Group={GroupCode}, Host={IsCurrentUserHost}");
            await SendCurrentLocationAndNavigateToMapAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al iniciar el grupo: {ex.Message}";
            Console.WriteLine($"[LobbyVM] Error en StartAsync: {ex}");
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

            Console.WriteLine($"[LobbyVM] LeaveGroupAsync -> Group={GroupCode}, Host={IsCurrentUserHost}");

            await _groupService.LeaveGroupAsync(GroupCode);
            await Shell.Current.GoToAsync("//main/groups");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al salir del grupo: {ex.Message}";
            Console.WriteLine($"[LobbyVM] Error en LeaveGroupAsync: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendCurrentLocationAndNavigateToMapAsync()
    {
        Console.WriteLine($"[LobbyVM] Entrando en SendCurrentLocationAndNavigateToMapAsync. Group={GroupCode}, Host={IsCurrentUserHost}");

        var permission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        Console.WriteLine($"[LobbyVM] Permiso ubicación inicial: {permission}");

        if (permission != PermissionStatus.Granted)
        {
            permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            Console.WriteLine($"[LobbyVM] Permiso ubicación tras pedirlo: {permission}");
        }

        if (permission != PermissionStatus.Granted)
            throw new InvalidOperationException("Permiso de ubicación denegado.");

        await Task.Delay(1500);

        Location? location = await Geolocation.Default.GetLocationAsync(
            new GeolocationRequest(
                GeolocationAccuracy.High,
                TimeSpan.FromSeconds(15)));

        Console.WriteLine($"[LobbyVM] GetLocationAsync null? {location == null}");

        if (location == null)
            throw new InvalidOperationException("No se pudo obtener la ubicación actual.");

        Console.WriteLine($"[LobbyVM] Ubicación obtenida: {location.Latitude}, {location.Longitude}");

        Console.WriteLine($"[LobbyVM] Llamando a SendLocationAndWaitResultAsync. Group={GroupCode}, Host={IsCurrentUserHost}");
        MeetingResultModel? result = await _groupService.SendLocationAndWaitResultAsync(
            GroupCode,
            location.Latitude,
            location.Longitude);

        Console.WriteLine($"[LobbyVM] Resultado null? {result == null}");

        if (result == null)
            throw new InvalidOperationException("No se recibió resultado del servidor.");

        Console.WriteLine($"[LobbyVM] Resultado recibido: {result.Latitude}, {result.Longitude}, {result.DurationSeconds}");

        _meetingStateService.CurrentResult = result;

        await Task.Delay(1800);
        await Shell.Current.GoToAsync("//main/map");
    }
}