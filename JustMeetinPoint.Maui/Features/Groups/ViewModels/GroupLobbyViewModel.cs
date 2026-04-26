using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustMeetinPoint.Maui.Features.Groups.Services;
using JustMeetinPoint.Maui.Features.Map.Models;
using JustMeetinPoint.Maui.Features.Shared.Services;
using System.Diagnostics;

namespace JustMeetinPoint.Maui.Features.Groups.ViewModels;

[QueryProperty(nameof(GroupCode), "groupCode")]
[QueryProperty(nameof(IsCurrentUserHostRaw), "isCurrentUserHost")]
public partial class GroupLobbyViewModel : ObservableObject
{
    private readonly IGroupService _groupService;
    private readonly IMeetingStateService _meetingStateService;

    private CancellationTokenSource? _autoRefreshCts;

    /// <summary>
    /// Indica que este cliente ya entró en el flujo final:
    /// obtener ubicación → enviarla → esperar resultado → navegar.
    /// Evita doble envío de ubicación.
    /// </summary>
    private bool _hasSentLocation;

    private bool _isAutoRefreshRunning;

    public GroupLobbyViewModel(
        IGroupService groupService,
        IMeetingStateService meetingStateService)
    {
        _groupService = groupService;
        _meetingStateService = meetingStateService;
    }

    #region Observable properties

    [ObservableProperty] private string groupCode = string.Empty;
    [ObservableProperty] private int memberCount;
    [ObservableProperty] private bool hasStarted;
    [ObservableProperty] private bool isCurrentUserHost;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;

    [ObservableProperty] private string currentStatus = "Esperando participantes...";
    [ObservableProperty] private string currentSubStatus = "Comparte el código del grupo para que se unan más personas.";
    [ObservableProperty] private double progressValue = 0.35;

    #endregion

    #region Query properties

    public string IsCurrentUserHostRaw
    {
        set
        {
            if (bool.TryParse(value, out bool parsed))
            {
                IsCurrentUserHost = parsed;
                OnPropertyChanged(nameof(CanStartGroup));
                OnPropertyChanged(nameof(RoleText));
            }
        }
    }

    #endregion

    #region Computed properties

    public bool CanStartGroup => IsCurrentUserHost && !HasStarted;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsCalculating => HasStarted && !HasError;

    public string ParticipantsText =>
        $"{MemberCount} participante{(MemberCount == 1 ? "" : "s")} conectado{(MemberCount == 1 ? "" : "s")}";

    public string LobbyTitle =>
        HasStarted ? "Calculando punto de encuentro" : "Sala del grupo";

    public string RoleText =>
        IsCurrentUserHost ? "Eres el host del grupo" : "Te has unido como participante";

    public int CurrentStep => HasStarted ? 3 : 2;

    public string Step1Color => CurrentStep >= 1 ? "#1F5FBF" : "#D9DEE5";
    public string Step2Color => CurrentStep >= 2 ? "#1F5FBF" : "#D9DEE5";
    public string Step3Color => CurrentStep >= 3 ? "#1F5FBF" : "#D9DEE5";

    #endregion

    #region Partial methods

    partial void OnGroupCodeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            UpdateWaitingState();

