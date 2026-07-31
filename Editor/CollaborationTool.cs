using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
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
    private enum Page { Home, Join, Create, Session }

    private static readonly CollaborationSession Session = new CollaborationSession();
    internal static CollaborationSession SharedSession => Session;
    private Page page;
    private string playerName = "";
    private string serverLink = "";
    private string chatText = "";
    private Vector2 chatScroll;
    private Vector2 playersScroll;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle centeredLabelStyle;
    private GUIStyle centeredDetailStyle;

    [MenuItem("Collaborate/Window")]
    private static void OpenWindow()
    {
        CollaborationTool window = GetWindow<CollaborationTool>();
        window.titleContent = new GUIContent("Collaboration");
        window.minSize = new Vector2(390f, 440f);
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Collaboration");
        Session.Changed -= Repaint;
        Session.Changed += Repaint;
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        if (Session.Connected)
            page = Page.Session;
    }

    private void OnDisable()
    {
        Session.Changed -= Repaint;
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnGUI()
    {
        EnsureStyles();
        if (Session.IsHost && (Session.Connecting || (Session.Connected && string.IsNullOrEmpty(Session.ShareLink))))
        {
            DrawCreatingServer();
            return;
        }

        EditorGUILayout.Space(14f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(14f);
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Collaboration", titleStyle);
                EditorGUILayout.LabelField(Session.Connected ? "Work together in real time" : "Create together, wherever you are", subtitleStyle);
                EditorGUILayout.Space(12f);
                if (Session.Connected || Session.Connecting)
                    DrawSession();
                else
                    DrawSetup();
            }
            GUILayout.Space(14f);
        }
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, fixedHeight = 25f };
        subtitleStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.68f, 0.7f, 0.74f) : new Color(0.35f, 0.37f, 0.4f) }
        };
        centeredLabelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
        centeredDetailStyle = new GUIStyle(subtitleStyle) { alignment = TextAnchor.MiddleCenter };
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
        EditorGUILayout.LabelField("Your share link will appear in a moment.", centeredDetailStyle, GUILayout.Height(20f));
        if (!string.IsNullOrEmpty(Session.Error))
        {
            GUILayout.Space(14f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(24f);
                EditorGUILayout.HelpBox(Session.Error, MessageType.Error);
                GUILayout.Space(24f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(110f), GUILayout.Height(28f)))
                {
                    Session.Close();
                    page = Page.Home;
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
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Session.CheckingForUpdate))
                {
                    if (GUILayout.Button(Session.CheckingForUpdate ? "Checking…" : "↻  Check for Updates", GUILayout.Width(145f), GUILayout.Height(25f)))
                        Session.CheckForUpdatesNow();
                }
                if (!string.IsNullOrEmpty(Session.UpdateStatus))
                    GUILayout.Label(Session.UpdateStatus, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }
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
                    if (page == Page.Create)
                        Session.Create(playerName.Trim());
                    else
                        Session.Join(playerName.Trim(), serverLink.Trim());
                    page = Page.Session;
                }
            }
            if (GUILayout.Button("Back", GUILayout.Height(24f)))
            {
                Session.Close();
                page = Page.Home;
            }
            GUILayout.Space(4f);
        }
        DrawError();
    }

    private void DrawSession()
    {
        if (Session.IsHost && !string.IsNullOrEmpty(Session.ShareLink))
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Invite others", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Share this server link with your collaborators.", EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(5f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.SelectableLabel(Session.ShareLink, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (GUILayout.Button("Copy", GUILayout.Width(58f)))
                        EditorGUIUtility.systemCopyBuffer = Session.ShareLink;
                }
            }
        }

        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("Players", EditorStyles.boldLabel);
        playersScroll = EditorGUILayout.BeginScrollView(playersScroll, GUILayout.Height(Mathf.Min(125f, 25f + Session.Players.Count * 22f)));
        foreach (CollaborationPlayer player in Session.Players.ToArray())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(player.Name + (player.IsHost ? " (Host)" : ""), GUILayout.ExpandWidth(true));
                GUILayout.Label(player.PingMs < 0 ? "— ms" : player.PingMs + " ms", EditorStyles.miniLabel, GUILayout.Width(52f));
                if (Session.IsHost && !player.IsHost && GUILayout.Button("Kick", GUILayout.Width(48f)))
                    Session.Kick(player.Id);
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("Chat", EditorStyles.boldLabel);
        chatScroll = EditorGUILayout.BeginScrollView(chatScroll, EditorStyles.helpBox, GUILayout.MinHeight(110f), GUILayout.ExpandHeight(true));
        foreach (string line in Session.Chat.ToArray())
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.SetNextControlName("CollaborationChat");
            chatText = EditorGUILayout.TextField(chatText);
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
                         GUI.GetNameOfFocusedControl() == "CollaborationChat";
            if ((GUILayout.Button("Send", GUILayout.Width(55f)) || enter) && !string.IsNullOrWhiteSpace(chatText))
            {
                Session.SendChat(chatText.Trim());
                chatText = "";
                GUI.FocusControl("CollaborationChat");
                Event.current.Use();
            }
        }

        EditorGUILayout.Space(5f);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Ping: " + (Session.PingMs < 0 ? "—" : Session.PingMs.ToString()) + " ms", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Sent: " + Session.PacketsSent + " packets / " + FormatBytes(Session.BytesSent), EditorStyles.miniLabel);
            GUILayout.Space(8f);
            GUILayout.Label("Received: " + Session.PacketsReceived + " / " + FormatBytes(Session.BytesReceived), EditorStyles.miniLabel);
        }

        DrawError();
        string button = Session.IsHost ? "Close Server" : "Leave Server";
        if (GUILayout.Button(button, GUILayout.Height(26f)))
        {
            Session.Close();
            page = Page.Home;
        }
    }

    private void DrawError()
    {
        if (!string.IsNullOrEmpty(Session.Error))
            EditorGUILayout.HelpBox(Session.Error, MessageType.Error);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.0") + " KB";
        return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!Session.Connected) return;
        Event current = Event.current;
        if (current != null && current.mousePosition.x >= 0f && current.mousePosition.y >= 0f &&
            current.mousePosition.x <= sceneView.position.width && current.mousePosition.y <= sceneView.position.height)
        {
            Session.UpdateCursor(current.mousePosition.x / sceneView.position.width,
                                 current.mousePosition.y / sceneView.position.height);
        }

        Handles.BeginGUI();
        foreach (CollaborationPlayer player in Session.Players.ToArray())
        {
            if (player.Id == Session.LocalId || player.CursorX < 0f) continue;
            float x = player.CursorX * sceneView.position.width;
            float y = player.CursorY * sceneView.position.height;
            Color previous = GUI.color;
            GUI.color = player.Color;
            GUI.Label(new Rect(x - 4f, y - 24f, 160f, 20f), player.Name, EditorStyles.boldLabel);
            GUI.Label(new Rect(x - 5f, y - 7f, 28f, 28f), "▶", EditorStyles.boldLabel);
            GUI.color = previous;
        }
        Handles.EndGUI();
    }
}

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
    public string path;
    public string path2;
    public string data;
}

