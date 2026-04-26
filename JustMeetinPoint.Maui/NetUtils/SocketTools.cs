using System.Net;
using System.Net.Sockets;
using System.Text;

namespace JustMeetingPoint.Maui.NetUtils
{
    /// <summary>
    /// Utilidades de bajo nivel para el protocolo TCP del cliente MAUI.
    ///
    /// TCP no preserva mensajes completos. Solo transmite bytes.
    /// Por eso no se debe asumir que Socket.Send enviará todo el buffer
    /// ni que Socket.Receive leerá todo el mensaje.
    /// </summary>
    public static class SocketTools
    {
        public static byte[] ReceiveExact(Socket socket, int size)
        {
            if (size < 0)
                throw new InvalidOperationException($"Tamaño de lectura inválido: {size}");

            byte[] buffer = new byte[size];
            int totalRead = 0;

            while (totalRead < size)
            {
                int read = socket.Receive(
                    buffer,
                    totalRead,
                    size - totalRead,
                    SocketFlags.None);

                if (read == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                totalRead += read;
            }

            return buffer;
        }

        private static void SendExact(Socket socket, byte[] bytes)
        {
            int totalSent = 0;

            while (totalSent < bytes.Length)
            {
                int sent = socket.Send(
                    bytes,
                    totalSent,
                    bytes.Length - totalSent,
                    SocketFlags.None);

                if (sent == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                totalSent += sent;
            }
        }

        public static void sendBool(Socket socket, bool value)
        {
            SendExact(socket, BitConverter.GetBytes(value));
        }

        public static void sendBool(bool value, Socket socket)
        {
            sendBool(socket, value);
        }

        public static bool receiveBool(Socket socket)
        {
            byte[] bytes = ReceiveExact(socket, sizeof(bool));
            return BitConverter.ToBoolean(bytes, 0);
        }

        public static void sendInt(Socket socket, int value)
        {
            SendExact(socket, BitConverter.GetBytes(value));
        }

        public static void sendInt(int value, Socket socket)
        {
            sendInt(socket, value);
        }

        public static int receiveInt(Socket socket)
        {
            byte[] bytes = ReceiveExact(socket, sizeof(int));
            return BitConverter.ToInt32(bytes, 0);
        }

        public static void sendDouble(Socket socket, double value)
        {
            SendExact(socket, BitConverter.GetBytes(value));
        }

        public static void sendDouble(double value, Socket socket)
        {
            sendDouble(socket, value);
        }

        public static double receiveDouble(Socket socket)
        {
            byte[] bytes = ReceiveExact(socket, sizeof(double));
            return BitConverter.ToDouble(bytes, 0);
        }

        public static void sendString(string message, Socket socket)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);

            sendInt(socket, bytes.Length);
            SendExact(socket, bytes);
        }

        public static void sendString(Socket socket, string message)
        {
            sendString(message, socket);
        }

        public static string receiveString(Socket socket)
        {
            int length = receiveInt(socket);

            if (length < 0)
                throw new InvalidOperationException($"Longitud de string inválida: {length}");

            byte[] bytes = ReceiveExact(socket, length);
            return Encoding.UTF8.GetString(bytes);
        }

        public static void sendDate(DateOnly date, Socket socket)
        {
            sendString(date.ToString("yyyy-MM-dd"), socket);
        }

        public static Socket CreateSocketConnection(string ip, int port)
        {
            IPAddress address = IPAddress.Parse(ip);
            IPEndPoint endpoint = new IPEndPoint(address, port);

            Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(endpoint);

            return socket;
        }
    }
}