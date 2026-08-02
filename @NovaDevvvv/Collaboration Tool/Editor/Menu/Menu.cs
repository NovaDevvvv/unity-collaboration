using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class Menu : EditorWindow
{
    private const string PlugIcon = "\uf1e6";
    private const string HourglassIcon = "\uf254";
    private const string CopyIcon = "\uf0c5";
    private const string CheckIcon = "\uf00c";
    private const string EyeIcon = "\uf06e";
    private const string EyeSlashIcon = "\uf070";
    private const string ThemePreferenceKey = "NovaCollaboration.Theme";
    private const string GithubPatPreferenceKey = "NovaCollaboration.GithubPat";
    private const string InstalledCommitPreferenceKey = "NovaCollaboration.InstalledCommit";
    private const string LatestCommitUrl =
        "https://api.github.com/repos/novadevvvv/unity-collaboration/commits/main";
    private const string DownloadUrl =
        "https://raw.githubusercontent.com/novadevvvv/unity-collaboration/{0}/%40NovaDevvvv/Collaboration%20Tool/{1}";
    private static readonly string[] UpdateFiles =
    {
        "Editor.meta",
        "Editor/Menu.meta",
        "Editor/Menu/Menu.cs",
        "Editor/Menu/Menu.cs.meta",
        "Editor/Menu/Menu.uss",
        "Editor/Menu/Menu.uss.meta",
        "Editor/Menu/Menu.uxml",
        "Editor/Menu/Menu.uxml.meta",
        "Editor/Menu/Fonts.meta",
        "Editor/Menu/Fonts/Font Awesome 6 Free-Solid-900.otf",
        "Editor/Menu/Fonts/Font Awesome 6 Free-Solid-900.otf.meta",
        "Editor/Menu/Fonts/Font Awesome LICENSE.txt",
        "Editor/Menu/Fonts/Font Awesome LICENSE.txt.meta"
    };
    private const string CloudflaredPath =
        @"C:\Program Files (x86)\cloudflared\cloudflared.exe";
    private const string MenuAssetFolder =
        "Assets/@NovaDevvvv/Collaboration Tool/Editor/Menu/";
    private static readonly Regex TunnelUrlPattern = new Regex(
        @"https://[a-z0-9.-]+(?:\.trycloudflare\.com|\.lhr\.life|\.localhost\.run)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [SerializeField]
    private VisualTreeAsset m_VisualTreeAsset = default;

    private IVisualElementScheduledItem m_HourglassRotation;
    private IVisualElementScheduledItem m_ConnectCompletion;
    private IVisualElementScheduledItem m_IconSwitch;
    private IVisualElementScheduledItem m_IconExpand;
    private IVisualElementScheduledItem m_LoadingWheelAnimation;
    private IVisualElementScheduledItem m_StartTunnelScheduled;
    private IVisualElementScheduledItem m_CopyIconReset;
    private IVisualElementScheduledItem m_TabTransition;
    private IVisualElementScheduledItem m_UpdateSpinnerAnimation;
    private readonly List<IVisualElementScheduledItem> m_MenuRevealSchedules =
        new List<IVisualElementScheduledItem>();
    private Button m_ConnectButton;
    private Label m_ConnectIcon;
    private VisualElement m_MainActions;
    private VisualElement m_CreateServerModal;
    private VisualElement m_NameStep;
    private VisualElement m_WizardFooter;
    private VisualElement m_CreatingState;
    private VisualElement m_ServerReadyState;
    private Label m_CreatingStatus;
    private VisualElement m_LoadingWheel;
    private TextField m_DisplayNameInput;
    private TextField m_ServerCodeField;
    private Label m_CopyCodeIcon;
    private Label m_ViewCodeIcon;
    private VisualElement m_MainTabContent;
    private VisualElement m_ChatTabContent;
    private VisualElement m_SettingsPage;
    private VisualElement m_TabSlider;
    private Button m_MainTabButton;
    private Button m_ChatTabButton;
    private ScrollView m_PlayerList;
    private ScrollView m_ChatMessageList;
    private TextField m_ChatMessageInput;
    private VisualElement m_ServerContextMenu;
    private VisualElement m_PlayerContextMenu;
    private VisualElement m_UpdateOverlay;
    private VisualElement m_UpdateSpinner;
    private Label m_UpdateLoadingStatus;
    private Label m_UpdateStatusLabel;
    private TextField m_GithubPatField;
    private Button m_KickPlayerButton;
    private Button m_GoToPlayerButton;
    private VisualElement m_ContextPlayerRow;
    private RemotePlayer m_ContextPlayer;
    private Button m_WizardNextButton;
    private Button m_CloseServerButton;
    private bool m_IsConnecting;
    private float m_HourglassRotationDegrees;
    private float m_LoadingWheelDegrees;
    private float m_UpdateSpinnerDegrees;
    private bool m_UpdateCheckRunning;
    private double m_NextAutomaticUpdateCheck;
    private HttpListener m_LocalServer;
    private Process m_TunnelProcess;
    private int m_TunnelUrlFound;
    private string m_ServerCode;
    private string m_TunnelUrl;
    private int m_ServerPort;
    private bool m_UsingLhr;
    private bool m_IsHost;
    private bool m_IsServerCodeVisible;
    private string m_SceneChatOverlaySender;
    private string m_SceneChatOverlayTime;
    private string m_SceneChatOverlayMessage;
    private bool m_SceneChatOverlayIsOwn;
    private VisualElement m_SceneChatOverlayTarget;
    private double m_SceneChatOverlayExpiresAt;
    private Texture2D m_OwnSceneBubbleTexture;
    private Texture2D m_OtherSceneBubbleTexture;
    private AudioClip m_ButtonClickClip;
    private MethodInfo m_PlayPreviewClipMethod;
    private bool m_IsUnityFocused = true;
    private readonly object m_PlayerLock = new object();
    private readonly Dictionary<string, RemotePlayer> m_RemotePlayers = new Dictionary<string, RemotePlayer>();
    private string m_LocalPlayerId = Guid.NewGuid().ToString("N");
    private string m_LocalPlayerName;
    private IVisualElementScheduledItem m_PlayerRefresh;

    [Serializable]
    private sealed class RemotePlayer
    {
        public string id;
        public string name;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public long lastSeenTicks;
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui -= DrawSceneChatOverlay;
        SceneView.duringSceneGui += DrawSceneChatOverlay;
        EditorApplication.focusChanged -= HandleUnityFocusChanged;
        EditorApplication.focusChanged += HandleUnityFocusChanged;
        EditorApplication.update -= AutomaticUpdateTick;
        EditorApplication.update += AutomaticUpdateTick;
        m_NextAutomaticUpdateCheck = EditorApplication.timeSinceStartup + 10d;
    }

    [MenuItem("Collaboration/Open Window")]
    public static void ShowExample()
    {
        Menu wnd = GetWindow<Menu>();
        wnd.titleContent = new GUIContent("Menu");
    }

    [MenuItem("Collaboration/Refresh Styles")]
    public static void RefreshStyles()
    {
        AssetDatabase.ImportAsset(MenuAssetFolder + "Menu.uxml", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(MenuAssetFolder + "Menu.uss", ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        foreach (Menu window in Resources.FindObjectsOfTypeAll<Menu>())
        {
            window.rootVisualElement.Clear();
            window.CreateGUI();
            window.Repaint();
        }
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        if (m_VisualTreeAsset == null)
            m_VisualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MenuAssetFolder + "Menu.uxml");
        if (m_VisualTreeAsset == null)
        {
            root.Add(new HelpBox("The Collaboration menu layout could not be loaded.", HelpBoxMessageType.Error));
            return;
        }
        VisualElement content = m_VisualTreeAsset.Instantiate();
        content.style.flexGrow = 1;
        root.Add(content);

        m_ConnectButton = root.Q<Button>("collaborate-button");
        m_ConnectIcon = root.Q<Label>("connect-icon");
        if (m_ConnectButton != null && m_ConnectIcon != null)
        {
            m_ConnectButton.clicked += BeginConnecting;
        }

        SetupCreateServerFlow(root);
        SetupButtonSounds(root);
    }

    private void SetupCreateServerFlow(VisualElement root)
    {
        m_MainActions = root.Q<VisualElement>("main-actions");
        m_CreateServerModal = root.Q<VisualElement>("create-server-modal");
        m_NameStep = root.Q<VisualElement>("wizard-step-name");
        m_WizardFooter = root.Q<VisualElement>("wizard-footer");
        m_CreatingState = root.Q<VisualElement>("creating-server-state");
        m_ServerReadyState = root.Q<VisualElement>("server-ready-state");
        m_CreatingStatus = root.Q<Label>("creating-server-status");
        m_LoadingWheel = root.Q<VisualElement>("loading-wheel");
        m_DisplayNameInput = root.Q<TextField>("display-name-input");
        m_ServerCodeField = root.Q<TextField>("server-code-field");
        m_ServerCodeField.isReadOnly = true;
        m_ServerCodeField.isPasswordField = true;
        m_CopyCodeIcon = root.Q<Label>("copy-code-icon");
        m_ViewCodeIcon = root.Q<Label>("view-code-icon");
        m_MainTabContent = root.Q<VisualElement>("main-tab-content");
        m_ChatTabContent = root.Q<VisualElement>("chat-tab-content");
        m_SettingsPage = root.Q<VisualElement>("settings-page");
        m_TabSlider = root.Q<VisualElement>("server-tab-slider");
        m_MainTabButton = root.Q<Button>("main-tab-button");
        m_ChatTabButton = root.Q<Button>("chat-tab-button");
        m_PlayerList = root.Q<ScrollView>("player-list");
        m_ChatMessageList = root.Q<ScrollView>("chat-message-list");
        m_ChatMessageInput = root.Q<TextField>("chat-message-input");
        m_ServerContextMenu = root.Q<VisualElement>("app-context-menu");
        m_PlayerContextMenu = root.Q<VisualElement>("player-context-menu");
        m_UpdateOverlay = root.Q<VisualElement>("update-overlay");
        m_UpdateSpinner = root.Q<VisualElement>("update-spinner");
        m_UpdateLoadingStatus = root.Q<Label>("update-loading-status");
        m_UpdateStatusLabel = root.Q<Label>("update-status-label");
        m_GithubPatField = root.Q<TextField>("github-pat-field");
        m_KickPlayerButton = root.Q<Button>("kick-player-button");
        m_GoToPlayerButton = root.Q<Button>("goto-player-button");
        m_WizardNextButton = root.Q<Button>("wizard-next-button");
        m_CloseServerButton = root.Q<Button>("close-server-button");

        root.Q<Button>("create-server-button").clicked += OpenCreateServerFlow;
        root.Q<Button>("wizard-cancel-button").clicked += CloseCreateServerFlow;
        root.Q<Button>("creating-server-cancel-button").clicked += CancelServerCreation;
        root.Q<Button>("close-server-button").clicked += CloseRunningServer;
        root.Q<Button>("app-menu-button").clicked += ToggleServerContextMenu;
        root.Q<Button>("app-settings-button").clicked += ShowSettings;
        root.Q<Button>("refresh-installation-button").clicked += () => CheckForUpdates(true);
        root.Q<Button>("settings-back-button").clicked += HideSettings;
        root.Q<Button>("theme-light-button").clicked += () => ApplyTheme("light");
        root.Q<Button>("theme-dark-button").clicked += () => ApplyTheme("dark");
        root.Q<Button>("theme-extra-dark-button").clicked += () => ApplyTheme("extra-dark");
        m_GithubPatField.isPasswordField = true;
        m_GithubPatField.SetValueWithoutNotify(EditorPrefs.GetString(GithubPatPreferenceKey, string.Empty));
        m_GithubPatField.RegisterValueChangedCallback(evt =>
            EditorPrefs.SetString(GithubPatPreferenceKey, evt.newValue.Trim()));
        m_KickPlayerButton.clicked += KickContextPlayer;
        m_GoToPlayerButton.clicked += GoToContextPlayer;
        m_MainTabButton.clicked += () => ShowServerTab(true);
        m_ChatTabButton.clicked += () => ShowServerTab(false);
        root.Q<Button>("chat-send-button").clicked += SendChatMessage;
        root.Q<Button>("copy-code-button").clicked += CopyServerCode;
        root.Q<Button>("view-code-button").clicked += ToggleServerCodeVisibility;
        m_ChatMessageInput.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                SendChatMessage();
                evt.StopPropagation();
            }
        });
        m_WizardNextButton.clicked += BeginServerCreation;
        m_DisplayNameInput.RegisterValueChangedCallback(_ => UpdateCreateNavigation());
        root.RegisterCallback<PointerDownEvent>(evt =>
        {
            VisualElement target = evt.target as VisualElement;
            if ((target == null || !m_ServerContextMenu.Contains(target)) &&
                (target == null || !m_PlayerContextMenu.Contains(target)))
            {
                HideCustomMenus();
            }
        });

        HideCustomMenus();
        ApplyTheme(EditorPrefs.GetString(ThemePreferenceKey, "extra-dark"), false);
    }

    private void ApplyTheme(string theme, bool save = true)
    {
        VisualElement root = rootVisualElement;
        foreach (string value in new[] { "light", "dark", "extra-dark" })
        {
            root.EnableInClassList("theme-" + value, value == theme);
            Button choice = root.Q<Button>("theme-" + value + "-button");
            choice?.EnableInClassList("is-theme-selected", value == theme);
        }
        if (save) EditorPrefs.SetString(ThemePreferenceKey, theme);
    }

    private void ShowSettings()
    {
        HideCustomMenus();
        m_SettingsPage.RemoveFromClassList("is-hidden");
    }

    private void HideSettings()
    {
        m_SettingsPage.AddToClassList("is-hidden");
        m_NextAutomaticUpdateCheck = EditorApplication.timeSinceStartup + 15d;
    }

    private void AutomaticUpdateTick()
    {
        if (EditorApplication.timeSinceStartup < m_NextAutomaticUpdateCheck)
            return;

        if ((m_SettingsPage != null && !m_SettingsPage.ClassListContains("is-hidden")) ||
            m_IsHost || m_IsConnecting)
        {
            m_NextAutomaticUpdateCheck = EditorApplication.timeSinceStartup + 15d;
            return;
        }

        m_NextAutomaticUpdateCheck = EditorApplication.timeSinceStartup + 60d;
        CheckForUpdates(false);
    }

    private async void CheckForUpdates(bool manual)
    {
        if (m_UpdateCheckRunning)
            return;

        m_UpdateCheckRunning = true;
        SetUpdateStatus(manual ? "Checking GitHub and verifying the installation..." : "Checking for updates...");
        try
        {
            string latestCommit;
            using (WebClient web = CreateGithubClient())
            {
                string json = await web.DownloadStringTaskAsync(new Uri(
                    LatestCommitUrl + "?menu=" + DateTime.UtcNow.Ticks));
                Match match = Regex.Match(json, "\\\"sha\\\"\\s*:\\s*\\\"([0-9a-f]{40})\\\"",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                    throw new InvalidDataException("GitHub did not return a valid commit.");
                latestCommit = match.Groups[1].Value;
            }

            string installedCommit = EditorPrefs.GetString(InstalledCommitPreferenceKey, string.Empty);
            if (!manual && string.IsNullOrEmpty(installedCommit))
            {
                EditorPrefs.SetString(InstalledCommitPreferenceKey, latestCommit);
                SetUpdateStatus("Updates are checked automatically every minute.");
                return;
            }
            if (!manual && string.Equals(installedCommit, latestCommit, StringComparison.OrdinalIgnoreCase))
            {
                SetUpdateStatus("Up to date. Checked " + DateTime.Now.ToString("t") + ".");
                return;
            }

            SetUpdateStatus(manual
                ? "Verifying and downloading the current installation..."
                : "A new version was found. Downloading it in the background...");
            await DownloadAndInstallUpdate(latestCommit, manual);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning("Collaboration update check failed: " + exception.Message);
            SetUpdateStatus("Update check failed: " + exception.Message);
            HideUpdateOverlay();
        }
        finally
        {
            m_UpdateCheckRunning = false;
        }
    }

    private async Task DownloadAndInstallUpdate(string commit, bool showWhileDownloading)
    {
        var downloads = new Dictionary<string, byte[]>();
        if (showWhileDownloading)
            ShowUpdateOverlay("Downloading the latest version...");
        using (WebClient web = CreateGithubClient())
        {
            foreach (string fileName in UpdateFiles)
            {
                downloads[fileName] = await web.DownloadDataTaskAsync(
                    new Uri(string.Format(DownloadUrl, commit, fileName)));
            }
        }

        string menuSource = Encoding.UTF8.GetString(downloads["Editor/Menu/Menu.cs"]);
        if (!menuSource.Contains("public class Menu : EditorWindow"))
            throw new InvalidDataException("The downloaded update is not a valid Collaboration menu.");

        if (!showWhileDownloading)
        {
            ShowUpdateOverlay("Installing the downloaded update...");
            await Task.Delay(240);
        }
        SetUpdateOverlayStatus("Installing update...");
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string installRoot = Path.GetFullPath(Path.Combine(
            projectRoot, "Assets", "@NovaDevvvv", "Collaboration Tool"));
        string requiredPrefix = projectRoot + Path.DirectorySeparatorChar;
        if (!installRoot.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update directory resolved outside the Unity project.");

        foreach (KeyValuePair<string, byte[]> download in downloads)
        {
            string destination = Path.GetFullPath(Path.Combine(installRoot, download.Key));
            if (!destination.StartsWith(installRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An update file resolved outside the install directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporaryPath = destination + ".download";
            File.WriteAllBytes(temporaryPath, download.Value);
            File.Copy(temporaryPath, destination, true);
            File.Delete(temporaryPath);
        }

        EditorPrefs.SetString(InstalledCommitPreferenceKey, commit);
        SetUpdateOverlayStatus("Update installed. Reloading scripts...");
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
    }

    private static WebClient CreateGithubClient()
    {
        WebClient web = new WebClient();
        web.Headers[HttpRequestHeader.UserAgent] = "Unity-Collaboration-Menu";
        web.Headers[HttpRequestHeader.CacheControl] = "no-cache";
        web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
        string token = EditorPrefs.GetString(GithubPatPreferenceKey, string.Empty).Trim();
        if (!string.IsNullOrEmpty(token))
            web.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
        return web;
    }

    private void ShowUpdateOverlay(string status)
    {
        if (m_UpdateOverlay == null)
            return;
        SetUpdateOverlayStatus(status);
        m_UpdateOverlay.RemoveFromClassList("is-hidden");
        m_UpdateOverlay.schedule.Execute(() => m_UpdateOverlay.RemoveFromClassList("is-transparent"))
            .StartingIn(10);
        StartUpdateSpinner();
    }

    private void HideUpdateOverlay()
    {
        if (m_UpdateOverlay == null || m_UpdateOverlay.ClassListContains("is-hidden"))
            return;
        StopUpdateSpinner();
        m_UpdateOverlay.AddToClassList("is-transparent");
        m_UpdateOverlay.schedule.Execute(() => m_UpdateOverlay.AddToClassList("is-hidden")).StartingIn(220);
    }

    private void SetUpdateOverlayStatus(string status)
    {
        if (m_UpdateLoadingStatus != null)
            m_UpdateLoadingStatus.text = status;
    }

    private void SetUpdateStatus(string status)
    {
        if (m_UpdateStatusLabel != null)
            m_UpdateStatusLabel.text = status;
    }

    private void StartUpdateSpinner()
    {
        m_UpdateSpinnerAnimation?.Pause();
        m_UpdateSpinnerDegrees = 0f;
        if (m_UpdateSpinner == null)
            return;
        m_UpdateSpinnerAnimation = m_UpdateSpinner.schedule.Execute(() =>
        {
            m_UpdateSpinnerDegrees = (m_UpdateSpinnerDegrees + 12f) % 360f;
            m_UpdateSpinner.style.rotate = new Rotate(new Angle(m_UpdateSpinnerDegrees, AngleUnit.Degree));
        }).Every(16);
    }

    private void StopUpdateSpinner()
    {
        m_UpdateSpinnerAnimation?.Pause();
        m_UpdateSpinnerAnimation = null;
    }

    private void OpenCreateServerFlow()
    {
        m_NameStep.RemoveFromClassList("is-hidden");
        m_NameStep.RemoveFromClassList("is-transparent");
        m_WizardFooter.RemoveFromClassList("is-hidden");
        m_WizardFooter.RemoveFromClassList("is-transparent");
        m_CreatingState.AddToClassList("is-hidden");
        m_CreatingState.AddToClassList("is-transparent");
        m_ServerReadyState.AddToClassList("is-hidden");
        m_ServerReadyState.AddToClassList("is-transparent");
        m_CreateServerModal.RemoveFromClassList("server-dashboard");
        SetServerCodeVisibility(false);
        m_ChatMessageList.Clear();
        ShowServerTab(true);
        UpdateCreateNavigation();

        m_MainActions.AddToClassList("is-transparent");
        m_MainActions.schedule.Execute(() =>
        {
            m_MainActions.AddToClassList("is-hidden");
            m_CreateServerModal.RemoveFromClassList("is-hidden");
            m_CreateServerModal.schedule.Execute(() =>
            {
                m_CreateServerModal.RemoveFromClassList("is-transparent");
                m_DisplayNameInput.Focus();
            }).StartingIn(20);
        }).StartingIn(200);
    }

    private void CloseCreateServerFlow()
    {
        m_CreateServerModal.AddToClassList("is-transparent");
        m_CreateServerModal.schedule.Execute(() =>
        {
            m_CreateServerModal.AddToClassList("is-hidden");
            m_MainActions.RemoveFromClassList("is-hidden");
            m_MainActions.schedule.Execute(() =>
            {
                m_MainActions.RemoveFromClassList("is-transparent");
            }).StartingIn(20);
        }).StartingIn(200);
    }

    private void UpdateCreateNavigation()
    {
        m_WizardNextButton.SetEnabled(
            !string.IsNullOrWhiteSpace(m_DisplayNameInput.value));
    }

    private void BeginServerCreation()
    {
        if (string.IsNullOrWhiteSpace(m_DisplayNameInput.value))
        {
            return;
        }

        m_NameStep.AddToClassList("is-transparent");
        m_WizardFooter.AddToClassList("is-transparent");
        m_NameStep.schedule.Execute(() =>
        {
            m_NameStep.AddToClassList("is-hidden");
            m_WizardFooter.AddToClassList("is-hidden");
            m_ServerReadyState.AddToClassList("is-hidden");
            m_CreatingStatus.text = "Creating Server...";
            m_ServerCode = UnityEngine.Random.Range(0, 100000000).ToString("D8");
            m_IsHost = true;
            m_LocalPlayerName = m_DisplayNameInput.value.Trim();
            lock (m_PlayerLock)
            {
                m_RemotePlayers.Clear();
                m_RemotePlayers[m_LocalPlayerId] = new RemotePlayer
                {
                    id = m_LocalPlayerId, name = m_LocalPlayerName,
                    cameraPosition = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero,
                    cameraRotation = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.rotation : Quaternion.identity
                };
            }
            m_CreatingState.RemoveFromClassList("is-hidden");
            StartLoadingWheel();
            m_StartTunnelScheduled = m_CreatingState.schedule.Execute(() =>
            {
                m_StartTunnelScheduled = null;
                m_CreatingState.RemoveFromClassList("is-transparent");
                StartTunnel();
            }).StartingIn(20);
        }).StartingIn(200);
    }

    private void StartLoadingWheel()
    {
        m_LoadingWheelAnimation?.Pause();
        m_LoadingWheelDegrees = 0f;
        m_LoadingWheel.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
        m_LoadingWheelAnimation = m_LoadingWheel.schedule.Execute(() =>
        {
            m_LoadingWheelDegrees = (m_LoadingWheelDegrees + 12f) % 360f;
            m_LoadingWheel.style.rotate = new Rotate(
                new Angle(m_LoadingWheelDegrees, AngleUnit.Degree));
        }).Every(16);
    }

    private void StopLoadingWheel()
    {
        m_LoadingWheelAnimation?.Pause();
        m_LoadingWheelAnimation = null;
    }

    private void CancelServerCreation()
    {
        Interlocked.Exchange(ref m_TunnelUrlFound, 1);
        m_StartTunnelScheduled?.Pause();
        m_StartTunnelScheduled = null;
        StopLoadingWheel();
        StopServer();
        m_IsHost = false;
        m_CloseServerButton?.AddToClassList("is-hidden");
        m_PlayerRefresh?.Pause();
        m_PlayerRefresh = null;
        CloseCreateServerFlow();
    }

    private void CloseRunningServer()
    {
        HideCustomMenus();
        Interlocked.Exchange(ref m_TunnelUrlFound, 1);
        m_StartTunnelScheduled?.Pause();
        m_StartTunnelScheduled = null;
        StopLoadingWheel();
        StopServer();
        m_IsHost = false;
        m_CloseServerButton?.AddToClassList("is-hidden");
        m_PlayerRefresh?.Pause();
        m_PlayerRefresh = null;
        CloseCreateServerFlow();
    }

    private void CopyServerCode()
    {
        if (string.IsNullOrEmpty(m_ServerCode))
        {
            return;
        }

        EditorGUIUtility.systemCopyBuffer = m_ServerCode;
        m_CopyCodeIcon.text = CheckIcon;
        m_CopyIconReset?.Pause();
        m_CopyIconReset = m_CopyCodeIcon.schedule.Execute(() =>
        {
            m_CopyCodeIcon.text = CopyIcon;
            m_CopyIconReset = null;
        }).StartingIn(1400);
    }

    private void ToggleServerCodeVisibility()
    {
        SetServerCodeVisibility(!m_IsServerCodeVisible);
    }

    private void SetServerCodeVisibility(bool visible)
    {
        m_IsServerCodeVisible = visible;
        m_ServerCodeField.isPasswordField = !visible;
        m_ViewCodeIcon.text = visible ? EyeSlashIcon : EyeIcon;
    }

    private void ShowServerTab(bool showMain)
    {
        HideCustomMenus();
        m_SettingsPage?.AddToClassList("is-hidden");
        if (m_TabTransition != null)
        {
            return;
        }

        m_MainTabButton.EnableInClassList("is-selected", showMain);
        m_ChatTabButton.EnableInClassList("is-selected", !showMain);
        m_TabSlider?.EnableInClassList("show-chat", !showMain);

        VisualElement incoming = showMain ? m_MainTabContent : m_ChatTabContent;
        VisualElement outgoing = showMain ? m_ChatTabContent : m_MainTabContent;
        if (!incoming.ClassListContains("is-hidden"))
        {
            if (!showMain)
            {
                m_ChatMessageInput.Focus();
            }
            return;
        }

        m_TabTransition?.Pause();
        m_PlayerRefresh?.Pause();
        outgoing.AddToClassList("is-tab-transparent");
        m_TabTransition = outgoing.schedule.Execute(() =>
        {
            outgoing.AddToClassList("is-hidden");
            outgoing.RemoveFromClassList("is-tab-transparent");
            incoming.AddToClassList("is-tab-transparent");
            incoming.RemoveFromClassList("is-hidden");
            incoming.schedule.Execute(() =>
            {
                incoming.RemoveFromClassList("is-tab-transparent");
                m_TabTransition = null;
                if (!showMain)
                {
                    m_ChatMessageInput.Focus();
                }
            }).StartingIn(20);
        }).StartingIn(160);
    }

    private void PopulatePlayerList()
    {
        m_PlayerList.Clear();
        AddPlayerRow(
            m_DisplayNameInput.value.Trim(),
            0,
            GetPlayerColor(m_DisplayNameInput.value),
            true);
        lock (m_PlayerLock)
        {
            foreach (RemotePlayer player in m_RemotePlayers.Values)
            {
                if (player.id == m_LocalPlayerId) continue;
                AddPlayerRow(player.name, 0, GetPlayerColor(player.name), false, player);
            }
        }
    }

    private void AddPlayerRow(
        string playerName,
        int pingMilliseconds,
        Color playerColor,
        bool isLocalPlayer,
        RemotePlayer remotePlayer = null)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("player-row");
        row.userData = remotePlayer;

        VisualElement color = new VisualElement();
        color.AddToClassList("player-color");
        color.style.backgroundColor = playerColor;

        Label name = new Label(isLocalPlayer ? $"{playerName} (Host)" : playerName);
        name.AddToClassList("player-name");

        Label ping = new Label($"{pingMilliseconds} ms");
        ping.AddToClassList("player-ping");

        row.Add(color);
        row.Add(name);
        row.Add(ping);
        m_PlayerList.Add(row);

        row.RegisterCallback<ContextClickEvent>(evt =>
        {
            if (isLocalPlayer) return;
            ShowPlayerContextMenu(evt.mousePosition, row, isLocalPlayer);
            evt.StopPropagation();
        });
    }

    private void ToggleServerContextMenu()
    {
        if (m_ServerContextMenu.ClassListContains("is-menu-hidden"))
        {
            ShowCascadingMenu(m_ServerContextMenu);
        }
        else
        {
            HideCustomMenus();
        }
    }

    private void ShowPlayerContextMenu(
        Vector2 panelPosition,
        VisualElement playerRow,
        bool isLocalPlayer)
    {
        if (isLocalPlayer) return;
        HideCustomMenus();
        m_ContextPlayerRow = playerRow;
        m_ContextPlayer = playerRow.userData as RemotePlayer;
        m_KickPlayerButton.SetEnabled(m_IsHost && !isLocalPlayer);
        Vector2 localPosition = m_PlayerContextMenu.parent.WorldToLocal(panelPosition);
        m_PlayerContextMenu.style.left = localPosition.x;
        m_PlayerContextMenu.style.top = localPosition.y;
        ShowCascadingMenu(m_PlayerContextMenu);
    }

    private void ShowCascadingMenu(VisualElement menu)
    {
        CancelMenuRevealSchedules();
        if (menu != m_ServerContextMenu)
        {
            HideCustomMenu(m_ServerContextMenu);
        }
        if (menu != m_PlayerContextMenu)
        {
            HideCustomMenu(m_PlayerContextMenu);
        }

        menu.pickingMode = PickingMode.Position;
        menu.RemoveFromClassList("is-menu-hidden");
        List<VisualElement> items = menu.Query<VisualElement>(className: "custom-menu-item").ToList();
        for (int index = 0; index < items.Count; index++)
        {
            VisualElement item = items[index];
            IVisualElementScheduledItem reveal = menu.schedule.Execute(() =>
            {
                item.AddToClassList("is-revealed");
            }).StartingIn(35 + index * 65);
            m_MenuRevealSchedules.Add(reveal);
        }
    }

    private void HideCustomMenus()
    {
        CancelMenuRevealSchedules();
        HideCustomMenu(m_ServerContextMenu);
        HideCustomMenu(m_PlayerContextMenu);
    }

    private static void HideCustomMenu(VisualElement menu)
    {
        if (menu == null)
        {
            return;
        }

        menu.AddToClassList("is-menu-hidden");
        menu.pickingMode = PickingMode.Ignore;
        menu.Query<VisualElement>(className: "custom-menu-item").ForEach(
            item => item.RemoveFromClassList("is-revealed"));
    }

    private void CancelMenuRevealSchedules()
    {
        foreach (IVisualElementScheduledItem scheduledItem in m_MenuRevealSchedules)
        {
            scheduledItem?.Pause();
        }
        m_MenuRevealSchedules.Clear();
    }

    private void KickContextPlayer()
    {
        if (!m_IsHost || m_ContextPlayerRow == null)
        {
            return;
        }

        m_ContextPlayerRow.RemoveFromHierarchy();
        m_ContextPlayerRow = null;
        HideCustomMenus();
    }

    private void GoToContextPlayer()
    {
        if (m_ContextPlayer == null || SceneView.lastActiveSceneView == null) return;
        SceneView view = SceneView.lastActiveSceneView;
        view.pivot = m_ContextPlayer.cameraPosition;
        view.rotation = m_ContextPlayer.cameraRotation;
        view.Repaint();
        HideCustomMenus();
    }

    private static Color GetPlayerColor(string playerName)
    {
        Color[] colors =
        {
            new Color32(64, 156, 255, 255),
            new Color32(72, 201, 123, 255),
            new Color32(255, 178, 66, 255),
            new Color32(190, 112, 255, 255),
            new Color32(255, 104, 137, 255)
        };

        int hash = string.IsNullOrEmpty(playerName) ? 0 : playerName.GetHashCode();
        int index = (hash & int.MaxValue) % colors.Length;
        return colors[index];
    }

    private void SendChatMessage()
    {
        string message = m_ChatMessageInput.value?.Trim();
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        AddChatMessage(message, true, "You");
        m_ChatMessageInput.SetValueWithoutNotify(string.Empty);
        m_ChatMessageInput.Focus();
    }

    private void AddChatMessage(string message, bool isOwnMessage, string senderName)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("chat-message-row");
        row.AddToClassList(isOwnMessage ? "is-own" : "is-other");

        string timestamp = DateTime.Now.ToString("h:mm tt");
        Label metadata = new Label($"{senderName} • {timestamp}");
        metadata.AddToClassList("chat-message-meta");

        Label bubble = new Label(message);
        bubble.AddToClassList("chat-message-bubble");
        row.Add(metadata);
        row.Add(bubble);
        m_ChatMessageList.Add(row);
        m_ChatMessageList.schedule.Execute(() => m_ChatMessageList.ScrollTo(row));
        ShowSceneChatOverlay(senderName, timestamp, message, isOwnMessage, row);
        if (!isOwnMessage && !m_IsUnityFocused)
        {
            SendWindowsNotification(senderName, message);
        }
    }

    private void HandleUnityFocusChanged(bool focused)
    {
        m_IsUnityFocused = focused;
    }

    private static void SendWindowsNotification(string senderName, string message)
    {
#if UNITY_EDITOR_WIN
        try
        {
            string safeSender = SecurityElement.Escape(senderName) ?? "New message";
            string safeMessage = SecurityElement.Escape(message) ?? string.Empty;
            string script =
                "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime] > $null;" +
                "[Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType=WindowsRuntime] > $null;" +
                "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType=WindowsRuntime] > $null;" +
                "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument;" +
                "$xml.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text>" +
                safeSender + "</text><text>" + safeMessage +
                "</text></binding></visual></toast>');" +
                "$toast = New-Object Windows.UI.Notifications.ToastNotification $xml;" +
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Unity Editor').Show($toast);";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning($"Could not show chat notification: {exception.Message}");
        }
#endif
    }

    private void SetupButtonSounds(VisualElement root)
    {
        CreateButtonClickClip();
        root.Query<Button>().ForEach(button => button.clicked += PlayButtonClickSound);
    }

    private void CreateButtonClickClip()
    {
        if (m_ButtonClickClip != null)
        {
            return;
        }

        const int sampleRate = 44100;
        const int sampleCount = 1764;
        float[] samples = new float[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            float progress = index / (float)sampleCount;
            float envelope = Mathf.Pow(1f - progress, 3f);
            samples[index] = Mathf.Sin(2f * Mathf.PI * 760f * index / sampleRate) *
                envelope * 0.12f;
        }

        m_ButtonClickClip = AudioClip.Create(
            "Collaboration Button Click",
            sampleCount,
            1,
            sampleRate,
            false);
        m_ButtonClickClip.hideFlags = HideFlags.HideAndDontSave;
        m_ButtonClickClip.SetData(samples, 0);

        Type audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtil == null)
        {
            return;
        }

        foreach (MethodInfo method in audioUtil.GetMethods(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            ParameterInfo[] parameters = method.GetParameters();
            if ((method.Name == "PlayPreviewClip" || method.Name == "PlayClip") &&
                parameters.Length > 0 &&
                parameters[0].ParameterType == typeof(AudioClip))
            {
                m_PlayPreviewClipMethod = method;
                break;
            }
        }
    }

    private void PlayButtonClickSound()
    {
        if (m_ButtonClickClip == null || m_PlayPreviewClipMethod == null)
        {
            return;
        }

        try
        {
            ParameterInfo[] parameters = m_PlayPreviewClipMethod.GetParameters();
            object[] arguments = new object[parameters.Length];
            arguments[0] = m_ButtonClickClip;
            for (int index = 1; index < parameters.Length; index++)
            {
                arguments[index] = parameters[index].HasDefaultValue
                    ? parameters[index].DefaultValue
                    : parameters[index].ParameterType == typeof(bool)
                        ? (object)false
                        : 0;
            }
            m_PlayPreviewClipMethod.Invoke(null, arguments);
        }
        catch (Exception)
        {
            // Audio preview APIs vary slightly between Unity versions.
        }
    }

    private void ShowSceneChatOverlay(
        string senderName,
        string timestamp,
        string message,
        bool isOwnMessage,
        VisualElement target)
    {
        m_SceneChatOverlaySender = senderName;
        m_SceneChatOverlayTime = timestamp;
        m_SceneChatOverlayMessage = message;
        m_SceneChatOverlayIsOwn = isOwnMessage;
        m_SceneChatOverlayTarget = target;
        m_SceneChatOverlayExpiresAt = EditorApplication.timeSinceStartup + 5d;
        EditorApplication.update -= RepaintSceneChatOverlay;
        EditorApplication.update += RepaintSceneChatOverlay;
        SceneView.RepaintAll();
    }

    private void RepaintSceneChatOverlay()
    {
        if (EditorApplication.timeSinceStartup >= m_SceneChatOverlayExpiresAt)
        {
            EditorApplication.update -= RepaintSceneChatOverlay;
        }

        SceneView.RepaintAll();
    }

    private void DrawSceneChatOverlay(SceneView sceneView)
    {
        if (string.IsNullOrEmpty(m_SceneChatOverlayMessage) ||
            EditorApplication.timeSinceStartup >= m_SceneChatOverlayExpiresAt)
        {
            return;
        }

        Handles.BeginGUI();
        const float width = 300f;
        const float bubbleWidth = 250f;
        GUIStyle metadataStyle = new GUIStyle(EditorStyles.miniLabel);
        metadataStyle.normal.textColor = new Color(0.8f, 0.8f, 0.82f, 1f);

        GUIStyle bubbleStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            padding = new RectOffset(12, 12, 8, 8),
            border = new RectOffset(12, 12, 12, 12)
        };
        bubbleStyle.normal.textColor = m_SceneChatOverlayIsOwn
            ? Color.white
            : new Color(0.08f, 0.08f, 0.09f, 1f);
        bubbleStyle.normal.background = GetSceneBubbleTexture(m_SceneChatOverlayIsOwn);

        float bubbleHeight = Mathf.Max(
            34f,
            bubbleStyle.CalcHeight(new GUIContent(m_SceneChatOverlayMessage), bubbleWidth));
        float bubbleX = m_SceneChatOverlayIsOwn ? 12f + width - bubbleWidth : 12f;
        Rect metadataRect = new Rect(bubbleX + 8f, 10f, bubbleWidth - 16f, 16f);
        Rect bubbleRect = new Rect(bubbleX, 27f, bubbleWidth, bubbleHeight);
        Rect clickRect = new Rect(12f, 8f, width, bubbleHeight + 23f);

        GUI.Label(
            metadataRect,
            $"{m_SceneChatOverlaySender} • {m_SceneChatOverlayTime}",
            metadataStyle);
        GUI.Label(bubbleRect, m_SceneChatOverlayMessage, bubbleStyle);
        if (GUI.Button(clickRect, GUIContent.none, GUIStyle.none))
        {
            PlayButtonClickSound();
            OpenChatAtSceneMessage();
        }
        Handles.EndGUI();
    }

    private Texture2D GetSceneBubbleTexture(bool isOwnMessage)
    {
        Texture2D texture = isOwnMessage
            ? m_OwnSceneBubbleTexture
            : m_OtherSceneBubbleTexture;
        if (texture != null)
        {
            return texture;
        }

        Color color = isOwnMessage
            ? new Color32(46, 116, 235, 255)
            : Color.white;
        texture = CreateRoundedTexture(color);
        if (isOwnMessage)
        {
            m_OwnSceneBubbleTexture = texture;
        }
        else
        {
            m_OtherSceneBubbleTexture = texture;
        }

        return texture;
    }

    private static Texture2D CreateRoundedTexture(Color color)
    {
        const int size = 32;
        const float radius = 11f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nearestX = Mathf.Clamp(x, radius, size - 1f - radius);
                float nearestY = Mathf.Clamp(y, radius, size - 1f - radius);
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(nearestX, nearestY));
                Color pixel = color;
                pixel.a *= Mathf.Clamp01(radius + 0.5f - distance);
                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();
        return texture;
    }

    private void OpenChatAtSceneMessage()
    {
        Focus();
        ShowServerTab(false);
        if (m_SceneChatOverlayTarget == null || m_SceneChatOverlayTarget.panel == null)
        {
            return;
        }

        VisualElement target = m_SceneChatOverlayTarget;
        m_ChatMessageList.schedule.Execute(() => m_ChatMessageList.ScrollTo(target));
        target.AddToClassList("is-highlighted");
        target.schedule.Execute(() => target.RemoveFromClassList("is-highlighted"))
            .StartingIn(2500);
        m_SceneChatOverlayExpiresAt = 0d;
        SceneView.RepaintAll();
    }

    private void StartTunnel()
    {
        StopServer();
        Interlocked.Exchange(ref m_TunnelUrlFound, 0);

        try
        {
            int port = FindAvailablePort();
            m_ServerPort = port;
            m_LocalServer = new HttpListener();
            m_LocalServer.Prefixes.Add($"http://127.0.0.1:{port}/");
            m_LocalServer.Start();
            _ = RunLocalServerAsync(m_LocalServer);

            if (File.Exists(CloudflaredPath))
            {
                StartTunnelProcess(CloudflaredPath,
                    $"tunnel --no-autoupdate --url http://127.0.0.1:{port}", false);
            }
            else
            {
                StartLhrFallback();
            }
        }
        catch (Exception exception)
        {
            ShowTunnelError(exception.Message);
        }
    }

    private void StartTunnelProcess(string executable, string arguments, bool usingLhr)
    {
        m_UsingLhr = usingLhr;
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = executable, Arguments = arguments, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        m_TunnelProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        m_TunnelProcess.OutputDataReceived += HandleTunnelOutput;
        m_TunnelProcess.ErrorDataReceived += HandleTunnelOutput;
        m_TunnelProcess.Exited += HandleTunnelExited;
        m_TunnelProcess.Start();
        m_TunnelProcess.BeginOutputReadLine();
        m_TunnelProcess.BeginErrorReadLine();
    }

    private void StartLhrFallback()
    {
        try
        {
            m_CreatingStatus.text = "Primary tunnel unavailable. Trying LHR...";
            StartTunnelProcess("ssh.exe",
                $"-o StrictHostKeyChecking=no -o ServerAliveInterval=30 -R 80:localhost:{m_ServerPort} nokey@localhost.run", true);
        }
        catch (Exception exception)
        {
            ShowTunnelError("Cloudflare and LHR failed: " + exception.Message);
        }
    }

    private static int FindAvailablePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task RunLocalServerAsync(HttpListener listener)
    {
        byte[] response = Encoding.UTF8.GetBytes("Collaboration server is running.");
        while (listener.IsListening)
        {
            try
            {
                HttpListenerContext context = await listener.GetContextAsync();
                string path = context.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();
                if (path == "join" || path == "heartbeat")
                {
                    UpdateRemotePlayer(context.Request.QueryString);
                    response = Encoding.UTF8.GetBytes(BuildPlayerSnapshot());
                }
                else response = Encoding.UTF8.GetBytes("Collaboration server is running.");
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = response.Length;
                await context.Response.OutputStream.WriteAsync(response, 0, response.Length);
                context.Response.Close();
            }
            catch (Exception)
            {
                if (listener.IsListening)
                {
                    throw;
                }
            }
        }
    }

    private void UpdateRemotePlayer(System.Collections.Specialized.NameValueCollection query)
    {
        string id = query["id"];
        if (string.IsNullOrEmpty(id)) return;
        float parsed;
        RemotePlayer player = new RemotePlayer { id = id, name = query["name"] ?? "Player", cameraRotation = Quaternion.identity, lastSeenTicks = DateTime.UtcNow.Ticks };
        if (float.TryParse(query["px"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) player.cameraPosition.x = parsed;
        if (float.TryParse(query["py"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) player.cameraPosition.y = parsed;
        if (float.TryParse(query["pz"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) player.cameraPosition.z = parsed;
        lock (m_PlayerLock) m_RemotePlayers[id] = player;
        EditorApplication.delayCall += PopulatePlayerList;
    }

    private string BuildPlayerSnapshot()
    {
        StringBuilder result = new StringBuilder();
        lock (m_PlayerLock)
        {
            RemotePlayer local;
            if (m_RemotePlayers.TryGetValue(m_LocalPlayerId, out local) && SceneView.lastActiveSceneView != null)
            {
                local.cameraPosition = SceneView.lastActiveSceneView.pivot;
                local.cameraRotation = SceneView.lastActiveSceneView.rotation;
            }
            foreach (RemotePlayer player in m_RemotePlayers.Values)
                result.Append(player.id).Append('\t').Append(player.name.Replace("\t", " ")).Append('\t')
                    .Append(player.cameraPosition.x.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                    .Append(player.cameraPosition.y.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                    .Append(player.cameraPosition.z.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }
        return result.ToString();
    }

    private void HandleTunnelOutput(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Data))
        {
            return;
        }

        Match match = TunnelUrlPattern.Match(args.Data);
        if (!match.Success || Interlocked.Exchange(ref m_TunnelUrlFound, 1) != 0)
        {
            return;
        }

        string tunnelUrl = match.Value;
        EditorApplication.delayCall += () => ShowTunnelLink(tunnelUrl);
    }

    private void HandleTunnelExited(object sender, EventArgs args)
    {
        if (Interlocked.CompareExchange(ref m_TunnelUrlFound, 0, 0) == 0)
        {
            EditorApplication.delayCall += () => { if (!m_UsingLhr) StartLhrFallback(); else ShowTunnelError("Cloudflare and LHR could not create a public link."); };
        }
    }

    private void ShowTunnelLink(string tunnelUrl)
    {
        if (m_ServerReadyState == null)
        {
            return;
        }

        StopLoadingWheel();
        m_CreatingState.schedule.Execute(() =>
        {
            m_CreatingState.AddToClassList("is-transparent");
            m_CreatingState.schedule.Execute(() =>
            {
                m_CreatingState.AddToClassList("is-hidden");
                m_TunnelUrl = tunnelUrl;
                m_ServerCode = m_ServerCode + "." + Convert.ToBase64String(Encoding.UTF8.GetBytes(tunnelUrl)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                m_ServerCodeField.value = m_ServerCode;
                PopulatePlayerList();
                ShowServerTab(true);
                m_CreateServerModal.AddToClassList("server-dashboard");
                m_CloseServerButton?.RemoveFromClassList("is-hidden");
                m_ServerReadyState.RemoveFromClassList("is-hidden");
                m_ServerReadyState.schedule.Execute(() =>
                {
                    m_ServerReadyState.RemoveFromClassList("is-transparent");
                    Repaint();
                }).StartingIn(20);
            }).StartingIn(200);
        }).StartingIn(260);
    }

    private void ShowTunnelError(string message)
    {
        EditorApplication.delayCall += () =>
        {
            if (m_CreatingStatus != null)
            {
                m_CreatingStatus.text = $"Could not create server: {message}";
                Repaint();
            }
        };
    }

    private async void BeginConnecting()
    {
        if (m_IsConnecting || m_ConnectButton == null || m_ConnectIcon == null)
        {
            return;
        }

        m_IsConnecting = true;
        m_ConnectButton.tooltip = "Connecting...";
        SwapIcon(HourglassIcon, StartConnectingWait);
        string enteredCode = rootVisualElement.Q<TextField>("server-code-input").value?.Trim();
        try
        {
            int separator = enteredCode == null ? -1 : enteredCode.IndexOf('.');
            if (separator < 1) throw new InvalidOperationException("Use the complete code copied by the host.");
            string encoded = enteredCode.Substring(separator + 1).Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            m_TunnelUrl = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            m_ServerCode = enteredCode;
            m_IsHost = false;
            m_LocalPlayerName = Environment.UserName;
            string snapshot = await SendHeartbeatAsync("join");
            ApplyRemoteSnapshot(snapshot);
            ShowJoinedLobby();
        }
        catch (Exception exception)
        {
            m_ConnectButton.tooltip = "Could not connect: " + exception.Message;
        }
    }

    private async Task<string> SendHeartbeatAsync(string action)
    {
        Vector3 position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        string invariant = "R";
        string url = m_TunnelUrl.TrimEnd('/') + "/" + action + "?id=" + Uri.EscapeDataString(m_LocalPlayerId) +
            "&name=" + Uri.EscapeDataString(m_LocalPlayerName ?? Environment.UserName) +
            "&px=" + position.x.ToString(invariant, System.Globalization.CultureInfo.InvariantCulture) +
            "&py=" + position.y.ToString(invariant, System.Globalization.CultureInfo.InvariantCulture) +
            "&pz=" + position.z.ToString(invariant, System.Globalization.CultureInfo.InvariantCulture);
        using (WebClient client = new WebClient()) return await client.DownloadStringTaskAsync(url);
    }

    private void ShowJoinedLobby()
    {
        m_DisplayNameInput.value = m_LocalPlayerName;
        m_MainActions.AddToClassList("is-hidden");
        m_CreateServerModal.RemoveFromClassList("is-hidden");
        m_CreateServerModal.AddToClassList("server-dashboard");
        m_CloseServerButton?.AddToClassList("is-hidden");
        m_NameStep.AddToClassList("is-hidden"); m_WizardFooter.AddToClassList("is-hidden"); m_CreatingState.AddToClassList("is-hidden");
        m_ServerReadyState.RemoveFromClassList("is-hidden"); m_ServerReadyState.RemoveFromClassList("is-transparent");
        m_ServerCodeField.value = m_ServerCode; PopulatePlayerList(); ShowServerTab(true);
        m_PlayerRefresh?.Pause();
        m_PlayerRefresh = m_ServerReadyState.schedule.Execute(async () => { try { ApplyRemoteSnapshot(await SendHeartbeatAsync("heartbeat")); } catch { } }).Every(1500);
    }

    private void ApplyRemoteSnapshot(string snapshot)
    {
        if (snapshot == null) return;
        foreach (string line in snapshot.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t'); if (fields.Length < 5 || fields[0] == m_LocalPlayerId) continue;
            float x, y, z; float.TryParse(fields[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x); float.TryParse(fields[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y); float.TryParse(fields[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
            lock (m_PlayerLock) m_RemotePlayers[fields[0]] = new RemotePlayer { id = fields[0], name = fields[1], cameraPosition = new Vector3(x,y,z), cameraRotation = Quaternion.identity };
        }
        PopulatePlayerList();
    }

    private void StartConnectingWait()
    {
        if (m_ConnectButton == null || m_ConnectIcon == null)
        {
            return;
        }

        m_ConnectButton.AddToClassList("is-connecting");
        m_HourglassRotationDegrees = 0f;
        m_ConnectIcon.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
        m_ConnectIcon.AddToClassList("is-spinning");
        m_HourglassRotation = m_ConnectIcon.schedule.Execute(AdvanceHourglassRotation)
            .StartingIn(20)
            .Every(1350);
        m_ConnectCompletion = m_ConnectIcon.schedule.Execute(EndConnecting).StartingIn(2050);
    }

    private void AdvanceHourglassRotation()
    {
        if (m_ConnectIcon == null)
        {
            return;
        }

        m_HourglassRotationDegrees += 180f;
        m_ConnectIcon.style.rotate = new Rotate(
            new Angle(m_HourglassRotationDegrees, AngleUnit.Degree));
    }

    private void EndConnecting()
    {
        m_HourglassRotation?.Pause();
        m_HourglassRotation = null;
        m_ConnectCompletion = null;

        if (m_ConnectButton != null && m_ConnectIcon != null)
        {
            m_ConnectButton.RemoveFromClassList("is-connecting");
            SwapIcon(PlugIcon, FinishConnecting);
        }
    }

    private void FinishConnecting()
    {
        if (m_ConnectButton != null)
        {
            m_ConnectButton.tooltip = "Connect";
        }

        m_IsConnecting = false;
    }

    private void SwapIcon(string icon, Action onComplete)
    {
        if (m_ConnectIcon == null)
        {
            onComplete?.Invoke();
            return;
        }

        m_ConnectIcon.AddToClassList("is-icon-collapsed");
        m_IconSwitch = m_ConnectIcon.schedule.Execute(() =>
        {
            if (m_ConnectIcon == null)
            {
                return;
            }

            m_ConnectIcon.text = icon;
            m_ConnectIcon.RemoveFromClassList("is-spinning");
            m_ConnectIcon.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
            m_IconExpand = m_ConnectIcon.schedule.Execute(() =>
            {
                if (m_ConnectIcon == null)
                {
                    return;
                }

                m_ConnectIcon.RemoveFromClassList("is-icon-collapsed");
                m_ConnectIcon.schedule.Execute(() => onComplete?.Invoke()).StartingIn(200);
            }).StartingIn(20);
        }).StartingIn(180);
    }

    private void StopServer()
    {
        try
        {
            m_LocalServer?.Close();
        }
        catch (Exception)
        {
            // The listener may already be shutting down.
        }
        m_LocalServer = null;

        try
        {
            if (m_TunnelProcess != null && !m_TunnelProcess.HasExited)
            {
                m_TunnelProcess.Kill();
            }
        }
        catch (Exception)
        {
            // The process may have exited between the checks.
        }
        m_TunnelProcess?.Dispose();
        m_TunnelProcess = null;
    }

    private void OnDisable()
    {
        m_HourglassRotation?.Pause();
        m_ConnectCompletion?.Pause();
        m_IconSwitch?.Pause();
        m_IconExpand?.Pause();
        m_LoadingWheelAnimation?.Pause();
        m_StartTunnelScheduled?.Pause();
        m_CopyIconReset?.Pause();
        m_TabTransition?.Pause();
        m_UpdateSpinnerAnimation?.Pause();
        CancelMenuRevealSchedules();
        m_HourglassRotation = null;
        m_ConnectCompletion = null;
        m_IconSwitch = null;
        m_IconExpand = null;
        m_LoadingWheelAnimation = null;
        m_StartTunnelScheduled = null;
        m_CopyIconReset = null;
        m_TabTransition = null;
        m_UpdateSpinnerAnimation = null;
        m_PlayerRefresh = null;
        m_IsConnecting = false;

        SceneView.duringSceneGui -= DrawSceneChatOverlay;
        EditorApplication.update -= RepaintSceneChatOverlay;
        EditorApplication.update -= AutomaticUpdateTick;
        EditorApplication.focusChanged -= HandleUnityFocusChanged;

        if (m_OwnSceneBubbleTexture != null)
        {
            DestroyImmediate(m_OwnSceneBubbleTexture);
            m_OwnSceneBubbleTexture = null;
        }

        if (m_OtherSceneBubbleTexture != null)
        {
            DestroyImmediate(m_OtherSceneBubbleTexture);
            m_OtherSceneBubbleTexture = null;
        }

        if (m_ButtonClickClip != null)
        {
            DestroyImmediate(m_ButtonClickClip);
            m_ButtonClickClip = null;
        }
        m_PlayPreviewClipMethod = null;

        StopServer();
    }
}
