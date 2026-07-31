using UnityEditor;

internal static class CollaborationSettings
{
    internal const string InstalledCommitKey = "NovaDev.UnityCollaboration.InstalledCommit";
    internal const string GitHubPatKey = "NovaDev.UnityCollaboration.GitHubPat";

    internal static string LoadGitHubPat() => EditorPrefs.GetString(GitHubPatKey, "");
    internal static void SaveGitHubPat(string value) => EditorPrefs.SetString(GitHubPatKey, value ?? "");
}
