using UnityEditor;
using UnityEngine;

internal static class CollaborationStyles
{
    internal static GUIStyle Title() => new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, fixedHeight = 25f };
    internal static GUIStyle Subtitle() => new GUIStyle(EditorStyles.label) { wordWrap = true };
    internal static GUIStyle Centered(int size, bool bold = false) => new GUIStyle(bold ? EditorStyles.boldLabel : EditorStyles.label)
    {
        alignment = TextAnchor.MiddleCenter,
        fontSize = size,
        wordWrap = true
    };
}
