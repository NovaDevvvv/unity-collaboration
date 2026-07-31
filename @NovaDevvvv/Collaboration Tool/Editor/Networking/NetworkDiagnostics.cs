using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;

internal static class NetworkDiagnostics
{
    internal static string Describe(Exception exception)
    {
        var details = new List<string>();
        for (Exception current = exception; current != null && details.Count < 6; current = current.InnerException)
        {
            string message = string.IsNullOrWhiteSpace(current.Message) ? current.GetType().Name : current.Message.Trim();
            WebException web = current as WebException;
            if (web != null) message += " [Network status: " + web.Status + "]";
            WebSocketException socket = current as WebSocketException;
            if (socket != null) message += " [WebSocket error: " + socket.NativeErrorCode + "]";
            System.Net.Sockets.SocketException tcp = current as System.Net.Sockets.SocketException;
            if (tcp != null)
            {
                message += " [Socket error: " + tcp.SocketErrorCode + "]";
                if (tcp.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound ||
                    tcp.SocketErrorCode == System.Net.Sockets.SocketError.TryAgain)
                    message += " The server address exists publicly, but this computer's DNS service could not resolve it. Try DNS 1.1.1.1 or 8.8.8.8, or use another network/VPN.";
            }
            if (!details.Contains(message)) details.Add(message);
        }
        return string.Join("\nCaused by: ", details.ToArray());
    }
}