            MainThread.BeginInvokeOnMainThread(async () => await LoadLobbyAsync());
        }
    }

    partial void OnMemberCountChanged(int value)
    {
        OnPropertyChanged(nameof(ParticipantsText));

        if (!HasStarted)
            UpdateWaitingState();
    }

    partial void OnHasStartedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartGroup));
        OnPropertyChanged(nameof(IsCalculating));
        OnPropertyChanged(nameof(LobbyTitle));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(Step1Color));
        OnPropertyChanged(nameof(Step2Color));
        OnPropertyChanged(nameof(Step3Color));

        if (value)
            UpdateCalculatingState("El grupo se ha iniciado. Preparando ubicación...", 0.45);
        else
            UpdateWaitingState();
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsCalculating));

        if (!string.IsNullOrWhiteSpace(value))
        {
            CurrentStatus = "Ha ocurrido un problema";
            CurrentSubStatus = value;
            ProgressValue = 1.0;
        }
        else if (!HasStarted)
        {
            UpdateWaitingState();
        }
    }

    #endregion

    #region Lobby lifecycle

    public void StartAutoRefreshLoop()
    {
        if (_isAutoRefreshRunning || string.IsNullOrWhiteSpace(GroupCode))
            return;

        _autoRefreshCts = new CancellationTokenSource();
        _isAutoRefreshRunning = true;

        Debug.WriteLine($"[LobbyVM] StartAutoRefreshLoop -> Group={GroupCode}");

        _ = RunAutoRefreshLoopAsync(_autoRefreshCts.Token);
    }

    public void StopAutoRefreshLoop()
    {
        Debug.WriteLine($"[LobbyVM] StopAutoRefreshLoop -> Group={GroupCode}");

        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
        _isAutoRefreshRunning = false;
    }

    private async Task RunAutoRefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!string.IsNullOrWhiteSpace(GroupCode) && !_hasSentLocation)
                {
                    await LoadLobbyAsync();
                }

                await Task.Delay(1500, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LobbyVM] Error en auto refresh: {ex}");
        }
        finally
        {
            _isAutoRefreshRunning = false;
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task LoadLobbyAsync()
    {
        if (IsBusy || _hasSentLocation || string.IsNullOrWhiteSpace(GroupCode))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            Debug.WriteLine($"[LobbyVM] Refresh lobby -> Group={GroupCode}, Host={IsCurrentUserHost}");

            var lobby = await _groupService.RefreshLobbyAsync(GroupCode, IsCurrentUserHost);

            MemberCount = lobby.MemberCount;
            HasStarted = lobby.HasStarted;

            Debug.WriteLine(
                $"[LobbyVM] Refresh result -> Members={MemberCount}, Started={HasStarted}, " +
                $"HasSentLocation={_hasSentLocation}");

            if (!HasStarted)
            {
                UpdateWaitingState();
                return;
            }

            if (!_hasSentLocation)
            {
                _hasSentLocation = true;
                StopAutoRefreshLoop();

                UpdateCalculatingState("Obteniendo tu ubicación actual...", 0.45);

                await SendCurrentLocationAndNavigateToMapAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al refrescar el lobby: {ex.Message}";
            Debug.WriteLine($"[LobbyVM] Error LoadLobbyAsync: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
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

            UpdateCalculatingState("Iniciando grupo...", 0.40);

            bool started = await _groupService.StartGroupAsync(GroupCode, IsCurrentUserHost);

            Debug.WriteLine($"[LobbyVM] StartGroupAsync => {started}");

            if (!started)
            {
                ErrorMessage = "No se pudo iniciar el grupo.";
                return;
            }

            HasStarted = true;
            _hasSentLocation = true;
            StopAutoRefreshLoop();

            UpdateCalculatingState("Obteniendo tu ubicación actual...", 0.45);

            await SendCurrentLocationAndNavigateToMapAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al iniciar el grupo: {ex.Message}";
            Debug.WriteLine($"[LobbyVM] Error StartAsync: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LeaveGroupAsync()
    {
        try
        {
            StopAutoRefreshLoop();

            await _groupService.LeaveGroupAsync(GroupCode);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//main/groups");
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al salir del grupo: {ex.Message}";
            Debug.WriteLine($"[LobbyVM] Error LeaveGroupAsync: {ex}");
        }
    }

    #endregion

    #region Main flow

    private async Task SendCurrentLocationAndNavigateToMapAsync()
    {
        try
        {
            Debug.WriteLine("[LobbyVM] 1. Comprobando permisos");
            UpdateCalculatingState("Comprobando permisos de ubicación...", 0.50);

            var permission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (permission != PermissionStatus.Granted)
                throw new InvalidOperationException("Permiso de ubicación denegado.");

            Debug.WriteLine("[LobbyVM] 2. Obteniendo ubicación");
            UpdateCalculatingState("Obteniendo tu posición actual...", 0.60);

            /*
             * En emulador, High puede depender de GNSS simulado y tardar demasiado.
             * Medium suele ser suficiente para pruebas y evita bloqueos aparentes.
             */
            Location? location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(
                    GeolocationAccuracy.Medium,
                    TimeSpan.FromSeconds(5)));

            if (location is null)
                throw new InvalidOperationException("No se pudo obtener la ubicación actual.");

            Debug.WriteLine(
                $"[LobbyVM] 3. Ubicación obtenida: Lat={location.Latitude}, Lon={location.Longitude}");

            UpdateCalculatingState("Enviando tu ubicación al servidor...", 0.75);

            Debug.WriteLine("[LobbyVM] 3.5. Enviando ubicación y esperando resultado del servidor...");

            MeetingResultModel? result = await _groupService.SendLocationAndWaitResultAsync(
                GroupCode,
                location.Latitude,
                location.Longitude);

            Debug.WriteLine(result is null
                ? "[LobbyVM] 4. Resultado NULL"
                : $"[LobbyVM] 4. Resultado recibido: Lat={result.Latitude}, Lon={result.Longitude}, " +
                  $"Duration={result.DurationSeconds}, Valid={result.HasValidRoute}, Legs={result.Legs?.Count ?? 0}");

            if (result is null)
                throw new InvalidOperationException("No se recibió resultado del servidor.");

            _meetingStateService.CurrentResult = result;

            UpdateCalculatingState(
                result.DurationSeconds > 0
                    ? "Ruta encontrada. Abriendo mapa..."
                    : "Punto calculado. Abriendo mapa...",
                0.95);

            Debug.WriteLine("[LobbyVM] 5. Antes de navegar a //main/map");

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//main/map");
            });

            Debug.WriteLine("[LobbyVM] 6. Navegación completada");
        }
        catch (Exception ex)
        {
            /*
             * No reseteamos _hasSentLocation aquí.
             * Si el servidor ya recibió la ubicación, reintentar puede duplicar
             * el flujo y desincronizar el protocolo.
             */
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = $"Fallo al calcular el punto de encuentro: {ex.Message}";
                IsBusy = false;
            });

            Debug.WriteLine($"[LobbyVM] Error SendCurrentLocationAndNavigateToMapAsync: {ex}");
        }
    }

    #endregion

    #region UI helpers

    private void UpdateWaitingState()
    {
        CurrentStatus = MemberCount <= 1
            ? "Esperando a más participantes"
            : "Grupo listo para iniciar";

        CurrentSubStatus = MemberCount <= 1
            ? "Comparte el código del grupo y espera a que se una alguien más."
            : $"Ahora mismo hay {MemberCount} participantes conectados.";

        ProgressValue = MemberCount <= 1 ? 0.35 : 0.55;
    }

    private void UpdateCalculatingState(string subStatus, double progress = 0.80)
    {
        CurrentStatus = "Cálculo en curso";
        CurrentSubStatus = subStatus;
        ProgressValue = progress;
    }

    #endregion
}