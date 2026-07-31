using UnityEngine;

internal sealed class CollaborationPlayer
{
    public string Id;
    public string Name;
    public string SceneName;
    public bool IsHost;
    public int PingMs = -1;
    public Vector3 CameraPosition;
    public Quaternion CameraRotation = Quaternion.identity;
    public bool HasCameraPose;
    public Color Color;
}
