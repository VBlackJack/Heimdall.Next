<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Architecture

Heimdall.Next is a .NET 10 WPF application organized as a multi-project solution with strict dependency boundaries. Supports RDP, SSH, SFTP, FTP, VNC, Telnet, Citrix, and Local Shell connection types with ~5,102 i18n keys per locale (EN/FR), 49 built-in sysops tools with contextual help, cross-tool navigation, and 4,201 automated tests. Health monitor polls in parallel (Task.WhenAll), XML importers hardened against XXE, all Debug.WriteLine replaced with FileLogger. WCAG AA compliant Design System with 45 design tokens (typography min 11px, spacing, corner radius, opacity, icon sizes, font family), micro-animations, FocusIndicatorBrush for keyboard accessibility, unified two-tier icon system (vector geometries + MDL2), per-category tool color coding, declarative i18n via `{loc:Translate}` markup extension, and progressive disclosure ServerDialog.

## Solution Structure

```
Heimdall.slnx (8 projects)
├── src/
│   ├── Heimdall.Core          net10.0         Models, session diagnostics, security, config, state machine, i18n, network scanner, utilities
│   ├── Heimdall.Ssh           net10.0         SSH engine, tunnels, Pageant, TOFU, failure classifier, health monitor
│   ├── Heimdall.Rdp           net10.0-windows RDP + Citrix engine (ActiveX, StoreBrowse), credential autofill
│   ├── Heimdall.Sftp          net10.0         SFTP/FTP browser (SSH.NET + FtpWebRequest), remote file editing
│   ├── Heimdall.Terminal      net10.0-windows Terminal sessions (pipe mode, ConPTY, Telnet)
│   └── Heimdall.App           net10.0-windows WPF application (MVVM, views, themes, DI)
│       ├── Views: MainWindow, SessionPaneControl, SplitContainerControl,
│       │          EmbeddedRdpView, EmbeddedSshView, EmbeddedSftpView,
│       │          EmbeddedCitrixView, EmbeddedVncView, FloatingSessionWindow
│       ├── Views/Tools: 49 built-in sysops tools (IToolView interface)
│       └── Services: ConnectionService (.Rdp/.Ssh/.Sftp/.Ftp/.Vnc/.Telnet/.Citrix/.Local/.Tunnel),
│                     SplitService, SessionSplitService, EmbeddedSessionManager, ToolRegistry,
│                     TaskSchedulerService, MacroService, EphemeralFileServer, FileShareService,
│                     X11ServerManager, WebSocketVncProxy, KeyboardShortcutService,
│                     ContextMenuFactory, SessionTabContextMenuFactory, ToolsTabPopulationService
└── tests/
    ├── Heimdall.Core.Tests    State machine, HMAC integrity, input validation, PIN manager, config manager tests
    ├── Heimdall.Ssh.Tests     SSH engine tests (failure classifier, preflight, TOFU, Pageant, Plink)
    ├── Heimdall.App.Tests     SplitService, SessionDiagnostic, NotesStorage, ThemeService, Migration, EphemeralFileServer, tool coherence
    └── Heimdall.App.UiTests   Desktop UIAutomation smoke and accessibility coverage
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

### 2b. Local Shell Elevation Strategy

**Problem**: gsudo's `ServiceHelper.StartService` crashes when endpoint privilege managers (AdminByRequest, CyberArk, BeyondTrust) intercept the UAC prompt and invalidate process handles mid-elevation.

**Solution**: Configurable `ElevationMode` enum with fallback chain:

| Mode | Mechanism | Embedded Terminal | AdminByRequest Compatible |
|------|-----------|-------------------|---------------------------|
| `None` | No elevation | Yes | N/A |
| `Auto` | gsudo `--direct` → external window fallback | Yes (gsudo) / No (fallback) | Yes |
| `Gsudo` | gsudo `--direct` only | Yes | Partial |
| `Runas` | `ShellExecute` with `runas` verb | No (external window) | Yes |

Key design decisions:
- `--direct` flag bypasses gsudo's service/cache mechanism, avoiding the `ServiceHelper.StartService` crash
- `Auto` mode tries gsudo first (best UX: embedded terminal), catches `InvalidOperationException`, retries as external window
- `Runas` mode uses `Process.Start` with `Verb="runas"` and `UseShellExecute=true` — cannot redirect stdin/stdout (Windows limitation), so the terminal opens in a separate window
- UAC cancellation (Win32 error 1223) caught and reported as user-friendly message
- Backward compatible: legacy `LocalShellElevated=true` maps to `Auto` via `EffectiveElevationMode` computed property

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

**Problem**: WPF's `WindowsFormsHost` has an "airspace" issue where the rendering surface is not properly bound to the visible HWND if layout hasn't been flushed before `Connect()`. Additionally, the Win32 HWND always renders above WPF content in the same window — `Panel.ZIndex` has no effect.

**Solution**: Mandatory layout flush before every `Connect()`:

```
UpdateLayout() -> DoEvents() -> Dispatcher.Invoke(Render) -> EnsureHandle -> Connect()
```

**Airspace overlay rule**: Any WPF UI that must render above a `WindowsFormsHost` surface (RDP, VNC) MUST use a WPF `Popup`. A Popup creates its own top-level HWND that the OS composites above the embedded ActiveX surface. The Command Palette uses this pattern — it was originally a `Grid` overlay with `Panel.ZIndex="9999"` which was invisible over RDP sessions.

Additional guards:
- Resolution updates blocked for 5 seconds after `OnConnected` (prevents disconnect code 4360)
- COM dispose follows strict order: collapse visibility, detach from tree, disconnect, detach event sink, dispose — do NOT call `Marshal.ReleaseComObject` (let AxHost handle RCW cleanup)
- Auto-reconnect with bounded retry (`MaxReconnectAttempts = 20`) and cancel support via COM event sink
- Disconnect reason decoder: `GetDisconnectReasonKey()` maps 24 MsTscAx codes to i18n keys

**Performance optimizations** (cold-start mitigation):
- **COM pre-warm**: Background STA thread creates/disposes throwaway `RdpActiveXHost` at startup, forcing mstscax.dll + 22 static dependencies into memory (~400ms saved on first connection)
- **DNS pre-resolution**: `Dns.GetHostEntryAsync()` fire-and-forget on server selection in tree view
- **TCP keep-alive**: `KeepAliveIntervalMs = 60_000` for network break detection
- **Per-server experience flags**: `AdvancedSettings9.PerformanceFlags` bitmask (wallpaper, themes, animations, drag, cursor shadow, composition) configurable in Server Dialog
- **TCP-only mode**: `BandwidthDetection = false` + `NetworkConnectionType = 6` (LAN) disables UDP probe to avoid firewall timeouts

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

**Architecture**: A `Popup`-based Command Palette (own HWND, renders above ActiveX/WindowsFormsHost surfaces) parses connection strings of the form `[protocol://]user@host[:port]`. The parser infers protocol from port if omitted (22=SSH, 3389=RDP, 1494=Citrix, 5900=VNC, 23=Telnet, 21=FTP). A `ServerProfileDto` is created transiently (not persisted) and passed to `ConnectionService.ConnectAsync()`. Recent connections are stored in `settings.json` for quick re-use. When opened in split mode, the palette forces Embedded connection mode and attaches the new session as the secondary pane of the active tab.

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

### 16. Recursive N-Pane Split System

**Architecture**: The split layout is modeled as a binary tree of `ISplitContent` nodes:

```
ISplitContent (marker interface)
├── SessionPaneModel     Leaf: PaneId (GUID), HostControl, ServerId, OriginalServerId, Title, Status, FailureDetails, ...
└── SplitContainerModel  Branch: First, Second (ISplitContent), Orientation, SplitRatio
                         Constants: MinRatio (0.1), MaxRatio (0.9), DefaultRatio (0.5), SplitterThickness (4)
                         Auto-clamping: SplitRatio setter clamps to [MinRatio, MaxRatio] BEFORE PropertyChanged
```

**Pane identity** — two distinct IDs serve different purposes:
- `ServerId` (session-scoped): assigned AFTER successful connection; used as state machine key and tunnel tracking key. Empty during connection phase.
- `OriginalServerId` (stable): set at pane creation from server inventory ID; never changes. Used for reconnect lookups, disconnect history, and `SplitLayoutMemory` pairing. Set early in `SplitSessionWithServerAsync` for proper cleanup if the pane is closed during connection.

`SessionTabViewModel.RootContent` holds the tree root. A single pane is a `SessionPaneModel`. A split is a `SplitContainerModel` whose children can themselves be split — enabling arbitrary layouts (2x2, L-shape, 3 side-by-side, etc.) up to 8 panes per tab. `SplitTreeHelper` provides static traversal: `EnumerateLeaves`, `FindPane`, `FindPaneByServerId`, `FindPaneByHostControl`, `FindParent`, `FindSibling`, `RemovePane`, `ReplacePane`, `CountLeaves`, `FirstLeaf`. Internal mutations use `bool`-returning helpers (`ReplacePaneRecursive`, `ReplaceContainer`) for short-circuit after first match. Pane-scoped failure diagnostics now live on `SessionPaneModel` (`FailureDetails` plus derived visibility helpers), so SSH/RDP failure disclosure remains attached to the correct pane even when tabs are split.

**SplitService** (extracted from MainViewModel): All split/merge orchestration lives in `Heimdall.App.Services.SplitService`, a singleton DI service that owns:
- `SplitSessionWithServerAsync` — async connection + tree insertion with CancellationToken propagated to protocol handlers
- `SplitSessionWithTool` — synchronous tool docking
- `MergeExistingSession` — live reparent with CanClose() check on all source tree leaves (not just primary shim); user feedback when blocked by busy tool
- `ClosePane` — type-aware cleanup with fixed disposal order (detach → remove → dispose)
- `CloseAllPanes` — centralized tab teardown: CanClose() gate, cancellation, history, tunnel release, state reset, disposal (called by `ConnectionViewModel.CloseSessionInternal`)
- `ReconnectPaneAsync` — deferred old state machine cleanup (released only after new connection succeeds); no longer creates self-referential LayoutMemory entries
- `SwapSplitPanesAsync` — async two-phase swap: detach host controls → await visual tree → swap model → await again → restore (prevents WebView2/ActiveX reparenting race)
- `ToggleSplitOrientation` — in-place tree mutation
- `ConnectByProtocolAsync` — unified 8-protocol dispatch with CancellationToken passthrough to all `ConnectionService.Connect*Async` handlers
- Per-session `CancellationTokenSource` lifecycle (`RegisterSession`/`CancelSession` with deferred dispose to avoid leaks)
- `SplitLayoutMemory` instance for layout persistence

`ConnectionViewModel` is a thin shell: `CloseSessionInternal` delegates entirely to `SplitService.CloseAllPanes`, keeping tab collection management as its only responsibility. Callbacks to `ConnectionViewModel` (ActiveSessions, ActiveSession, HasActiveSessions, StatusText) are wired by `MainViewModel` at construction time, following the same pattern as `EmbeddedSessionManager`.

**Rendering**: WPF implicit `DataTemplate`s in `Window.Resources` recursively instantiate `SessionPaneControl` (leaf) and `SplitContainerControl` (branch with `GridSplitter`). Each leaf manages its own overlays (loading spinner, disconnect with Reconnect/Close buttons, accessible labels). Focus accent (`IsKeyboardFocusWithin`) on active pane + hover border (`IsMouseOver`) for visual feedback. Both controls subscribe in constructor and detach all event handlers in `Unloaded` (PropertyChanged, Click, DragCompleted, MouseDoubleClick) to prevent memory leaks. Minimum pane size enforced (MinWidth=120, MinHeight=80). Double-click splitter resets ratio to 50/50. NaN/Infinity guard on drag completion. GridSplitter cursor updates dynamically (`SizeNS` for Horizontal, `SizeWE` for Vertical) in `ApplyLayout()`.

**Split new connection**: Right-click → "Split..." → Horizontal | Vertical → Command Palette in split mode → select server → new `SessionPaneModel` inserted into tree via `SplitContainerModel` wrapping. Loading overlay visible during async connection. Post-await guard aborts if pane was removed or tab closed during connection. Per-session CancellationToken ensures graceful abort when tab is closed. Split palette shows ALL servers from inventory (not limited to recent).

**Split with tool**: When a built-in tool is selected in split mode (palette search or recent tools), `SplitSessionWithTool()` creates the tool control synchronously via `EmbeddedSessionManager.CreateToolControl()` and docks it directly into the split pane — no loading overlay or async connection needed. Tool panes use `ConnectionType = "TOOL:<ID>"` and a GUID-based `ServerId` for tree addressing.

**Merge existing session or tool**: Right-click → "Merge with..." → session or tool → Horizontal | Vertical → `MergeExistingSession()` reparents the live `HostControl` into a new pane without reconnecting. Checks `CanClose()` on all source tool panes before proceeding (busy tool blocks merge). Works symmetrically for both connection tabs and tool tabs. Uses `OriginalServerId` as stable lookup key (fallback from `ServerId` which may be empty during connection; tool tabs use `ServerId` directly). Consults `SplitLayoutMemory` to restore prior ratio for previously-paired servers. Cancels any in-progress operations for the source session. State machine entries preserved during merge (connections alive, just reparented) — cleanup happens when the tab is eventually closed.

**Drag-to-split**: Drag a tab onto the content area of another tab. Orientation is auto-detected from drop position (closest edge). Works on already-split sessions to create 3+ pane layouts.

**Operations**: Swap panes, toggle orientation (Ctrl+Shift+O), detach any pane to `FloatingSessionWindow`, close individual pane (promotes sibling in tree), unsplit (restores pane as independent tab). Pane close is type-aware: connection panes get disconnect history + tunnel release + state machine reset; tool panes check `IToolView.CanClose()` and skip state machine/tunnel teardown. Disposal order is fixed: detach HostControl from visual tree → remove pane from tree → dispose host control (prevents RDP/ActiveX airspace issues).

**Splitter ratio**: Model auto-clamps `SplitRatio` to `[0.1, 0.9]` in the setter (before PropertyChanged fires) — the view reads the ratio directly without redundant clamping. Captured via `GridSplitter.DragCompleted` per `SplitContainerControl` with NaN/Infinity guard, persisted in the tree model. Restored on tab switch via layout rebuild. Double-click splitter resets to `DefaultRatio` (0.5).

**Split layout persistence**: `SplitLayoutMemory` records server pair associations in `config/split-layouts.json` with versioned JSON schema (`{ "version": 1, "entries": [...] }`). Backward-compatible with legacy bare-array format. Thread-safe via `lock` on all public methods. Atomic save via unique temp file (`Guid`-suffixed) + `File.Move(overwrite: true)` with `finally` cleanup. When opening the Command Palette in split mode, previously paired servers are boosted to the top of results.

**Race condition guards**:
- Per-session `CancellationToken` propagated through `ConnectByProtocolAsync` to all protocol handlers — closing a tab cancels the actual connection attempt, not just the outer wrapper
- `CancelSession` disposes the CTS after a 5-second delay (deferred dispose) so in-flight operations can observe cancellation before the source is reclaimed
- Post-await check `!ActiveSessions.Contains(session) || FindPane(...) is null` prevents orphaned connections
- `CountLeaves >= 8` gate prevents unbounded tree growth
- Anti-double-reconnect via `pane.HostControl is null` check (overlay hides button when connection starts)
- `RemovePane` null subtree guard: promotes sibling instead of assigning null to container children
- Deferred state machine cleanup in reconnect: old tunnel/state released only after new connection definitively succeeds or fails (prevents state loss on reconnect failure)
- `OriginalServerId` set at pane creation (not post-connection) for proper cleanup if pane closed during async connection
- `MergeExistingSession` checks all source tree leaves for HostControl presence (not just the primary shim), preventing false merge rejection on split tabs with a disconnected primary

**Backward compatibility**: `SessionTabViewModel` exposes shim properties (`ServerId`, `Title`, `Status`, `HostControl`, `IsSplit`, `SplitOrientation`, etc.) that delegate to `PrimaryPane` (first leaf). `Secondary*` shim properties target the first leaf of the second child at root level. `NotifyTreeDependentProperties()` (shared method) is called after both `RootContent` changes and in-place tree mutations (swap). `_emptyPane` is per-instance (not static) to prevent cross-session state leakage.

### 16b. Tab Detach to Floating Window

**Problem**: Users need to view multiple sessions side by side, or move a session to a second monitor.

**Solution**: `FloatingSessionWindow` hosts a single detached `SessionTabViewModel`. Any individual pane can be detached from a split tree via `DetachPaneToFloatingWindow(paneId)` — the pane is extracted from the tree, promoted to an independent tab, then detached to a floating window. The window applies the current theme via `WindowThemeHelper`, displays session metadata (title, tunnel route), and provides a reattach button. On close, if not explicitly reattached, the session is returned to the main window for proper cleanup.

### 17. Tunnel Ref-Counting for Shared Tunnels

**Problem**: Multiple connections may traverse the same SSH tunnel (e.g., two RDP sessions through the same gateway). Tearing down a tunnel when one connection closes would kill the others.

**Solution**: `TunnelManager` maintains a `ConcurrentDictionary<int, int>` of reference counts keyed by local port. `AddReference()` increments the count when a new connection uses an existing tunnel. `ReleaseReference()` decrements it, and only calls `CloseTunnel()` when the count reaches zero. `CloseTunnel()` itself checks the ref count before tearing down, providing a double guard.

### 18. Connection Inheritance (GroupDefaultsDto)

**Problem**: Enterprises organize hundreds of servers into groups that share the same gateway, SSH user, key path, or connection type. Configuring each server individually is tedious.

**Solution**: `GroupDefaultsDto` defines default connection settings (gateway, SSH username, key path, port, connection type) at the group/folder level. Servers inherit these values when their own fields are null or empty. Resolution is hierarchical: a server in `PROD/Linux` inherits from `PROD/Linux` first, then falls back to `PROD` if the nested group does not override the field.

### 19. External Credential Provider (CommandCredentialProvider)

**Problem**: Security-conscious environments store credentials in external password managers (KeePassXC, Bitwarden CLI, 1Password CLI, `pass`), not in the application's DPAPI vault.

**Solution**: `CommandCredentialProvider` implements `ICredentialProvider` by executing a user-configured CLI command template. Placeholders `{Host}`, `{Port}`, `{User}`, `{Title}`, `{Database}` are substituted at runtime with context-aware sanitization: `InputValidator.IsShellTarget()` inspects the template's executable to choose strict stripping for shell interpreters (cmd.exe, PowerShell, WSL, WSH) or relaxed stripping for regular executables (keepassxc-cli, bw, op). The command's stdout is captured and trimmed as the password. A 10-second timeout prevents hangs. Soft failures (non-zero exit, empty output) surface a warning dialog to the user. This enables zero-knowledge credential retrieval where Heimdall never persists the password.

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

### 26. MainWindow Code-Behind Split Strategy

**Problem**: `MainWindow.xaml.cs` naturally accretes: it is the owner of ~300 named XAML elements, event handlers, localization wiring, context menus, tab population, and session orchestration. Left unchecked it bloats past 5,000 lines, making navigation, review, and unit testing hard.

**Solution**: two complementary patterns applied as pure structural splits (no logic change, no rename, no signature change):

1. **Extract to DI-registered services** when the logic does not need named XAML element access — a service takes a handful of dependencies via its constructor, is registered as a singleton in `App.xaml.cs`, and is injected into `MainWindow` alongside `MainViewModel`. Communication back into the window uses either a small callback interface (when many methods need window state) or plain `Action<T>` delegates (when only one or two callbacks are needed).
   - **`ContextMenuFactory`** (647 lines) — builds the four session `TreeView` context menus and the "Detected Tools" submenu. Reaches back into `MainWindow` through `IContextMenuCallbacks`.
   - **`SessionTabContextMenuFactory`** (335 lines) — builds the session tab strip context menu (19 conditional items: close/close others/close all/rename/duplicate/detach/split/merge/unsplit/reconnect/…). Reaches back through `ISessionTabContextCallbacks`.
   - **`ToolsTabPopulationService`** (605 lines) — owns the full-page Tools tab rebuild and the sidebar Tools `TreeView` data/filter. Reaches back via `Action<ToolDescriptor>` (card click) + `Action<string>` (pin click). Theme tokens are resolved via `Application.Current.FindResource` so the service stays decoupled from any `FrameworkElement`.
   - **`FileShareService`** — ephemeral HTTP/TFTP folder sharing lifecycle (previously inline in `OnShareFolderClick`). Event-based API (`ShareStarted` / `ShareStopped`), `IAsyncDisposable` — `App.OnExit` routes through `IAsyncDisposable.DisposeAsync` on the service provider to properly dispose async-only services.
   - **`KeyboardShortcutService`** (18 shortcuts) — fluent shortcut registration with `canExecute` gating, replacing the monolithic `OnPreviewKeyDown` switch. Registered in `MainWindow` constructor.
   - **`SessionSplitService`** — split/merge/detach/unsplit orchestration moved out of MainWindow. Exposes `SplitPaletteRequested` event for MainWindow to open the palette in split mode.

2. **Split into `partial class` files** when the logic *must* touch named XAML elements directly (so extraction to a service would require passing dozens of `FrameworkElement` parameters on every call). The new file declares `public partial class MainWindow` and holds a thematically coherent subset of methods. Cross-file access is free (same class, same assembly) so static helpers and private fields remain shared without any visibility changes. POCOs co-located with the partials (`WindowUIState`, `TreeInteractionState`, `TabInteractionState`) own the fields/flags previously scattered across the monolith.
   - **`MainWindow.Localization.cs`** (519 lines) — the 8 `Apply*Localization` methods (`ApplyLocalization` orchestrator + Navigation / Toolbar / Tunnel / Scheduled / Settings / About / Accessibility). Phase 5A/5B have since migrated Navigation/Toolbar/Accessibility to `{loc:Translate}` — those apply helpers are now empty stubs pending deletion after Phase 5C/5D.
   - **`MainWindow.WindowUI.cs`** + `WindowUIState` POCO — fullscreen toggle, sidebar collapse, tree scroll persistence, folder expand/collapse memory, window-bounds save/restore.
   - **`MainWindow.TreeInteractions.cs`** + `TreeInteractionState` POCO — session `TreeView` drag-drop, filter box, inline rename, context-menu plumbing. The move-to-group UX now routes both context-menu and drag-drop through the same `ServerListViewModel` core method, validates drag-drop targets against the same project-scoped group set, preserves `_expandedNodes` by avoiding `LoadServers` reloads, and exposes a dedicated no-group drop zone for drag-to-root parity.
   - **`MainWindow.TabInteractions.cs`** + `TabInteractionState` POCO — session tab drag-to-reorder, drag-to-detach, drop target resolution, tab-strip hover tracking.

**Decision rule**: if the method's entire body is `Mw_X.Text = vm.Localize(...)` against named elements, use a partial class. If it manipulates the tree or builds controls from data and can be reshaped to take a `Panel`/`Control` parameter, extract to a service. The same `ConnectionService` in `Heimdall.App/Services/` already uses this partial-class pattern with 10 files for its per-protocol connection flows.

**Result**: `MainWindow.xaml.cs` dropped from **4,895 → 2,123 lines (−57%)** across Chantier 1 + Phases 1–3, with each extracted unit now independently reviewable and the door open for targeted unit tests where appropriate. Phase 1 extracted `OnboardingFlowViewModel`, `FileShareService`, and the `WindowUI` partial. Phase 2 extracted `KeyboardShortcutService`, `SidebarViewModel`, `ToolsTabViewModel`, and removed a dead `OnWindowDeactivated` Command Palette handler that had been closing the palette on open. Phase 3 extracted `TreeInteractions`/`TabInteractions` partials, `SessionTabContextMenuFactory`, and `SessionSplitService`.

### 27. MainViewModel Sub-VM Composition

**Problem**: `MainViewModel.cs` had grown to 1,917 lines as the single orchestration point for the sidebar, tools tab, command palette, tunnels panel, scheduled tasks, session lifecycle, broadcast mode, and workspace restore. Every new feature landed a few more `[ObservableProperty]` / `[RelayCommand]` / event handler in the same class, blurring domain boundaries and making the VM hard to test in isolation.

**Solution**: composed sub-VMs instantiated inside the `MainViewModel` constructor (no DI registration, no service-locator lookup). Each sub-VM takes `MainViewModel` as its first constructor parameter and reaches sibling state through `_main.X` (same pattern already used by `TunnelsViewModel` and `ScheduledTasksViewModel`). Sub-VMs that own event subscriptions implement `IDisposable` and are disposed from `MainViewModel.Dispose`.

Four sub-VMs extracted in Phase 4:

- **`CommandPaletteViewModel`** (Ctrl+K palette) — 14 methods covering fuzzy search ranking, tool-command parsing (`tools`, `ping 10.0.0.1`), ad-hoc connection string parsing (`user@host:port` with protocol inference), recent-tools boosting, and the connect/split flows. Owns the `IsCommandPaletteOpen` state and the `SplitLayoutMemory` pairing lookup.
- **`TunnelsViewModel`** — tunnel panel collection, tunnel tab, route resolver (`ResolveRoute(sessionId)` for the session header display). Subscribes to `TunnelManager.ActiveTunnels` `CollectionChanged` and tears it down in `Dispose`.
- **`ScheduledTasksViewModel`** — `TaskSchedulerService` ownership, `TasksProvider`/`TaskDueCallback`/`PersistCallback` wire-up, idempotent `_started` flag to survive `LoadAsync` re-entrancy.
- **`SessionCoordinator`** — session-lifecycle hub: 8 external wire-ups (5 `Split.*` providers/setters + 3 `EmbeddedSessionManager` callbacks: `BroadcastCallback`, `IsBroadcastActive`, `ReconnectRequestedCallback`), the broadcast-mode cluster (toggle + fan-out + per-view indicators), `OnSessionReady` (materialize session tabs, resolve tunnel route, record history, auto-open SFTP companion pane), and `OnReconnectRequestedAsync` (close stale tab + re-trigger connect flow). `OpenToolCallback` stayed on `MainViewModel` because `OpenToolTabAsync` is a shell concern shared with the sidebar/tools-tab/palette consumers.

Two additional shell-layer sub-VMs were extracted during Phase 2 to mirror the XAML binding story for the left sidebar:

- **`SidebarViewModel`** — Sessions/Tools tab toggle, tool filter text, `SidebarToolCategoryViewModel` tree, lazy population on first activation, `Ctrl+Shift+T` toggle target selection (the sibling RadioButton must be explicitly set — see `ToggleSidebarTab()` gotcha in §Sidebar).
- **`ToolsTabViewModel`** — full-page Tools browser VM state (favorites, recents, filter, section visibility). Section rendering itself stays in `ToolsTabPopulationService` (which writes to named XAML panels), wired through a Panel-injection event so the VM never touches `FrameworkElement` directly.

**Result**: `MainViewModel.cs` dropped from **1,917 → 628 lines (−67%)**. The shell class now orchestrates sub-VM instantiation, shared settings, the single `OpenToolCallback`, and the composed `LoadAsync` pipeline. Each sub-VM is independently navigable, testable in isolation (sub-VMs that don't touch `Application.Current.Dispatcher` run cleanly in xUnit), and owns its own event-subscription lifecycle via `IDisposable`.

### 28. Declarative i18n Migration (Phase 5)

**Problem**: before Phase 5, `MainWindow.Localization.cs` owned localization as an imperative 523-line code-behind pass. `ApplyLocalization()` ran at startup and on every `LocalizationManager.LocaleChanged` notification, dispatching to 7 `Apply*Localization` methods (Navigation, Toolbar, Tunnel, Scheduled, Settings, About, Accessibility) that touched 300+ named XAML elements with assignments such as `Mw_X.Text = vm.Localize("Key")`, `AutomationProperties.SetName(Mw_X, vm.Localize("Key"))`, `Mw_X.Tag = vm.Localize("Key")`, and tooltip/header equivalents. Every locale switch re-ran the full pass and rewrote labels, tooltips, accessibility names, and watermarks by name.

**Solution**: Phase 5 moved approximately 307 imperative localization sites to declarative XAML using the existing `{loc:Translate Key}` markup extension. `TranslateExtension` and `LocalizationSource` were intentionally unchanged: the extension creates a WPF `Binding` to `LocalizationSource.Instance[Key]`, and `LocalizationSource` raises `PropertyChanged("Item[]")` when the locale changes so bound DependencyProperties refresh without a code-behind render pass.

Migration was split by UI pattern:

- **5A — Navigation + Toolbar labels (58 sites)**: tab strip headers, toolbar button content/tooltips, Quick Connect / Quick File Server, broadcast toggle label, status-bar ready text, and shortcuts hint. Straight mappings used the mechanical pattern `Mw_X.Text = vm.Localize("Key")` → `Text="{loc:Translate Key}"`.
- **5B — Accessibility attributes (39 sites)**: all imperative `AutomationProperties.SetName(Mw_X, vm.Localize("Key"))` calls moved to `AutomationProperties.Name="{loc:Translate Key}"` on the owning XAML element. `ApplyAccessibilityLocalization` was deleted entirely.
- **5C.1 — Tunnel + Scheduled + About (40 sites)**: tunnel/scheduled DataGrid column headers, context-menu headers, action buttons, and field labels moved 1-for-1 to XAML. `ApplyScheduledLocalization` had no residual work and was deleted.
- **5C.2 — Settings tab (160 sites)**: the densest pass covered 6 settings sub-tabs with radios, checkboxes, labels, watermarks, tooltips, and option groups. It included 24 `Content` + `AutomationProperties.Name` twin migrations spanning theme variants, session persistence, transport modes, RDP display/audio options, gateway actions, apply-mode buttons, and credential-provider actions. `ApplySettingsLocalization` remains only as a residual runtime UI population stub.
- **5D.1 — Composites via inline `<Run>` (8 sites)**: status-bar composites (`" " + key + " " + key`) and About feature bullets (`"\u2022 " + key`) were split into anonymous inline `Run` elements containing literal text plus `{loc:Translate Key}` bindings. `ApplyNavigationLocalization` and `ApplyAboutLocalization` were deleted.
- **5D.2 — Logic-heavy cases (2 sites + helper extraction)**: the share-folder conditional label moved out of `ApplyToolbarLocalization` into `UpdateShareFolderLabel()` on `MainWindow`, called from `SharingStarted`, `SharingStopped`, the locale handler, and startup while `FileShareService` remains non-INPC. The tunnel-panel header `{0}` split moved to `TunnelsViewModel.TunnelPanelHeaderPrefix` / `TunnelPanelHeaderSuffix`, with `Mode=OneWay` inline `Run` bindings and `LocalizationManager.LocaleChanged` re-notification. `ApplyToolbarLocalization` and `ApplyTunnelLocalization` were deleted.

**Result**: `MainWindow.Localization.cs` dropped from **523 → 122 lines (−77%)**. Its remaining responsibilities are deliberately not pure XAML label localization:

- `ApplyLocalization()` — now a one-call dispatcher to `ApplySettingsLocalization(vm)`.
- `ApplySettingsLocalization()` — residual runtime UI population: `PopulateCredProvPresets`, `PopulateExtToolPlaceholderList`, `UpdateExtToolPreview`, `UpdateExternalToolProviderStatus`, and the async token-status check. These helpers generate or update dynamic UI from runtime state, so they stay imperative until a dedicated settings-helper extraction.
- `RefreshVmDrivenLocalization(vm)` — helper called from the constructor and locale change handler to refresh VM-driven ToolsTab labels that were previously cascaded through `ApplyLocalization()`. This preserves sub-VM refresh behavior after the dispatcher was reduced to its single settings call.

The Phase 5 final smoke test also surfaced a latent Command Palette regression from the Phase 2A/4A refactor path: single-click inside the palette closed the popup before double-click could fire. `OnWindowPreviewMouseDown` now guards clicks originating inside `CommandPalettePopup.Child` with fallback `IsMouseOver` and bounds checks, preserving outside-click dismissal while allowing normal ListBox selection and double-click execution.

**Future work**:

- `FileShareService` can implement `INotifyPropertyChanged` so `Mw_ShareFolderLabel` becomes a pure binding and `UpdateShareFolderLabel()` disappears.
- Eight accessibility keys (five navigation tabs from Phase 5B and three gateway buttons from Phase 5C.2) currently preserve imperative behavior over the more descriptive `Access*` variants. If NVDA testing flags phrasing issues, this is a small XAML-only key swap.
- The 5 residual settings helpers can move into dedicated CredProv / external-tool helper services, eliminating `MainWindow.Localization.cs` entirely. That is an architectural cleanup, not an i18n migration requirement.

### 29. Post-connect Command Library Resolution

Batch 57 introduced structured post-connect steps for SSH embedded sessions.
Batch 58 keeps the runner stateless but adds a runtime resolver bridge to
TwinShell so a step can either remain literal (`Input`) or reference a Command
Library action by ID. The data contract stays additive on
`Heimdall.Core.Models.PostConnectStep`:

- `Input` remains the literal command and is preserved for unlink UX.
- `CommandLibraryId` identifies the linked TwinShell action.
- `CommandLibraryParams` stores parameter values keyed by
  `TemplateParameter.Name`.

`SessionCoordinator` still owns the SSH embedded trigger point, but it now
passes an optional `IPostConnectStepResolver` into
`IPostConnectSequenceRunner.RunAsync(...)`. The resolver is implemented in App
because it needs TwinShell services, while the migration logic stays in Core
next to `ServerProfileDto` and `ConfigManager`.

The resolution chain is:

1. `SessionCoordinator.OnSessionReady` starts the post-connect sequence for SSH
   embedded tabs only.
2. `PostConnectSequenceRunner` inspects each step.
3. Literal step (`CommandLibraryId == null`) executes `Input` exactly as in
   batch 57.
4. Linked step opens a fresh DI scope via `IServiceScopeFactory`, resolves
   `IActionService` and `ICommandGeneratorService`, and attempts to resolve the
   Linux command template at run time.
5. Successful resolution emits `Resolved` and the generated Linux command is
   written to the session callback.
6. Configuration faults (`action missing`, `no Linux template`,
   `invalid parameters`) emit `Broken`, increment `StepsBroken`, and continue
   the sequence without honoring `OnFailure.Stop`.

This keeps Command Library linkage fresh on every connect, avoids caching scoped
TwinShell services, and prevents a stale or deleted library entry from silently
executing the dormant literal fallback.

Batch 59 keeps the runtime untouched and improves authoring only. The
`ServerDialog` captures a minimal `AutoPrefillContext` (`Host`, `Port`,
`Username`, `ConnectionType`) when opening the Command Library picker. The
picker applies a strict alias table (`host`/`hostname`/..., `port`/`sshPort`/...,
`user`/`username`/...) to prefill matching parameters once, at selection time.
Prefill is snapshot-only, never live-bound back to server fields, and existing
parameter values always win. Parameters whose technical name matches the secret
blacklist (`password`, `token`, `secret`, etc.) are structurally excluded from
prefill even if future alias tables expand.

## Design System (CommonControls.xaml — 1,880+ lines, 45 tokens, WCAG AA)

The application uses a centralized Design System defined in `CommonControls.xaml` with full WCAG AA contrast compliance across all **7 Dracula theme variants** (DraculaPro, Alucard, Blade, Buffy, Lincoln, Morbius, VanHelsing). Theme swapping is owned by the centralized `ThemeService` (singleton DI) — see `docs/TROUBLESHOOTING.md` ("Theme Switching — Stale Colors After Swap") for the reactivity patterns.

**Typography tokens (10)** — `sys:Double` resources for consistent font sizing:
- `FontSizeSmallCaption` (11), `FontSizeCaption` (12), `FontSizeBody` (13), `FontSizeBodyLarge` (14), `FontSizeSubtitle` (15), `FontSizeLarge` (17), `FontSizeTitle` (20), `FontSizeDisplay` (22), `FontSizeHeadline` (24), `FontSizeHero` (64)
- Usage: `FontSize="{StaticResource FontSizeBody}"` instead of `FontSize="12"`

**Font family tokens**:
- `FontFamilyMonospace` (`Consolas, Courier New, monospace`) — used for path boxes, code editors, terminal text

**Spacing tokens (5 uniform + 3 asymmetric)** — `Thickness` resources for margins/padding:
- Uniform: `SpacingXs` (4), `SpacingSm` (8), `SpacingMd` (12), `SpacingLg` (20), `SpacingXl` (24)
- Asymmetric: `ContentAreaMargin` (16,0,16,16) for tool content areas, `SessionHeaderPadding` (8,4) for session header strips, `ToolHeaderPadding` (12,8) / `ToolFooterPadding` (12,8) for tool panel headers/footers
- Button padding by role: `PaddingButtonHelp` (6,2), `PaddingButtonCopy` (10,4), `PaddingButtonPrimary` (12,6), `PaddingButtonPreset` (8,2) — Copy/Export buttons must use `PaddingButtonCopy`, not `PaddingButtonPrimary`
- Input field padding: `PaddingInput` (8,6) for all TextBox inputs
- Truly one-off asymmetric margins (`Margin="0,0,8,0"`) stay hardcoded — standard WPF practice

**Corner radius tokens (5)**: `CornerRadiusXs` (2), `CornerRadiusSm` (4), `CornerRadiusMd` (8), `CornerRadiusLg` (10), `CornerRadiusXl` (12)

**Opacity tokens (4)**: `OpacityDisabled` (0.55), `OpacityReadOnly` (0.75), `OpacityOverlay` (0.20), `OpacityAccentOverlay` (0.20)

**Icon size tokens (6)**: `IconSizeSmall` (12), `IconSizeMedium` (16), `IconSizeLarge` (20), `IconSizeXLarge` (36), `IconSizeEmptyState` (32), `IconSizeHero` (48)

**Tool category brushes** — 4 distinct colors per tool category (defined once per Dracula variant):
- `ToolNetworkBrush` (blue), `ToolSecurityBrush` (amber), `ToolEncodingBrush` (purple), `ToolSystemBrush` (teal)
- Each tool has a per-tool glyph (Segoe MDL2 Assets) + category color in tree view and palette

**Micro-animations** — Subtle transitions for panels toggling visibility:
- `FadeInPanelStyle`: `DoubleAnimation` opacity 0→1 in 150ms on `Visibility=Visible`
- Duration tokens: `AnimationFast` (150ms), `AnimationMedium` (250ms)
- Applied to: session loading overlay, SSH/RDP/VNC reconnect overlays

**Accessibility**:
- `FocusIndicatorBrush` (cyan on dark, blue on light) — dedicated keyboard focus ring on all button styles
- `TextOnAccentBrush` (white) — used on accent-colored surfaces (buttons, DataGrid selections, checkboxes)
- All foreground/background pairs verified for WCAG AA (4.5:1 minimum contrast ratio)
- `AutomationProperties.Name` on all interactive controls across all 57 tool views + all dialog views, via runtime-localized `SetName()` pattern in `ApplyLocalization()` — no empty XAML placeholders
- `Focusable="False"` on decorative icon TextBlocks (empty state MDL2 glyphs) to exclude from keyboard focus and screen reader navigation
- `ToolAsyncStateController`: centralized loading/error/empty-state/results visibility management for async tools (13 tools adopted)
- `ToolLoadingBarStyle` (indeterminate, 4px) and `ToolDeterminateProgressBarStyle` (determinate, 20px) — all tool ProgressBars use shared styles
- Tool header pattern: Row 0 = title + help button only; input controls in a dedicated input strip (Row 2)

**Protocol icons** — Unique Segoe MDL2 glyphs per protocol type in TreeView:
- RDP (`E7F4`), SSH (`E756`), SFTP (`E8B7`), Local (`E770`), Citrix (`E753`), VNC (`E7F4`), Telnet (`E968`), FTP (`E896`)

**19 themed control styles** with complete state coverage (hover, pressed, focused, disabled):
- Window, PrimaryButton, SecondaryButton, ToolbarGhostButton, TextBox, PasswordBox, ComboBox, TabControl, TabItem, TreeView, ContextMenu, MenuItem, CheckBox, RadioButton, ToolTip, ListBox, Expander, ProgressBar, Slider, DataGrid

**Global defaults**:
- `DataGrid.ClipboardCopyMode="IncludeHeader"` — enables native Ctrl+C on all DataGrids
- `TextBox.IsReadOnly` trigger — `SurfaceBrush` background + `Opacity=0.75` for read-only fields
- `TreeViewItem`/`ListBoxItem` — `IsKeyboardFocused` trigger with `FocusIndicatorBrush` border

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
| Input validation | Compiled regex patterns against injection (CWE-78) via `InputValidator`: `EscapeShellArg()`, `EscapeForDoubleQuotedString()`, `ValidateDomain()`, `SanitizeCsvCell()` |
| Shell injection prevention | `InputValidator.EscapeShellArg()` applied on all SSH tunnel and tool `CreateCommand()` calls (16+ tool views) |
| CSV formula injection | `InputValidator.SanitizeCsvCell()` in 10 exporters + generic `ToolContextMenuHelper` |
| CRLF sanitization | Raw HTTP Host header construction sanitized against header injection |
| Command construction | Structured argument lists for Plink/gsudo (no string concatenation of user input) |
| Placeholder sanitization | Context-aware: `InputValidator.IsShellTarget()` detects shell interpreters (cmd, powershell, bash, wsl, cscript, mshta + .bat/.cmd/.ps1/.vbs/.js/.wsf/.hta); shell targets get strict metacharacter stripping, regular .exe targets get relaxed stripping that preserves `()`, `'`, `%` in legitimate values |
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
| **Security** | External credential provider (command/database/browse/presets/test), Credential Guard |
| **Advanced** | Logging, session logging, timeouts (tunnel/RDP/external tools), external tools (edit/preview/test/validate) |

Action buttons (Save / Reset / Export / Import) are pinned at the bottom, always visible regardless of sub-tab.

Settings persistence: ViewModel -> AppSettings -> ConfigManager -> settings.json (UTF-8 no BOM). ConfigManager writes are protected by a `SemaphoreSlim` to prevent concurrent save corruption.

## WebView2 Deployment Strategy

WebView2 is required for embedded SSH terminals (xterm.js) and VNC sessions (noVNC). `WebView2Helper` centralizes runtime detection:

1. **Bundled Fixed Version Runtime** in `runtimes/webview2/` (Self-Contained edition, ~436 MB)
2. **System Evergreen Runtime** via Edge or standalone installer (Standard edition)
3. **Unavailable** — shows localized error message, no crash

Build editions:

| Edition | Build flag | Size (zip / installer) | WebView2 |
|---------|-----------|----------------------|----------|
| **Standard** | `-Variant Standard` | ~177 MB / ~127 MB | Requires Edge (pre-installed on Windows 10/11) |
| **Self-Contained** | `-Variant SelfContained` | ~397 MB / ~288 MB | Bundled Fixed Version Runtime for air-gapped/isolated environments |

`Build.ps1 -Variant Both` (default) produces both variants + Inno Setup installers. `Build.ps1 -Mode Release -Publish` creates a GitHub release with all artifacts. `Build.ps1 -DryRun` simulates the release without touching git/GitHub. Batch shortcuts: `Run.bat`, `Test.bat`, `Build.bat`, `Release.bat`.

### Test baseline

`dotnet test Heimdall.slnx --no-build` discovers 4201 tests across the four test projects (`Heimdall.App.Tests`, `Heimdall.App.UiTests`, `Heimdall.Core.Tests`, `Heimdall.Ssh.Tests`): 4195 passing and 6 known skipped `ThemeServiceTests` that require a live WPF Application context. Partial per-project TRX files can report smaller counts and be mistaken for a regression — always run the aggregated command for a correct baseline.

## Tool Architecture

### ToolRegistry (Single Source of Truth)

All 49 built-in tools are registered in `ToolRegistry` (singleton). Each tool is described by a `ToolDescriptor` record:

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
    bool CanClose() => true; // default implementation, override to prevent close during async ops
}
```

All tool views implement this contract. `EmbeddedSessionManager.CreateToolControl()` uses the registry's factory delegate to instantiate views without any protocol-specific switch logic. `SplitService.CloseAllPanes()` checks `CanClose()` per-pane before disposing — works for both standalone tool tabs and tool panes inside mixed splits (e.g., SSH + tool in the same tab). `SplitService.ClosePane()` also checks `CanClose()` when closing an individual tool pane in a split tree. `MergeExistingSession` shows a status bar message when a busy tool blocks the merge.

### ToolContextMenuHelper (Shared DataGrid Actions)

Standard context menu actions shared across tool DataGrids:
- `BuildHostActions()`: cross-tool navigation (Ping, PortScan, DNS, Whois, Cert, Browser, Add to servers)
- `BuildCopyRowAction()`: copy selected row as tab-separated text
- `BuildCopyAllAction()`: copy all rows with headers
- `BuildExportCsvAction()`: export DataGrid to CSV file via SaveFileDialog
- `SelectRowOnRightClick()`: select row under cursor on right-click

### ToolContext (Enriched Server Context)

```csharp
public record ToolContext(
    string? TargetHost, int? TargetPort, string? Argument,
    string? DisplayName, string? Username, string? ConnectionType,
    string? ProjectName, string? GroupName, string? SourceServerId);
```

When opening a tool from a server context menu, all available server metadata is passed. Network tools prefill their host input; security tools can use credentials context.

### Tool Navigation

- **Ctrl+Shift+T**: Toggle between the Servers and Tools tabs of the left sidebar (`SidebarTabServers` / `SidebarTabTools` grouped RadioButtons)
- **Ctrl+K → "tools"**: Command palette lists all tools grouped by category
- **Ctrl+K → "ping 10.0.0.1"**: Opens tool with prefilled argument
- **Recent tools**: Last 5 used tools shown at top of palette when opened
- **Singleton behavior**: Context-free tools (UUID, Password, Chmod) reuse existing tab
- **External tools**: Also searchable in Ctrl+K palette
- **Help system**: "?" button on all 49 tools shows localized description, usage instructions, and examples (i18n key pattern: `ToolHelp<UPPERCASE_ID>`, e.g., `ToolHelpBASE64`)
- **Detail panel**: Selecting a tool in TreeView shows dedicated panel (name, category, description, "Open in Tab")
- **Password presets**: Custom presets saved to `config/password-presets.json`, restored on click, deleted via right-click
- **Protocol colors**: Theme-aware brushes defined once per Dracula variant (DraculaPro, Alucard, Blade, Buffy, Lincoln, Morbius, VanHelsing) — resolved through `DynamicResource` everywhere and re-evaluated on theme swap via `ThemeService.ThemeRevision` triggers for converter-based bindings
- **Cross-tool navigation**: `ToolContextMenuHelper` with `OpenToolAction` callback enables right-click → open another tool with prefilled context

### Notes Tool (Obsidian-style)

The Notes tool (#34) provides a local-first Markdown editing experience inspired by Obsidian:

**Editor stack**:
- **Primary**: Milkdown WYSIWYG editor (ProseMirror-based, MIT) hosted via WebView2. Bundled as a single `Assets/milkdown/index.html` (Vite + vite-plugin-singlefile). Source in `Assets/milkdown-editor/`.
- **Fallback**: AvalonEdit with `MarkdownHighlighting` (XSHD) + `MarkdownLivePreviewTransformer` (header scaling, strikethrough decorations, dimmed syntax chars).
- **Selection**: `MilkdownEditorControl.IsAvailable` checks for `index.html` asset; `IsHostInitialized` verifies WebView2 host was created; WebView2 init deferred to `Loaded` event via `WaitUntilLoadedAsync()` dispatcher yield. Falls back to AvalonEdit if `!IsHostInitialized` after `InitializeAsync()`.

**C# ↔ JS bridge** (`MilkdownEditorControl`):
- JS → C#: `ready`, `change { markdown, dirty }`, `open-link { payload }`
- C# → JS: `set-content`, `set-theme`, `set-readonly`, `focus`, `insert`, `set-menu-labels`
- Content sync via `ContentChanged` event (debounced 200ms on JS side)
- Theme: Dracula palette in dark mode via Crepe `--crepe-*` CSS tokens (removed legacy `@milkdown/theme-nord`). AvalonEdit highlighting uses matching Dracula colors

**File management** (`NotesStorageService`):
- Storage: `config/notes/` (configurable via `AppSettings.NotesDirectory`)
- `NoteTreeNode.BuildTree()`: builds filesystem-mirroring tree from flat note list, includes empty folders via `AddEmptyFolders()`
- Inter-note links: `FindNotePathAsync()` resolves by title → filename → slug → relative path with accent-insensitive fallback (`RemoveDiacritics`); `ResolveOrCreateNoteAsync()` creates on miss
- Tags: extracted from `> tags: x, y` metadata lines in blockquotes
- Path traversal: `ValidatePathWithinRoot()` on all I/O operations
- Sync save: `SaveNote()` synchronous method for `CanClose()`/`Dispose()` (avoids sync-over-async)

**Sidebar toggle**: hamburger button in header collapses/expands the TreeView panel. Width persisted to `AppSettings.NotesSidebarWidth` via `ConfigManager.MergeSettingAsync()` (atomic load-mutate-save under write lock).

**Template localization**: `NotesTemplateFactory.Create()` accepts optional `LocalizationManager` — all section headings use `ToolNotesTpl*` i18n keys. `Slugify()` strips diacritics via Unicode normalization so French titles produce ASCII-safe filenames.

**Editor context menu**: right-click in the editor shows 17 Markdown formatting actions (bold, italic, headings, lists, links, code blocks, table, horizontal rule). In Milkdown: JS-native context menu with localized labels via `set-menu-labels` message. In AvalonEdit: WPF `ContextMenu` built dynamically with `WrapEditorSelection`, `PrefixEditorLines`, `InsertInEditor` helpers.

**TreeView context menu**: `OnTreeViewContextMenuOpening` builds dynamic menu; `OnTreeViewPreviewRightClick` stops tunneling to prevent `MainWindow.OnSessionTabRightClick` interception. `MainWindow` also excludes `TreeView` inside `TOOL:*` sessions from session tab menu.

**Drag & drop**: Internal (move note between folders via `MoveNoteToFolderAsync`) + external (.md file import via copy to notes root)
- **Network Cartography engine**: `Heimdall.Core.Discovery/` namespace with CartographyEngine (ping sweep + TTL capture, port scan, banner grab, HTTP/HTTPS header extraction, TLS cert inspection, NetBIOS/SNMP/mDNS UDP probes, OS fingerprinting, KB cache-skip), UdpProbeEngine (raw NetBIOS NBSTAT + SNMPv2c GET + mDNS service discovery), OsFingerprinter (TTL + 33 banner patterns), RoleClassifier (46+ port patterns, 96+ banner fingerprints, compiled CnRegex, multi-source ClassifyEnriched), OuiDatabase (300+ MAC prefixes), VlanDetector, DrawIoExporter, ScanHistoryManager (atomic write, ACL, retention, typed HostChange diff), KnowledgeBaseManager (persistent per-field Observation\<T\> timestamps, merge-on-scan, TTL-based cache acceleration, host purge)
- **PowerShell Execution Policy**: Configurable in Settings > Terminal, applied as `-ExecutionPolicy` flag on local shell launch
- **Elevation modes**: `None` / `Auto` (gsudo `--direct` → external window fallback) / `Gsudo` / `Runas` — `Auto` default for AdminByRequest/CyberArk/BeyondTrust compatibility, configurable per server profile

### Theme System (`ThemeService`)

**Problem**: Runtime theme swapping across 7 Dracula variants must keep every surface in sync — including converters that resolve brushes at convert time (server icons, status dots), UI built in code-behind (sidebar tool browser), the AvalonEdit file editor, and the DWM title-bar chrome. Duplicated swap logic in multiple places caused drift (one code path knew only Dark/Light, another knew all variants).

**Solution**: `Services/ThemeService.cs` is the single owner of the theme dictionary swap.

- **Singleton DI**: registered once in `App.xaml.cs`, injected into `MainWindow`, `MainViewModel`, and `EmbeddedEditorView`
- **`ApplyTheme(string? themeName)`**: idempotent swap. Replaces the existing theme `ResourceDictionary` in `Application.Resources.MergedDictionaries` by searching for a `Source.OriginalString` containing `Theme.xaml`. Legacy values `"Dark"` / `"Light"` from pre-Dracula settings are silently migrated to `DraculaPro` and persisted via `ConfigManager.MergeSettingAsync`. Unknown names fall back to `DraculaPro`. After a successful swap the service updates the DWM dark-mode flag on every open `Window` via `WindowThemeHelper.ApplyCurrentTheme`.
- **`ThemeRevision` counter**: monotonic `int`, bumped *before* `ThemeChanged` fires. XAML `MultiBinding`s that depend on brush-resolving converters add `DataContext.ThemeRevision` (`ElementName=MainWindowRoot`) as a trailing trigger value to force WPF to re-run the converter on each swap. `ElementName` (not `RelativeSource AncestorType=Window`) is required so the binding resolves from inside the Command Palette `Popup`, whose content has its own visual root.
- **`event Action<string> ThemeChanged`**: consumed by downstream views that rebuild brush caches (`EmbeddedEditorView.ApplyTheme` re-reads AvalonEdit chrome colors from the active dictionary) and by `MainWindow.OnThemeServiceThemeChanged` as a safety-net for any residual code-behind UI not expressible via `DynamicResource`.

**Brush-resolving converters** (4 in total): `ConnectionTypeToColorConverter`, `ConnectionTypeToBrushConverter`, `ConnectionStateToBrushConverter`, `ServerStatusToColorConverter`. Each implements both `IValueConverter` (for legacy single-value bindings and direct code-behind use in `FloatingSessionWindow`) and `IMultiValueConverter` (accepts the `ThemeRevision` trigger, delegates to a shared `ResolveBrush` helper).

**Generic resource-key converters**: `ResourceKeyToBrushConverter` (dual `IValue`/`IMulti`, used by the sidebar tool browser to resolve category brushes from VM properties) and `ResourceKeyToGeometryConverter` (simple `IValue`, resolves `Geo.Tool.*` geometries — immutable across themes, no trigger needed).

**Code-built UI reactivity**: instead of caching `Brush` instances from `FindResource`, builders like `MainWindow.PopulateToolsTab` / `RefreshToolsTabSections` / `CreateToolsTabCard` use `element.SetResourceReference(<DP>, "BrushKey")`. Hover-state toggles (e.g. `cardBorder` active/default) call `SetResourceReference` with a conditional key inside the handler rather than flipping pre-cached brushes. Residual `FindResource("<Name>Brush")` call sites remain in transient contexts (drag highlights, context menus rebuilt per interaction) or one-shot views (onboarding overlay, ext-tool placeholder list).

### Sidebar (Servers / Tools Tabs)

**Problem**: The legacy collapsible `ToolsQuickPanel` (`MaxHeight=350`, bottom-docked inside the Servers sidebar) was cramped and competed for vertical space with the server `TreeView`. Mini-card rendering in code-behind froze brushes at build time and required a lazy-rebuild safety net after every theme swap.

**Solution**: the left sidebar is now a tabbed region. Two `RadioButton`s (`SidebarTabServers` / `SidebarTabTools`, `GroupName=SidebarTabs`) sit at the top of the sidebar, styled via `SidebarTabStyle` in `CommonControls.xaml` (flat tab with accent underline on `IsChecked`, `HighlightBrush` hover, `FocusIndicatorBrush` keyboard focus, all colors via `DynamicResource`). `Visibility` of `SidebarServersContent` and `SidebarToolsContent` is bound to each RadioButton's `IsChecked` via `BoolToVisibilityConverter`, so both content containers consume the full remaining sidebar height, one at a time.

**Servers tab**: unchanged — toolbar (search, add, expand/collapse) on top of the `ServerTreeView`.

**Tools tab**: filter `TextBox` + context label (mirrors `Mw_ToolsTabContextText` — "Network tools open without gateway" / "…with <host>") + full-height `TreeView` populated lazily from `ToolRegistry.All` on first `SidebarTabTools.Checked`. The tree now always inserts a localized Favorites category at index 0, populated from `AppSettings.FavoriteToolIds` and sorted alphabetically by the localized `Name` shown in the UI. Data model:
- `SidebarToolCategoryViewModel` (`ObservableObject` via CommunityToolkit.Mvvm): `CategoryName`, `BrushKey`, `Tools`, `VisibleCount` (drives the header badge), `IsExpanded` (two-way), `IsVisible`
- `SidebarToolItemViewModel`: `Id`, `Name`, `BrushKey`, `IconGeometryKey`, pre-lowercased `Searchable` blob (`name + aliases`) for allocation-free filtering. Favorite state is not stored on the leaf VM; it is resolved live from `FavoriteToolIds`.

`HierarchicalDataTemplate` renders category headers (accent dot + name + count badge) and leaves (14×14 vector icon + name). Brush bindings use `MultiBinding` over `[BrushKey, DataContext.ThemeRevision]` routed through `ResourceKeyToBrushConverter` — theme swap reactivity is automatic, no rebuild required. Icon geometries use `ResourceKeyToGeometryConverter` (immutable across themes).

**Filter**: `OnSidebarToolsFilterChanged` updates `IsVisible` per item (via `Searchable.Contains(filterLower)`) and `VisibleCount` / `IsExpanded` per category. Auto-expand when a filter is active, collapse when cleared. The Favorites category participates in the same filtering rules as every other category. An empty-state label appears when no category has a visible child.

**Launch flow**: `OnSidebarToolsSelectedItemChanged` → `LaunchSidebarTool(item)` → resolves descriptor via `ToolRegistry.All.FirstOrDefault(Id)` → reuses the same `CreateInheritedToolContext` / `ResolveToolTabTitle` / `vm.OpenToolTabAsync` / `vm.TrackRecentTool` primitives as the full-page Tools tab. Before opening, the main Servers tab is activated so the session panel is visible. Right-click sets a `_suppressSidebarLaunch` guard before selection changes so the tool does not open while the favorites ContextMenu is targeted. The redundant sidebar `MouseDoubleClick` launcher was removed because single-click already opens the tool and could otherwise produce duplicate tabs for context/network tools.

**Favorites sync**: `MainViewModel.ToggleFavoriteToolAsync` remains the single writer for `FavoriteToolIds` and raises a `FavoritesChanged` event after persistence. `SidebarViewModel` subscribes to that signal and applies targeted add/remove mutation to the Favorites category, then invalidates the sidebar filter. This keeps the sidebar synchronized whether the toggle originated from the sidebar ContextMenu or the full-page Tools tab pin button. A favorited tool is represented by two independent `SidebarToolItemViewModel` instances: one in Favorites and one in its original category.

**Favorites ContextMenu**: attached to leaf items only and built programmatically in code-behind on right-click. Label and `AutomationProperties.Name` are resolved at open time from the current membership of `FavoriteToolIds`, using the existing `TreeCtxAddFavorite` / `TreeCtxRemoveFavorite` and `A11yPinTool` / `A11yUnpinTool` localization keys.

**Ctrl+Shift+T gotcha**: `RadioButton.IsChecked = !IsChecked` on a grouped button does **not** auto-check its sibling — both end up unchecked, both content containers collapse, the sidebar goes blank. `ToggleSidebarTab()` therefore explicitly sets the target: `if (SidebarTabTools.IsChecked == true) SidebarTabServers.IsChecked = true; else SidebarTabTools.IsChecked = true;`.

**Persistence**: reuses the existing `ShowToolsPanel` bool setting (`true` = Tools tab active at startup). Restored in the window `Loaded` handler.

**External tools refresh**: `ToolRegistry.ExternalToolsChanged` invalidates `_sidebarToolsPopulated` and rebuilds immediately if the sidebar Tools tab is currently active; lazy rebuild on next switch otherwise.

### Dedicated Tools Tab (full-page)

Full-page browser on the main navigation rail, independent of the sidebar Tools tab. Contains 3 sections — Favorites (pinned tools, persisted in `AppSettings.FavoriteToolIds`), Recently Used (`_recentToolIds`, max 5), and All Tools by category. Cards are 280px wide with pin/unpin button and category-colored icon background. Search filters across name, aliases, and descriptions.

**Launch flow**: `OnToolsTabCardClick` → `vm.OpenToolTabAsync` → `EmbeddedSessionManager.CreateToolControl` → `ToolRegistry.CreateView` (factory lambda) → `view.Initialize(context, localizer)`. Non-network tools use singleton tab behavior. Network tools pass selected server as `TargetHost` directly (no intermediate prompt). `OpenToolTabAsync` cleans up orphaned tabs on `CreateToolControl` failure.

**Onboarding**: 3-step first-launch overlay (`OnboardingOverlay`, `Panel.ZIndex=500`). Steps: Connect to Servers → Built-in Tools → Quick Connect. Each step navigates to the relevant UI area (Servers tab → Settings tab → switches the sidebar to the Tools tab). Keyboard accessible (Escape, Tab cycle, focus management). Persisted via `AppSettings.OnboardingCompleted`.

**NetworkCartography responsive**: Columns use proportional (`*`) widths with `MinWidth`. `SizeChanged` handler hides detail columns below 1100px and secondary columns below 800px for split pane support.

**Design token gotcha**: `SpacingRowGap` is `sys:Double` (for Margin/Height). `RowDefinition.Height` requires `GridLength` — use `SpacingRowGapGrid` for grid row spacers.

### Tool Categories (49 tools)

| Category | Count | Tools |
|----------|-------|-------|
| **Network** | 16 | **Network Cartography** (ping sweep, port scan, banner grab, TLS cert inspection, OS fingerprinting from 5 sources (TTL/banner/ports/SNMP/NTLM), **SMB2 NTLM challenge extraction** (hostname/domain/OS build/GUID/uptime), **SSH HASSH fingerprinting**, **Shodan-compatible favicon hashing** (30+ known devices), **HTTP product URL probing** (13 vendor paths), **cookie/error page framework detection**, SNMPv2c 6-OID query + IANA PEN vendor decode, NetBIOS NBSTAT, mDNS/Bonjour, **SSDP + UPnP rootDesc.xml** fetch, 320+ OUI MAC lookup + randomized MAC detection, 50+ role patterns + 100+ banner fingerprints + 6 conflict rules, dynamic CIDR VLAN detection, Draw.io topology export, scan history with typed diff, remote subnet scan via SSH gateway (batched probes), **persistent Knowledge Base with TTL-based cache + KB backfill**, **tunnel scan: ping sweep + ARP discovery + parallel `/dev/tcp` probes with per-probe timeout**), Ping, DNS (custom server, via tunnel), Cert Inspector (chain+TLS, via tunnel), Port Scanner (banner grab, via tunnel), Subnet (IPv4+IPv6), IP Converter, HTTP Status, Whois, HTTP Header Analyzer, Banner Grabber, TCP Traceroute, SNMP Walker, ARP Monitor, Firewall Rule Tester, Network Calculator (supernet+VLAN) |
| **Security** | 15 | Password (3 modes, crack time, history, custom presets, clipboard auto-clear), SSH Key (RSA+Ed25519), Hash (SHA3+progress), HMAC, JWT (signature verify), Certificate Generator (CA+leaf), TOTP (RFC 6238), Password Policy Checker, SSH Key Auditor, SSL/TLS Auditor, DNS Security Checker (SPF/DKIM/DMARC), SMB Enumerator, Default Credential Scanner, CVE Lookup, **SecNumCloud Audit** (15 checks, 4 chapters, `Func<string,string> localize` constructor, HTML/CSV/Draw.io export), **Security Audit** (25 scenarios, JSON packs, playlists, CRT mode) |
| **Encoding** | 6 | Base64 (URL-safe), URL Encoder, JSON (error position), Regex (match highlight), Text Diff (word-level), Text Case (8 formats) |
| **System** | 12 | Chmod, Crontab Builder, DateTime (timezone+relative), UUID (v4+v7), Hosts Editor, SSH Config Generator, Log Viewer/Tail, Cron Job Manager, Service Status Dashboard, **Notes** (Obsidian-style Markdown), **Diagram Editor** (draw.io embedded offline), **Hacker Simulator** |

### Declarative i18n (`{loc:Translate}` Markup Extension)

**Problem**: The legacy `ApplyLocalization()` code-behind pattern requires ~385 manual `L("key")` calls, makes the WPF designer show empty controls, and adds boilerplate to every new view.

**Solution**: Custom `MarkupExtension` enabling declarative i18n directly in XAML:
```xml
<TextBlock Text="{loc:Translate StatusReady}"/>
<Button AutomationProperties.Name="{loc:Translate BtnUnlock}"/>
```

**Architecture** (`src/Heimdall.App/Localization/`):
- `TranslateExtension` — `MarkupExtension` that creates a live `Binding` to `LocalizationSource.Instance[Key]` for DependencyProperty targets (auto-updates on locale change). Falls back to static string for non-DP targets. Shows `[Key]` in designer mode.
- `LocalizationSource` — Singleton bridge implementing `INotifyPropertyChanged`. Wraps `LocalizationManager` indexer and fires `PropertyChanged("Item[]")` on `LocaleChanged`, causing all bindings to re-evaluate.
- Initialized in `App.xaml.cs` after locale load: `LocalizationSource.Instance.Initialize(localization)`

**Migration strategy**: Coexists with `ApplyLocalization()`. New views use `{loc:Translate}`, legacy views migrate incrementally. PinDialog fully migrated as POC.

### Two-Tier Icon System

**Problem**: Three parallel icon systems (BitmapImage, Vector Geometry, MDL2 glyphs) complicated maintenance and caused visual inconsistencies between tree view, tabs, and tools.

**Solution**: Unified to two tiers:
1. **Tier 1 — Vector Geometries** (`IconGeometries.xaml`): Named `Geo.<Category>.<Name>` resources (Protocol.Rdp, Status.Connected, Tool.Ping, Tree.Group, etc.). Consumed via `Path` elements + `ConnectionTypeToGeometryConverter` / `ConnectionStateToGeometryConverter`.
2. **Tier 2 — Segoe MDL2 Assets**: Inline in XAML for standard UI chrome (toolbar, navigation, menus). Not centralized — used as `TextBlock` with font-family.

**Key changes**: `ToolRegistry` stores `Geo.Tool.*` keys per tool with `FrozenDictionary` lookups. Converters resolve `TOOL:*` connection types via `ToolRegistry.GetGeometryKey()` / `GetCategoryBrushKey()`. TreeView uses 2 converter bindings instead of ~180 lines of DataTriggers.

### Progressive Disclosure (ServerDialog)

**Problem**: The server add/edit dialog presented 5 tabs of options on first open, overwhelming new users for what is usually a simple "name + host + port" operation.

**Solution**: Two-mode dialog:
- **Simple mode** (default): Shows only essential fields — Name, Connection Type, Host, Port, Project, Gateway.
- **Advanced mode** (toggle): Animated slide-down (ScaleY + Opacity, 300ms ease-out / 250ms ease-in) reveals the full TabControl with protocol-specific options.
- Mode preference persisted to `AppSettings.ServerDialogAdvancedMode` via `ConfigManager.MergeSettingAsync()`.

### DialogCommonStyles.xaml

Shared resource dictionary (`src/Heimdall.App/Themes/DialogCommonStyles.xaml`) with 8 reusable styles extracted from ServerDialog/GatewayDialog/ProjectDialog: `DialogLabelStyle`, `DialogSectionTitleStyle`, `DialogSectionDescriptionStyle`, `DialogHintTextStyle`, `DialogSectionCardStyle`, `DialogFormTextBoxStyle`, `DialogFormComboBoxStyle`, `DialogFormPasswordBoxStyle`.