internal sealed class CollaborationPlayer
{
    public string Id;
    public string Name;
    public bool IsHost;
    public int PingMs = -1;
    public float CursorX = -1f;
    public float CursorY = -1f;
    public Color Color;
}

[InitializeOnLoad]
internal sealed class CollaborationSession
{
    [Serializable]
    private sealed class GitHubCommit
    {
        public string sha;
    }

    private const string LatestCommitUrl = "https://api.github.com/repos/novadevvvv/unity-collaboration/commits/main";
    private const string RawToolUrl = "https://raw.githubusercontent.com/novadevvvv/unity-collaboration/{0}/Editor/CollaborationTool.cs";
    private const string InstalledCommitKey = "NovaDev.UnityCollaboration.InstalledCommit";
    private const double UpdateCheckInterval = 60d;

    private sealed class Peer
    {
        public string Id;
        public string Name;
        public WebSocket Socket;
        public readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
        public int Ping = -1;
    }

    private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
    private readonly ConcurrentDictionary<string, Peer> peers = new ConcurrentDictionary<string, Peer>();
    private readonly SemaphoreSlim clientSendLock = new SemaphoreSlim(1, 1);
    private readonly List<CollaborationPlayer> players = new List<CollaborationPlayer>();
    private readonly List<string> chat = new List<string>();
    private CancellationTokenSource cancellation;
    private HttpListener listener;
    private ClientWebSocket client;
    private Process cloudflared;
    private string localName;
    private double lastCursorSend;
    private double lastPingSend;
    private float pendingCursorX = -1f;
    private float pendingCursorY = -1f;
    private long packetsSent;
    private long packetsReceived;
    private long bytesSent;
    private long bytesReceived;
    private double saveAt = -1d;
    private double suppressAssetEventsUntil;
    private double nextUpdateCheck;
    private bool checkingForUpdate;

