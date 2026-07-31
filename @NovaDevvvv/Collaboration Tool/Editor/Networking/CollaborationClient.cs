using System;

internal static class CollaborationClient
{
    internal static Uri MakeWebSocketUri(string link)
    {
        string value = link.Trim().TrimEnd('/');
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = "wss://" + value.Substring(8);
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = "ws://" + value.Substring(7);
        else if (!value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
                 !value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) value = "wss://" + value;
        return new Uri(value + "/collaboration/");
    }
}
