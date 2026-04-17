using JustMeetinPoint.Maui.Features.Auth.Dtos;
using System.Net.Sockets;
using JustMeetingPoint.Maui.NetUtils;

namespace JustMeetinPoint.Maui.Features.Auth.Services;

public class SocketAuthService : IAuthService
{
    private readonly string _serverIp = "192.168.1.36";
    private readonly int _serverPort = 1001;

    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request)
    {
        return await Task.Run(() =>
        {
            Socket? socket = null;

            try
            {
                Console.WriteLine("Conectando al servidor...");
                socket = SocketTools.CreateSocketConnection(_serverIp, _serverPort);
                Console.WriteLine("Conexión OK");

                SocketTools.sendInt(socket, 2); // MainUser.Register
                Console.WriteLine("Opción Register enviada");

                SocketTools.sendString(request.Username, socket);
                SocketTools.sendString(request.Email, socket);
                SocketTools.sendString(request.Password, socket);
                SocketTools.sendString(request.BirthDate.ToString("yyyy-MM-dd"), socket);
                Console.WriteLine("Datos enviados");

                bool success = SocketTools.receiveBool(socket);
                Console.WriteLine($"Respuesta recibida: {success}");

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
                Console.WriteLine($"Error en SocketAuthService: {ex}");

                return new RegisterResponseDto
                {
                    Success = false,
                    Message = $"Error de conexión: {ex.Message}"
                };
            }
            finally
            {
                socket?.Close();
            }
        });
    }
}