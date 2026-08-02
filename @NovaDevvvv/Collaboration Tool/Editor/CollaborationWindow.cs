using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

public sealed class CollaborationTool : EditorWindow
{
    private enum Page { Home, Join, Create, Session, Settings }

    private static readonly CollaborationSession Session = new CollaborationSession();
    internal static CollaborationSession SharedSession => Session;
    private Page page;
    private string playerName = "";
    private string serverLink = "";
    private string chatText = "";
    private string githubPatInput = "";
    private bool homeMenuOpen;
    private string selectedTheme = "Extra Dark";
    private Vector2 chatScroll;
    private Vector2 playersScroll;
    private int sessionTab;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle centeredLabelStyle;
    private GUIStyle centeredDetailStyle;
    private GUIStyle panelStyle;
    private GUIStyle tabStyle;
    private GUIStyle tabActiveStyle;
    private GUIStyle tabContainerStyle;
    private GUIStyle leaveStyle;
    private GUIStyle fieldStyle;
    private GUIStyle flatButtonStyle;
    private GUIStyle chatPanelStyle;
    private GUIStyle sendStyle;
    private Color WindowBackground => selectedTheme == "Light" ? new Color32(226, 228, 233, 255) :
        selectedTheme == "Dark" ? new Color32(43, 46, 53, 255) :
        selectedTheme == "Midnight" ? new Color32(8, 16, 34, 255) : new Color32(23, 25, 29, 255);

    [MenuItem("Collaborate/Window")]
    private static void OpenWindow()
    {
        CollaborationTool window = GetWindow<CollaborationTool>();
        window.titleContent = new GUIContent("Collaboration");
        window.minSize = new Vector2(390f, 440f);
        window.Show();
    }

    [MenuItem("Collaborate/Refresh Version")]
    private static void RefreshVersion()
    {
        OpenWindow();
        Session.CheckForUpdatesNow();
    }

    private void OnEnable()
    {
        selectedTheme = EditorPrefs.GetString("NovaDev.UnityCollaboration.Theme", "Extra Dark");
        ResetStyles();
        titleContent = new GUIContent("Collaboration");
        Session.Changed -= Repaint;
        Session.Changed += Repaint;
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyItemGUI;
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        if (Session.Connected)
            page = Page.Session;
    }

    private void ResetStyles()
    {
        titleStyle = null;
        subtitleStyle = null;
        centeredLabelStyle = null;
        centeredDetailStyle = null;
        panelStyle = null;
        tabStyle = null;
        tabActiveStyle = null;
        tabContainerStyle = null;
        leaveStyle = null;
        fieldStyle = null;
        flatButtonStyle = null;
        chatPanelStyle = null;
        sendStyle = null;
    }

    private void OnDisable()
    {
        Session.Changed -= Repaint;
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyItemGUI;
    }

