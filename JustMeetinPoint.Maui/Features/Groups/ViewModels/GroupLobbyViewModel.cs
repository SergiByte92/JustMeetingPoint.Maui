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

    // Estado principal visible en la UI
    [ObservableProperty] private string currentStatus = "Esperando participantes...";

    // Subestado / descripción más detallada
    [ObservableProperty] private string currentSubStatus = "Comparte el código del grupo para que se unan más personas.";

    // Valor de la barra de progreso (0 a 1)
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

    /// <summary>
    /// Paso actual del stepper visual superior.
    /// 1 = grupo creado
    /// 2 = esperando / listo para iniciar
    /// 3 = cálculo en curso
    /// </summary>
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
            // Al entrar, colocamos un estado coherente de espera
            UpdateWaitingState();

            // Hacemos la carga inicial del lobby
            MainThread.BeginInvokeOnMainThread(async () => await LoadLobbyAsync());
        }
    }

    partial void OnMemberCountChanged(int value)
    {
        OnPropertyChanged(nameof(ParticipantsText));

        // Mientras el grupo no haya empezado,
        // queremos que el texto y progreso reflejen el número real de miembros.
        if (!HasStarted)
        {
            UpdateWaitingState();
        }
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
        {
            // Cuando el grupo arranca, cambiamos a estado de cálculo
            UpdateCalculatingState("El grupo se ha iniciado. Preparando ubicación...", 0.45);
        }
        else
        {
            UpdateWaitingState();
        }
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
                // Solo refrescamos mientras no se haya entrado
                // en la fase final de envío de ubicación/cálculo.
                if (!string.IsNullOrWhiteSpace(GroupCode) && !_hasSentLocation)
                {
                    await LoadLobbyAsync();
                }

                await Task.Delay(1500, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelación normal, no hay que hacer nada.
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
        // Protegemos la entrada para no solapar llamadas
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

            Debug.WriteLine($"[LobbyVM] Refresh result -> Members={MemberCount}, Started={HasStarted}, HasSentLocation={_hasSentLocation}");

            // Si todavía no ha empezado, solo actualizamos la UI de espera y salimos.
            if (!HasStarted)
            {
                UpdateWaitingState();
                return;
            }

            // Si el grupo ya empezó y todavía no hemos enviado ubicación,
            // paramos el auto refresh y entramos al flujo final.
            if (!_hasSentLocation)
            {
                _hasSentLocation = true;
                StopAutoRefreshLoop();

                UpdateCalculatingState("Obteniendo tu ubicación actual...", 0.45);

                // IMPORTANTE:
                // NO usamos Task.Run aquí.
                // Dejamos el flujo lineal y determinista.
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

            // Igual que antes: sin Task.Run
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
            {
                permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (permission != PermissionStatus.Granted)
            {
                throw new InvalidOperationException("Permiso de ubicación denegado.");
            }

            Debug.WriteLine("[LobbyVM] 2. Obteniendo ubicación");
            UpdateCalculatingState("Obteniendo tu posición actual...", 0.60);

            Location? location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(
                    GeolocationAccuracy.High,
                    TimeSpan.FromSeconds(10)));

            if (location == null)
            {
                throw new InvalidOperationException("No se pudo obtener la ubicación actual.");
            }

            Debug.WriteLine($"[LobbyVM] 3. Ubicación obtenida: {location.Latitude}, {location.Longitude}");
            UpdateCalculatingState("Enviando tu ubicación al servidor...", 0.75);

            MeetingResultModel? result = await _groupService.SendLocationAndWaitResultAsync(
                GroupCode,
                location.Latitude,
                location.Longitude);

            if (result == null)
            {
                throw new InvalidOperationException("No se recibió resultado del servidor.");
            }

            Debug.WriteLine($"[LobbyVM] 4. Resultado recibido: {result.Latitude}, {result.Longitude}, {result.DurationSeconds}");

            _meetingStateService.CurrentResult = result;

            if (result.DurationSeconds > 0)
            {
                UpdateCalculatingState("Ruta encontrada. Abriendo mapa...", 0.95);
            }
            else
            {
                // Para el caso -3 o similares: hay punto, pero no ruta disponible
                UpdateCalculatingState("Punto calculado. No hay ruta disponible. Abriendo mapa...", 0.95);
            }

            Debug.WriteLine("[LobbyVM] 5. Antes de navegar");

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//main/map");
            });

            Debug.WriteLine("[LobbyVM] 6. Navegación completada");
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = $"Fallo al calcular el punto de encuentro: {ex.Message}";
                _hasSentLocation = false; // permitimos reintento si algo falla
                IsBusy = false;
            });

            Debug.WriteLine($"[LobbyVM] Error SendCurrentLocationAndNavigateToMapAsync: {ex}");
        }
    }

    #endregion

    #region UI helpers

    /// <summary>
    /// Estado visual cuando el grupo aún no ha empezado.
    /// </summary>
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

    /// <summary>
    /// Estado visual durante el cálculo.
    /// </summary>
    private void UpdateCalculatingState(string subStatus, double progress = 0.80)
    {
        CurrentStatus = "Cálculo en curso";
        CurrentSubStatus = subStatus;
        ProgressValue = progress;
    }

    #endregion
}