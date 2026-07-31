using System;

[Serializable]
internal sealed class CollaborationMessage
{
    public string type;
    public string id;
    public string name;
    public string text;
    public float x;
    public float y;
    public long stamp;
    public string[] ids;
    public string[] names;
    public string[] scenes;
    public string scene;
    public string path;
    public string path2;
    public string data;
}
