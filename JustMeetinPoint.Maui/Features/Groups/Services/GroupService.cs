using JustMeetinPoint.Maui.Features.Auth.Services;
using JustMeetinPoint.Maui.Features.Groups.Models;
using JustMeetinPoint.Maui.Features.Map.Models;
using JustMeetingPoint.Maui.NetUtils;
using System.Net.Sockets;
using System.Text.Json;

namespace JustMeetinPoint.Maui.Features.Groups.Services;

public class GroupService : IGroupService
{
    #region Constants - Validation

    private const int MinGroupNameLength = 3;
    private const int MaxGroupNameLength = 50;
    private const int MaxGroupDescriptionLength = 250;

    private const int MinGroupCodeLength = 4;
    private const int MaxGroupCodeLength = 12;

    #endregion

    #region Constants - TCP Protocol

    /*
     * Estos códigos deben coincidir exactamente con los valores esperados
     * por el servidor. Si cambian aquí, también deben cambiar allí.
     */
    private const int MainGroupCreateGroup = 1;
    private const int MainGroupJoinGroup = 2;

    private const int LobbyOptionRefresh = 1;
    private const int LobbyOptionExit = 2;
    private const int LobbyOptionStart = 3;
    private const int LobbyOptionSendLocation = 4;
    private const int LobbyOptionPollResult = 5;

    #endregion

    #region Constants - Polling

    private const int PollDelayMilliseconds = 1500;

    /*
     * 80 intentos * 1.5 segundos = 120 segundos.
     * OTP en Docker + varios emuladores puede tardar en devolver todas las rutas.
     */
    private const int MaxPollAttempts = 80;

    #endregion

    #region Fields

    private readonly IAuthService _authService;

    /*
     * El cliente mantiene un único socket TCP autenticado.
     * Este lock evita operaciones concurrentes de send/receive sobre el mismo socket,
     * ya que podrían romper el orden del protocolo cliente-servidor.
     */
    private readonly SemaphoreSlim _socketLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region Constructor

    public GroupService(IAuthService authService)
    {
        _authService = authService;
    }

    #endregion

    #region Public API - Group Lifecycle

