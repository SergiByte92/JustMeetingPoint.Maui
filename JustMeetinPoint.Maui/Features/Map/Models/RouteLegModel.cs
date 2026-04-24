namespace JustMeetinPoint.Maui.Features.Map.Models;

/// <summary>
/// Representa un tramo de la ruta en cliente.
/// 
/// Cada leg puede ser:
/// - WALK
/// - BUS
/// - RAIL
/// - SUBWAY
/// - etc.
/// 
/// Este modelo se usa directamente en el CollectionView del mapa.
/// </summary>
public sealed class RouteLegModel
{
    public string Mode { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;
    public string ToName { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }
    public double DistanceMeters { get; set; }

    public string? PublicCode { get; set; }
    public string? LineName { get; set; }
    public string? Headsign { get; set; }

    public string? EncodedPolyline { get; set; }

    /// <summary>
    /// Duración formateada para UI.
    /// </summary>
    public string DurationText
    {
        get
        {
            if (DurationSeconds <= 0)
                return "Duración no disponible";

            int minutes = DurationSeconds / 60;

            if (minutes <= 0)
                return $"{DurationSeconds} seg";

            return $"{minutes} min";
        }
    }

    /// <summary>
    /// Distancia formateada para UI.
    /// </summary>
    public string DistanceText
    {
        get
        {
            if (DistanceMeters <= 0)
                return "Distancia no disponible";

            if (DistanceMeters < 1000)
                return $"{DistanceMeters:0} m";

            return $"{DistanceMeters / 1000:0.0} km";
        }
    }

    /// <summary>
    /// Título principal del tramo.
    /// </summary>
    public string DisplayTitle
    {
        get
        {
            if (Mode.Equals("WALK", StringComparison.OrdinalIgnoreCase))
                return "Caminar";

            if (!string.IsNullOrWhiteSpace(PublicCode))
                return $"{Mode} {PublicCode}";

            return string.IsNullOrWhiteSpace(Mode) ? "Tramo" : Mode;
        }
    }

    /// <summary>
    /// Subtítulo del tramo.
    /// Prioridad:
    /// - dirección del transporte
    /// - nombre de línea
    /// - origen → destino
    /// </summary>
    public string DisplaySubtitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Headsign))
                return $"Dirección {Headsign}";

            if (!string.IsNullOrWhiteSpace(LineName))
                return LineName;

            if (!string.IsNullOrWhiteSpace(FromName) || !string.IsNullOrWhiteSpace(ToName))
                return $"{FromName} → {ToName}";

            return "Detalle no disponible";
        }
    }
}