<img width="1500" height="360" alt="Unity Collaboration banner" src="https://github.com/user-attachments/assets/3a559b2a-bf7f-4238-9ab8-7f23cea3ff73" />

# Unity Collaboration

Real-time collaboration tools built directly into the Unity Editor. Host a session, share its four-character code, and work with other developers without manually exchanging scene and asset files.

> [!WARNING]
> This project is experimental. Collaboration writes remote files into the client's `Assets` directory. Use version control and commit or back up important work before starting a session.

## Features

- Host or join sessions from inside Unity
- Live GameObject creation, deletion, transform, and component-property updates
- Selection indicators and editing locks for objects being edited by another player
- Host-to-client asset snapshots on join
- Live synchronization of saved scenes, prefabs, folders, materials, textures, meshes, models, audio, and other Unity assets
- Chunked transfer for large binary assets
- Unity `.meta` synchronization to preserve GUIDs and references
- Separate scene support: collaborators can work in different scenes without forcing other editors to switch
- In-editor chat, player list, scene status, and latency display
- Remote-session cleanup for assets introduced into a client's project
- Built-in update checking

## Requirements

- A supported Unity Editor project on each computer
- The same Unity version and compatible project/package dependencies on every computer
- Internet access for installation, update checks, and remote sessions
- HTTPS and WebSocket access to `collaborate.novaa.dev`
- Version control is strongly recommended

Runtime scripts and editor assemblies are deliberately excluded from session asset snapshots because importing code would trigger a Unity recompilation and disconnect the active session. Install required packages and scripts on every collaborator's base project before connecting.

## Installation

1. Download [`Installer/UnityCollaborationTool.unitypackage`](https://github.com/NovaDevvvv/unity-collaboration/releases/download/Main/UnityCollaborationTool.unitypackage).
2. Import the package into the Unity project.
3. In Unity, select **ATTENTION NEEDED > Install Collaboration Tool**.
4. Wait for the installer to download the current tool into:

   ```text
   Assets/@NovaDevvvv/Collaboration Tool
   ```

5. After Unity recompiles, open **Collaborate > Window**.

The installer requires access to GitHub. A DNS error such as `NameResolutionFailure` means the machine could not resolve GitHub; it is not a collaboration-session error.

## Starting a session

![Hosting workflow: create a server, share the link, and join from another Unity Editor](https://github.com/user-attachments/assets/a57afbf0-2dc4-4043-9a5c-df72aab46a7d)

### Host

1. Open **Collaborate > Window**.
2. Select **Create Server**.
3. Enter a display name and wait for the four-character session code.
4. Send that code to collaborators.
5. Keep Unity and the Collaboration window running while the session is active.

Closing the server notifies connected clients and leaves the host in its current scene. Only the host is prompted to save host-side scene changes.

### Client

1. Open the same base project in Unity.
2. Open **Collaborate > Window**.
3. Enter a display name and the host's four-character code, then press Connect.
4. Wait for the initial host asset snapshot to finish importing.

The Players tab is selected when a session starts. The client can open any synchronized scene from the Project window; opening a scene does not force the host or other clients to open it.

## How synchronization works

### Project assets

When a client joins, the host sends an initial snapshot of its importable `Assets` content. Folder metadata is created first, followed by each asset's `.meta` and data file. Large files are split into chunks and reassembled before import.

New or modified assets are then sent while the session is active. A newly created scene must be saved somewhere under `Assets` before it can be synchronized. Once saved, its `.meta` and `.unity` files are sent to the host and other clients.

Existing client-only assets are not hidden or deleted on join. Files received from the session are tracked: newly introduced client files are removed on exit, while pre-existing files overwritten during the session are restored. Files created before this tracking starts remain the client's responsibility.

### Scene objects

Objects in a scene loaded by multiple collaborators receive live creation, deletion, transform, selection, and supported component-property updates. If another collaborator is editing an object, it appears locked and cannot be modified through the normal editor controls.

If a collaborator is working in a scene that is not loaded locally, real-time object messages are not inserted into the wrong active scene. The saved `.unity` asset remains the authoritative way to transfer that scene.

### Safety boundaries

- Remote paths are restricted to `Assets/...`.
- GameObject deletions synchronize between editors.
- Project asset deletions and moves do not remove the old remote file automatically.
- Remote client assets are cleaned up only from client projects, never from the host.
- The collaboration tool's own files and code assemblies are excluded from asset snapshots.

## Troubleshooting

### A scene is missing on another computer

- Save the scene under `Assets`.
- Confirm both computers are connected and running the latest tool version.
- Allow Unity time to import the `.meta` and `.unity` pair.
- Scenes are synchronized but not automatically opened; open the scene manually from the Project window.

### Missing textures, meshes, materials, or prefab references

- Wait for the initial asset snapshot to complete before opening the host scene.
- Ensure both projects have the same packages and scripts installed.
- Verify the host has saved/imported the referenced assets under `Assets`.
- Check the Console for an asset that exceeded transfer or import limits.

### Unity reports an orphaned `.meta` file

Both users should update to the latest version and reconnect. Current snapshots send each `.meta` immediately before its corresponding asset. An orphan left by an older interrupted transfer can be removed manually if the actual asset no longer exists.

### Changes are desynchronized

Save the authoritative scene on the host, reconnect the affected client, and allow the initial snapshot to complete. Keep version control available as a recovery path for interrupted sessions.

### Update check says offline

The tool treats DNS, timeout, and connection failures as an offline update check. Collaboration sessions require DNS, HTTPS, and WebSocket access to `collaborate.novaa.dev`.

## Development

The tool source lives in [`@NovaDevvvv/Collaboration Tool/Editor`](@NovaDevvvv/Collaboration%20Tool/Editor). The small `.unitypackage` in `Installer` installs a downloader, which retrieves the current source into the consuming Unity project.

The relay Worker lives in [`Worker`](Worker). `POST /v1/create` allocates a four-character uppercase alphanumeric code. `GET /v1/connect` upgrades to a WebSocket backed by a Durable Object dedicated to that session. Deploy it with `wrangler deploy` from the `Worker` directory.

When changing the network message format, update both host and client before testing. Different revisions are not guaranteed to be protocol-compatible.

## Contributing

Issues and pull requests are welcome. Include the Unity version, host/client operating systems, reproduction steps, and relevant Console output when reporting synchronization or networking problems.