    public async Task<GroupLobbyModel> CreateGroupAsync(
        string name,
        string description,
        string method,
        string category)
    {
        string normalizedName = NormalizeText(name);
        string normalizedDescription = NormalizeText(description);
        string normalizedMethod = NormalizeText(method);
        string normalizedCategory = NormalizeText(category);

        ValidateCreateGroupPayload(
            normalizedName,
            normalizedDescription,
            normalizedMethod,
            normalizedCategory);

        await _socketLock.WaitAsync();

        try
        {
            return await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                SocketTools.sendInt(socket, MainGroupCreateGroup);
                SocketTools.sendString(normalizedName, socket);
                SocketTools.sendString(normalizedCategory, socket);
                SocketTools.sendString(normalizedDescription, socket);
                SocketTools.sendString(normalizedMethod, socket);

                bool success = SocketTools.receiveBool(socket);

                if (!success)
                    throw new InvalidOperationException("No se pudo crear el grupo.");

                string groupCode = SocketTools.receiveString(socket);
                LobbyHeader header = ReadLobbyHeader(socket, "La sesión de lobby no es válida.");

                return new GroupLobbyModel
                {
                    GroupCode = groupCode,
                    MemberCount = header.MemberCount,
                    HasStarted = header.HasStarted,
                    IsCurrentUserHost = true
                };
            });
        }
        finally
        {
            _socketLock.Release();
        }
    }

    public async Task<GroupLobbyModel> JoinGroupAsync(string groupCode)
    {
        string normalizedCode = NormalizeGroupCode(groupCode);
        ValidateGroupCode(normalizedCode);

        await _socketLock.WaitAsync();

        try
        {
            return await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                SocketTools.sendInt(socket, MainGroupJoinGroup);
                SocketTools.sendString(normalizedCode, socket);

                bool success = SocketTools.receiveBool(socket);

                if (!success)
                    throw new InvalidOperationException(
                        "No se pudo unir al grupo. Comprueba el código o si el grupo ya empezó.");

                LobbyHeader header = ReadLobbyHeader(socket, "La sesión de lobby no es válida.");

                return new GroupLobbyModel
                {
                    GroupCode = normalizedCode,
                    MemberCount = header.MemberCount,
                    HasStarted = header.HasStarted,
                    IsCurrentUserHost = false
                };
            });
        }
        finally
        {
            _socketLock.Release();
        }
    }

    #endregion

    #region Public API - Lobby Lifecycle

    public async Task<GroupLobbyModel> RefreshLobbyAsync(
        string groupCode,
        bool isCurrentUserHost)
    {
        string normalizedCode = NormalizeGroupCode(groupCode);
        ValidateGroupCode(normalizedCode);

        await _socketLock.WaitAsync();

        try
        {
            return await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine($"[GroupService] RefreshLobbyAsync -> Group={normalizedCode}");

                SocketTools.sendInt(socket, LobbyOptionRefresh);

                LobbyHeader header = ReadLobbyHeader(socket, "La sesión del grupo ya no existe.");

                Console.WriteLine(
                    $"[GroupService] RefreshLobbyAsync <- Members={header.MemberCount}, HasStarted={header.HasStarted}");

                return new GroupLobbyModel
                {
                    GroupCode = normalizedCode,
                    MemberCount = header.MemberCount,
                    HasStarted = header.HasStarted,
                    IsCurrentUserHost = isCurrentUserHost
                };
            });
        }
        finally
        {
            _socketLock.Release();
        }
    }

    public async Task LeaveGroupAsync(string groupCode)
    {
        string normalizedCode = NormalizeGroupCode(groupCode);
        ValidateGroupCode(normalizedCode);

        await _socketLock.WaitAsync();

        try
        {
            await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine($"[GroupService] LeaveGroupAsync -> Group={normalizedCode}");

                SocketTools.sendInt(socket, LobbyOptionExit);
            });
        }
        finally
        {
            _socketLock.Release();
        }
    }

    public async Task<bool> StartGroupAsync(
        string groupCode,
        bool isCurrentUserHost)
    {
        if (!isCurrentUserHost)
            return false;

        string normalizedCode = NormalizeGroupCode(groupCode);
        ValidateGroupCode(normalizedCode);

        await _socketLock.WaitAsync();

        try
        {
            return await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine($"[GroupService] StartGroupAsync -> Group={normalizedCode}");

                SocketTools.sendInt(socket, LobbyOptionStart);

                bool started = SocketTools.receiveBool(socket);

                /*
                 * Tras Start, el servidor vuelve al ciclo estándar del lobby
                 * y envía una nueva cabecera. Por eso se lee aquí.
                 */
                LobbyHeader header = ReadLobbyHeader(
                    socket,
                    "La sesión de lobby es inválida tras el Start.");

                Console.WriteLine(
                    $"[GroupService] Start={started}, SessionValid={header.SessionValid}, " +
                    $"Members={header.MemberCount}, HasStarted={header.HasStarted}");

                return started;
            });
        }
        finally
        {
            _socketLock.Release();
        }
    }

    #endregion

    #region Public API - Location And Results

    public async Task<MeetingResultModel?> SendLocationAndWaitResultAsync(
        string groupCode,
        double latitude,
        double longitude)
    {
        string normalizedCode = NormalizeGroupCode(groupCode);
        ValidateGroupCode(normalizedCode);
        ValidateCoordinates(latitude, longitude);

        await _socketLock.WaitAsync();

        try
        {
            /*
             * Este flujo mantiene el lock hasta completar SendLocation + PollResult.
             * Si otro método usara el socket durante este proceso, se podría leer
             * una respuesta que pertenece a otra operación.
             */
            MeetingResultModel? immediateResult = await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine(
                    $"[GroupService] SendLocation -> Group={normalizedCode}, " +
                    $"Lat={latitude}, Lon={longitude}");

                SocketTools.sendInt(socket, LobbyOptionSendLocation);
                SocketTools.sendDouble(socket, latitude);
                SocketTools.sendDouble(socket, longitude);

                return ReceiveMeetingResultJson(socket);
            });

            Console.WriteLine(
                $"[GroupService] SendLocation result <- Duration={immediateResult?.DurationSeconds}, " +
                $"HasValidRoute={immediateResult?.HasValidRoute}");

            if (immediateResult is not null && immediateResult.DurationSeconds != -1)
                return NormalizeResult(immediateResult, latitude, longitude);

            return await PollForResultInternalAsync(latitude, longitude);
        }
        finally
        {
            _socketLock.Release();
        }
    }

    #endregion

    #region Private Workflow Helpers

    private async Task<MeetingResultModel?> PollForResultInternalAsync(
        double latitude,
        double longitude)
    {
        for (int attempt = 1; attempt <= MaxPollAttempts; attempt++)
        {
            await Task.Delay(PollDelayMilliseconds);

            MeetingResultModel? pollResult = await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine(
                    $"[GroupService] Poll {attempt}/{MaxPollAttempts}: leyendo cabecera.");

                /*
                 * El servidor envía una cabecera al inicio de cada iteración del lobby.
                 * El cliente debe consumirla antes de enviar PollResult.
                 */
                LobbyHeader header = ReadLobbyHeader(socket, "La sesión del grupo ha finalizado.");

                Console.WriteLine(
                    $"[GroupService] Poll header <- Members={header.MemberCount}, HasStarted={header.HasStarted}");

                SocketTools.sendInt(socket, LobbyOptionPollResult);

                MeetingResultModel? result = ReceiveMeetingResultJson(socket);

                Console.WriteLine(
                    $"[GroupService] Poll result <- Duration={result?.DurationSeconds}, " +
                    $"HasValidRoute={result?.HasValidRoute}");

                return result;
            });

            if (pollResult is null)
                continue;

            if (pollResult.DurationSeconds == -1)
                continue;

            return NormalizeResult(pollResult, latitude, longitude);
        }

        throw new TimeoutException(
            "El cálculo está tardando demasiado. Inténtalo de nuevo.");
    }

    #endregion

    #region Private Socket Protocol Helpers

    private static LobbyHeader ReadLobbyHeader(Socket socket, string invalidSessionMessage)
    {
        /*
         * Orden exacto esperado desde el servidor:
         * 1. bool sessionValid
         * 2. int memberCount
         * 3. bool hasStarted
         */
        bool sessionValid = SocketTools.receiveBool(socket);

        if (!sessionValid)
            throw new InvalidOperationException(invalidSessionMessage);

        int memberCount = SocketTools.receiveInt(socket);
        bool hasStarted = SocketTools.receiveBool(socket);

        return new LobbyHeader(sessionValid, memberCount, hasStarted);
    }

    private static MeetingResultModel? ReceiveMeetingResultJson(Socket socket)
    {
        string json = SocketTools.receiveString(socket);

        Console.WriteLine($"[GroupService] JSON recibido: {json}");

        try
        {
            MeetingResultModel? result = JsonSerializer.Deserialize<MeetingResultModel>(
                json,
                JsonOptions);

            if (result is null)
                throw new InvalidOperationException("No se pudo deserializar el resultado de ruta.");

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"El servidor devolvió un resultado de ruta inválido: {json}", ex);
        }
    }

    private Socket GetAuthenticatedSocket()
    {
        Socket? socket = _authService.CurrentSocket;

        Console.WriteLine($"[GroupService] Socket null? {socket is null}");
        Console.WriteLine($"[GroupService] Socket connected? {socket?.Connected}");

        if (socket is null || !socket.Connected)
            throw new InvalidOperationException("No hay una sesión autenticada activa.");

        return socket;
    }

    #endregion

    #region Private Result Mapping Helpers

    private static MeetingResultModel NormalizeResult(
        MeetingResultModel result,
        double originLatitude,
        double originLongitude)
    {
        /*
         * Convenciones del servidor:
         * - DurationSeconds = -1 => resultado pendiente.
         * - DurationSeconds = -2 => error funcional en el cálculo.
         */
        if (result.DurationSeconds == -2)
            throw new InvalidOperationException(result.AddressText);

        if (result.OriginLatitude == 0 && result.OriginLongitude == 0)
        {
            result.OriginLatitude = originLatitude;
            result.OriginLongitude = originLongitude;
        }

        result.RoutePoints ??= BuildFallbackRoutePoints(result);
        result.Itinerary ??= BuildItinerary(result);

        if (string.IsNullOrWhiteSpace(result.MeetingPointName))
            result.MeetingPointName = "Punto de encuentro";

        if (string.IsNullOrWhiteSpace(result.AddressText))
        {
            result.AddressText = result.HasValidRoute
                ? "Ruta calculada correctamente"
                : "No se encontró una ruta válida";
        }

        if (string.IsNullOrWhiteSpace(result.DistanceText))
        {
            result.DistanceText = result.DistanceMeters > 0
                ? $"{result.DistanceMeters / 1000:0.0} km"
                : "Distancia no disponible";
        }

        if (string.IsNullOrWhiteSpace(result.FairnessText))
        {
            result.FairnessText = result.TransferCount == 0
                ? "Ruta directa sin transbordos"
                : $"Ruta con {result.TransferCount} transbordo{(result.TransferCount == 1 ? "" : "s")}";
        }

        return result;
    }

    private static TransitItineraryModel? BuildItinerary(MeetingResultModel result)
    {
        if (result.Legs is null || result.Legs.Count == 0)
            return null;

        return new TransitItineraryModel
        {
            DurationSeconds = result.DurationSeconds,
            DistanceMeters = result.DistanceMeters,
            TransfersCount = result.TransferCount,
            Legs = result.Legs
        };
    }

    private static List<RoutePointModel> BuildFallbackRoutePoints(MeetingResultModel result)
    {
        return new List<RoutePointModel>
        {
            new()
            {
                Latitude = result.OriginLatitude,
                Longitude = result.OriginLongitude
            },
            new()
            {
                Latitude = result.Latitude,
                Longitude = result.Longitude
            }
        };
    }

    #endregion

    #region Private Validation Helpers

    private static void ValidateCreateGroupPayload(
        string name,
        string description,
        string method,
        string category)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length < MinGroupNameLength ||
            name.Length > MaxGroupNameLength)
        {
            throw new ArgumentException("El nombre del grupo no tiene un formato válido.");
        }

        if (description.Length > MaxGroupDescriptionLength)
            throw new ArgumentException("La descripción del grupo es demasiado larga.");

        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("El método de cálculo no es válido.");

        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("La categoría del grupo no es válida.");
    }

    private static void ValidateGroupCode(string groupCode)
    {
        if (string.IsNullOrWhiteSpace(groupCode) ||
            groupCode.Length < MinGroupCodeLength ||
            groupCode.Length > MaxGroupCodeLength ||
            !groupCode.All(char.IsLetterOrDigit))
        {
            throw new ArgumentException("El código de grupo no tiene un formato válido.");
        }
    }

    private static void ValidateCoordinates(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) ||
            double.IsInfinity(latitude) ||
            double.IsNaN(longitude) ||
            double.IsInfinity(longitude) ||
            latitude < -90 ||
            latitude > 90 ||
            longitude < -180 ||
            longitude > 180)
        {
            throw new ArgumentException("Las coordenadas de ubicación no son válidas.");
        }
    }

    #endregion

    #region Private Normalization Helpers

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string NormalizeGroupCode(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .ToUpperInvariant();
    }

    #endregion

    #region Private Records

    private sealed record LobbyHeader(
        bool SessionValid,
        int MemberCount,
        bool HasStarted);

    #endregion
}