    public event Action Changed;
    public bool IsHost { get; private set; }
    public bool Connected { get; private set; }
    public bool Connecting { get; private set; }
    public string LocalId { get; private set; }
    public string ShareLink { get; private set; }
    public string Status { get; private set; }
    public string Error { get; private set; }
    public string UpdateStatus { get; private set; }
    public bool CheckingForUpdate => checkingForUpdate;
    public int PingMs { get; private set; } = -1;
    public long PacketsSent => Interlocked.Read(ref packetsSent);
    public long PacketsReceived => Interlocked.Read(ref packetsReceived);
    public long BytesSent => Interlocked.Read(ref bytesSent);
    public long BytesReceived => Interlocked.Read(ref bytesReceived);
    public IReadOnlyList<CollaborationPlayer> Players => players;
    public IReadOnlyList<string> Chat => chat;

    static CollaborationSession() { }

    public CollaborationSession()
    {
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += Close;
        EditorApplication.quitting += Close;
        Undo.postprocessModifications += OnPostprocessModifications;
        EditorApplication.hierarchyChanged += ScheduleProjectSave;
        ObjectChangeEvents.changesPublished += OnObjectChanges;
        EditorApplication.delayCall += () => CheckForUpdate();
    }

    public async void Create(string name)
    {
        Close();
        Reset(name, true);
        Connecting = true;
        Status = "Creating server…";
        Changed?.Invoke();
        try
        {
            int port = FindFreePort();
            cancellation = new CancellationTokenSource();
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            _ = AcceptLoop(cancellation.Token);
            AddOrUpdatePlayer(LocalId, localName, true);
            Connected = true;
            Connecting = false;
            Status = "Creating server link…";
            StartCloudflared(port);
        }
        catch (Exception exception)
        {
            Fail("Could not create the server: " + exception.Message);
        }
        await Task.Yield();
    }

