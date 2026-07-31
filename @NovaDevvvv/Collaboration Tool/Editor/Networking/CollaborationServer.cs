using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class CollaborationServer
{
    internal static int FindFreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    internal static async Task<WebSocket> AcceptWebSocket(TcpClient connection, CancellationToken token)
    {
        NetworkStream stream = connection.GetStream();
        byte[] buffer = new byte[16384];
        int length = 0;
        while (length < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer, length, buffer.Length - length, token).ConfigureAwait(false);
            if (count == 0) return null;
            length += count;
            string partial = Encoding.ASCII.GetString(buffer, 0, length);
            if (partial.IndexOf("\r\n\r\n", System.StringComparison.Ordinal) < 0) continue;

            string[] lines = partial.Split(new[] { "\r\n" }, System.StringSplitOptions.None);
            bool correctPath = lines.Length > 0 && lines[0].StartsWith("GET /collaboration/ ", System.StringComparison.Ordinal);
            string key = null;
            bool upgrade = false;
            bool connectionUpgrade = false;
            foreach (string line in lines)
            {
                if (line.StartsWith("Sec-WebSocket-Key:", System.StringComparison.OrdinalIgnoreCase))
                    key = line.Substring(line.IndexOf(':') + 1).Trim();
                else if (line.StartsWith("Upgrade:", System.StringComparison.OrdinalIgnoreCase))
                    upgrade = line.IndexOf("websocket", System.StringComparison.OrdinalIgnoreCase) >= 0;
                else if (line.StartsWith("Connection:", System.StringComparison.OrdinalIgnoreCase))
                    connectionUpgrade = line.IndexOf("upgrade", System.StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (!correctPath || !upgrade || !connectionUpgrade || string.IsNullOrEmpty(key))
            {
                byte[] rejected = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 400 Bad Request\r\nX-Collaboration-Server: true\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(rejected, 0, rejected.Length, token).ConfigureAwait(false);
                return null;
            }

            string accept;
            using (SHA1 sha = SHA1.Create())
                accept = System.Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(
                    key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " +
                accept + "\r\n\r\n");
            await stream.WriteAsync(response, 0, response.Length, token).ConfigureAwait(false);
            return WebSocket.CreateFromStream(stream, true, null, System.TimeSpan.FromSeconds(30));
        }
        return null;
    }
}
