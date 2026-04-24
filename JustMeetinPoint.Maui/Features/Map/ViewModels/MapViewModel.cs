using CommunityToolkit.Mvvm.ComponentModel;
using JustMeetinPoint.Maui.Features.Shared.Services;
using JustMeetinPoint.Maui.Features.Map.Models;

namespace JustMeetinPoint.Maui.Features.Map.ViewModels;

/// <summary>
/// ViewModel de la pantalla de mapa.
/// 
/// Responsabilidad:
/// - Leer el resultado calculado desde IMeetingStateService.
/// - Exponer propiedades limpias para el XAML.
/// - Evitar bindings frágiles contra objetos null.
/// - Formatear duración, resumen y transbordos.
/// </summary>
public partial class MapViewModel : ObservableObject
{
    private readonly IMeetingStateService _meetingStateService;

    public MapViewModel(IMeetingStateService meetingStateService)
    {
        _meetingStateService = meetingStateService;
    }

    [ObservableProperty]
    private double latitude;

    [ObservableProperty]
    private double longitude;

    [ObservableProperty]
    private int durationSeconds;

    [ObservableProperty]
    private bool isDefaultMap;

    [ObservableProperty]
    private double originLatitude;

    [ObservableProperty]
    private double originLongitude;

    [ObservableProperty]
    private string meetingPointName = "Punto de encuentro";

    [ObservableProperty]
    private string addressText = "Dirección no disponible";

    [ObservableProperty]
    private string distanceText = "Distancia no disponible";

    [ObservableProperty]
    private string fairnessText = "Equilibrio no disponible";

    [ObservableProperty]
    private bool isSheetExpanded;

    [ObservableProperty]
    private TransitItineraryModel? itinerary;

    [ObservableProperty]
    private bool hasValidRoute;

    /// <summary>
    /// Puntos usados para pintar una línea básica en el mapa.
    /// Por ahora suele ser:
    /// - origen del usuario
    /// - punto de encuentro
    /// 
    /// Más adelante se puede sustituir por la polyline real de OTP.
    /// </summary>
    public List<RoutePointModel> RoutePoints { get; private set; } = new();

    /// <summary>
    /// Indica si existe itinerario detallado con legs.
    /// </summary>
    public bool HasItinerary => Itinerary is not null && Itinerary.Legs.Count > 0;

    /// <summary>
    /// Propiedad segura para el CollectionView.
    /// 
    /// Evita usar ItemsSource="{Binding Itinerary.Legs}" en XAML,
    /// porque Itinerary puede ser null durante la construcción inicial de la vista.
    /// </summary>
    public List<RouteLegModel> Legs => Itinerary?.Legs ?? new List<RouteLegModel>();

    /// <summary>
    /// Muestra el mensaje contrario a HasItinerary.
    /// Lo usamos en XAML para evitar necesitar InvertedBoolConverter.
    /// </summary>
    public bool ShowNoItineraryMessage => !HasItinerary;

    /// <summary>
    /// Texto resumen del bottom sheet.
    /// </summary>
    public string SummaryText
    {
        get
        {
            if (IsDefaultMap)
                return "Sin datos de ruta";

            if (!HasValidRoute)
                return "Ruta no disponible";

            return $"{DurationText} · {DistanceText}";
        }
    }

    /// <summary>
    /// Duración formateada para UI.
    /// </summary>
    public string DurationText
    {
        get
        {
            if (IsDefaultMap)
                return "Sin datos de ruta";

            if (!HasValidRoute || DurationSeconds <= 0)
                return "Duración no disponible";

            if (DurationSeconds < 60)
                return $"{DurationSeconds} seg";

            int minutes = DurationSeconds / 60;
            int seconds = DurationSeconds % 60;

            if (seconds == 0)
                return $"{minutes} min";

            return $"{minutes} min {seconds} seg";
        }
    }

    /// <summary>
    /// Texto de transbordos.
    /// 
    /// Usa Itinerary porque ahí están los legs ya normalizados.
    /// </summary>
    public string TransfersText
    {
        get
        {
            if (!HasItinerary)
                return "Sin itinerario";

            int transfers = Itinerary!.TransfersCount;
            return transfers == 0
                ? "Sin transbordos"
                : $"{transfers} transbordo{(transfers == 1 ? "" : "s")}";
        }
    }

    partial void OnDurationSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(SummaryText));
    }

    partial void OnIsDefaultMapChanged(bool value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(SummaryText));
    }

    partial void OnDistanceTextChanged(string value)
    {
        OnPropertyChanged(nameof(SummaryText));
    }

    partial void OnItineraryChanged(TransitItineraryModel? value)
    {
        OnPropertyChanged(nameof(HasItinerary));
        OnPropertyChanged(nameof(ShowNoItineraryMessage));
        OnPropertyChanged(nameof(TransfersText));
        OnPropertyChanged(nameof(Legs));
    }

    partial void OnHasValidRouteChanged(bool value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(TransfersText));
    }

    /// <summary>
    /// Carga los datos del mapa.
    /// 
    /// Si existe CurrentResult, muestra el resultado real.
    /// Si no existe, carga un mapa por defecto centrado en Barcelona.
    /// </summary>
    public Task Load()
    {
        Console.WriteLine($"[MapViewModel] CurrentResult null? {_meetingStateService.CurrentResult == null}");

        if (_meetingStateService.CurrentResult is not null)
        {
            var result = _meetingStateService.CurrentResult;

            Latitude = result.Latitude;
            Longitude = result.Longitude;
            DurationSeconds = result.DurationSeconds;

            OriginLatitude = result.OriginLatitude;
            OriginLongitude = result.OriginLongitude;

            MeetingPointName = result.MeetingPointName;
            AddressText = result.AddressText;
            DistanceText = result.DistanceText;
            FairnessText = result.FairnessText;

            RoutePoints = result.RoutePoints ?? new List<RoutePointModel>();
            Itinerary = result.Itinerary;
            HasValidRoute = result.HasValidRoute;

            IsDefaultMap = false;
        }
        else
        {
            Latitude = 41.3874;
            Longitude = 2.1686;
            DurationSeconds = 0;

            OriginLatitude = 41.3874;
            OriginLongitude = 2.1686;

            MeetingPointName = "Barcelona";
            AddressText = "Vista por defecto";
            DistanceText = "—";
            FairnessText = "Sin resultado";

            RoutePoints = new List<RoutePointModel>();
            Itinerary = null;
            HasValidRoute = false;

            IsDefaultMap = true;
        }

        /*
         * Forzamos refresco de propiedades calculadas.
         * Estas no son [ObservableProperty], así que hay que notificarlas manualmente.
         */
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(RoutePoints));
        OnPropertyChanged(nameof(HasItinerary));
        OnPropertyChanged(nameof(ShowNoItineraryMessage));
        OnPropertyChanged(nameof(TransfersText));
        OnPropertyChanged(nameof(Legs));

        return Task.CompletedTask;
    }
}