    private void OnGUI()
    {
        OnGlobalEditorEvent();
        EnsureStyles();
        EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), WindowBackground);
        if (Session.ShowingUpdateCheck)
        {
            DrawBusyScreen("Checking For Updates…", "Looking for a newer version.");
            return;
        }
        if (Session.Updating)
        {
            DrawBusyScreen("Installing Update", "v" + Session.UpdateHash);
            return;
        }
        if (!Session.IsHost && Session.Connecting)
        {
            DrawBusyScreen("Connecting to Server…", Session.ConnectionDetail, true);
            return;
        }
        if (Session.IsHost && (Session.Connecting || (Session.Connected && string.IsNullOrEmpty(Session.ShareLink))))
        {
            DrawCreatingServer();
            return;
        }

        EditorGUILayout.Space(8f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope())
            {
                if (Session.Connected || Session.Connecting)
                    DrawSession();
                else
                {
                    EditorGUILayout.LabelField("Collaboration", titleStyle);
                    EditorGUILayout.LabelField("Create together, wherever you are", subtitleStyle);
                    EditorGUILayout.Space(12f);
                    DrawSetup();
                }
            }
            GUILayout.Space(6f);
        }
    }

    private void EnsureStyles()
    {
        if (titleStyle != null && panelStyle != null && tabStyle != null && tabActiveStyle != null &&
            tabContainerStyle != null && leaveStyle != null && fieldStyle != null && flatButtonStyle != null &&
            chatPanelStyle != null && sendStyle != null) return;
        titleStyle = CollaborationStyles.Title();
        subtitleStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.68f, 0.7f, 0.74f) : new Color(0.35f, 0.37f, 0.4f) }
        };
        centeredLabelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
        centeredDetailStyle = new GUIStyle(subtitleStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
        panelStyle = new GUIStyle
        {
            normal = { background = SolidTexture(new Color32(34, 37, 43, 255)) },
            padding = new RectOffset(14, 14, 14, 14),
            margin = new RectOffset(0, 0, 0, 0)
        };
        chatPanelStyle = new GUIStyle(panelStyle) { padding = new RectOffset(4, 4, 12, 12) };
        tabStyle = new GUIStyle(EditorStyles.miniButton)
        {
            normal = { background = SolidTexture(new Color32(17, 19, 24, 255)), textColor = new Color32(155, 163, 175, 255) },
            hover = { background = SolidTexture(new Color32(34, 37, 43, 255)), textColor = Color.white },
            fixedHeight = 30f,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(4, 4, 2, 2),
            border = new RectOffset(0, 0, 0, 0),
            contentOffset = Vector2.zero,
            clipping = TextClipping.Overflow
        };
        tabActiveStyle = new GUIStyle(tabStyle)
        {
            normal = { background = SolidTexture(new Color32(41, 45, 52, 255)), textColor = new Color32(242, 244, 247, 255) }
        };
        tabContainerStyle = new GUIStyle
        {
            normal = { background = SolidTexture(new Color32(17, 19, 24, 255)) },
            padding = new RectOffset(3, 3, 3, 3)
        };
        flatButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            normal = { background = SolidTexture(new Color32(41, 45, 52, 255)), textColor = new Color32(242, 244, 247, 255) },
            hover = { background = SolidTexture(new Color32(52, 58, 68, 255)), textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(12, 12, 2, 2),
            border = new RectOffset(0, 0, 0, 0),
            contentOffset = Vector2.zero,
            clipping = TextClipping.Overflow,
            fixedHeight = 30f,
            stretchHeight = false
        };
        leaveStyle = new GUIStyle(flatButtonStyle)
        {
            normal = { background = SolidTexture(new Color32(41, 45, 52, 255)), textColor = new Color32(242, 244, 247, 255) },
            hover = { background = SolidTexture(new Color32(138, 42, 51, 255)), textColor = Color.white },
            fontStyle = FontStyle.Bold,
            fixedHeight = 32f
        };
        sendStyle = new GUIStyle(flatButtonStyle)
        {
            normal = { background = SolidTexture(new Color32(46, 116, 235, 255)), textColor = Color.white },
            hover = { background = SolidTexture(new Color32(62, 132, 250, 255)), textColor = Color.white }
        };
        fieldStyle = new GUIStyle(EditorStyles.textField)
        {
            normal = { background = SolidTexture(new Color32(21, 23, 27, 255)), textColor = new Color32(242, 244, 247, 255) },
            focused = { background = SolidTexture(new Color32(21, 23, 27, 255)), textColor = Color.white },
            padding = new RectOffset(10, 10, 7, 7),
            fixedHeight = 30f,
            border = new RectOffset(0, 0, 0, 0)
        };
    }

    private static Texture2D SolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void DrawCreatingServer()
    {
        GUILayout.FlexibleSpace();
        int frame = (int)(EditorApplication.timeSinceStartup * 10d) % 12;
        GUIContent spinner = EditorGUIUtility.IconContent("WaitSpin" + frame.ToString("00"));
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(spinner, GUILayout.Width(32f), GUILayout.Height(32f));
            GUILayout.FlexibleSpace();
        }
        GUILayout.Space(12f);
        EditorGUILayout.LabelField("Creating Server…", centeredLabelStyle, GUILayout.Height(24f));
        EditorGUILayout.LabelField(string.IsNullOrEmpty(Session.ConnectionDetail) ? "Your share link will appear in a moment." : Session.ConnectionDetail,
            centeredDetailStyle, GUILayout.Height(42f));
        if (!string.IsNullOrEmpty(Session.Error))
        {
            GUILayout.Space(14f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(24f);
                EditorGUILayout.HelpBox(Session.Error, MessageType.Error);
                GUILayout.Space(24f);
            }
        }
        GUILayout.Space(8f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(120f), GUILayout.Height(30f)))
            {
                Session.Close();
                page = Page.Home;
                GUIUtility.ExitGUI();
            }
            GUILayout.FlexibleSpace();
        }
        GUILayout.FlexibleSpace();
        Repaint();
    }

    private void DrawBusyScreen(string heading, string detail, bool canCancel = false)
    {
        GUILayout.FlexibleSpace();
        int frame = (int)(EditorApplication.timeSinceStartup * 10d) % 12;
        GUIContent spinner = EditorGUIUtility.IconContent("WaitSpin" + frame.ToString("00"));
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(spinner, GUILayout.Width(32f), GUILayout.Height(32f));
            GUILayout.FlexibleSpace();
        }
        GUILayout.Space(12f);
        EditorGUILayout.LabelField(heading, centeredLabelStyle, GUILayout.Height(24f));
        EditorGUILayout.LabelField(detail ?? "", centeredDetailStyle, GUILayout.Height(42f));
        if (canCancel)
        {
            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(120f), GUILayout.Height(30f)))
                {
                    Session.Close();
                    page = Page.Home;
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
            }
        }
        GUILayout.FlexibleSpace();
        Repaint();
    }

    private void DrawSetup()
    {
        if (page == Page.Session)
            page = Page.Home;

        if (page == Page.Home)
        {
            DrawHomeHamburger();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Start a shared workspace", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Host a new session or join with a server link.", EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(12f);
                if (GUILayout.Button("Create Server", GUILayout.Height(38f))) page = Page.Create;
                GUILayout.Space(3f);
                if (GUILayout.Button("Join Server", GUILayout.Height(34f))) page = Page.Join;
                GUILayout.Space(8f);
            }
            DrawError();
            GUILayout.FlexibleSpace();
            return;
        }

        if (page == Page.Settings)
        {
            DrawSettings();
            return;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField(page == Page.Create ? "Create a server" : "Join a server", EditorStyles.boldLabel);
            GUILayout.Space(8f);
            playerName = EditorGUILayout.TextField("Your name", playerName);
            if (page == Page.Join)
                serverLink = EditorGUILayout.TextField("Server link", serverLink);

            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(playerName) ||
                                               (page == Page.Join && string.IsNullOrWhiteSpace(serverLink))))
            {
                if (GUILayout.Button(page == Page.Create ? "Create Server" : "Join Server", GUILayout.Height(34f)))
                {
                    sessionTab = 0;
                    if (page == Page.Create)
                        Session.Create(playerName.Trim());
                    else
                        Session.Join(playerName.Trim(), serverLink.Trim());
                    page = Page.Session;
                }
            }
            if (GUILayout.Button(page == Page.Create ? "Cancel" : "Back", GUILayout.Height(24f)))
            {
                Session.Close();
                page = Page.Home;
            }
            GUILayout.Space(4f);
        }
        DrawError();
    }

    private void DrawHomeHamburger()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("☰", flatButtonStyle, GUILayout.Width(34f), GUILayout.Height(30f)))
                homeMenuOpen = !homeMenuOpen;
        }
        if (!homeMenuOpen) return;
        using (new EditorGUILayout.VerticalScope(panelStyle))
        {
            if (GUILayout.Button("Settings", flatButtonStyle))
            {
                githubPatInput = Session.GitHubPat;
                page = Page.Settings;
                homeMenuOpen = false;
            }
            using (new EditorGUI.DisabledScope(Session.CheckingForUpdate || Session.Updating))
            {
                if (GUILayout.Button(Session.CheckingForUpdate ? "Checking…" : "Check for Updates", flatButtonStyle))
                {
                    Session.CheckForUpdatesNow();
                    homeMenuOpen = false;
                }
            }
        }
        GUILayout.Space(8f);
    }

    private void DrawSettings()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Theme", EditorStyles.miniBoldLabel);
            string[] themes = { "Extra Dark", "Dark", "Light", "Midnight" };
            int themeIndex = Mathf.Max(0, Array.IndexOf(themes, selectedTheme));
            int nextThemeIndex = EditorGUILayout.Popup(themeIndex, themes);
            if (nextThemeIndex != themeIndex)
            {
                selectedTheme = themes[nextThemeIndex];
                EditorPrefs.SetString("NovaDev.UnityCollaboration.Theme", selectedTheme);
                ResetStyles();
            }
            GUILayout.Space(10f);
            EditorGUILayout.LabelField("GitHub personal access token", EditorStyles.miniBoldLabel);
            githubPatInput = EditorGUILayout.PasswordField(githubPatInput ?? "");
            EditorGUILayout.LabelField(
                "Optional. Used to authorize update checks and avoid anonymous API limits. A fine-grained, read-only token is sufficient.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox("The token is stored locally in Unity EditorPrefs and is not encrypted.", MessageType.Warning);
            EditorGUILayout.LabelField(Session.HasGitHubPat ? "Authorized requests enabled" : "Using anonymous requests",
                EditorStyles.miniLabel);
            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save", GUILayout.Height(28f)))
                    Session.SetGitHubPat(githubPatInput);
                using (new EditorGUI.DisabledScope(!Session.HasGitHubPat))
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(80f), GUILayout.Height(28f)))
                    {
                        githubPatInput = "";
                        Session.SetGitHubPat("");
                    }
                }
            }
            if (GUILayout.Button("Back", GUILayout.Height(24f))) page = Page.Home;
            GUILayout.Space(4f);
        }
    }

    private void DrawSession()
    {
        using (new EditorGUILayout.HorizontalScope(tabContainerStyle, GUILayout.Height(36f)))
        {
            if (GUILayout.Button("Players", sessionTab == 0 ? tabActiveStyle : tabStyle, GUILayout.ExpandWidth(true))) sessionTab = 0;
            if (GUILayout.Button("Chat", sessionTab == 1 ? tabActiveStyle : tabStyle, GUILayout.ExpandWidth(true))) sessionTab = 1;
        }
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Collaboration", titleStyle);
        EditorGUILayout.LabelField("Realtime Editing", subtitleStyle);
        EditorGUILayout.Space(12f);

        if (Session.Connecting)
        {
            EditorGUILayout.HelpBox(Session.Status, MessageType.Info);
            GUILayout.Space(6f);
        }
        if (sessionTab == 0)
        {
            if (Session.IsHost && !string.IsNullOrEmpty(Session.ShareLink))
            {
                using (new EditorGUILayout.VerticalScope(panelStyle))
                {
                    EditorGUILayout.LabelField("Invite others", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Share this server link with collaborators.", EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(6f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PasswordField(Session.ShareLink, fieldStyle);
                        if (GUILayout.Button("Copy", flatButtonStyle, GUILayout.Width(58f), GUILayout.Height(30f)))
                            EditorGUIUtility.systemCopyBuffer = Session.ShareLink;
                    }
                }
                EditorGUILayout.Space(12f);
                Rect divider = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(new Rect(divider.x + 10f, divider.y, Mathf.Max(0f, divider.width - 20f), 1f),
                    EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.1f));
                EditorGUILayout.Space(12f);
            }
            playersScroll = EditorGUILayout.BeginScrollView(playersScroll, GUILayout.ExpandHeight(true));
            foreach (CollaborationPlayer player in Session.Players.ToArray())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(18f));
                    EditorGUI.DrawRect(new Rect(swatch.x, swatch.y + 4f, 10f, 10f), player.Color);
                    string scene = string.IsNullOrEmpty(player.SceneName) ? "Unknown scene" : player.SceneName;
                    GUILayout.Label(player.Name + "  •  " + scene, GUILayout.ExpandWidth(true));
                    GUILayout.Label(player.PingMs < 0 ? "— ms" : player.PingMs + " ms", EditorStyles.miniLabel, GUILayout.Width(52f));
                    if (Session.IsHost && !player.IsHost && GUILayout.Button("Kick", flatButtonStyle, GUILayout.Width(48f), GUILayout.Height(24f)))
                        Session.Kick(player.Id);
                }
                Rect playerRow = GUILayoutUtility.GetLastRect();
                if (player.Id != Session.LocalId && Event.current.type == EventType.ContextClick &&
                    playerRow.Contains(Event.current.mousePosition))
                {
                    CollaborationPlayer selectedPlayer = player;
                    GenericMenu context = new GenericMenu();
                    if (selectedPlayer.HasCameraPose)
                        context.AddItem(new GUIContent("Go To"), false, () => GoToPlayer(selectedPlayer));
                    else context.AddDisabledItem(new GUIContent("Go To"));
                    if (Session.IsHost && !selectedPlayer.IsHost)
                        context.AddItem(new GUIContent("Kick"), false, () => Session.Kick(selectedPlayer.Id));
                    context.ShowAsContext();
                    Event.current.Use();
                }
                GUILayout.Space(3f);
            }
            EditorGUILayout.EndScrollView();
        }
        else
        {
            chatScroll = EditorGUILayout.BeginScrollView(chatScroll, chatPanelStyle, GUILayout.ExpandHeight(true));
            foreach (string line in Session.Chat.ToArray())
            {
                bool own = Session.IsLocalChatLine(line);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (own) GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(Session.FormatChatLine(line), EditorStyles.wordWrappedLabel,
                        GUILayout.MaxWidth(position.width * 0.9f));
                    if (!own) GUILayout.FlexibleSpace();
                }
                GUILayout.Space(3f);
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.SetNextControlName("CollaborationChat");
                chatText = EditorGUILayout.TextField(chatText, fieldStyle);
                bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
                             GUI.GetNameOfFocusedControl() == "CollaborationChat";
                if ((GUILayout.Button("Send", sendStyle, GUILayout.Width(55f), GUILayout.Height(30f)) || enter) && !string.IsNullOrWhiteSpace(chatText))
                {
                    Session.SendChat(chatText.Trim());
                    chatText = "";
                    GUI.FocusControl("CollaborationChat");
                    Event.current.Use();
                }
            }
        }

        DrawError();
        GUILayout.FlexibleSpace();
        string button = Session.IsHost ? "Close Server" : "Leave Server";
        if (GUILayout.Button(button, leaveStyle, GUILayout.Height(32f)))
        {
            if (Session.IsHost) Session.EndRemoteSession();
            else if (Session.LeaveAndOpenEmptyScene()) page = Page.Home;
        }
        EditorGUILayout.Space(5f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Ping " + (Session.PingMs < 0 ? "—" : Session.PingMs.ToString()) + " ms", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Session.PacketsSent + " packets sent  •  " + Session.PacketsReceived + " received", EditorStyles.miniLabel);
        }
    }

    private static void GoToPlayer(CollaborationPlayer player)
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null || player == null || !player.HasCameraPose) return;
        view.pivot = player.CameraPosition;
        view.rotation = player.CameraRotation;
        view.Repaint();
    }

    private void DrawError()
    {
        if (!string.IsNullOrEmpty(Session.Error))
        {
            EditorGUILayout.HelpBox(Session.Error, MessageType.Error);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy Error Details", GUILayout.Width(130f)))
                    EditorGUIUtility.systemCopyBuffer = Session.Error;
                GUILayout.FlexibleSpace();
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.0") + " KB";
        return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        OnGlobalEditorEvent();
        if (!Session.Connected) return;
        if (sceneView.camera != null)
            Session.UpdateCameraPose(sceneView.camera.transform.position, sceneView.camera.transform.rotation);
        foreach (CollaborationPlayer player in Session.Players.ToArray())
        {
            if (!string.IsNullOrEmpty(player.SelectedObjectId))
            {
                GameObject selected = Session.ResolveGameObject(player.SelectedObjectId);
                if (selected != null && TryGetBounds(selected, out Bounds bounds))
                {
                    Color old = Handles.color;
                    Handles.color = new Color(player.Color.r, player.Color.g, player.Color.b, 0.85f);
                    Handles.DrawWireCube(bounds.center, bounds.size * 1.015f);
                    Handles.color = old;
                }
            }
            if (player.Id == Session.LocalId || !player.HasCameraPose) continue;
            float size = HandleUtility.GetHandleSize(player.CameraPosition) * 0.7f;
            Color previous = Handles.color;
            Handles.color = player.Color;
            Handles.ArrowHandleCap(0, player.CameraPosition, player.CameraRotation, size, EventType.Repaint);
            Handles.Label(player.CameraPosition + Vector3.up * size * 0.35f, player.Name, EditorStyles.boldLabel);
            Handles.color = previous;
        }
    }

    private static bool TryGetBounds(GameObject target, out Bounds bounds)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        Collider[] colliders = target.GetComponentsInChildren<Collider>();
        bool found = false;
        bounds = new Bounds(target.transform.position, Vector3.one * HandleUtility.GetHandleSize(target.transform.position) * 0.1f);
        foreach (Renderer renderer in renderers)
        {
            if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
        }
        foreach (Collider collider in colliders)
        {
            if (!found) { bounds = collider.bounds; found = true; } else bounds.Encapsulate(collider.bounds);
        }
        return true;
    }

    private static void OnHierarchyItemGUI(int instanceId, Rect rect)
    {
        OnGlobalEditorEvent();
        if (!Session.Connected || !Session.TryGetSelectionColor(instanceId, out Color color)) return;
        Rect marker = new Rect(rect.xMax - 10f, rect.y + (rect.height - 7f) * 0.5f, 7f, 7f);
        EditorGUI.DrawRect(marker, color);
    }

    private static void OnGlobalEditorEvent()
    {
        Event current = Event.current;
        if (!Session.Connected || current == null ||
            (current.type != EventType.ExecuteCommand && current.type != EventType.ValidateCommand)) return;
        string command = current.commandName;
        if (command != "Delete" && command != "SoftDelete" && command != "Cut" && command != "Duplicate") return;
        if (!Selection.objects.Any(Session.IsLockedSelectionObject)) return;
        current.Use();
        SceneView.lastActiveSceneView?.ShowNotification(new GUIContent("This object is being edited by another player."));
    }
}

internal class CollaborationSessionImplementation
{
    [Serializable]
    private sealed class GitHubCommit
    {
        public string sha;
    }

    private const string LatestCommitUrl = UpdateService.LatestCommitUrl;
    private const string RawToolUrl = UpdateService.RawToolUrl;
    private static readonly string[] ToolFiles = UpdateService.ToolFiles;
    private const string InstalledCommitKey = CollaborationSettings.InstalledCommitKey;
    private const string GitHubPatKey = CollaborationSettings.GitHubPatKey;
    private const double UpdateCheckInterval = 300d;
    private const long MaxSyncedFileBytes = 8L * 1024L * 1024L;
    private const int FileChunkBytes = 6 * 1024 * 1024;
    private const int MaxMessageBytes = 12 * 1024 * 1024;

    private sealed class Peer
    {
        public string Id;
        public string Name;
        public string SceneName;
        public WebSocket Socket;
        public TcpClient Transport;
        public readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
        public int Ping = -1;
    }