    public async void Join(string name, string link)
    {
        Close();
        Reset(name, false);
        Connecting = true;
        Status = "Connecting…";
        Changed?.Invoke();
        try
        {
            cancellation = new CancellationTokenSource();
            client = new ClientWebSocket();
            Uri uri = MakeWebSocketUri(link);
            await client.ConnectAsync(uri, cancellation.Token);
            Connected = true;
            Connecting = false;
            _ = ClientReceiveLoop(cancellation.Token);
            await SendClient(new CollaborationMessage { type = "join", id = LocalId, name = localName });
            AddOrUpdatePlayer(LocalId, localName, false);
            Status = "Connected";
            QueueChanged();
        }
        catch (Exception exception)
        {
            Fail("Could not join: " + exception.Message);
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

    public void UpdateCursor(float x, float y)
    {
        pendingCursorX = Mathf.Clamp01(x);
        pendingCursorY = Mathf.Clamp01(y);
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
        try { listener?.Stop(); listener?.Close(); } catch { }
        try { client?.Abort(); client?.Dispose(); } catch { }
        foreach (Peer peer in peers.Values)
            try { peer.Socket.Abort(); peer.Socket.Dispose(); } catch { }
        peers.Clear();
        try
        {
            if (cloudflared != null && !cloudflared.HasExited)
                cloudflared.Kill();
            cloudflared?.Dispose();
        }
        catch { }
        listener = null;
        client = null;
        cloudflared = null;
        cancellation?.Dispose();
        cancellation = null;
        players.Clear();
        chat.Clear();
        ShareLink = "";
        Status = "";
        Error = "";
        if (wasConnected) Changed?.Invoke();
    }

    private void Reset(string name, bool host)
    {
        localName = name;
        LocalId = Guid.NewGuid().ToString("N");
        IsHost = host;
        Error = "";
        ShareLink = "";
        PingMs = host ? 0 : -1;
        packetsSent = packetsReceived = bytesSent = bytesReceived = 0;
    }

    private void Update()
    {
        while (mainThread.TryDequeue(out Action action))
            action();
        double now = EditorApplication.timeSinceStartup;
        if (now >= nextUpdateCheck) CheckForUpdate();
        if (!Connected) return;

        if (saveAt > 0d && now >= saveAt)
        {
            saveAt = -1d;
            SaveChangedProjectState();
        }
        if (pendingCursorX >= 0f && now - lastCursorSend > 0.066)
        {
            lastCursorSend = now;
            CollaborationMessage cursor = new CollaborationMessage
            {
                type = "cursor", id = LocalId, name = localName, x = pendingCursorX, y = pendingCursorY
            };
            if (IsHost) _ = Broadcast(cursor); else _ = SendClient(cursor);
            pendingCursorX = -1f;
            SceneView.RepaintAll();
        }
        if (!IsHost && now - lastPingSend > 2.0)
        {
            lastPingSend = now;
            _ = SendClient(new CollaborationMessage { type = "ping", stamp = DateTime.UtcNow.Ticks });
        }
        else if (IsHost && now - lastPingSend > 2.0)
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
                HttpListenerContext context = await listener.GetContextAsync();
                if (!context.Request.IsWebSocketRequest || context.Request.Url.AbsolutePath != "/collaboration/")
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }
                HttpListenerWebSocketContext webSocket = await context.AcceptWebSocketAsync(null);
                Peer peer = new Peer { Id = Guid.NewGuid().ToString("N"), Socket = webSocket.WebSocket };
                _ = PeerReceiveLoop(peer, token);
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested) QueueError("Server listener stopped: " + exception.Message);
                break;
            }
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
                    peers[peer.Id] = peer;
                    mainThread.Enqueue(() => AddOrUpdatePlayer(peer.Id, peer.Name, false));
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
                }
                else
                {
                    HandleIncoming(message);
                    await Broadcast(message, peer.Id);
                }
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception) { }
        finally
        {
            if (!string.IsNullOrEmpty(peer.Id) && peers.TryRemove(peer.Id, out _))
            {
                mainThread.Enqueue(() =>
                {
                    players.RemoveAll(item => item.Id == peer.Id);
                    Changed?.Invoke();
                });
                BroadcastRoster();
            }
            try { peer.Socket.Dispose(); } catch { }
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
                    QueueChanged();
                }
                else if (message.type == "ping")
                    await SendClient(new CollaborationMessage { type = "pong", stamp = message.stamp });
                else if (message.type == "kicked")
                {
                    string reason = message.text;
                    mainThread.Enqueue(() => { Close(); Error = reason; Changed?.Invoke(); });
                    return;
                }
                else HandleIncoming(message);
            }
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested) QueueError("Connection lost: " + exception.Message);
        }
    }

    private void HandleIncoming(CollaborationMessage message)
    {
        mainThread.Enqueue(() =>
        {
            if (message.type == "chat") AddChat(message.name, message.text);
            else if (message.type == "cursor")
            {
                CollaborationPlayer player = AddOrUpdatePlayer(message.id, message.name, message.id == LocalId && IsHost);
                player.CursorX = message.x;
                player.CursorY = message.y;
                SceneView.RepaintAll();
            }
            else if (message.type == "roster")
            {
                HashSet<string> current = new HashSet<string>(message.ids ?? Array.Empty<string>());
                players.RemoveAll(item => item.Id != LocalId && !current.Contains(item.Id));
                for (int i = 0; i < (message.ids?.Length ?? 0); i++)
                    AddOrUpdatePlayer(message.ids[i], message.names[i], i == 0);
            }
            else if (message.type == "file" || message.type == "delete" || message.type == "move")
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
            names = new[] { localName }.Concat(connected.Select(peer => peer.Name)).ToArray()
        };
        _ = Broadcast(roster);
        mainThread.Enqueue(() =>
        {
            foreach (Peer peer in connected)
            {
                CollaborationPlayer player = AddOrUpdatePlayer(peer.Id, peer.Name, false);
                player.PingMs = peer.Ping;
            }
            Changed?.Invoke();
        });
    }

    private async Task Broadcast(CollaborationMessage message, string exceptId = null)
    {
        foreach (Peer peer in peers.Values.ToArray())
        {
            if (peer.Id == exceptId || peer.Socket.State != WebSocketState.Open) continue;
            try { await Send(peer.Socket, peer.SendLock, message); } catch { }
        }
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
        if (File.Exists(ToAbsolutePath(metaPath)))
            SendProjectFile(metaPath);
        SendProjectFile(assetPath);
    }

    public void PublishDeletedAsset(string assetPath)
    {
        if (!ShouldPublishAssetEvents || !IsSafeProjectPath(assetPath)) return;
        SendProjectMessage(new CollaborationMessage { type = "delete", id = LocalId, path = NormalizePath(assetPath) });
        string metaPath = assetPath + ".meta";
        SendProjectMessage(new CollaborationMessage { type = "delete", id = LocalId, path = NormalizePath(metaPath) });
    }

    public void PublishMovedAsset(string from, string to)
    {
        if (!ShouldPublishAssetEvents || !IsSafeProjectPath(from) || !IsSafeProjectPath(to)) return;
        SendProjectMessage(new CollaborationMessage
        {
            type = "move", id = LocalId, path = NormalizePath(from), path2 = NormalizePath(to)
        });
        SendProjectMessage(new CollaborationMessage
        {
            type = "move", id = LocalId, path = NormalizePath(from + ".meta"), path2 = NormalizePath(to + ".meta")
        });
        PublishImportedAsset(to);
    }

    private void SendProjectFile(string projectPath)
    {
        try
        {
            string absolutePath = ToAbsolutePath(projectPath);
            if (!File.Exists(absolutePath)) return;
            FileInfo info = new FileInfo(absolutePath);
            if (info.Length > 32L * 1024L * 1024L)
            {
                QueueError("Skipped syncing " + projectPath + " because it is larger than 32 MB.");
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
            if (message.type == "file")
            {
                string directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(absolutePath, Convert.FromBase64String(message.data ?? ""));
            }
            else if (message.type == "delete")
            {
                if (File.Exists(absolutePath)) File.Delete(absolutePath);
                else if (Directory.Exists(absolutePath)) Directory.Delete(absolutePath, true);
            }
            else
            {
                string destination = ToAbsolutePath(message.path2);
                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (File.Exists(absolutePath))
                {
                    if (File.Exists(destination)) File.Delete(destination);
                    File.Move(absolutePath, destination);
                }
                else if (Directory.Exists(absolutePath))
                {
                    if (Directory.Exists(destination)) Directory.Delete(destination, true);
                    Directory.Move(absolutePath, destination);
                }
            }

            string importPath = message.type == "move" ? NormalizePath(message.path2) : path;
            if (!importPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                AssetDatabase.ImportAsset(importPath, ImportAssetOptions.ForceUpdate);
            ReloadSceneIfOpen(importPath);
        }
        catch (Exception exception)
        {
            Error = "Could not apply remote change to " + message.path + ": " + exception.Message;
        }
    }

    private void ReloadSceneIfOpen(string path)
    {
        if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) return;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!string.Equals(NormalizePath(scene.path), NormalizePath(path), StringComparison.OrdinalIgnoreCase)) continue;
            bool active = scene == SceneManager.GetActiveScene();
            EditorApplication.delayCall += () =>
            {
                suppressAssetEventsUntil = EditorApplication.timeSinceStartup + 3d;
                if (active)
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                else
                {
                    Scene existing = SceneManager.GetSceneByPath(path);
                    if (existing.IsValid()) EditorSceneManager.CloseScene(existing, true);
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }
            };
            break;
        }
    }

    private UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
    {
        ScheduleProjectSave();
        return modifications;
    }

    private void OnObjectChanges(ref ObjectChangeEventStream stream)
    {
        ScheduleProjectSave();
    }

    private void ScheduleProjectSave()
    {
        if (Connected && ShouldPublishAssetEvents && saveAt < 0d)
            saveAt = EditorApplication.timeSinceStartup + 0.2d;
    }

    public void CheckForUpdatesNow()
    {
        nextUpdateCheck = 0d;
        CheckForUpdate(true);
    }

    private async void CheckForUpdate(bool showStatus = false)
    {
        if (checkingForUpdate || EditorApplication.timeSinceStartup < nextUpdateCheck) return;
        checkingForUpdate = true;
        nextUpdateCheck = EditorApplication.timeSinceStartup + UpdateCheckInterval;
        if (showStatus)
        {
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
                QueueUpdateStatus("You’re up to date");
                return;
            }
            if (string.Equals(installedCommit, commit.sha, StringComparison.OrdinalIgnoreCase))
            {
                QueueUpdateStatus("You’re up to date");
                return;
            }

            string source;
            using (WebClient web = CreateGitHubClient())
                source = await web.DownloadStringTaskAsync(new Uri(string.Format(RawToolUrl, commit.sha)));

            QueueUpdateStatus("Installing update…");
            mainThread.Enqueue(() => InstallUpdate(commit.sha, source));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Collaboration: could not check GitHub for updates: " + exception.Message);
            QueueUpdateStatus("Update check failed");
        }
        finally
        {
            checkingForUpdate = false;
        }
    }

    private void QueueUpdateStatus(string status)
    {
        mainThread.Enqueue(() =>
        {
            UpdateStatus = status;
            Changed?.Invoke();
        });
    }

    private static WebClient CreateGitHubClient()
    {
        WebClient web = new WebClient();
        web.Headers[HttpRequestHeader.UserAgent] = "Unity-Collaboration-Tool";
        web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
        return web;
    }

    private static void InstallUpdate(string commitSha, string source)
    {
        try
        {
            string assetPath = FindToolAssetPath();
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("Collaboration: an update is available, but the installed CollaborationTool.cs could not be located.");
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!string.Equals(File.ReadAllText(absolutePath), source, StringComparison.Ordinal))
            {
                string temporaryPath = absolutePath + ".update";
                File.WriteAllText(temporaryPath, source, new UTF8Encoding(false));
                File.Copy(temporaryPath, absolutePath, true);
                File.Delete(temporaryPath);
                Debug.Log("Collaboration updated automatically from GitHub to commit " + commitSha.Substring(0, 7) + ".");
            }

            EditorPrefs.SetString(InstalledCommitKey, commitSha);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Collaboration: could not install the GitHub update: " + exception.Message);
        }
    }

    private static string FindToolAssetPath()
    {
        foreach (string guid in AssetDatabase.FindAssets("CollaborationTool t:MonoScript"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == typeof(CollaborationTool)) return path;
        }
        return null;
    }

    private void SaveChangedProjectState()
    {
        if (!Connected || !ShouldPublishAssetEvents) return;
        try
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
                {
                    EditorSceneManager.SaveScene(scene);
                    PublishImportedAsset(scene.path);
                }
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene.isDirty)
            {
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                PublishImportedAsset(stage.assetPath);
            }
            AssetDatabase.SaveAssets();
        }
        catch (Exception exception) { Error = "Could not save a changed asset for syncing: " + exception.Message; }
    }

    private static bool IsSafeProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string normalized = NormalizePath(path);
        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase)) return false;
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) + Path.DirectorySeparatorChar;
        string absolute = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        return absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => (path ?? "").Replace('\\', '/').TrimStart('/');
    private static string ToAbsolutePath(string path) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", NormalizePath(path)));

    private void StartCloudflared(int port)
    {
        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = "cloudflared",
            Arguments = "tunnel --no-autoupdate --url http://127.0.0.1:" + port,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        cloudflared = new Process { StartInfo = info, EnableRaisingEvents = true };
        DataReceivedEventHandler output = (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;
            Match match = Regex.Match(args.Data, @"https://[a-zA-Z0-9-]+\.trycloudflare\.com");
            if (match.Success)
                mainThread.Enqueue(() =>
                {
                    ShareLink = match.Value;
                    Status = "Server is online";
                    Changed?.Invoke();
                });
        };
        cloudflared.OutputDataReceived += output;
        cloudflared.ErrorDataReceived += output;
        cloudflared.Exited += (_, __) =>
        {
            if (Connected && string.IsNullOrEmpty(ShareLink))
                QueueError("The server stopped before creating a share link.");
        };
        try
        {
            cloudflared.Start();
            cloudflared.BeginOutputReadLine();
            cloudflared.BeginErrorReadLine();
        }
        catch (Exception exception)
        {
            Error = "The server link could not be created. Make sure the server service is installed and available, then try again. " + exception.Message;
            Changed?.Invoke();
        }
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

    private static int FindFreePort()
    {
        System.Net.Sockets.TcpListener probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static Uri MakeWebSocketUri(string link)
    {
        string value = link.Trim().TrimEnd('/');
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = "wss://" + value.Substring(8);
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = "ws://" + value.Substring(7);
        else if (!value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
                 !value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) value = "wss://" + value;
        return new Uri(value + "/collaboration/");
    }
}

internal sealed class CollaborationAssetSyncProcessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        CollaborationSession session = CollaborationTool.SharedSession;
        if (!session.ShouldPublishAssetEvents) return;

        for (int i = 0; i < movedAssets.Length; i++)
            session.PublishMovedAsset(movedFromAssetPaths[i], movedAssets[i]);
        foreach (string path in deletedAssets)
            session.PublishDeletedAsset(path);
        foreach (string path in importedAssets)
            session.PublishImportedAsset(path);
    }
}
