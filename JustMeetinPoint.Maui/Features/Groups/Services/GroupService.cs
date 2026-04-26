using JustMeetinPoint.Maui.Features.Auth.Services;
using JustMeetinPoint.Maui.Features.Groups.Models;
using JustMeetinPoint.Maui.Features.Map.Models;
using JustMeetingPoint.Maui.NetUtils;
using System.Net.Sockets;
using System.Text.Json;

namespace JustMeetinPoint.Maui.Features.Groups.Services;

public class GroupService : IGroupService
{
    private readonly IAuthService _authService;

    /// <summary>
    /// El cliente mantiene un único socket TCP autenticado.
    ///
    /// Regla crítica:
    /// Un único socket no puede tener varias operaciones concurrentes de send/receive.
    ///
    /// Sin este lock puede pasar:
    /// - RefreshLobbyAsync consume bytes de SendLocation.
    /// - PollResult lee una cabecera que esperaba otro método.
    /// - SendLocation espera un JSON que ya fue consumido por otra operación.
    ///
    /// Esto explica el fallo intermitente: depende del timing entre tareas.
    /// </summary>
    private readonly SemaphoreSlim _socketLock = new(1, 1);

    private const int MainGroupCreateGroup = 1;
    private const int MainGroupJoinGroup = 2;

    private const int LobbyOptionRefresh = 1;
    private const int LobbyOptionExit = 2;
    private const int LobbyOptionStart = 3;
    private const int LobbyOptionSendLocation = 4;
    private const int LobbyOptionPollResult = 5;

    private const int PollDelayMilliseconds = 1500;

