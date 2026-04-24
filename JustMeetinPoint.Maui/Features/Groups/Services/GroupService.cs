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

    private const int MainGroupCreateGroup = 1;
    private const int MainGroupJoinGroup = 2;

    private const int LobbyOptionRefresh = 1;
    private const int LobbyOptionExit = 2;
    private const int LobbyOptionStart = 3;
    private const int LobbyOptionSendLocation = 4;
    private const int LobbyOptionPollResult = 5;

    private const int PollDelayMilliseconds = 1500;
    private const int MaxPollAttempts = 40;

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

            // El servidor, tras crear el grupo, vuelve al bucle del lobby y envía
            // la cabecera estándar antes de esperar la siguiente opción.
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

    public async Task<GroupLobbyModel> JoinGroupAsync(string groupCode)
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

    public async Task<GroupLobbyModel> RefreshLobbyAsync(string groupCode, bool isCurrentUserHost)
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

            Console.WriteLine($"[GroupService] RefreshLobbyAsync <- MemberCount={memberCount}, HasStarted={hasStarted}");

            return new GroupLobbyModel
            {
                GroupCode = groupCode,
                MemberCount = memberCount,
                HasStarted = hasStarted,
                IsCurrentUserHost = isCurrentUserHost
            };
        });
    }

    public async Task LeaveGroupAsync(string groupCode)
    {
        await Task.Run(() =>
        {
            Socket socket = GetAuthenticatedSocket();
            SocketTools.sendInt(socket, LobbyOptionExit);
        });
    }

    /// <summary>
    /// Envía la opción Start al servidor y gestiona la respuesta.
    ///
    /// BUG CORREGIDO: el servidor, después de procesar Start (tanto si
    /// started==true como si started==false), vuelve al inicio del bucle
    /// del lobby y envía SIEMPRE la cabecera estándar (bool, int, bool)
    /// antes de esperar la siguiente opción del cliente.
    ///
    /// Si el cliente no leía esa cabecera cuando started==false,
    /// los 6 bytes quedaban en el buffer y la siguiente llamada
    /// del auto-refresh leía basura → desincronización permanente
    /// del protocolo. El socket quedaba irrecuperable.
    ///
    /// Fix: leemos la cabecera SIEMPRE, independientemente del valor de started.
    /// </summary>
    public async Task<bool> StartGroupAsync(string groupCode, bool isCurrentUserHost)
    {
        return await Task.Run(() =>
        {
            if (!isCurrentUserHost)
                return false;

            Socket socket = GetAuthenticatedSocket();

            Console.WriteLine($"[GroupService] StartGroupAsync -> Group={groupCode}");

            SocketTools.sendInt(socket, LobbyOptionStart);

            // 1. Resultado del Start
            bool started = SocketTools.receiveBool(socket);

            // 2. Cabecera estándar del lobby — SIEMPRE se consume.
            //    El servidor la envía en cada iteración del bucle,
            //    independientemente de si Start fue exitoso o no.
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

    /// <summary>
    /// Envía la ubicación del usuario y espera el resultado del cálculo de ruta.
    ///
    /// CAMBIO: el polling ya no usa Thread.Sleep (que bloquea un thread del pool)
    /// sino await Task.Delay, que libera el thread mientras espera.
    ///
    /// El envío inicial y cada iteración de polling se ejecutan en Task.Run
    /// porque SocketTools es síncrono/bloqueante.
    /// </summary>
    public async Task<MeetingResultModel?> SendLocationAndWaitResultAsync(
        string groupCode,
        double latitude,
        double longitude)
    {
        // ── Envío inicial de ubicación ─────────────────────────────────────────
        MeetingResultModel? immediateResult = await Task.Run(() =>
        {
            Socket socket = GetAuthenticatedSocket();

            Console.WriteLine($"[GroupService] Enviando ubicación: {latitude}, {longitude}");

            SocketTools.sendInt(socket, LobbyOptionSendLocation);
            SocketTools.sendDouble(socket, latitude);
            SocketTools.sendDouble(socket, longitude);

            return ReceiveMeetingResultJson(socket);
        });

        Console.WriteLine(
            $"[GroupService] Respuesta inicial => Duration={immediateResult?.DurationSeconds}, " +
            $"HasValidRoute={immediateResult?.HasValidRoute}");

        if (immediateResult is not null && immediateResult.DurationSeconds != -1)
            return NormalizeResult(immediateResult, latitude, longitude);

        // ── Polling asíncrono ──────────────────────────────────────────────────
        // DurationSeconds == -1 significa que el servidor aún espera ubicaciones
        // de otros miembros del grupo. Hacemos polling hasta obtener resultado.
        return await PollForResultAsync(latitude, longitude);
    }

    /// <summary>
    /// Bucle de polling que espera el resultado OTP del servidor.
    /// Usa await Task.Delay para no bloquear threads del pool durante la espera.
    /// </summary>
    private async Task<MeetingResultModel?> PollForResultAsync(double latitude, double longitude)
    {
        for (int attempt = 1; attempt <= MaxPollAttempts; attempt++)
        {
            // ✅ No bloquea el thread del pool mientras espera.
            await Task.Delay(PollDelayMilliseconds);

            MeetingResultModel? pollResult = await Task.Run(() =>
            {
                Socket socket = GetAuthenticatedSocket();

                Console.WriteLine(
                    $"[GroupService] Poll attempt {attempt}/{MaxPollAttempts}: leyendo cabecera...");

                // El servidor envía la cabecera antes de esperar la opción.
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
                    $"[GroupService] Poll result => Duration={result?.DurationSeconds}, " +
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lee el JSON enviado por el servidor y lo convierte a MeetingResultModel.
    /// </summary>
    private static MeetingResultModel? ReceiveMeetingResultJson(Socket socket)
    {
        string json = SocketTools.receiveString(socket);

        Console.WriteLine($"[GroupService] JSON recibido: {json}");

        MeetingResultModel? result = JsonSerializer.Deserialize<MeetingResultModel>(
            json,
            JsonOptions);

        if (result == null)
            throw new InvalidOperationException("No se pudo deserializar el resultado de ruta.");

        return result;
    }

    /// <summary>
    /// Normaliza el resultado recibido:
    /// - rellena origen si no viniera informado
    /// - construye RoutePoints de fallback
    /// - convierte Legs en Itinerary
    /// - aplica textos por defecto
    /// </summary>
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
            result.AddressText = result.HasValidRoute
                ? "Ruta calculada correctamente"
                : "No se encontró una ruta válida";

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
        if (result.Legs == null || result.Legs.Count == 0)
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
            new RoutePointModel
            {
                Latitude = result.OriginLatitude,
                Longitude = result.OriginLongitude
            },
            new RoutePointModel
            {
                Latitude = result.Latitude,
                Longitude = result.Longitude
            }
        };
    }

    private Socket GetAuthenticatedSocket()
    {
        Socket? socket = _authService.CurrentSocket;

        Console.WriteLine($"[GroupService] Socket null? {socket == null}");
        Console.WriteLine($"[GroupService] Socket connected? {socket?.Connected}");

        if (socket == null || !socket.Connected)
            throw new InvalidOperationException("No hay una sesión autenticada activa.");

        return socket;
    }
}