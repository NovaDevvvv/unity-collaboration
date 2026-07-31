using UnityEngine;

internal sealed class CollaborationPlayer
{
    public string Id;
    public string Name;
    public string SceneName;
    public bool IsHost;
    public int PingMs = -1;
    public float CursorX = -1f;
    public float CursorY = -1f;
    public Color Color;
}