    private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
    private readonly ConcurrentDictionary<string, Peer> peers = new ConcurrentDictionary<string, Peer>();
    private readonly SemaphoreSlim clientSendLock = new SemaphoreSlim(1, 1);
    private readonly List<CollaborationPlayer> players = new List<CollaborationPlayer>();
    private readonly List<string> chat = new List<string>();
    private CancellationTokenSource cancellation;
    private TcpListener listener;
    private ClientWebSocket client;
    private Process cloudflared;
    private Process backupTunnel;
    private HostHeaderProxy backupProxy;
    private string serverServiceDetail;
    private int serverLinkAttempt;
    private bool validatingServerLink;
    private bool backupLinkAttempted;
    private int backupLinkAttempt;
    private string localName;
    private string lastLocalScene;
    private string lastLocalScenePath;
    private bool sceneSnapshotReady;
    private double lastPresenceSend;
    private double lastTransformScan;
    private double lastPropertyScan;
    private double lastPingSend;
    private Vector3 pendingCameraPosition;
    private Quaternion pendingCameraRotation = Quaternion.identity;
    private bool cameraPoseDirty;
    private readonly Dictionary<string, string> transformStates = new Dictionary<string, string>();
    private readonly Dictionary<string, string> transformObjectIds = new Dictionary<string, string>();
    private readonly Dictionary<string, string> componentStates = new Dictionary<string, string>();
    private readonly Dictionary<string, string> componentObjectIds = new Dictionary<string, string>();
    private readonly Dictionary<string, List<CollaborationMessage>> pendingComponentCreates = new Dictionary<string, List<CollaborationMessage>>();
    private readonly Dictionary<string, UnityEngine.Object> remoteObjects = new Dictionary<string, UnityEngine.Object>();
    private readonly Dictionary<string, byte[]> remoteAssetBackups = new Dictionary<string, byte[]>();
    private readonly Dictionary<string, string[]> incomingFileChunks = new Dictionary<string, string[]>();
    private readonly HashSet<string> remoteCreatedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<UnityEngine.Object, HideFlags> lockedObjectFlags = new Dictionary<UnityEngine.Object, HideFlags>();
    private readonly Dictionary<string, string> selectionOwners = new Dictionary<string, string>();
    private bool applyingRemoteTransform;
    private bool applyingRemoteProperty;
    private bool transformSnapshotReady;
    private string localSelectionId;
    private long packetsSent;
    private long packetsReceived;
    private long bytesSent;
    private long bytesReceived;
    private double suppressAssetEventsUntil;
    private double nextUpdateCheck;
    private bool checkingForUpdate;
    private bool showingUpdateCheck;
    private bool updating;
    private bool endingSession;
    private string githubPat;
    private string availableUpdateCommit;
    private bool settingsLoaded;

    public event Action Changed;
    public bool IsHost { get; private set; }
    public bool Connected { get; private set; }
    public bool Connecting { get; private set; }
    public string LocalId { get; private set; }
    public string ShareLink { get; private set; }
    public string Status { get; private set; }
    public string ConnectionDetail { get; private set; }
    public string Error { get; private set; }
    public string UpdateStatus { get; private set; }
    public string UpdateHash { get; private set; }
    public bool Updating => updating;
    public bool CheckingForUpdate => checkingForUpdate;
    public bool ShowingUpdateCheck => showingUpdateCheck;
    public bool UpdateAvailable => !string.IsNullOrEmpty(availableUpdateCommit);
    public string GitHubPat => githubPat ?? "";
    public bool HasGitHubPat => !string.IsNullOrEmpty(githubPat);
    public int PingMs { get; private set; } = -1;
    public long PacketsSent => Interlocked.Read(ref packetsSent);
    public long PacketsReceived => Interlocked.Read(ref packetsReceived);
    public long BytesSent => Interlocked.Read(ref bytesSent);
    public long BytesReceived => Interlocked.Read(ref bytesReceived);
    public IReadOnlyList<CollaborationPlayer> Players => players;
    public IReadOnlyList<string> Chat => chat;

    public bool IsLocalChatLine(string line)
    {
        return (line ?? "").Contains("] " + CleanName(localName) + ":");
    }

    public string FormatChatLine(string line)
    {
        if (!IsLocalChatLine(line)) return line ?? "";
        return (line ?? "").Replace("] " + CleanName(localName) + ":", "] You:");
    }

    public CollaborationSessionImplementation()
    {
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += Close;
        EditorApplication.quitting += Close;
        Selection.selectionChanged += OnLocalSelectionChanged;
        EditorSceneManager.sceneSaved += OnSceneSaved;
        EditorApplication.delayCall += InitializeEditorState;
    }

    private void InitializeEditorState()
    {
        githubPat = EditorPrefs.GetString(GitHubPatKey, "");
        settingsLoaded = true;
        CheckForUpdate();
        Changed?.Invoke();
    }

    public void SetGitHubPat(string value)
    {
        settingsLoaded = true;
        githubPat = (value ?? "").Trim();
        if (string.IsNullOrEmpty(githubPat)) EditorPrefs.DeleteKey(GitHubPatKey);
        else EditorPrefs.SetString(GitHubPatKey, githubPat);
        UpdateStatus = string.IsNullOrEmpty(githubPat) ? "GitHub authorization removed" : "GitHub authorization saved";
        Changed?.Invoke();
    }

    public async void Create(string name)
    {
        Close();
        RestoreRemoteAssets();
        Reset(name, true);
        Connecting = true;
        Status = "Creating server…";
        ConnectionDetail = "Starting the local server…";
        Changed?.Invoke();
        try
        {
            int port = FindFreePort();
            cancellation = new CancellationTokenSource();
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            ConnectionDetail = "Local server started. Creating a secure share link…";
            _ = AcceptLoop(cancellation.Token);
            CollaborationPlayer hostPlayer = AddOrUpdatePlayer(LocalId, localName, true);
            hostPlayer.SceneName = GetActiveSceneName();
            hostPlayer.PingMs = 0;
            Connected = true;
            Connecting = false;
            Status = "Creating server link…";
            StartBackupTunnel(port, cancellation.Token);
        }
        catch (Exception exception)
        {
            Fail("Could not create the server.\n\n" + DescribeException(exception));
        }
        await Task.Yield();
    }

    public async void Join(string name, string link)
    {
        Close();
        RestoreRemoteAssets();
        Reset(name, false);
        Connecting = true;
        Status = "Connecting…";
        ConnectionDetail = "Validating the server link…";
        Changed?.Invoke();
        try
        {
            cancellation = new CancellationTokenSource();
            CancellationToken joinToken = cancellation.Token;
            Uri uri = MakeWebSocketUri(link);
            ConnectionDetail = "Opening a secure WebSocket connection to " + uri.Host + "…";
            Exception lastError = null;
            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    client?.Dispose();
                    client = new ClientWebSocket();
                    Status = attempt == 1 ? "Connecting…" : "Waiting for server… (" + attempt + "/6)";
                    ConnectionDetail = "Attempt " + attempt + " of 6: contacting " + uri.Host + "…";
                    Changed?.Invoke();
                    await client.ConnectAsync(uri, joinToken);
                    ConnectionDetail = "WebSocket connected. Joining the collaboration session…";
                    Changed?.Invoke();
                    lastError = null;
                    break;
                }
                catch (Exception exception) when (!(exception is OperationCanceledException))
                {
                    lastError = exception;
                    ConnectionDetail = "Attempt " + attempt + " failed: " + DescribeException(exception);
                    Changed?.Invoke();
                    client?.Dispose();
                    client = null;
                    if (joinToken.IsCancellationRequested) return;
                    if (attempt < 6) await Task.Delay(1500, joinToken);
                }
            }
            if (lastError != null)
                throw new InvalidOperationException("The server did not respond. Check that the host still has the server open and use its newest link.", lastError);