    /// <summary>
    /// 80 intentos * 1.5 segundos = 120 segundos.
    /// OTP en Docker + varios emuladores puede tardar bastante.
    /// </summary>
    private const int MaxPollAttempts = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GroupService(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<GroupLobbyModel> CreateGroupAsync(
        string name,
        string description,
        string method,
        string category)
    {
        await _socketLock.WaitAsync();

        try
        {
            return await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                SocketTools.sendInt(socket, MainGroupCreateGroup);
                SocketTools.sendString(name, socket);
                SocketTools.sendString(category, socket);
                SocketTools.sendString(description, socket);
                SocketTools.sendString(method, socket);

                bool success = SocketTools.receiveBool(socket);

                if (!success)
                    throw new InvalidOperationException("No se pudo crear el grupo.");

                string groupCode = SocketTools.receiveString(socket);

                bool sessionValid = SocketTools.receiveBool(socket);

                if (!sessionValid)
                    throw new InvalidOperationException("La sesión de lobby no es válida.");

                int memberCount = SocketTools.receiveInt(socket);
                bool hasStarted = SocketTools.receiveBool(socket);

                return new GroupLobbyModel
                {
                    GroupCode = groupCode,
                    MemberCount = memberCount,
                    HasStarted = hasStarted,
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
        await _socketLock.WaitAsync();

        try
        {
            return await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                SocketTools.sendInt(socket, MainGroupJoinGroup);
                SocketTools.sendString(groupCode, socket);

                bool success = SocketTools.receiveBool(socket);

                if (!success)
                    throw new InvalidOperationException("No se pudo unir al grupo.");

                bool sessionValid = SocketTools.receiveBool(socket);

                if (!sessionValid)
                    throw new InvalidOperationException("La sesión de lobby no es válida.");

                int memberCount = SocketTools.receiveInt(socket);
                bool hasStarted = SocketTools.receiveBool(socket);

                return new GroupLobbyModel
                {
                    GroupCode = groupCode,
                    MemberCount = memberCount,
                    HasStarted = hasStarted,
                    IsCurrentUserHost = false
                };
            });
        }
        finally
        {
            _socketLock.Release();
        }
    }

    public async Task<GroupLobbyModel> RefreshLobbyAsync(
        string groupCode,
        bool isCurrentUserHost)
    {
        await _socketLock.WaitAsync();

        try
        {
            return await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine($"[GroupService] RefreshLobbyAsync -> Group={groupCode}");

                SocketTools.sendInt(socket, LobbyOptionRefresh);

                bool sessionValid = SocketTools.receiveBool(socket);

                if (!sessionValid)
                    throw new InvalidOperationException("La sesión del grupo ya no existe.");

                int memberCount = SocketTools.receiveInt(socket);
                bool hasStarted = SocketTools.receiveBool(socket);

                Console.WriteLine(
                    $"[GroupService] RefreshLobbyAsync <- Members={memberCount}, HasStarted={hasStarted}");

                return new GroupLobbyModel
                {
                    GroupCode = groupCode,
                    MemberCount = memberCount,
                    HasStarted = hasStarted,
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
        await _socketLock.WaitAsync();

        try
        {
            await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine($"[GroupService] LeaveGroupAsync -> Group={groupCode}");

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

        await _socketLock.WaitAsync();

        try
        {
            return await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine($"[GroupService] StartGroupAsync -> Group={groupCode}");

                SocketTools.sendInt(socket, LobbyOptionStart);

                bool started = SocketTools.receiveBool(socket);

                /*
                 * Después de procesar Start, el servidor vuelve al inicio del bucle
                 * del lobby y envía SIEMPRE la cabecera estándar:
                 * bool sessionValid, int memberCount, bool hasStarted.
                 *
                 * Hay que consumirla aquí para mantener el protocolo sincronizado.
                 */
                bool sessionValid = SocketTools.receiveBool(socket);
                int memberCount = SocketTools.receiveInt(socket);
                bool hasStarted = SocketTools.receiveBool(socket);

                Console.WriteLine(
                    $"[GroupService] Start={started}, SessionValid={sessionValid}, " +
                    $"Members={memberCount}, HasStarted={hasStarted}");

                if (!sessionValid)
                    throw new InvalidOperationException("La sesión de lobby es inválida tras el Start.");

                return started;
            });
        }
        finally
        {
            _socketLock.Release();
        }
    }

    public async Task<MeetingResultModel?> SendLocationAndWaitResultAsync(
        string groupCode,
        double latitude,
        double longitude)
    {
        await _socketLock.WaitAsync();

        try
        {
            /*
             * Mantenemos el lock durante TODO el flujo final:
             * SendLocation + posible PollResult.
             *
             * Motivo:
             * Mientras este usuario espera su resultado final, ningún Refresh
             * debe consumir bytes del mismo socket.
             */
            MeetingResultModel? immediateResult = await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine(
                    $"[GroupService] SendLocation -> Group={groupCode}, " +
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

            /*
             * DurationSeconds = -1 significa:
             * "ubicación registrada, pero faltan ubicaciones de otros usuarios".
             *
             * A partir de aquí el cliente pregunta cada X segundos si el resultado
             * ya está disponible.
             */
            return await PollForResultInternalAsync(latitude, longitude);
        }
        finally
        {
            _socketLock.Release();
        }
    }

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

                bool sessionValid = SocketTools.receiveBool(socket);

                if (!sessionValid)
                    throw new InvalidOperationException("La sesión del grupo ha finalizado.");

                int memberCount = SocketTools.receiveInt(socket);
                bool hasStarted = SocketTools.receiveBool(socket);

                Console.WriteLine(
                    $"[GroupService] Poll header <- Members={memberCount}, HasStarted={hasStarted}");

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

        throw new InvalidOperationException(
            "El cálculo está tardando demasiado. Inténtalo de nuevo.");
    }

    private static MeetingResultModel? ReceiveMeetingResultJson(Socket socket)
    {
        string json = SocketTools.receiveString(socket);

        Console.WriteLine($"[GroupService] JSON recibido: {json}");

        MeetingResultModel? result = JsonSerializer.Deserialize<MeetingResultModel>(
            json,
            JsonOptions);

        if (result is null)
            throw new InvalidOperationException("No se pudo deserializar el resultado de ruta.");

        return result;
    }

    private static MeetingResultModel NormalizeResult(
        MeetingResultModel result,
        double originLatitude,
        double originLongitude)
    {
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

    private Socket GetAuthenticatedSocket()
    {
        Socket? socket = _authService.CurrentSocket;

        Console.WriteLine($"[GroupService] Socket null? {socket is null}");
        Console.WriteLine($"[GroupService] Socket connected? {socket?.Connected}");

        if (socket is null || !socket.Connected)
            throw new InvalidOperationException("No hay una sesión autenticada activa.");

        return socket;
    }
}