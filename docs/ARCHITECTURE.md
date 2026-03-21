<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Architecture

Heimdall.Next is a .NET 10 WPF application organized as a multi-project solution with strict dependency boundaries. Supports RDP, SSH, SFTP, FTP, VNC, Telnet, Citrix, and Local Shell connection types with ~3,035 i18n keys per locale (EN/FR), 33 built-in sysops tools with contextual help, cross-tool navigation, and 1,213 automated tests. Health monitor polls in parallel (Task.WhenAll), XML importers hardened against XXE, all Debug.WriteLine replaced with FileLogger.

## Solution Structure

```
Heimdall.slnx (8 projects)
├── src/
│   ├── Heimdall.Core          net10.0         Models, security, config, state machine, i18n, network scanner, utilities
│   ├── Heimdall.Ssh           net10.0         SSH engine, tunnels, Pageant, TOFU, failure classifier, health monitor
│   ├── Heimdall.Rdp           net10.0-windows RDP + Citrix engine (ActiveX, StoreBrowse), credential autofill
│   ├── Heimdall.Sftp          net10.0         SFTP/FTP browser (SSH.NET + FtpWebRequest), remote file editing
│   ├── Heimdall.Terminal      net10.0-windows Terminal sessions (pipe mode, ConPTY, Telnet)
│   └── Heimdall.App           net10.0-windows WPF application (MVVM, views, themes, DI)
│       ├── Views: MainWindow, EmbeddedRdpView, EmbeddedSshView, EmbeddedSftpView,
│       │          EmbeddedCitrixView, EmbeddedVncView, FloatingSessionWindow
│       ├── Views/Tools: 31 built-in sysops tools (IToolView interface)
│       └── Services: ConnectionService (.Rdp/.Ssh/.Sftp/.Ftp/.Vnc/.Telnet/.Citrix/.Local/.Tunnel),
│                     EmbeddedSessionManager, ToolRegistry, TaskSchedulerService, MacroService,
│                     EphemeralFileServer, X11ServerManager, WebSocketVncProxy
└── tests/
    ├── Heimdall.Core.Tests    State machine, HMAC integrity, input validation, PIN manager, config manager tests
    └── Heimdall.Ssh.Tests     SSH engine tests (failure classifier, preflight, TOFU, Pageant, Plink)
```

## Dependency Graph

```
                    +-----------------+
                    |  Heimdall.App   |  WPF, MVVM, DI container
                    +--------+--------+
                             |
          +------------------+------------------+
          |         |        |        |         |
     +----v---+ +--v---+ +--v--+ +---v----+ +--v-------+
     |  Core  | |  Ssh | |  Rdp| |  Sftp  | | Terminal |
     +--------+ +--+---+ +--+--+ +---+----+ +----+-----+
                   |         |        |           |
                   +----+----+    +---+---+       |
                        |         | Core  |       |
                   +----v----+    | + Ssh |  +----v----+
                   |  Core   |    +-------+  |  Core   |
                   +---------+               +---------+
```

- **Heimdall.Core** has zero internal project dependencies (only NuGet: CommunityToolkit.Mvvm, ProtectedData, DI abstractions)
- **Heimdall.Ssh** depends on Core + SSH.NET; includes `ServerHealthMonitor` for multiplexed health polling
- **Heimdall.Rdp** depends on Core (uses WPF + WinForms for ActiveX hosting; includes Citrix StoreBrowse integration)
- **Heimdall.Sftp** depends on Core + Ssh (reuses SSH.NET connection factory). `SftpSessionBundle` in ConnectionService bundles SftpClient + SshClient for sudo operations. `FtpBrowser` implements `IRemoteBrowser` for FTP connections
- **Heimdall.Terminal** depends on Core (uses Win32 APIs for ConPTY + pipe mode + Telnet raw TCP)
- **Heimdall.App** references all five libraries and owns the DI composition root

## Key Design Decisions

### 1. SSH.NET + Plink Dual Strategy

**Problem**: SSH.NET 2025.1.0 has no built-in Pageant agent support. Many enterprise environments use PPK keys loaded exclusively in Pageant.

**Solution**: Two-pronged approach:

1. **SSH.NET (primary)**: Programmatic auth with password, private key file, or keyboard-interactive. Custom `PageantClient` communicates with Pageant via Win32 shared memory (`CreateFileMapping` + `WM_COPYDATA`) and wraps keys as `IPrivateKeySource` for SSH.NET.

2. **Plink fallback**: When `AuthPreflightChecker.RequiresPageantFallback()` detects that the only viable auth method is Pageant, `PlinkTunnelRunner` handles tunnels and `PipeModeSession` handles interactive SSH. Plink communicates with Pageant natively.

**Pageant integration fixes** (3 critical bugs resolved):
- `AGENT_COPYDATA_ID` must be `0x804e50ba` — any other value causes Pageant to silently ignore the request
- RSA-SHA2 algorithms (`rsa-sha2-256`, `rsa-sha2-512`) must be registered on the `ConnectionInfo` for modern servers that reject legacy `ssh-rsa`
- `PageantHostAlgorithm.Sign()` must return the full SSH signature blob (algorithm name length + algorithm name + signature length + signature), not just the raw signature bytes — SSH.NET expects the wire-format blob

### 2. Pipe Mode for SSH Terminals (NOT ConPTY)

**Problem**: ConPTY converts VT input to Windows console key events, then reconverts back to VT. This double-conversion breaks arrow keys, function keys, and other escape sequences when piped through plink.

**Solution**: `PipeModeSession` redirects stdin/stdout as raw pipes without a pseudo-console. Combined with plink's `-t` flag (forces remote PTY allocation even when stdin is not a terminal), VT sequences pass through unmodified:

```
xterm.js  -->  stdin pipe  -->  plink -t  -->  remote PTY  -->  bash
                                                    |
xterm.js  <--  stdout pipe <--  plink     <---------+
```

ConPTY (`ConPtySession`) is kept for local shell scenarios only.

### 3. WebView2 + xterm.js for Terminal Rendering

**Problem**: WPF has no native terminal control. Microsoft.Terminal.Control requires ConPTY, which breaks SSH (see above).

**Solution**: WebView2 hosts xterm.js, the industry-standard terminal renderer:

- Binary-safe data via base64 encoding between C# and JavaScript
- `PostWebMessageAsString` (C# to JS) and `WebMessageReceived` (JS to C#)
- xterm.js handles all VT100/xterm rendering: colors, cursor, scrollback, mouse, selection
- CSS cursor blink rate set to 1.2s to avoid WPF/WebView2 focus fight

#### WebView2 Security Model

The terminal page (`terminal.html`) is loaded via `NavigateToString` (no external origin). Security hardening:

- **CSP**: `default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'none'; frame-src 'none'` — all scripts are inlined, no external resource loading permitted
- **Navigation blocking**: `NavigationStarting` handler cancels any navigation away from `about:` or `data:` origins
- **Message origin validation**: `OnWebMessageReceived` rejects messages from unexpected sources
- **URL opening**: Only `http://` and `https://` URIs are passed to `Process.Start` with `UseShellExecute`

### 4. RDP ActiveX with Layout Flush Protocol

**Problem**: WPF's `WindowsFormsHost` has an "airspace" issue where the rendering surface is not properly bound to the visible HWND if layout hasn't been flushed before `Connect()`.

**Solution**: Mandatory layout flush before every `Connect()`:

```
UpdateLayout() -> DoEvents() -> Dispatcher.Invoke(Render) -> EnsureHandle -> Connect()
```

Additional guards:
- Resolution updates blocked for 5 seconds after `OnConnected` (prevents disconnect code 4360)
- COM dispose follows strict order: collapse visibility, detach from tree, disconnect, detach event sink, dispose

### 5. Credential Autofill via EnumThreadWindows

**Problem**: `EnumWindows` only finds top-level windows. CredUI dialogs from embedded ActiveX controls are thread-owned child windows, invisible to standard window enumeration.

**Solution**: Scan all threads of the current process with `EnumThreadWindows` in addition to `EnumWindows`. Use UI Automation (`System.Windows.Automation`) for modern XAML-based CredUI, with Win32 `SendMessage`/`BM_CLICK` fallback for classic dialogs.

### 6. TOFU Host Key Verification

SSH host key fingerprints are persisted in `HostKeyStore` and saved to `settings.json` (`TrustedHostKeys` dictionary). On first connection, the fingerprint is recorded and persisted via the `HostKeyEvent` callback. On subsequent connections (including after app restart), mismatches trigger `SshFailureCode.HostKeyMismatch` with a user-facing warning. Fingerprints are loaded from config at startup via `LoadFromConfig()`.

### 7. SSH Failure Classification

`FailureClassifier` maps SSH.NET exceptions (and Plink stderr patterns) to 25 structured `SshFailureCode` values. This enables the UI to display targeted, localized error messages (e.g., `ErrorSshKeyRejected`, `ErrorSshNetworkTimedOut`) instead of raw exception text.

### 8. Citrix StoreBrowse Integration

**Problem**: Citrix published applications and desktops require StoreFront authentication and ICA file generation before launching a session.

**Solution**: `ConnectionService.Citrix.cs` uses the `storebrowse.exe` CLI from Citrix Workspace App:
1. Auto-detects `storebrowse.exe` in `%ProgramFiles(x86)%\Citrix\ICA Client\SelfServicePlugin\`
2. Authenticates against StoreFront to enumerate published resources
3. Generates ICA file for the selected resource
4. `EmbeddedCitrixView` hosts the session in a tab, following the same lifecycle as RDP sessions

### 9. ISessionResult Type Hierarchy

All connection operations return an `ISessionResult` (defined in `Heimdall.Core/Models/`). Concrete implementations carry protocol-specific session state:
- `RdpSessionResult` — ActiveX handle, resolution info
- `SshSessionResult` — shell stream or pipe mode session reference
- `SftpSessionResult` — `SftpSessionBundle` (SftpClient + SshClient for sudo)
- `CitrixSessionResult` — ICA session handle
- `LocalSessionResult` — ConPTY session reference
- `VncSessionResult` — WebSocket proxy handle, noVNC connection info
- `TelnetSessionResult` — `TelnetSession` reference (raw TCP)
- `FtpSessionResult` — `FtpBrowser` (IRemoteBrowser) reference

### 10. Multi-Exec Broadcast

**Data flow**: The broadcast source terminal captures user input at the xterm.js `onData` level. When broadcast is active, the input event is relayed via `PostWebMessageAsString` to every opted-in terminal's WebView2 instance, which forwards it to the respective stdin pipe. Each terminal independently echoes the input through its own remote PTY, so output remains per-session.

### 11. Quick Connect (Ctrl+K)

**Architecture**: A modal overlay (`QuickConnectOverlay`) parses connection strings of the form `[protocol://]user@host[:port]`. The parser infers protocol from port if omitted (22=SSH, 3389=RDP, 1494=Citrix, 5900=VNC, 23=Telnet, 21=FTP). A `ServerProfileDto` is created transiently (not persisted) and passed to `ConnectionService.ConnectAsync()`. Recent connections are stored in `settings.json` for quick re-use.

### 12. Tunnel Panel (Retractable)

**Architecture**: A `GridSplitter`-based side panel bound to `TunnelPanelViewModel`. The panel observes `TunnelManager.ActiveTunnels` (an `ObservableCollection<TunnelSession>`) and displays real-time status. Tunnel teardown sends a cancel request to the specific `TunnelSession` without affecting other tunnels or the parent SSH connection. Panel visibility is toggled via a toolbar button and persisted in user settings.

### 13. VNC via noVNC WebView2 + WebSocket Proxy

**Problem**: WPF has no native VNC control. VNC (RFB protocol) operates over raw TCP, but noVNC requires a WebSocket transport.

**Solution**: `WebSocketVncProxy` is a lightweight in-process proxy that listens on a random local port, accepts a single WebSocket connection from noVNC, and bidirectionally pipes binary frames to the VNC server's TCP socket. `EmbeddedVncView` hosts noVNC inside a WebView2 control pointing at `ws://localhost:{ListenPort}`. This reuses the same WebView2 infrastructure as the terminal views.

### 14. Telnet Raw TCP with IAC Negotiation

**Problem**: Legacy network devices (switches, routers, serial consoles) require Telnet access.

**Solution**: `TelnetSession` in `Heimdall.Terminal` implements `ITerminalSession` over a raw TCP socket with minimal Telnet IAC negotiation (WILL/WONT/DO/DONT handling, NAWS sub-negotiation for terminal size). It plugs into the same WebView2 + xterm.js rendering pipeline used by SSH pipe mode, so the user experience is identical. No external Telnet client is required.

### 15. FTP via IRemoteBrowser Abstraction

**Problem**: Some servers expose FTP instead of SFTP. The file browser UI should work identically regardless of protocol.

**Solution**: `IRemoteBrowser` defines the common surface (`Connect`, `ListDirectory`, `Upload`, `Download`, `Disconnect`, events). `SftpBrowser` (SSH.NET) and `FtpBrowser` (`FtpWebRequest`) both implement this interface. `EmbeddedSftpView` binds to `IRemoteBrowser` without knowing the underlying protocol. `RemoteFileEditor` works with both via the same interface.

**Dual edit modes**: Right-click a file to choose between:
- **Edit (integrated)**: Opens AvalonEdit inside the app with syntax highlighting. Save triggers upload.
- **Edit with external editor**: Downloads to temp, launches the configured editor (Settings > General > External editor path), `FileSystemWatcher` with 2-second debounce auto-uploads on save.

### 15b. SFTP Sudo Elevation System

**Problem**: SFTP runs with the connected user's permissions. Root-owned files and directories (e.g. `/etc/shadow`, `/root/`) are inaccessible. Unlike SSH where `sudo su -` gives full access, the SFTP protocol has **no built-in privilege escalation**.

**Solution**: Two-tier approach using SSH exec channels alongside the SFTP session:

**Tier 1 — Automatic fallback** (transparent to user):
Every file operation catches `SftpPermissionDeniedException` and `SshException("Failure")` (SSH_FX_FAILURE, common on servers that don't distinguish error codes), then retries via SSH exec:
- Upload: SFTP to `/tmp/` → `sudo tee` to target
- Download: `sudo cat` via SSH exec
- Edit: delegates to `RemoteFileEditor.EditFileSudoAsync`
- Chmod/Rename/Delete/Mkdir: `sudo chmod`/`mv`/`rm`/`mkdir` via SSH exec

**Tier 2 — "Browse as root" toggle** (user-initiated):
Toolbar toggle button switches directory listing from SFTP `ListDirectory` to `sudo ls -la --time-style=long-iso` via SSH exec. Enables browsing ANY directory regardless of permissions.

**Key design decisions and pitfalls encountered**:

- **SSH auth must match the main session**: Sudo helpers must use `SshConnectionFactory.Create()` with the same Pageant/key/password auth as the original connection. Early implementation used raw `new SshClient(connInfo)` which bypassed Pageant integration — the SSH connection failed with "Permission denied (publickey,password)" and the user saw a confusing error.
- **Host key verification required**: Sudo SSH clients must call `AttachHostKeyVerification()` with the TOFU `HostKeyStore`. Without this, connections fail silently on strict-host-key servers.
- **Exception detection is fragile**: SSH.NET throws `SftpPermissionDeniedException` for explicit denials, but many servers return `SSH_FX_FAILURE` (status 4) instead of `SSH_FX_PERMISSION_DENIED` (status 3). This surfaces as `SshException("Failure")` — the classifier checks both `Sftp*` and `Ssh*` exception type names with "Failure" message.
- **`ls -la` output parsing**: The `--time-style=long-iso` format produces **8 columns** (permissions, links, owner, group, size, date, time, name). Early parser expected 9 columns and silently skipped all entries. Filename column must be the last split part to handle spaces.
- **Sudo toggle hidden for FTP**: FTP sessions have no SSH channel, so the sudo button is collapsed.

### 16. Tab Detach to Floating Window

**Problem**: Users need to view multiple sessions side by side, or move a session to a second monitor.

**Solution**: `FloatingSessionWindow` hosts a single detached `SessionTabViewModel`. The session's view (RDP, SSH, SFTP, VNC, etc.) is reparented from the main tab control to the floating window. The window applies the current theme via `WindowThemeHelper`, displays session metadata (title, tunnel route), and provides a reattach button. On close, if not explicitly reattached, the session is returned to the main window.

### 17. Tunnel Ref-Counting for Shared Tunnels

**Problem**: Multiple connections may traverse the same SSH tunnel (e.g., two RDP sessions through the same gateway). Tearing down a tunnel when one connection closes would kill the others.

**Solution**: `TunnelManager` maintains a `ConcurrentDictionary<int, int>` of reference counts keyed by local port. `AddReference()` increments the count when a new connection uses an existing tunnel. `ReleaseReference()` decrements it, and only calls `CloseTunnel()` when the count reaches zero. `CloseTunnel()` itself checks the ref count before tearing down, providing a double guard.

### 18. Connection Inheritance (GroupDefaultsDto)

**Problem**: Enterprises organize hundreds of servers into groups that share the same gateway, SSH user, key path, or connection type. Configuring each server individually is tedious.

**Solution**: `GroupDefaultsDto` defines default connection settings (gateway, SSH username, key path, port, connection type) at the group/folder level. Servers inherit these values when their own fields are null or empty. Resolution is hierarchical: a server in `PROD/Linux` inherits from `PROD/Linux` first, then falls back to `PROD` if the nested group does not override the field.

### 19. External Credential Provider (CommandCredentialProvider)

**Problem**: Security-conscious environments store credentials in external password managers (KeePassXC, Bitwarden CLI, 1Password CLI, `pass`), not in the application's DPAPI vault.

**Solution**: `CommandCredentialProvider` implements `ICredentialProvider` by executing a user-configured CLI command template. Placeholders `{Host}`, `{Port}`, `{User}`, `{Title}`, `{Database}` are substituted at runtime. The command's stdout is captured and trimmed as the password. A 10-second timeout prevents hangs. This enables zero-knowledge credential retrieval where Heimdall never persists the password.

### 20. Scheduled Tasks Engine (TaskSchedulerService)

**Problem**: Automated connections (e.g., daily SSH backup scripts, maintenance windows) need to run on a schedule without manual intervention.

**Solution**: `TaskSchedulerService` runs a background `System.Threading.Timer` that ticks every 60 seconds. On each tick, it evaluates `ScheduledTaskDto` entries (provided via `TasksProvider` callback) against the current time, fires `TaskDueCallback` for due tasks, and calls `PersistCallback` to save last-run timestamps. The timer is guarded by a `SemaphoreSlim` to prevent overlapping ticks.

### 21. Server Health Monitoring (Multiplexed SSH Channel)

**Problem**: Sysadmins want at-a-glance health data (CPU, RAM, disk) for connected servers without opening a separate monitoring tool.

**Solution**: `ServerHealthMonitor` in `Heimdall.Ssh` reuses the existing `SshClient` from an active shell session to run lightweight monitoring commands (`top -bn1`, `free -m`, `df -h /`) on a multiplexed SSH channel at a configurable interval (default 5 seconds). Results are parsed via compiled regex into a `ServerHealthData` record and surfaced in the UI.

### 22. Macro Recorder (Keystroke Capture with Delays)

**Problem**: Repetitive terminal workflows (login sequences, config commands) should be recordable and replayable.

**Solution**: `TerminalMacro` (in `Heimdall.Core.Models`) stores a sequence of `MacroEntry` records, each containing the input text and the delay (in milliseconds) since the previous entry. `MacroService` persists macros as individual JSON files in a `macros/` directory. During playback, entries are sent to the terminal session with their recorded inter-keystroke delays preserved.

### 23. Network Scanner (ICMP Sweep + Port Probe)

**Problem**: Sysadmins need to discover hosts on a subnet before adding them to the server inventory.

**Solution**: `NetworkScanner` (in `Heimdall.Core.Security`) accepts a CIDR subnet (e.g., `192.168.1.0/24`), performs parallel ICMP ping sweeps with a 1-second timeout, then probes common ports (22, 3389, 80, 443, 5900) on responsive hosts with a 500ms timeout. Results include IP address, hostname (reverse DNS), round-trip time, and open ports. A progress callback enables UI updates during the scan.

### 24. Quick File Server (Ephemeral HTTP/TFTP)

**Problem**: Some servers have no SFTP or SCP (hardened servers, minimal containers, network equipment). Users need a quick way to make local files available for `wget`/`curl`/`tftp` from a remote SSH session.

**Solution**: `EphemeralFileServer` provides dual read-only servers: HTTP (via `HttpListener` with directory listing) and TFTP (minimal RFC 1350 RRQ over `UdpClient`). On activation, a helper dialog displays ready-to-use download commands (`wget`, `curl`, `tftp`) and auto-copies the server URL to clipboard for pasting into the active SSH terminal. Both servers are disposed when the user clicks "Stop File Server".

### 25. X11 Server Auto-Detection and Management

**Problem**: X11 forwarding over SSH requires a local X server (VcXsrv, Xming, X410, XWin). Users forget to start one, or the `DISPLAY` variable is misconfigured.

**Solution**: `X11ServerManager` detects running X server processes by scanning known process names. If none is found, it searches known installation paths and starts the first available server automatically. The `DISPLAY` environment variable is set to `localhost:0.0` for the SSH session. The manager disposes the started process on shutdown.

## Connection Flow

```
User clicks Connect
        |
        v
ConnectionService.ConnectAsync(server)
        |
        +-- Resolve gateway chain (GatewayChainResolver)
        |       |
        |       +-- For each gateway: establish SSH tunnel
        |       |       |
        |       |       +-- AuthPreflightChecker: validate credentials
        |       |       +-- SshConnectionFactory: create SSH.NET client or Plink fallback
        |       |       +-- TunnelManager: start port forward
        |       |
        +-- Determine connection type
        |
        +-- RDP?
        |       +-- EmbeddedRdpView: ActiveX MsTscAx
        |       |       +-- Layout flush -> Connect() -> OnConnected -> credential autofill
        |       +-- OR mstsc.exe (external) + CredentialAutofill polling
        |
        +-- SSH?
        |       +-- EmbeddedSshView: WebView2 + xterm.js
        |               +-- PipeModeSession: plink -t -> stdin/stdout pipes
        |               +-- OR SshShellSession: SSH.NET shell stream
        |
        +-- SFTP?
        |       +-- EmbeddedSftpView: file browser panel
        |               +-- SftpSessionBundle: SftpClient (file ops) + SshClient (sudo exec)
        |               +-- RemoteFileEditor: FileSystemWatcher auto-upload (2s debounce)
        |               +-- Sudo fallback: permission denied -> sudo cat/sudo tee via SSH exec
        |
        +-- Citrix?
        |       +-- EmbeddedCitrixView: StoreBrowse session tab
        |               +-- storebrowse.exe: StoreFront auth + resource enumeration
        |               +-- ICA file generation -> Citrix Workspace launch
        |
        +-- VNC?
        |       +-- EmbeddedVncView: WebView2 + noVNC
        |               +-- WebSocketVncProxy: WS-to-TCP bridge on random local port
        |               +-- noVNC connects to ws://localhost:{port}
        |
        +-- Telnet?
        |       +-- EmbeddedSshView (reused): WebView2 + xterm.js
        |               +-- TelnetSession: raw TCP + IAC negotiation
        |
        +-- FTP?
                +-- EmbeddedSftpView (reused): file browser panel
                        +-- FtpBrowser: IRemoteBrowser over FtpWebRequest
```

## State Machines

### Connection State Machine

States: `Disconnected` -> `Initializing` -> `ValidatingConfig` -> `EstablishingTunnel` -> `TunnelEstablished` -> `LaunchingRdp` / `LaunchingSsh` / `LaunchingSftp` / `LaunchingCitrix` / `LaunchingVnc` / `LaunchingTelnet` / `LaunchingFtp` -> `Connected` -> `Disconnecting` -> `Disconnected`

Error state reachable from any active state. Transitions validated before application.

### Application Status Machine

States: `Initializing` -> `Ready` <-> `Busy` -> `Shutdown`

Error state reachable from Ready or Busy.

## Security Architecture

| Layer | Mechanism |
|---|---|
| Credential storage | DPAPI (user-scope) + HMAC-SHA256 integrity via unified `CredentialProtector` |
| Legacy migration | `CredentialProtector.Unprotect` accepts both HMAC-protected and plain DPAPI blobs |
| HMAC key management | Auto-generated on first run, DPAPI-protected, stored in `settings.json` |
| PIN protection | PBKDF2-SHA256, 100,000 iterations, 128-bit salt via `PinManager` |
| File protection | Windows ACLs (user + Admins + SYSTEM) via `AclEnforcer` on config dirs, logs, temp files |
| Input validation | Compiled regex patterns against injection (CWE-78) via `InputValidator` |
| Command construction | Structured argument lists for Plink/gsudo (no string concatenation of user input) |
| Placeholder sanitization | Shell metacharacter stripping in ExternalToolDefinition and CommandCredentialProvider |
| HTTP/TFTP traversal | Trailing-separator + exact-root check in EphemeralFileServer |
| Config concurrency | SemaphoreSlim write lock in ConfigManager prevents last-writer-wins |
| WebView2 hardening | CSP (`default-src 'none'`), navigation blocking, `WebMessage` source validation |
| Pageant IPC | Process owner identity verification before shared memory access |
| Credential autofill | Scoped to mstsc process lineage + host hint matching, `#32770` class excluded |
| RDP CredMan | Session-scoped persistence, deterministic cleanup after session launch |
| Temp file security | ACL enforcement on .rdp files, Plink -pwfile (atomic ACL, no fallback), SFTP edit directories |
| XXE prevention | `DtdProcessing.Prohibit` + `XmlResolver = null` on all XML importers |
| Citrix argument validation | Shell metacharacter check on `CitrixLaunchCommandLine` before `Process.Start` |
| SSH host trust | TOFU fingerprints persisted to `settings.json`, loaded at startup |
| File writes | UTF-8 without BOM via `SecureFileWriter` |
| Memory | Credentials cleared after COM injection, `SecureString` for handoff paths |
| Exception handling | Global handlers registered before first await, unobserved task exceptions caught |
| External credentials | `CommandCredentialProvider` executes CLI password managers (KeePassXC, Bitwarden, 1Password) with 10s timeout |
| Logging | `FileLogger.Dispose()` flushes before marking disposed (no lost diagnostics) |

## Settings Panel Architecture

The Settings panel uses a left-navigation `TabControl` with 6 sub-tabs:

| Sub-tab | Settings |
|---------|----------|
| **General** | Language, theme, max sessions, prevent sleep, external editor, projects |
| **Terminal** | Font family, font size, color scheme |
| **SSH & SFTP** | Plink path, default mode, anti-idle, TMOUT reset, SFTP auto-open, X11, gateways |
| **RDP** | Default mode, resolution, color depth, audio, NLA, dynamic res, multi-monitor, device redirection, caching |
| **Security** | External credential provider (command/database), Credential Guard |
| **Advanced** | Logging, session logging, timeouts (tunnel/RDP), external tools |

Action buttons (Save / Reset / Export / Import) are pinned at the bottom, always visible regardless of sub-tab.

Settings persistence: ViewModel -> AppSettings -> ConfigManager -> settings.json (UTF-8 no BOM). ConfigManager writes are protected by a `SemaphoreSlim` to prevent concurrent save corruption.

## WebView2 Deployment Strategy

WebView2 is required for embedded SSH terminals (xterm.js) and VNC sessions (noVNC). `WebView2Helper` centralizes runtime detection:

1. **Bundled Fixed Version Runtime** in `runtimes/webview2/` (Self-Contained edition, ~436 MB)
2. **System Evergreen Runtime** via Edge or standalone installer (Standard edition)
3. **Unavailable** — shows localized error message, no crash

Build editions:

| Edition | Build flag | Size | WebView2 |
|---------|-----------|------|----------|
| **Standard** | `-Variant Light` | ~195 MB | Requires Edge (pre-installed on Windows 10/11) |
| **Self-Contained** | `-Variant Portable` | ~653 MB | Bundled Fixed Version Runtime for air-gapped/isolated environments |

`Build.ps1 -Variant Both` produces both variants. `Setup-WebView2.ps1` automates Evergreen Runtime installation on machines with internet access.

## Tool Architecture

### ToolRegistry (Single Source of Truth)

All 31 built-in tools are registered in `ToolRegistry` (singleton). Each tool is described by a `ToolDescriptor` record:

```csharp
public record ToolDescriptor(
    string Id,                  // "PING", "CERTGEN", etc.
    ToolCategory Category,      // Network, Security, Encoding, System
    string CategoryLabelKey,    // i18n key for category header
    string LabelKey,            // i18n key for tool name
    string? LabelWithArgKey,    // i18n key for "tool with argument" variant
    string[] CommandPrefixes,   // Palette aliases: ["ping"], ["dns","dig"]
    bool IsNetworkTool,         // Prompts for host when opened standalone
    string? IconResourceKey);   // XAML BitmapImage key: "Icon.Tool.PortScanner"
```

The registry eliminates three formerly-duplicated lists (menu definitions, palette commands, view factory switch) into one ordered collection. Adding a new tool requires:
1. One XAML + code-behind file implementing `IToolView`
2. One `Entry()` line in `ToolRegistry`
3. i18n keys in both locale files

### IToolView Interface

```csharp
public interface IToolView : IDisposable
{
    void Initialize(ToolContext? context, LocalizationManager? localizer);
}
```

All tool views implement this contract. `EmbeddedSessionManager.CreateToolControl()` uses the registry's factory delegate to instantiate views without any protocol-specific switch logic.

### ToolContext (Enriched Server Context)

```csharp
public record ToolContext(
    string? TargetHost, int? TargetPort, string? Argument,
    string? DisplayName, string? Username, string? ConnectionType,
    string? ProjectName, string? GroupName, string? SourceServerId);
```

When opening a tool from a server context menu, all available server metadata is passed. Network tools prefill their host input; security tools can use credentials context.

### Tool Navigation

- **Ctrl+Shift+T**: Toggle retractable Tools sidebar panel (categorized with icons)
- **Ctrl+K → "tools"**: Command palette lists all tools grouped by category
- **Ctrl+K → "ping 10.0.0.1"**: Opens tool with prefilled argument
- **Recent tools**: Last 5 used tools shown at top of palette when opened
- **Singleton behavior**: Context-free tools (UUID, Password, Chmod) reuse existing tab
- **External tools**: Also searchable in Ctrl+K palette
- **Help system**: "?" button on tools shows description, usage instructions, and examples
- **Detail panel**: Selecting a tool in TreeView shows dedicated panel (name, category, description, "Open in Tab")
- **Password presets**: Custom presets saved to `config/password-presets.json`, restored on click, deleted via right-click
- **Protocol colors**: Theme-aware brushes (bright on dark, darker on light) defined per-theme, not globally
- **Cross-tool navigation**: `ToolContextMenuHelper` with `OpenToolAction` callback enables right-click → open another tool with prefilled context
- **Network Cartography engine**: `Heimdall.Core.Discovery/` namespace with CartographyEngine, RoleClassifier (50+ roles, 60+ banner fingerprints), OuiDatabase (140 MAC prefixes), VlanDetector, DrawIoExporter, ScanHistoryManager
- **PowerShell Execution Policy**: Configurable in Settings > Terminal, applied as `-ExecutionPolicy` flag on local shell launch

### Tool Categories (31 tools)

| Category | Count | Tools |
|----------|-------|-------|
| **Network** | 10 | **Network Cartography** (subnet discovery, port scan, TLS inspection, role classification, VLAN detection, MAC/OUI, Draw.io export, scan history/diff), Ping, DNS (custom server, via tunnel), Cert Inspector (chain+TLS, via tunnel), Port Scanner (banner grab, via tunnel), Subnet (IPv4+IPv6), IP Converter, HTTP Status, Whois, Network Calculator (supernet+VLAN) |
| **Security** | 7 | Password (crack time+history), SSH Key (RSA+Ed25519), Hash (SHA3+progress), HMAC, JWT (signature verify), Certificate Generator (CA+leaf), TOTP (RFC 6238) |
| **Encoding** | 6 | Base64 (URL-safe), URL Encoder, JSON (error position), Regex (match highlight), Text Diff (word-level), Text Case (8 formats) |
| **System** | 10 | Chmod, Crontab Builder, DateTime (timezone+relative), UUID (v4+v7), Hosts Editor, SSH Config Generator, Log Viewer/Tail, Cron Job Manager, Service Status Dashboard, **Diagram Editor** (draw.io embedded offline) |
