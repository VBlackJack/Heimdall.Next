<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Architecture

Heimdall.Next is a .NET 10 WPF application organized as a multi-project solution with strict dependency boundaries.

## Solution Structure

```
Heimdall.slnx (8 projects)
├── src/
│   ├── Heimdall.Core          net10.0         Models, security, config, state machine, i18n
│   ├── Heimdall.Ssh           net10.0         SSH engine, tunnels, Pageant, TOFU, failure classifier
│   ├── Heimdall.Rdp           net10.0-windows RDP + Citrix engine (ActiveX, StoreBrowse), credential autofill
│   ├── Heimdall.Sftp          net10.0         SFTP browser (SSH.NET), remote file editing
│   ├── Heimdall.Terminal      net10.0-windows Terminal sessions (pipe mode, ConPTY)
│   └── Heimdall.App           net10.0-windows WPF application (MVVM, views, themes, DI)
│       └── Views: MainWindow, EmbeddedRdpView, EmbeddedSshView, EmbeddedSftpView, EmbeddedCitrixView
└── tests/
    ├── Heimdall.Core.Tests    State machine tests
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
- **Heimdall.Ssh** depends on Core + SSH.NET
- **Heimdall.Rdp** depends on Core (uses WPF + WinForms for ActiveX hosting; includes Citrix StoreBrowse integration)
- **Heimdall.Sftp** depends on Core + Ssh (reuses SSH.NET connection factory). `SftpSessionBundle` in ConnectionService bundles SftpClient + SshClient for sudo operations
- **Heimdall.Terminal** depends on Core (uses Win32 APIs for ConPTY + pipe mode)
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

SSH host key fingerprints are persisted in a local store (`HostKeyStore`). On first connection, the fingerprint is recorded. On subsequent connections, mismatches trigger `SshFailureCode.HostKeyMismatch` with a user-facing warning.

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

### 10. Multi-Exec Broadcast

**Data flow**: The broadcast source terminal captures user input at the xterm.js `onData` level. When broadcast is active, the input event is relayed via `PostWebMessageAsString` to every opted-in terminal's WebView2 instance, which forwards it to the respective stdin pipe. Each terminal independently echoes the input through its own remote PTY, so output remains per-session.

### 11. Quick Connect (Ctrl+K)

**Architecture**: A modal overlay (`QuickConnectOverlay`) parses connection strings of the form `[protocol://]user@host[:port]`. The parser infers protocol from port if omitted (22=SSH, 3389=RDP, 1494=Citrix). A `ServerProfileDto` is created transiently (not persisted) and passed to `ConnectionService.ConnectAsync()`. Recent connections are stored in `settings.json` for quick re-use.

### 12. Tunnel Panel (Retractable)

**Architecture**: A `GridSplitter`-based side panel bound to `TunnelPanelViewModel`. The panel observes `TunnelManager.ActiveTunnels` (an `ObservableCollection<TunnelSession>`) and displays real-time status. Tunnel teardown sends a cancel request to the specific `TunnelSession` without affecting other tunnels or the parent SSH connection. Panel visibility is toggled via a toolbar button and persisted in user settings.

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
                +-- EmbeddedCitrixView: StoreBrowse session tab
                        +-- storebrowse.exe: StoreFront auth + resource enumeration
                        +-- ICA file generation -> Citrix Workspace launch
```

## State Machines

### Connection State Machine

States: `Disconnected` -> `Initializing` -> `ValidatingConfig` -> `EstablishingTunnel` -> `TunnelEstablished` -> `LaunchingRdp` / `LaunchingSsh` / `LaunchingSftp` / `LaunchingCitrix` -> `Connected` -> `Disconnecting` -> `Disconnected`

Error state reachable from any active state. Transitions validated before application.

### Application Status Machine

States: `Initializing` -> `Ready` <-> `Busy` -> `Shutdown`

Error state reachable from Ready or Busy.

## Security Architecture

| Layer | Mechanism |
|---|---|
| Credential storage | DPAPI (user-scope) via `DpapiProvider` |
| Integrity | HMAC-SHA256 on encrypted blobs via `HmacIntegrity` |
| PIN protection | PBKDF2-SHA256, 100,000 iterations via `PinManager` |
| File protection | Windows ACLs (user + Admins + SYSTEM) via `AclEnforcer` |
| Input validation | Regex patterns against injection (CWE-78) via `InputValidator` |
| File writes | UTF-8 without BOM via `SecureFileWriter` |
| Memory | Credentials handled as `SecureString`, disposed after use |
