using JustMeetingPoint.Maui.NetUtils;
using JustMeetinPoint.Maui.Features.Auth.Dtos;
using System.Net.Sockets;

namespace JustMeetinPoint.Maui.Features.Auth.Services;

public class SocketAuthService : IAuthService
{
    // ✅ CORRECTO: centralizar la configuración de red en constantes.
    // Así, si cambias de servidor, cambias UNA línea, no buscas por todo el código.
    // El siguiente paso ideal sería leer esto desde appsettings.json o Preferences.
    private const string ServerIp = "192.168.111.29";
    private const int ServerPort = 1001;

    public Socket? CurrentSocket { get; private set; }

    // IsAuthenticated: propiedad derivada, no almacena estado propio.
    // Consulta directamente el socket — si está null o desconectado, no estás autenticado.
    public bool IsAuthenticated => CurrentSocket != null && CurrentSocket.Connected;

    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request)
    {
        return await Task.Run(() =>
        {
            // Task.Run: ejecuta en un hilo de background.
            // Los sockets son bloqueantes — si corrieran en el hilo de UI,
            // la app se congela mientras espera respuesta del servidor.
            Socket? socket = null;

            try
            {
                Console.WriteLine("Register: intentando conectar...");
                socket = SocketTools.CreateSocketConnection(ServerIp, ServerPort);
                Console.WriteLine("Register: conexión OK");

                SocketTools.sendInt(socket, 2); // Opcode MainUser.Register
                Console.WriteLine("Register: opción enviada");

                SocketTools.sendString(request.Username, socket);
                SocketTools.sendString(request.Email, socket);
                SocketTools.sendString(request.Password, socket);
                SocketTools.sendString(request.BirthDate.ToString("yyyy-MM-dd"), socket);
                Console.WriteLine("Register: datos enviados");

                bool success = SocketTools.receiveBool(socket);
                Console.WriteLine($"Register: respuesta recibida = {success}");

                return new RegisterResponseDto
                {
                    Success = success,
                    Message = success
                        ? "Registrado correctamente."
                        : "No se pudo registrar el usuario."
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Register: error -> {ex}");

                return new RegisterResponseDto
                {
                    Success = false,
                    Message = $"Error de conexión: {ex.Message}"
                };
            }
            finally
            {
                // Register no guarda el socket: cierra siempre.
                // Solo Login persiste el socket para la sesión.
                socket?.Close();
            }
        });
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        return await Task.Run(() =>
        {
            Socket? socket = null;

            try
            {
                Console.WriteLine("Login: intentando conectar...");
                socket = SocketTools.CreateSocketConnection(ServerIp, ServerPort);
                Console.WriteLine("Login: conexión OK");

                SocketTools.sendInt(socket, 1); // Opcode MainUser.Login
                Console.WriteLine("Login: opción enviada");

                SocketTools.sendString(request.Email, socket);
                Console.WriteLine("Login: email enviado");

                SocketTools.sendString(request.Password, socket);
                Console.WriteLine("Login: password enviada");

                bool success = SocketTools.receiveBool(socket);
                Console.WriteLine($"Login: respuesta recibida = {success}");

                if (!success)
                {
                    socket.Close();
                    return new LoginResponseDto
                    {
                        Success = false,
                        Message = "Correo o contraseña incorrectos."
                    };
                }

                // ✅ Solo en Login exitoso se guarda el socket.
                // Este socket se reutiliza en GroupService para todas
                // las operaciones de grupo durante la sesión.
                CurrentSocket = socket;

                Console.WriteLine("Login OK. Socket autenticado guardado.");
                Console.WriteLine($"Socket connected = {CurrentSocket?.Connected}");

                return new LoginResponseDto
                {
                    Success = true,
                    Message = "Inicio de sesión correcto."
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login: error -> {ex}");

                try { socket?.Close(); } catch { }

                return new LoginResponseDto
                {
                    Success = false,
                    Message = $"Error de conexión: {ex.Message}"
                };
            }
        });
    }

    public void Logout()
    {
        try { CurrentSocket?.Shutdown(SocketShutdown.Both); } catch { }
        try { CurrentSocket?.Close(); } catch { }
        CurrentSocket = null;
    }
}