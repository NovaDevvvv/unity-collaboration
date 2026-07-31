internal static class UpdateService
{
    internal const string InstallDirectory = "Assets/@NovaDevvvv/Collaboration Tool";
    internal const string LatestCommitUrl =
        "https://api.github.com/repos/novadevvvv/unity-collaboration/commits/main";
    internal const string RawToolUrl =
        "https://raw.githubusercontent.com/novadevvvv/unity-collaboration/{0}/%40NovaDevvvv/Collaboration%20Tool/{1}";

    internal static readonly string[] ToolFiles =
    {
        "Editor/CollaborationWindow.cs",
        "Editor/Core/CollaborationSession.cs",
        "Editor/Core/CollaborationPlayer.cs",
        "Editor/Core/CollaborationMessage.cs",
        "Editor/Networking/CollaborationServer.cs",
        "Editor/Networking/CollaborationClient.cs",
        "Editor/Networking/TunnelManager.cs",
        "Editor/Networking/NetworkDiagnostics.cs",
        "Editor/Networking/HostHeaderProxy.cs",
        "Editor/Sync/AssetSyncService.cs",
        "Editor/Updates/UpdateService.cs",
        "Editor/UI/CollaborationStyles.cs",
        "Editor/Settings/CollaborationSettings.cs"
    };
}
