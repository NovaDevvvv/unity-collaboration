using System;
using System.Text.RegularExpressions;

internal static class TunnelManager
{
    internal static string SanitizeError(string message)
    {
        string value = message ?? "";
        if (value.IndexOf("Client.Timeout exceeded while awaiting headers", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("context deadline exceeded", StringComparison.OrdinalIgnoreCase) >= 0)
            return "The public link API did not respond before its timeout. The local server is running, but no public link could be issued. Wait a few minutes, or try another network/VPN and create the server again.";
        value = Regex.Replace(value, @"https://api\.trycloudflare\.com/tunnel", "the server API", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "cloudflared", "server service", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "cloudflare", "server", RegexOptions.IgnoreCase);
        return value.Length > 500 ? value.Substring(0, 500) : value;
    }
}