            Connected = true;
            Connecting = false;
            _ = ClientReceiveLoop(joinToken);
            string scene = GetActiveSceneName();
            await SendClient(new CollaborationMessage { type = "join", id = LocalId, name = localName, scene = scene });
            AddOrUpdatePlayer(LocalId, localName, false).SceneName = scene;
            Status = "Connected";
            QueueChanged();
        }
        catch (OperationCanceledException)
        {
            // The user pressed Cancel; Close() already restored the home state.
        }
        catch (Exception exception)
        {
            Fail("Could not join the server.\n\n" + DescribeException(exception));
        }
    }

    public void SendChat(string text)
    {
        if (!Connected) return;
        CollaborationMessage message = new CollaborationMessage { type = "chat", id = LocalId, name = localName, text = text };
        AddChat(localName, text);
        if (IsHost) _ = Broadcast(message);
        else _ = SendClient(message);
    }

    public void UpdateCameraPose(Vector3 position, Quaternion rotation)
    {
        if (Vector3.SqrMagnitude(position - pendingCameraPosition) < 0.0001f &&
            Quaternion.Angle(rotation, pendingCameraRotation) < 0.1f) return;
        pendingCameraPosition = position;
        pendingCameraRotation = rotation;
        cameraPoseDirty = true;
    }

    private void PublishLocalScene(string sceneName, string scenePath)
    {
        transformStates.Clear();
        transformObjectIds.Clear();
        componentStates.Clear();
        componentObjectIds.Clear();
        transformSnapshotReady = false;
        lastLocalScene = CleanSceneName(sceneName);
        lastLocalScenePath = NormalizePath(scenePath);
        CollaborationPlayer localPlayer = players.FirstOrDefault(player => player.Id == LocalId);
        if (localPlayer != null) localPlayer.SceneName = lastLocalScene;
        CollaborationMessage message = new CollaborationMessage
        {
            type = "scene", id = LocalId, name = localName, scene = lastLocalScene, path = lastLocalScenePath
        };
        if (IsHost) _ = Broadcast(message); else _ = SendClient(message);
        sceneSnapshotReady = true;
        Changed?.Invoke();
    }

    public void Kick(string id)
    {
        if (!IsHost || !peers.TryRemove(id, out Peer peer)) return;
        _ = SendAndClose(peer, new CollaborationMessage { type = "kicked", text = "The host removed you from the server." });
        mainThread.Enqueue(() =>
        {
            players.RemoveAll(item => item.Id == id);
            Changed?.Invoke();
        });
        BroadcastRoster();
    }

    public void Close()
    {
        bool wasConnected = Connected || Connecting;
        Connected = false;
        Connecting = false;
        try { cancellation?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { client?.Abort(); client?.Dispose(); } catch { }
        foreach (Peer peer in peers.Values)
            try { peer.Socket.Abort(); peer.Socket.Dispose(); peer.Transport?.Dispose(); } catch { }
        peers.Clear();
        RestoreLockedObjects();
        selectionOwners.Clear();
        componentStates.Clear();
        componentObjectIds.Clear();
        pendingComponentCreates.Clear();
        remoteObjects.Clear();
        localSelectionId = null;
        try
        {
            if (cloudflared != null && !cloudflared.HasExited)
                cloudflared.Kill();
            cloudflared?.Dispose();
        }
        catch { }
        try { backupProxy?.Dispose(); } catch { }
        try
        {
            if (backupTunnel != null && !backupTunnel.HasExited)
                backupTunnel.Kill();
            backupTunnel?.Dispose();
        }
        catch { }
        listener = null;
        client = null;
        cloudflared = null;
        backupTunnel = null;
        backupProxy = null;
        cancellation?.Dispose();
        cancellation = null;
        players.Clear();
        chat.Clear();
        ShareLink = "";
        Status = "";
        Error = "";
        ConnectionDetail = "";
        serverServiceDetail = "";
        serverLinkAttempt = 0;
        validatingServerLink = false;
        backupLinkAttempted = false;
        backupLinkAttempt = 0;
        if (wasConnected) Changed?.Invoke();
    }

    public bool LeaveAndOpenEmptyScene()
    {
        Close();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RestoreRemoteAssets();
        return true;
    }

    public async void EndRemoteSession()
    {
        if (!IsHost || endingSession) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        endingSession = true;
        try
        {
            CollaborationMessage ended = new CollaborationMessage
            {
                type = "kicked",
                text = "The remote session was ended."
            };
            Task[] notifications = peers.Values
                .Where(peer => peer.Socket.State == WebSocketState.Open)
                .Select(peer => Send(peer.Socket, peer.SendLock, ended))
                .ToArray();
            if (notifications.Length > 0) await Task.WhenAll(notifications);
            await Task.Delay(75);
        }
        catch { }
        finally
        {
            Close();
            endingSession = false;
        }
    }

    private void Reset(string name, bool host)
    {
        localName = name;
        lastLocalScene = null;
        lastLocalScenePath = null;
        sceneSnapshotReady = false;
        LocalId = Guid.NewGuid().ToString("N");
        IsHost = host;
        Error = "";
        ShareLink = "";
        PingMs = host ? 0 : -1;
        packetsSent = packetsReceived = bytesSent = bytesReceived = 0;
        transformStates.Clear();
        transformObjectIds.Clear();
        componentStates.Clear();
        componentObjectIds.Clear();
        pendingComponentCreates.Clear();
        remoteObjects.Clear();
        selectionOwners.Clear();
        localSelectionId = null;
        transformSnapshotReady = false;
    }

    private void Update()
    {
        while (mainThread.TryDequeue(out Action action))
            action();
        double now = EditorApplication.timeSinceStartup;
        if (settingsLoaded && now >= nextUpdateCheck) CheckForUpdate();
        if (!Connected) return;

        Scene activeScene = SceneManager.GetActiveScene();
        string activeSceneName = CleanSceneName(activeScene.name);
        string activeScenePath = NormalizePath(activeScene.path);
        if (!string.Equals(activeSceneName, lastLocalScene, StringComparison.Ordinal) ||
            !string.Equals(activeScenePath, lastLocalScenePath, StringComparison.Ordinal))
            PublishLocalScene(activeSceneName, activeScenePath);

        if (cameraPoseDirty && now - lastPresenceSend > 0.05d)
        {
            lastPresenceSend = now;
            CollaborationMessage presence = new CollaborationMessage
            {
                type = "camera", id = LocalId, name = localName,
                x = pendingCameraPosition.x, y = pendingCameraPosition.y, z = pendingCameraPosition.z,
                qx = pendingCameraRotation.x, qy = pendingCameraRotation.y,
                qz = pendingCameraRotation.z, qw = pendingCameraRotation.w
            };
            if (IsHost) _ = Broadcast(presence); else _ = SendClient(presence);
            cameraPoseDirty = false;
        }
        if (now - lastTransformScan > 0.05d) { lastTransformScan = now; PublishChangedTransforms(); }
        if (now - lastPropertyScan > 0.1d) { lastPropertyScan = now; PublishSelectedProperties(); }
        if (!IsHost && now - lastPingSend > 1.0)
        {
            lastPingSend = now;
            _ = SendClient(new CollaborationMessage { type = "ping", stamp = DateTime.UtcNow.Ticks });
        }
        else if (IsHost && now - lastPingSend > 1.0)
        {
            lastPingSend = now;
            long stamp = DateTime.UtcNow.Ticks;
            foreach (Peer peer in peers.Values)
                _ = Send(peer.Socket, peer.SendLock, new CollaborationMessage { type = "ping", stamp = stamp });
        }
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                TcpClient connection = await listener.AcceptTcpClientAsync();
                connection.NoDelay = true;
                _ = AcceptConnection(connection, token);
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested) QueueError("Server listener stopped.\n\n" + DescribeException(exception));
                break;
            }
        }
    }

    private async Task AcceptConnection(TcpClient connection, CancellationToken token)
    {
        try
        {
            WebSocket socket = await CollaborationServer.AcceptWebSocket(connection, token);
            if (socket == null) { connection.Dispose(); return; }
            Peer peer = new Peer { Id = Guid.NewGuid().ToString("N"), Socket = socket, Transport = connection };
            _ = PeerReceiveLoop(peer, token);
        }
        catch (Exception exception)
        {
            connection.Dispose();
            if (!token.IsCancellationRequested) QueueError("A connection handshake failed.\n\n" + DescribeException(exception));
        }
    }

    private async Task PeerReceiveLoop(Peer peer, CancellationToken token)
    {
        try
        {
            while (peer.Socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                CollaborationMessage message = await Receive(peer.Socket, token);
                if (message == null) break;
                if (message.type == "join")
                {
                    peer.Id = message.id;
                    peer.Name = CleanName(message.name);
                    peer.SceneName = CleanSceneName(message.scene);
                    peers[peer.Id] = peer;
                    mainThread.Enqueue(() => AddOrUpdatePlayer(peer.Id, peer.Name, false).SceneName = peer.SceneName);
                    string hostScenePath = NormalizePath(SceneManager.GetActiveScene().path);
                    _ = SendAssetSnapshotToPeer(peer, hostScenePath);
                    BroadcastRoster();
                    AddChat("Server", peer.Name + " joined.");
                }
                else if (message.type == "ping")
                    await Send(peer.Socket, peer.SendLock, new CollaborationMessage { type = "pong", stamp = message.stamp });
                else if (message.type == "pong")
                {
                    peer.Ping = (int)TimeSpan.FromTicks(DateTime.UtcNow.Ticks - message.stamp).TotalMilliseconds;
                    mainThread.Enqueue(() =>
                    {
                        CollaborationPlayer player = players.FirstOrDefault(item => item.Id == peer.Id);
                        if (player != null) player.PingMs = peer.Ping;
                        int[] remotePings = peers.Values.Where(item => item.Ping >= 0).Select(item => item.Ping).ToArray();
                        PingMs = remotePings.Length == 0 ? 0 : (int)remotePings.Average();
                        Changed?.Invoke();
                    });
                    await Broadcast(new CollaborationMessage { type = "latency", id = peer.Id, pingMs = peer.Ping });
                }
                else if (message.type == "select_request")
                    HandleSelectionRequest(peer.Id, peer.Name, message.objectId);
                else
                {
                    if (message.type == "scene") peer.SceneName = CleanSceneName(message.scene);
                    HandleIncoming(message);
                    await Broadcast(message, peer.Id);
                }
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
                QueueError("A player connection closed unexpectedly.\n\n" + DescribeException(exception));
        }
        finally
        {
            if (!string.IsNullOrEmpty(peer.Id) && peers.TryRemove(peer.Id, out _))
            {
                mainThread.Enqueue(() =>
                {
                    ApplySelection(peer.Id, "");
                    players.RemoveAll(item => item.Id == peer.Id);
                    Changed?.Invoke();
                });
                _ = Broadcast(new CollaborationMessage { type = "selection", id = peer.Id, objectId = "" });
                BroadcastRoster();
            }
            try { peer.Socket.Dispose(); } catch { }
            try { peer.Transport?.Dispose(); } catch { }
        }
    }

    private async Task ClientReceiveLoop(CancellationToken token)
    {
        try
        {
            while (client.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                CollaborationMessage message = await Receive(client, token);
                if (message == null) break;
                if (message.type == "pong")
                {
                    PingMs = (int)TimeSpan.FromTicks(DateTime.UtcNow.Ticks - message.stamp).TotalMilliseconds;
                    mainThread.Enqueue(() =>
                    {
                        CollaborationPlayer localPlayer = players.FirstOrDefault(item => item.Id == LocalId);
                        if (localPlayer != null) localPlayer.PingMs = PingMs;
                    });
                    QueueChanged();
                }
                else if (message.type == "ping")
                    await SendClient(new CollaborationMessage { type = "pong", stamp = message.stamp });
                else if (message.type == "kicked")
                {
                    string reason = message.text;
                    mainThread.Enqueue(() =>
                    {
                        Close();
                        Error = reason;
                        Changed?.Invoke();
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        RestoreRemoteAssets();
                    });
                    return;
                }
                else HandleIncoming(message);
            }
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested) QueueError("Connection lost.\n\n" + DescribeException(exception));
        }
    }

    private void HandleIncoming(CollaborationMessage message)
    {
        mainThread.Enqueue(() =>
        {
            if (message.type == "chat") AddChat(message.name, message.text);
            else if (message.type == "camera")
            {
                CollaborationPlayer player = AddOrUpdatePlayer(message.id, message.name, message.id == LocalId && IsHost);
                player.CameraPosition = new Vector3(message.x, message.y, message.z);
                player.CameraRotation = new Quaternion(message.qx, message.qy, message.qz, message.qw);
                player.HasCameraPose = true;
                SceneView.RepaintAll();
            }
            else if (message.type == "transform") ApplyRemoteTransform(message);
            else if (message.type == "create") ApplyRemoteCreate(message);
            else if (message.type == "object_delete") ApplyRemoteObjectDelete(message);
            else if (message.type == "component_create") ApplyRemoteComponentCreate(message);
            else if (message.type == "component_delete") ApplyRemoteComponentDelete(message);
            else if (message.type == "property") ApplyRemoteProperty(message);
            else if (message.type == "selection") ApplySelection(message.id, message.objectId);
            else if (message.type == "selection_denied")
            {
                Selection.activeObject = null;
                Error = string.IsNullOrEmpty(message.text) ? "That object is being edited by another player." : message.text;
            }
            else if (message.type == "latency")
            {
                CollaborationPlayer player = players.FirstOrDefault(item => item.Id == message.id);
                if (player != null) player.PingMs = message.pingMs;
            }
            else if (message.type == "roster")
            {
                HashSet<string> current = new HashSet<string>(message.ids ?? Array.Empty<string>());
                players.RemoveAll(item => item.Id != LocalId && !current.Contains(item.Id));
                for (int i = 0; i < (message.ids?.Length ?? 0); i++)
                {
                    CollaborationPlayer player = AddOrUpdatePlayer(message.ids[i], message.names[i], i == 0);
                    if (i < (message.scenes?.Length ?? 0)) player.SceneName = CleanSceneName(message.scenes[i]);
                    if (i < (message.selections?.Length ?? 0)) player.SelectedObjectId = message.selections[i] ?? "";
                    if (i < (message.pings?.Length ?? 0)) player.PingMs = message.pings[i];
                }
                selectionOwners.Clear();
                foreach (CollaborationPlayer player in players)
                    if (!string.IsNullOrEmpty(player.SelectedObjectId)) selectionOwners[player.SelectedObjectId] = player.Id;
                RefreshObjectLocks();
            }
            else if (message.type == "scene")
                AddOrUpdatePlayer(message.id, message.name, message.id == LocalId && IsHost).SceneName = CleanSceneName(message.scene);
            else if (message.type == "scene_open")
                ApplyInitialHostScene(message);
            else if (message.type == "file" || message.type == "file_chunk" || message.type == "folder" || message.type == "delete" || message.type == "move")
                ApplyRemoteFile(message);
            Changed?.Invoke();
        });
    }

    private void BroadcastRoster()
    {
        List<Peer> connected = peers.Values.Where(peer => !string.IsNullOrEmpty(peer.Name)).ToList();
        CollaborationMessage roster = new CollaborationMessage
        {
            type = "roster",
            ids = new[] { LocalId }.Concat(connected.Select(peer => peer.Id)).ToArray(),
            names = new[] { localName }.Concat(connected.Select(peer => peer.Name)).ToArray(),
            scenes = new[] { GetActiveSceneName() }.Concat(connected.Select(peer => CleanSceneName(peer.SceneName))).ToArray(),
            selections = new[] { players.FirstOrDefault(player => player.Id == LocalId)?.SelectedObjectId ?? "" }
                .Concat(connected.Select(peer => players.FirstOrDefault(player => player.Id == peer.Id)?.SelectedObjectId ?? "")).ToArray(),
            pings = new[] { 0 }.Concat(connected.Select(peer => peer.Ping)).ToArray()
        };
        _ = Broadcast(roster);
        mainThread.Enqueue(() =>
        {
            foreach (Peer peer in connected)
            {
                CollaborationPlayer player = AddOrUpdatePlayer(peer.Id, peer.Name, false);
                player.PingMs = peer.Ping;
                player.SceneName = CleanSceneName(peer.SceneName);
            }
            Changed?.Invoke();
        });
    }

    private async Task Broadcast(CollaborationMessage message, string exceptId = null)
    {
        Task[] sends = peers.Values
            .Where(peer => peer.Id != exceptId && peer.Socket.State == WebSocketState.Open)
            .Select(peer => SendIgnoringDisconnect(peer, message))
            .ToArray();
        if (sends.Length > 0) await Task.WhenAll(sends);
    }

    private async Task SendIgnoringDisconnect(Peer peer, CollaborationMessage message)
    {
        try { await Send(peer.Socket, peer.SendLock, message); }
        catch { }
    }

    private Task SendClient(CollaborationMessage message)
    {
        return client == null ? Task.CompletedTask : Send(client, clientSendLock, message);
    }

    private async Task Send(WebSocket socket, SemaphoreSlim sendLock, CollaborationMessage message)
    {
        byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
        await sendLock.WaitAsync();
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
                Interlocked.Increment(ref packetsSent);
                Interlocked.Add(ref bytesSent, data.Length);
            }
        }
        finally { sendLock.Release(); }
    }

    private async Task<CollaborationMessage> Receive(WebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[4096];
        using (MemoryStream stream = new MemoryStream())
        {
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (stream.Length + result.Count > MaxMessageBytes)
                    throw new InvalidDataException("The collaboration server rejected a message larger than 12 MB.");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            byte[] data = stream.ToArray();
            Interlocked.Increment(ref packetsReceived);
            Interlocked.Add(ref bytesReceived, data.Length);
            return JsonUtility.FromJson<CollaborationMessage>(Encoding.UTF8.GetString(data));
        }
    }

    private async Task SendAndClose(Peer peer, CollaborationMessage message)
    {
        try
        {
            await Send(peer.Socket, peer.SendLock, message);
            await peer.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Kicked", CancellationToken.None);
        }
        catch { peer.Socket.Abort(); }
    }

    public bool ShouldPublishAssetEvents =>
        Connected && EditorApplication.timeSinceStartup >= suppressAssetEventsUntil;

    public void PublishImportedAsset(string assetPath)
    {
        if (!ShouldPublishAssetEvents || !IsSafeProjectPath(assetPath)) return;
        string metaPath = assetPath + ".meta";
        if (Directory.Exists(ToAbsolutePath(assetPath)))
        {
            SendProjectMessage(new CollaborationMessage { type = "folder", id = LocalId, path = NormalizePath(assetPath) });
            if (File.Exists(ToAbsolutePath(metaPath))) SendProjectFile(metaPath);
            return;
        }
        if (File.Exists(ToAbsolutePath(metaPath)))
            SendProjectFile(metaPath);
        SendProjectFile(assetPath);
    }

    private void OnSceneSaved(Scene scene)
    {
        string savedPath = NormalizePath(scene.path);
        if (!Connected || !IsSafeScenePath(savedPath)) return;
        EditorApplication.delayCall += () => PublishSavedScene(savedPath);
    }

    private void PublishSavedScene(string scenePath)
    {
        if (!Connected || !IsSafeScenePath(scenePath)) return;
        string metaPath = scenePath + ".meta";
        if (File.Exists(ToAbsolutePath(metaPath))) SendProjectFile(metaPath);
        SendProjectFile(scenePath);
    }

    public void PublishDeletedAsset(string assetPath)
    {
        // Deletions are never transmitted. Keeping an obsolete remote file is
        // safer than allowing a collaboration message to remove project data.
    }

    public void PublishMovedAsset(string from, string to)
    {
        if (!ShouldPublishAssetEvents || !IsSafeProjectPath(from) || !IsSafeProjectPath(to)) return;
        if (Directory.Exists(ToAbsolutePath(to))) return;
        // Peers receive the new file but retain the old one. This makes moves
        // non-destructive across projects.
        PublishImportedAsset(to);
    }

    private void SendProjectFile(string projectPath)
    {
        try
        {
            string absolutePath = ToAbsolutePath(projectPath);
            if (!File.Exists(absolutePath)) return;
            FileInfo info = new FileInfo(absolutePath);
            if (info.Length > MaxSyncedFileBytes)
            {
                QueueError("Skipped syncing " + projectPath + " because it is larger than 8 MB.");
                return;
            }
            SendProjectMessage(new CollaborationMessage
            {
                type = "file",
                id = LocalId,
                path = NormalizePath(projectPath),
                data = Convert.ToBase64String(File.ReadAllBytes(absolutePath))
            });
        }
        catch (Exception exception) { QueueError("Could not sync " + projectPath + ": " + exception.Message); }
    }

    private async Task SendAssetSnapshotToPeer(Peer peer, string hostScenePath)
    {
        try
        {
            string assetsRoot = Path.GetFullPath(Application.dataPath);
            string[] directories = Directory.GetDirectories(assetsRoot, "*", SearchOption.AllDirectories)
                .Select(path => "Assets/" + NormalizePath(path.Substring(assetsRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                .Where(path => !path.StartsWith("Assets/@NovaDevvvv/Collaboration Tool", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.Length)
                .ToArray();
            foreach (string directory in directories)
                await Send(peer.Socket, peer.SendLock, new CollaborationMessage { type = "folder", id = LocalId, path = directory });
            string[] files = Directory.GetFiles(assetsRoot, "*", SearchOption.AllDirectories)
                .Select(path => "Assets/" + NormalizePath(path.Substring(assetsRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                .Where(ShouldIncludeInAssetSnapshot)
                .OrderBy(path => SnapshotAssetPath(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToArray();
            foreach (string path in files)
                await SendFileToPeer(peer, path);
            if (IsSafeScenePath(hostScenePath))
                await Send(peer.Socket, peer.SendLock, new CollaborationMessage
                {
                    type = "scene_open", id = LocalId, name = localName,
                    scene = Path.GetFileNameWithoutExtension(hostScenePath), path = hostScenePath
                });
        }
        catch (Exception exception)
        {
            QueueError("Could not finish sending the host asset snapshot: " + exception.Message);
        }
    }

    private static bool ShouldIncludeInAssetSnapshot(string path)
    {
        string normalized = NormalizePath(path);
        if (normalized.StartsWith("Assets/@NovaDevvvv/Collaboration Tool/", StringComparison.OrdinalIgnoreCase)) return false;
        string withoutMeta = normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(0, normalized.Length - 5) : normalized;
        string extension = Path.GetExtension(withoutMeta);
        return !new[] { ".cs", ".dll", ".asmdef", ".asmref", ".rsp", ".pdb", ".mdb" }
            .Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static string SnapshotAssetPath(string path)
    {
        return path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            ? path.Substring(0, path.Length - 5)
            : path;
    }

    private async Task SendFileToPeer(Peer peer, string projectPath)
    {
        byte[] bytes = File.ReadAllBytes(ToAbsolutePath(projectPath));
        int count = Math.Max(1, (bytes.Length + FileChunkBytes - 1) / FileChunkBytes);
        for (int index = 0; index < count; index++)
        {
            int offset = index * FileChunkBytes;
            int length = Math.Min(FileChunkBytes, bytes.Length - offset);
            byte[] chunk = new byte[length];
            Buffer.BlockCopy(bytes, offset, chunk, 0, length);
            await Send(peer.Socket, peer.SendLock, new CollaborationMessage
            {
                type = count == 1 ? "file" : "file_chunk", id = LocalId,
                path = NormalizePath(projectPath), chunkIndex = index, chunkCount = count,
                data = Convert.ToBase64String(chunk)
            });
        }
    }

    private void SendProjectMessage(CollaborationMessage message)
    {
        if (!Connected) return;
        if (IsHost) _ = Broadcast(message);
        else _ = SendClient(message);
    }

    private void ApplyRemoteFile(CollaborationMessage message)
    {
        if (!IsSafeProjectPath(message.path) || (message.type == "move" && !IsSafeProjectPath(message.path2)))
        {
            Error = "A remote user attempted to change a path outside Assets; the change was rejected.";
            return;
        }

        try
        {
            suppressAssetEventsUntil = EditorApplication.timeSinceStartup + 3d;
            string path = NormalizePath(message.path);
            string absolutePath = ToAbsolutePath(path);
            if (message.type == "folder")
            {
                if (!IsHost && !Directory.Exists(absolutePath)) remoteCreatedDirectories.Add(path);
                Directory.CreateDirectory(absolutePath);
                return;
            }
            if (message.type == "file_chunk")
            {
                if (message.chunkCount <= 0 || message.chunkIndex < 0 || message.chunkIndex >= message.chunkCount) return;
                if (!incomingFileChunks.TryGetValue(path, out string[] chunks) || chunks.Length != message.chunkCount)
                incomingFileChunks[path] = chunks = new string[message.chunkCount];
                chunks[message.chunkIndex] = message.data ?? "";
                if (chunks.Any(chunk => chunk == null)) return;
                byte[] assembledBytes;
                using (MemoryStream assembled = new MemoryStream())
                {
                    foreach (string chunk in chunks)
                    {
                        byte[] bytes = Convert.FromBase64String(chunk);
                        assembled.Write(bytes, 0, bytes.Length);
                    }
                    assembledBytes = assembled.ToArray();
                }
                message.data = Convert.ToBase64String(assembledBytes);
                incomingFileChunks.Remove(path);
                message.type = "file";
            }
            if (message.type == "file")
            {
                if (!IsHost && !remoteAssetBackups.ContainsKey(path))
                    remoteAssetBackups[path] = File.Exists(absolutePath) ? File.ReadAllBytes(absolutePath) : null;
                string directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(absolutePath, Convert.FromBase64String(message.data ?? ""));
            }
            else if (message.type == "delete")
            {
                Error = "A remote deletion was ignored for project safety.";
                return;
            }
            else if (message.type == "move")
            {
                Error = "A remote move was ignored for project safety.";
                return;
            }

            string importPath = message.type == "move" ? NormalizePath(message.path2) : path;
            if (!importPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                AssetDatabase.ImportAsset(importPath, ImportAssetOptions.ForceUpdate);
        }
        catch (Exception exception)
        {
            Error = "Could not apply remote change to " + message.path + ": " + exception.Message;
        }
    }

    private void RestoreRemoteAssets()
    {
        if (IsHost || remoteAssetBackups.Count == 0) return;
        HashSet<string> candidateDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            foreach (KeyValuePair<string, byte[]> entry in remoteAssetBackups.OrderByDescending(item => item.Key.Length))
            {
                if (!IsSafeProjectPath(entry.Key)) continue;
                string absolutePath = ToAbsolutePath(entry.Key);
                string directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory)) candidateDirectories.Add(directory);
                if (entry.Value != null)
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllBytes(absolutePath, entry.Value);
                }
                else if (File.Exists(absolutePath))
                    File.Delete(absolutePath);
            }

            string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string directory in candidateDirectories.OrderByDescending(path => path.Length))
            {
                string current = directory;
                while (!string.IsNullOrEmpty(current) &&
                       current.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                       Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
                {
                    Directory.Delete(current);
                    current = Path.GetDirectoryName(current);
                }
            }
            foreach (string directoryPath in remoteCreatedDirectories.OrderByDescending(path => path.Length))
            {
                string absoluteDirectory = ToAbsolutePath(directoryPath);
                if (Directory.Exists(absoluteDirectory) && !Directory.EnumerateFileSystemEntries(absoluteDirectory).Any())
                    Directory.Delete(absoluteDirectory);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Collaboration: could not completely clean up received assets: " + exception.Message);
        }
        finally
        {
            AssetDatabase.AllowAutoRefresh();
            remoteAssetBackups.Clear();
            remoteCreatedDirectories.Clear();
            incomingFileChunks.Clear();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
    }

    private static bool IsSafeScenePath(string path)
    {
        string normalized = NormalizePath(path);
        return normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("../");
    }

    private void ApplyInitialHostScene(CollaborationMessage message)
    {
        if (IsHost || !IsSafeScenePath(message.path)) return;
        string scenePath = NormalizePath(message.path);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Error = "The host scene could not be imported after asset synchronization: " + scenePath;
                return;
            }
        }
        Scene current = SceneManager.GetActiveScene();
        if (!string.Equals(NormalizePath(current.path), scenePath, StringComparison.OrdinalIgnoreCase))
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
    }

    private void PublishChangedTransforms()
    {
        if (!Connected || applyingRemoteTransform) return;
        bool publish = transformSnapshotReady;
        HashSet<string> currentTransformIds = new HashSet<string>();
        HashSet<string> currentComponentIds = new HashSet<string>();
#if UNITY_2022_2_OR_NEWER
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
#else
        Transform[] transforms = UnityEngine.Object.FindObjectsOfType<Transform>();
#endif
        foreach (Transform transform in transforms)
        {
            if (!transform.gameObject.scene.IsValid() || !transform.gameObject.scene.isLoaded) continue;
            string localTransformId = GlobalObjectId.GetGlobalObjectIdSlow(transform).ToString();
            currentTransformIds.Add(localTransformId);
            string id = GetSharedObjectId(transform);
            string gameObjectId = GetSharedObjectId(transform.gameObject);
            transformObjectIds[localTransformId] = gameObjectId;
            foreach (Component component in transform.gameObject.GetComponents<Component>())
            {
                if (component == null || component is Transform) continue;
                string localComponentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
                currentComponentIds.Add(localComponentId);
                bool componentExisted = componentObjectIds.TryGetValue(localComponentId, out string sharedComponentId);
                if (!componentExisted && TryGetMappedObjectId(component, out sharedComponentId)) componentExisted = true;
                if (!componentExisted)
                {
                    sharedComponentId = publish
                        ? "collab-component:" + Guid.NewGuid().ToString("N")
                        : GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
                    if (publish) remoteObjects[sharedComponentId] = component;
                }
                foreach (string staleId in componentObjectIds.Where(pair => pair.Key != localComponentId && pair.Value == sharedComponentId)
                             .Select(pair => pair.Key).ToArray())
                {
                    componentObjectIds.Remove(staleId);
                    componentStates.Remove(staleId);
                }
                componentObjectIds[localComponentId] = sharedComponentId;
                if (publish && !componentExisted)
                    SendProjectMessage(new CollaborationMessage
                    {
                        type = "component_create", id = LocalId, objectId = gameObjectId,
                        componentId = sharedComponentId,
                        text = component.GetType().AssemblyQualifiedName,
                        data = EditorJsonUtility.ToJson(component)
                    });
            }
            if (IsLockedByOther(gameObjectId)) continue;
            string state = TransformState(transform);
            bool existed = transformStates.TryGetValue(localTransformId, out string previous);
            if (existed && previous == state) continue;
            transformStates[localTransformId] = state;
            if (!publish) continue;
            Vector3 p = transform.localPosition;
            Quaternion r = transform.localRotation;
            Vector3 s = transform.localScale;
            if (!existed)
            {
                SendProjectMessage(new CollaborationMessage { type = "create", id = LocalId,
                    objectId = gameObjectId, componentId = id, text = transform.gameObject.name,
                    scene = transform.gameObject.scene.name,
                    path2 = transform.parent == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(transform.parent.gameObject).ToString(),
                    x = p.x, y = p.y, z = p.z, qx = r.x, qy = r.y, qz = r.z, qw = r.w,
                    sx = s.x, sy = s.y, sz = s.z });
                continue;
            }
            SendProjectMessage(new CollaborationMessage { type = "transform", id = LocalId, objectId = gameObjectId, componentId = id,
                x = p.x, y = p.y, z = p.z, qx = r.x, qy = r.y, qz = r.z, qw = r.w,
                sx = s.x, sy = s.y, sz = s.z });
        }
        if (publish)
        {
            foreach (string removedId in transformObjectIds.Keys.Where(key => !currentTransformIds.Contains(key)).ToArray())
            {
                string removedObjectId = transformObjectIds[removedId];
                transformObjectIds.Remove(removedId);
                transformStates.Remove(removedId);
                if (!string.IsNullOrEmpty(removedObjectId))
                    SendProjectMessage(new CollaborationMessage { type = "object_delete", id = LocalId, objectId = removedObjectId });
            }
            foreach (string removedId in componentObjectIds.Keys.Where(key => !currentComponentIds.Contains(key)).ToArray())
            {
                string sharedComponentId = componentObjectIds[removedId];
                componentObjectIds.Remove(removedId);
                componentStates.Remove(removedId);
                if (!string.IsNullOrEmpty(sharedComponentId))
                    SendProjectMessage(new CollaborationMessage { type = "component_delete", id = LocalId, componentId = sharedComponentId });
            }
        }
        transformSnapshotReady = true;
    }

    private string GetSharedObjectId(UnityEngine.Object target)
    {
        if (TryGetMappedObjectId(target, out string mappedId)) return mappedId;
        return GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
    }

    private bool TryGetMappedObjectId(UnityEngine.Object target, out string mappedId)
    {
        foreach (KeyValuePair<string, UnityEngine.Object> pair in remoteObjects)
        {
            if (pair.Value != target) continue;
            mappedId = pair.Key;
            return true;
        }
        mappedId = null;
        return false;
    }

    private void ApplyRemoteTransform(CollaborationMessage message)
    {
        string transformId = string.IsNullOrEmpty(message.componentId) ? message.objectId : message.componentId;
        Transform transform = ResolveRemoteObject(transformId) as Transform;
        if (transform == null) return;
        applyingRemoteTransform = true;
        try
        {
            transform.localPosition = new Vector3(message.x, message.y, message.z);
            transform.localRotation = new Quaternion(message.qx, message.qy, message.qz, message.qw);
            transform.localScale = new Vector3(message.sx, message.sy, message.sz);
            EditorUtility.SetDirty(transform);
            transformStates[transformId] = TransformState(transform);
            SceneView.RepaintAll();
        }
        finally { applyingRemoteTransform = false; }
    }

    private UnityEngine.Object ResolveRemoteObject(string objectId)
    {
        if (string.IsNullOrEmpty(objectId)) return null;
        if (remoteObjects.TryGetValue(objectId, out UnityEngine.Object mapped) && mapped != null) return mapped;
        if (!GlobalObjectId.TryParse(objectId, out GlobalObjectId globalId)) return null;
        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
    }

    private void ApplyRemoteCreate(CollaborationMessage message)
    {
        if (string.IsNullOrEmpty(message.objectId) || ResolveRemoteObject(message.objectId) != null) return;
        Scene targetScene = new Scene();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene loaded = SceneManager.GetSceneAt(i);
            if (loaded.isLoaded && string.Equals(loaded.name, message.scene, StringComparison.Ordinal))
            {
                targetScene = loaded;
                break;
            }
        }
        if (!targetScene.IsValid() || !targetScene.isLoaded) return;

        applyingRemoteTransform = true;
        try
        {
            GameObject created = new GameObject(string.IsNullOrWhiteSpace(message.text) ? "GameObject" : message.text);
            SceneManager.MoveGameObjectToScene(created, targetScene);
            UnityEngine.Object parentObject = ResolveRemoteObject(message.path2);
            GameObject parentGameObject = parentObject as GameObject;
            Transform parent = parentGameObject != null ? parentGameObject.transform : parentObject as Transform;
            created.transform.SetParent(parent, false);
            created.transform.localPosition = new Vector3(message.x, message.y, message.z);
            created.transform.localRotation = new Quaternion(message.qx, message.qy, message.qz, message.qw);
            created.transform.localScale = new Vector3(message.sx, message.sy, message.sz);
            remoteObjects[message.objectId] = created;
            if (!string.IsNullOrEmpty(message.componentId)) remoteObjects[message.componentId] = created.transform;

            string localTransformId = GlobalObjectId.GetGlobalObjectIdSlow(created.transform).ToString();
            transformStates[localTransformId] = TransformState(created.transform);
            transformObjectIds[localTransformId] = message.objectId;
            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorApplication.RepaintHierarchyWindow();
            SceneView.RepaintAll();
            if (pendingComponentCreates.TryGetValue(message.objectId, out List<CollaborationMessage> pending))
            {
                pendingComponentCreates.Remove(message.objectId);
                foreach (CollaborationMessage componentMessage in pending) ApplyRemoteComponentCreate(componentMessage);
            }
        }
        finally { applyingRemoteTransform = false; }
    }

    private void ApplyRemoteObjectDelete(CollaborationMessage message)
    {
        GameObject target = ResolveGameObject(message.objectId);
        if (target == null) return;
        applyingRemoteTransform = true;
        try
        {
            Transform[] removedTransforms = target.GetComponentsInChildren<Transform>(true);
            foreach (Transform removedTransform in removedTransforms)
            {
                string localTransformId = GlobalObjectId.GetGlobalObjectIdSlow(removedTransform).ToString();
                transformStates.Remove(localTransformId);
                transformObjectIds.Remove(localTransformId);
                foreach (Component component in removedTransform.GetComponents<Component>())
                {
                    if (component == null || component is Transform) continue;
                    string localComponentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
                    componentObjectIds.Remove(localComponentId);
                    componentStates.Remove(localComponentId);
                }
            }
            HashSet<UnityEngine.Object> removedObjects = new HashSet<UnityEngine.Object>(removedTransforms.Cast<UnityEngine.Object>());
            foreach (Transform removedTransform in removedTransforms) removedObjects.Add(removedTransform.gameObject);
            foreach (string key in remoteObjects.Where(pair => removedObjects.Contains(pair.Value))
                         .Select(pair => pair.Key).ToArray())
                remoteObjects.Remove(key);
            Undo.DestroyObjectImmediate(target);
            EditorApplication.RepaintHierarchyWindow();
            SceneView.RepaintAll();
        }
        finally { applyingRemoteTransform = false; }
    }

    private void ApplyRemoteComponentCreate(CollaborationMessage message)
    {
        if (string.IsNullOrEmpty(message.componentId) || ResolveRemoteObject(message.componentId) != null) return;
        GameObject target = ResolveGameObject(message.objectId);
        if (target == null)
        {
            if (!pendingComponentCreates.TryGetValue(message.objectId ?? "", out List<CollaborationMessage> pending))
                pendingComponentCreates[message.objectId ?? ""] = pending = new List<CollaborationMessage>();
            pending.Add(message);
            return;
        }
        Type type = Type.GetType(message.text ?? "");
        if (target == null || type == null || !typeof(Component).IsAssignableFrom(type) || type == typeof(Transform)) return;
        applyingRemoteProperty = true;
        try
        {
            Component component = Undo.AddComponent(target, type);
            if (component == null) return;
            EditorJsonUtility.FromJsonOverwrite(message.data ?? "{}", component);
            remoteObjects[message.componentId] = component;
            string localId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            componentObjectIds[localId] = message.componentId;
            componentStates[localId] = EditorJsonUtility.ToJson(component);
            EditorUtility.SetDirty(component);
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            SceneView.RepaintAll();
        }
        catch (Exception exception) { QueueError("Could not add remote component " + type.Name + ": " + exception.Message); }
        finally { applyingRemoteProperty = false; }
    }

    private void ApplyRemoteComponentDelete(CollaborationMessage message)
    {
        Component component = ResolveRemoteObject(message.componentId) as Component;
        if (component == null || component is Transform) return;
        applyingRemoteProperty = true;
        try
        {
            string localId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            componentObjectIds.Remove(localId);
            componentStates.Remove(localId);
            remoteObjects.Remove(message.componentId);
            Undo.DestroyObjectImmediate(component);
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            SceneView.RepaintAll();
        }
        finally { applyingRemoteProperty = false; }
    }

    private static string TransformState(Transform transform)
    {
        Vector3 p = transform.localPosition;
        Quaternion r = transform.localRotation;
        Vector3 s = transform.localScale;
        return p.x + "," + p.y + "," + p.z + "|" + r.x + "," + r.y + "," + r.z + "," + r.w + "|" + s.x + "," + s.y + "," + s.z;
    }

    private void OnLocalSelectionChanged()
    {
        if (!Connected) return;
        GameObject selected = Selection.activeGameObject;
        string requested = selected == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(selected).ToString();
        if (requested == localSelectionId) return;
        if (!string.IsNullOrEmpty(requested) && IsLockedByOther(requested)) requested = "";
        if (IsHost) HandleSelectionRequest(LocalId, localName, requested);
        else _ = SendClient(new CollaborationMessage { type = "select_request", id = LocalId, name = localName, objectId = requested });
    }

    private void HandleSelectionRequest(string playerId, string playerName, string requestedId)
    {
        mainThread.Enqueue(() =>
        {
            requestedId = requestedId ?? "";
            if (!string.IsNullOrEmpty(requestedId) && selectionOwners.TryGetValue(requestedId, out string owner) && owner != playerId)
            {
                if (peers.TryGetValue(playerId, out Peer deniedPeer))
                    _ = Send(deniedPeer.Socket, deniedPeer.SendLock, new CollaborationMessage { type = "selection_denied", text = "That object is being edited by another player." });
                return;
            }
            CollaborationMessage selection = new CollaborationMessage { type = "selection", id = playerId, name = playerName, objectId = requestedId };
            ApplySelection(playerId, requestedId);
            _ = Broadcast(selection);
        });
    }

    private void ApplySelection(string playerId, string objectId)
    {
        foreach (string oldId in selectionOwners.Where(pair => pair.Value == playerId).Select(pair => pair.Key).ToArray())
            selectionOwners.Remove(oldId);
        objectId = objectId ?? "";
        if (!string.IsNullOrEmpty(objectId)) selectionOwners[objectId] = playerId;
        CollaborationPlayer player = players.FirstOrDefault(item => item.Id == playerId);
        if (player != null) player.SelectedObjectId = objectId;
        if (playerId == LocalId) localSelectionId = objectId;
        RefreshObjectLocks();
        SceneView.RepaintAll();
        Changed?.Invoke();
    }

    private bool IsLockedByOther(string objectId)
    {
        return !string.IsNullOrEmpty(objectId) && selectionOwners.TryGetValue(objectId, out string owner) && owner != LocalId;
    }

    public GameObject ResolveGameObject(string objectId)
    {
        UnityEngine.Object target = ResolveRemoteObject(objectId);
        GameObject gameObject = target as GameObject;
        Component component = target as Component;
        return gameObject != null ? gameObject : component != null ? component.gameObject : null;
    }

    public bool IsLockedSelectionObject(UnityEngine.Object target)
    {
        GameObject gameObject = target as GameObject;
        Component component = target as Component;
        if (gameObject == null && component != null) gameObject = component.gameObject;
        if (gameObject == null) return false;
        if (lockedObjectFlags.ContainsKey(gameObject)) return true;
        return gameObject.GetComponents<Component>().Any(item => item != null && lockedObjectFlags.ContainsKey(item));
    }

    public bool TryGetSelectionColor(int instanceId, out Color color)
    {
        foreach (CollaborationPlayer player in players)
        {
            GameObject selected = ResolveGameObject(player.SelectedObjectId);
            if (selected == null || selected.GetInstanceID() != instanceId) continue;
            color = player.Color;
            return true;
        }
        color = Color.clear;
        return false;
    }

    private void RefreshObjectLocks()
    {
        RestoreLockedObjects();
        foreach (KeyValuePair<string, string> pair in selectionOwners)
        {
            if (pair.Value == LocalId) continue;
            GameObject target = ResolveGameObject(pair.Key);
            if (target == null) continue;
            SetNotEditable(target);
            foreach (Component component in target.GetComponents<Component>()) SetNotEditable(component);
        }
        ActiveEditorTracker.sharedTracker.ForceRebuild();
        EditorApplication.RepaintHierarchyWindow();
        SceneView.RepaintAll();
    }

    private void SetNotEditable(UnityEngine.Object target)
    {
        if (target == null || lockedObjectFlags.ContainsKey(target)) return;
        lockedObjectFlags[target] = target.hideFlags;
        target.hideFlags |= HideFlags.NotEditable;
    }

    private void RestoreLockedObjects()
    {
        foreach (KeyValuePair<UnityEngine.Object, HideFlags> pair in lockedObjectFlags)
            if (pair.Key != null) pair.Key.hideFlags = pair.Value;
        lockedObjectFlags.Clear();
        EditorApplication.RepaintHierarchyWindow();
    }

    private void PublishSelectedProperties()
    {
        if (!Connected || applyingRemoteProperty || string.IsNullOrEmpty(localSelectionId)) return;
        GameObject target = ResolveGameObject(localSelectionId);
        if (target == null) return;
        UnityEngine.Object[] objects = target.GetComponents<Component>().Cast<UnityEngine.Object>().ToArray();
        foreach (UnityEngine.Object component in objects)
        {
            if (component == null || component is Transform) continue;
            string localComponentId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            string componentId = GetSharedObjectId(component);
            string json = EditorJsonUtility.ToJson(component);
            if (componentStates.TryGetValue(localComponentId, out string oldJson) && oldJson == json) continue;
            componentStates[localComponentId] = json;
            SendProjectMessage(new CollaborationMessage { type = "property", id = LocalId, objectId = localSelectionId, componentId = componentId, data = json });
        }
    }

    private void ApplyRemoteProperty(CollaborationMessage message)
    {
        if (string.IsNullOrEmpty(message.componentId)) return;
        UnityEngine.Object target = ResolveRemoteObject(message.componentId);
        if (target == null || target is Transform) return;
        applyingRemoteProperty = true;
        try
        {
            HideFlags flags = target.hideFlags;
            EditorJsonUtility.FromJsonOverwrite(message.data ?? "{}", target);
            target.hideFlags = flags;
            EditorUtility.SetDirty(target);
            string localComponentId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
            componentStates[localComponentId] = EditorJsonUtility.ToJson(target);
            SceneView.RepaintAll();
        }
        finally { applyingRemoteProperty = false; }
    }

    public void CheckForUpdatesNow()
    {
        if (!settingsLoaded)
        {
            githubPat = EditorPrefs.GetString(GitHubPatKey, "");
            settingsLoaded = true;
        }
        nextUpdateCheck = 0d;
        CheckForUpdate(true);
    }

    public async void InstallAvailableUpdate()
    {
        if (updating || string.IsNullOrEmpty(availableUpdateCommit)) return;
        string commit = availableUpdateCommit;
        updating = true;
        Changed?.Invoke();
        try
        {
            Dictionary<string, string> sources = new Dictionary<string, string>();
            using (WebClient web = CreateGitHubClient())
                foreach (string fileName in ToolFiles)
                    sources[fileName] = await web.DownloadStringTaskAsync(
                        new Uri(string.Format(RawToolUrl, commit, fileName)));
            mainThread.Enqueue(() => InstallDownloadedUpdate(commit, sources));
        }
        catch (Exception exception)
        {
            updating = false;
            Error = "The update could not be downloaded.\n\n" + DescribeException(exception);
            Changed?.Invoke();
        }
    }

    private async void CheckForUpdate(bool showStatus = false)
    {
        if (checkingForUpdate || EditorApplication.timeSinceStartup < nextUpdateCheck) return;
        checkingForUpdate = true;
        DateTime checkStarted = DateTime.UtcNow;
        nextUpdateCheck = EditorApplication.timeSinceStartup + UpdateCheckInterval;
        if (showStatus)
        {
            showingUpdateCheck = true;
            UpdateStatus = "Checking GitHub…";
            Changed?.Invoke();
        }
        try
        {
            string commitJson;
            using (WebClient web = CreateGitHubClient())
                commitJson = await web.DownloadStringTaskAsync(new Uri(LatestCommitUrl));

            GitHubCommit commit = JsonUtility.FromJson<GitHubCommit>(commitJson);
            if (commit == null || string.IsNullOrWhiteSpace(commit.sha))
                return;
            string installedCommit = EditorPrefs.GetString(InstalledCommitKey);
            if (string.IsNullOrEmpty(installedCommit))
            {
                EditorPrefs.SetString(InstalledCommitKey, commit.sha);
                availableUpdateCommit = "";
                UpdateHash = "";
                QueueUpdateStatus("You’re up to date");
                return;
            }
            if (string.Equals(installedCommit, commit.sha, StringComparison.OrdinalIgnoreCase))
            {
                availableUpdateCommit = "";
                UpdateHash = "";
                QueueUpdateStatus("You’re up to date");
                return;
            }

            availableUpdateCommit = commit.sha;
            UpdateHash = commit.sha.Substring(0, Math.Min(7, commit.sha.Length));
            QueueUpdateStatus("Downloading update: " + UpdateHash);
            mainThread.Enqueue(InstallAvailableUpdate);
        }
        catch (WebException exception) when (IsExpectedNetworkFailure(exception.Status))
        {
            QueueUpdateStatus("Offline — update check skipped");
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Collaboration: could not check GitHub for updates: " + exception.Message);
            QueueUpdateStatus("Update check failed");
        }
        finally
        {
            if (showStatus)
            {
                TimeSpan remaining = TimeSpan.FromSeconds(2d) - (DateTime.UtcNow - checkStarted);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining);
                showingUpdateCheck = false;
                checkingForUpdate = false;
                Changed?.Invoke();
            }
            else checkingForUpdate = false;
        }
    }

    private static bool IsExpectedNetworkFailure(WebExceptionStatus status)
    {
        return status == WebExceptionStatus.NameResolutionFailure ||
               status == WebExceptionStatus.ProxyNameResolutionFailure ||
               status == WebExceptionStatus.ConnectFailure ||
               status == WebExceptionStatus.Timeout ||
               status == WebExceptionStatus.ConnectionClosed ||
               status == WebExceptionStatus.ReceiveFailure ||
               status == WebExceptionStatus.SendFailure;
    }

    private void QueueUpdateStatus(string status)
    {
        mainThread.Enqueue(() =>
        {
            UpdateStatus = status;
            Changed?.Invoke();
        });
    }

    private WebClient CreateGitHubClient()
    {
        WebClient web = new WebClient();
        web.Headers[HttpRequestHeader.UserAgent] = "Unity-Collaboration-Tool";
        web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
        if (!string.IsNullOrEmpty(githubPat))
            web.Headers[HttpRequestHeader.Authorization] = "Bearer " + githubPat;
        return web;
    }

    private void InstallDownloadedUpdate(string commitSha, Dictionary<string, string> sources)
    {
        try
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string shortHash = commitSha.Substring(0, Math.Min(7, commitSha.Length));
            string installDirectory = Path.GetFullPath(Path.Combine(projectRoot, UpdateService.InstallDirectory));
            Directory.CreateDirectory(installDirectory);

            // Downloading finishes before this method runs. Hold Unity's refresh
            // while every replacement is prepared, then request one compilation.
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                foreach (KeyValuePair<string, string> source in sources)
                {
                    string destination = Path.Combine(installDirectory, source.Key);
                    string destinationDirectory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
                    if (File.Exists(destination) &&
                        string.Equals(File.ReadAllText(destination), source.Value, StringComparison.Ordinal)) continue;

                    string temporaryPath = destination + ".update";
                    File.WriteAllText(temporaryPath, source.Value, new UTF8Encoding(false));
                    File.Copy(temporaryPath, destination, true);
                    File.Delete(temporaryPath);
                }
            }
            finally { AssetDatabase.AllowAutoRefresh(); }

            EditorPrefs.SetString(InstalledCommitKey, commitSha);
            availableUpdateCommit = "";
            UpdateStatus = "Updated to " + shortHash;
            UpdateHash = "";
            updating = false;
            Debug.Log("Collaboration updated to " + shortHash + ". Unity is recompiling the editor scripts.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
        catch (Exception exception)
        {
            updating = false;
            Error = "The update could not be installed.\n\n" + DescribeException(exception);
            Changed?.Invoke();
        }
    }

    private static string FindToolAssetPath()
    {
        string expectedPath = UpdateService.InstallDirectory + "/Editor/CollaborationWindow.cs";
        if (AssetDatabase.LoadAssetAtPath<MonoScript>(expectedPath) != null)
            return expectedPath;

        string[] searches = { "CollaborationWindow t:MonoScript", "CollaborationTool t:MonoScript" };
        foreach (string search in searches)
        {
            foreach (string guid in AssetDatabase.FindAssets(search))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileName(path);
                if (fileName.Equals("CollaborationWindow.cs", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("CollaborationTool.cs", StringComparison.OrdinalIgnoreCase))
                    return path;
            }
        }
        return null;
    }

    private static bool IsSafeProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string normalized = NormalizePath(path);
        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;
        if (normalized.StartsWith("Assets/@NovaDevvvv/Collaboration Tool/", StringComparison.OrdinalIgnoreCase)) return false;
        string contentPath = normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(0, normalized.Length - 5)
            : normalized;
        string extension = Path.GetExtension(contentPath).ToLowerInvariant();
        if (extension == ".cs" || extension == ".dll" || extension == ".asmdef" ||
            extension == ".asmref" || extension == ".rsp" || extension == ".mdb" ||
            extension == ".pdb") return false;
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
        string absolute = Path.GetFullPath(Path.Combine(projectRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        return absolute.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => (path ?? "").Replace('\\', '/').TrimStart('/');
    private static string ToAbsolutePath(string path) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", NormalizePath(path)));

    private void StartCloudflared(int port)
    {
        serverLinkAttempt++;
        CancellationToken sessionToken = cancellation?.Token ?? CancellationToken.None;
        ConnectionDetail = "Creating server link… (attempt " + serverLinkAttempt + " of 3)";
        Changed?.Invoke();
        string isolatedConfig = Path.Combine(Path.GetTempPath(), "unity-collaboration-quick-tunnel.yml");
        try { File.WriteAllText(isolatedConfig, "{}", new UTF8Encoding(false)); }
        catch (Exception exception)
        {
            Error = "The temporary server configuration could not be created.\n\n" + DescribeException(exception);
            Changed?.Invoke();
            return;
        }
        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = "cloudflared",
            // Keep the forwarded origin explicitly on the loopback listener.
            // Use an isolated empty configuration so a user's named-tunnel config
            // cannot prevent Quick Tunnel mode from generating a random link.
            Arguments = "--config \"" + isolatedConfig + "\" tunnel --no-autoupdate --url http://127.0.0.1:" + port +
                        " --http-host-header 127.0.0.1:" + port,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Process process = new Process { StartInfo = info, EnableRaisingEvents = true };
        cloudflared = process;
        DataReceivedEventHandler output = (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;
            if (args.Data.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                args.Data.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
                serverServiceDetail = SanitizeServiceMessage(args.Data);
            Match match = Regex.Match(args.Data, @"https://(?!api\.)[a-zA-Z0-9-]+\.trycloudflare\.com",
                RegexOptions.IgnoreCase);
            if (match.Success)
                _ = ValidatePrimaryServerLink(match.Value, process, sessionToken);
        };
        process.OutputDataReceived += output;
        process.ErrorDataReceived += output;
        process.Exited += (_, __) =>
        {
            if (Connected && string.IsNullOrEmpty(ShareLink))
            {
                if (serverLinkAttempt < 3)
                    _ = RetryServerLink(port, process, sessionToken);
                else if (!backupLinkAttempted)
                {
                    mainThread.Enqueue(() =>
                    {
                        try { process.Dispose(); } catch { }
                        if (sessionToken.IsCancellationRequested || !Connected || !string.IsNullOrEmpty(ShareLink)) return;
                        StartBackupTunnel(port, sessionToken);
                    });
                }
                else mainThread.Enqueue(() =>
                {
                    try { process.Dispose(); } catch { }
                    if (sessionToken.IsCancellationRequested || !Connected || !string.IsNullOrEmpty(ShareLink)) return;
                    Error = "A public server link could not be created after trying both available server services.";
                    ConnectionDetail = "Server link creation failed.";
                    Changed?.Invoke();
                });
            }
        };
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception exception)
        {
            Error = "The server link could not be created. Make sure the server service is installed and available, then try again.\n\n" + DescribeException(exception);
            Changed?.Invoke();
        }
    }

    private async Task RetryServerLink(int port, Process previousProcess, CancellationToken token)
    {
        try { await Task.Delay(2000, token); }
        catch (OperationCanceledException) { return; }
        mainThread.Enqueue(() =>
        {
            try { previousProcess.Dispose(); } catch { }
            if (token.IsCancellationRequested || !Connected || !string.IsNullOrEmpty(ShareLink)) return;
            serverServiceDetail = "";
            validatingServerLink = false;
            StartCloudflared(port);
        });
    }

    private async Task ValidatePrimaryServerLink(string link, Process process, CancellationToken token)
    {
        lock (peers)
        {
            if (validatingServerLink) return;
            validatingServerLink = true;
        }

        Uri uri;
        try { uri = new Uri(link); }
        catch
        {
            validatingServerLink = false;
            return;
        }

        for (int attempt = 0; attempt < 5 && !token.IsCancellationRequested; attempt++)
        {
            try
            {
                Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(uri.Host);
                Task completed = await Task.WhenAny(lookup, Task.Delay(2000, token));
                if (completed == lookup && lookup.Status == TaskStatus.RanToCompletion && lookup.Result.Length > 0)
                {
                    mainThread.Enqueue(() =>
                    {
                        if (token.IsCancellationRequested || !Connected || process.HasExited) return;
                        ShareLink = link;
                        Status = "Server is online";
                        ConnectionDetail = "Server link created. Waiting for players…";
                        validatingServerLink = false;
                        Changed?.Invoke();
                    });
                    return;
                }
            }
            catch { }

            try { await Task.Delay(1000, token); }
            catch (OperationCanceledException) { return; }
        }

        mainThread.Enqueue(() =>
        {
            validatingServerLink = false;
            if (token.IsCancellationRequested || !Connected || !string.IsNullOrEmpty(ShareLink)) return;
            serverServiceDetail = "The generated address could not be resolved by this computer's DNS service.";
            ConnectionDetail = "Server address failed DNS validation. Retrying…";
            Changed?.Invoke();
            try { if (!process.HasExited) process.Kill(); } catch { }
        });
    }

    private void StartBackupTunnel(int port, CancellationToken token)
    {
        backupLinkAttempted = true;
        backupLinkAttempt++;
        ConnectionDetail = "Creating server link... (LHR attempt " + backupLinkAttempt + " of 3)";
        Changed?.Invoke();
        int proxyPort = FindFreePort();
        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = "ssh",
            Arguments = "-o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ServerAliveInterval=30 " +
                        "-o ExitOnForwardFailure=yes -R 80:127.0.0.1:" + proxyPort + " nokey@localhost.run",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Process process = new Process { StartInfo = info, EnableRaisingEvents = true };
        backupTunnel = process;
        DataReceivedEventHandler output = (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            Match match = Regex.Match(args.Data,
                @"(?:https://)?([a-zA-Z0-9-]+(?:\.[a-zA-Z0-9-]+)*\.(?:localhost\.run|lhr\.life|lhr\.rocks))",
                RegexOptions.IgnoreCase);
            if (match.Success && args.Data.IndexOf("tunneled", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string host = match.Groups[1].Value;
                if (host.Equals("admin.localhost.run", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("www.localhost.run", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("ssh.localhost.run", StringComparison.OrdinalIgnoreCase)) return;
                string link = "https://" + host;
                _ = ValidateBackupServerLink(link, process, token);
            }
            else if (args.Data.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     args.Data.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     args.Data.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0)
                serverServiceDetail = SanitizeServiceMessage(args.Data);
        };
        process.OutputDataReceived += output;
        process.ErrorDataReceived += output;
        process.Exited += (_, __) => mainThread.Enqueue(() =>
        {
            if (token.IsCancellationRequested || !Connected || !string.IsNullOrEmpty(ShareLink) || backupTunnel != process) return;
            try { backupProxy?.Dispose(); } catch { }
            backupProxy = null;
            try { process.Dispose(); } catch { }
            backupTunnel = null;
            serverServiceDetail = "";
            ConnectionDetail = backupLinkAttempt < 3
                ? "LHR did not return a link. Retrying..."
                : "Trying another server service...";
            Changed?.Invoke();
            if (backupLinkAttempt < 3) _ = RetryBackupTunnel(port, token);
            else
            {
                serverLinkAttempt = 0;
                StartCloudflared(port);
            }
        });
        try
        {
            backupProxy = new HostHeaderProxy(port, proxyPort);
            backupProxy.Start(token);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = StopBackupTunnelAfterTimeout(process, token);
        }
        catch (Exception exception)
        {
            try { backupProxy?.Dispose(); } catch { }
            backupProxy = null;
            try { process.Dispose(); } catch { }
            backupTunnel = null;
            serverServiceDetail = SanitizeServiceMessage(DescribeException(exception));
            ConnectionDetail = backupLinkAttempt < 3
                ? "LHR could not start. Retrying..."
                : "Trying another server service...";
            Changed?.Invoke();
            if (backupLinkAttempt < 3) _ = RetryBackupTunnel(port, token);
            else
            {
                serverLinkAttempt = 0;
                StartCloudflared(port);
            }
        }
    }

    private async Task RetryBackupTunnel(int port, CancellationToken token)
    {
        try { await Task.Delay(1500, token); }
        catch (OperationCanceledException) { return; }
        mainThread.Enqueue(() =>
        {
            if (token.IsCancellationRequested || !Connected || !string.IsNullOrEmpty(ShareLink)) return;
            StartBackupTunnel(port, token);
        });
    }

    private async Task ValidateBackupServerLink(string link, Process process, CancellationToken token)
    {
        bool reachedLocalServer = await Task.Run(() =>
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(link + "/collaboration/");
            request.Method = "GET";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    return response.Headers["X-Collaboration-Server"] == "true";
            }
            catch (WebException exception)
            {
                using (HttpWebResponse response = exception.Response as HttpWebResponse)
                    return response != null && response.Headers["X-Collaboration-Server"] == "true";
            }
            catch { return false; }
        });

        mainThread.Enqueue(() =>
        {
            if (token.IsCancellationRequested || !Connected || process != backupTunnel || process.HasExited) return;
            if (reachedLocalServer)
            {
                ShareLink = link;
                Status = "Server is online";
                ConnectionDetail = "Server link verified. Waiting for players…";
            }
            else
            {
                serverServiceDetail = "The backup address was created, but its connection to Unity failed verification.";
                ConnectionDetail = "Backup server verification failed.";
                try { process.Kill(); } catch { }
            }
            Changed?.Invoke();
        });
    }

    private async Task StopBackupTunnelAfterTimeout(Process process, CancellationToken token)
    {
        try { await Task.Delay(30000, token); }
        catch (OperationCanceledException) { return; }
        mainThread.Enqueue(() =>
        {
            if (token.IsCancellationRequested || process != backupTunnel || !Connected ||
                !string.IsNullOrEmpty(ShareLink) || process.HasExited) return;
            serverServiceDetail = "The backup server did not return a public link within 30 seconds.";
            try { process.Kill(); } catch { }
        });
    }

    private static string SanitizeServiceMessage(string message)
    {
        return TunnelManager.SanitizeError(message);
    }

    private CollaborationPlayer AddOrUpdatePlayer(string id, string name, bool host)
    {
        CollaborationPlayer player = players.FirstOrDefault(item => item.Id == id);
        if (player == null)
        {
            player = new CollaborationPlayer { Id = id, Color = Color.HSVToRGB(Mathf.Abs(id.GetHashCode() % 1000) / 1000f, 0.65f, 1f) };
            players.Add(player);
        }
        player.Name = CleanName(name);
        player.IsHost = host;
        return player;
    }

    private void AddChat(string name, string text)
    {
        void Add()
        {
            chat.Add("[" + DateTime.Now.ToString("HH:mm") + "] " + CleanName(name) + ": " + (text ?? ""));
            if (chat.Count > 200) chat.RemoveAt(0);
            Changed?.Invoke();
        }
        if (Thread.CurrentThread.ManagedThreadId == 1) Add(); else mainThread.Enqueue(Add);
    }

    private void Fail(string message)
    {
        mainThread.Enqueue(() =>
        {
            Connected = false;
            Connecting = false;
            Error = message;
            Changed?.Invoke();
        });
    }

    private void QueueError(string message) => mainThread.Enqueue(() => { Error = message; Changed?.Invoke(); });
    private void QueueChanged() => mainThread.Enqueue(() => Changed?.Invoke());

    private static string CleanName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim();
        return value.Length > 32 ? value.Substring(0, 32) : value;
    }

    private static string DescribeException(Exception exception)
    {
        return exception == null ? "No technical details were provided." : NetworkDiagnostics.Describe(exception);
    }

    private static string GetActiveSceneName()
    {
        Scene scene = SceneManager.GetActiveScene();
        return CleanSceneName(scene.IsValid() && !string.IsNullOrWhiteSpace(scene.name) ? scene.name : "Untitled");
    }

    private static string CleanSceneName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Unknown scene" : value.Trim();
        return value.Length > 64 ? value.Substring(0, 64) : value;
    }

    private static int FindFreePort()
    {
        return CollaborationServer.FindFreePort();
    }

    private static Uri MakeWebSocketUri(string link)
    {
        Uri uri = CollaborationClient.MakeWebSocketUri(link);
        if (string.Equals(uri.Host, "api.trycloudflare.com", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("That is the server service API address, not a share link. Create a new server and copy the generated random link.");
        return uri;
    }
}
