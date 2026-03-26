# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

**Heimdall.Next** is a ground-up rewrite of Heimdall (PowerShell 5.1 + WPF) as a modern .NET 10 + WPF application. It is a secure Windows connection manager supporting 8 protocols (RDP, SSH, SFTP, VNC, Telnet, FTP, Citrix, Local Shell), designed as a MobaXterm and mRemoteNG alternative with superior security and modern UX.

**Current build**: v2026.032601 (Release)

## Repository Layout

- **Working directory**: `G:\_dev\SnapConnect\Heimdall.Next`
- **Solution file**: `Heimdall.slnx`
- **Legacy project**: `G:\_dev\SnapConnect\RDPManager` (PowerShell version, maintained in parallel)

## Codebase Size

- **~199 C# source files** (~55,000 LOC)
- **~55 XAML files** (~13,000 LOC)
- **49 test files** (~16,200 LOC), 1,586 xUnit tests
- **~3,566 i18n keys** per locale (EN/FR), declarative `{loc:Translate}` markup extension + legacy `ApplyLocalization()` coexistence
- **33 built-in sysops tools** (ToolRegistry with IToolView interface, cross-tool navigation, Network Cartography with deep fingerprinting)
- **1,880+ lines** of theme XAML (CommonControls + Dark/Light + DialogCommonStyles, Design Tokens, micro-animations)
- **Two-tier icon system**: Vector geometries (`Geo.*` in IconGeometries.xaml) + Segoe MDL2 for UI chrome

## Architecture (6 Projects)

```
Heimdall.slnx
├── src/
│   ├── Heimdall.Core/          # Shared foundation (net10.0)
│   │   ├── Configuration/      # AppSettings, ConfigManager, SchemaValidator, ServerProfileDto, GroupDefaultsDto, ExternalToolDefinition
│   │   ├── Localization/       # LocalizationManager (JSON-based i18n)
│   │   ├── Logging/            # FileLogger (daily rotation), ConnectionHistory
│   │   ├── Models/             # RdpServer, SshGateway, Project, TunnelSession, ISessionResult, ServerProfileDto, TerminalMacro, DefaultPorts, Enums,
│   │   │                       # ISplitContent, SessionPaneModel, SplitContainerModel, SplitTreeHelper
│   │   ├── Security/           # DpapiProvider, HmacIntegrity, CredentialProtector, PinManager, InputValidator, AclEnforcer,
│   │   │                       # NetworkScanner, WakeOnLan, CommandCredentialProvider, ICredentialProvider, SecureFileWriter
│   │   ├── Discovery/          # CartographyEngine, NtlmProbe (SMB2+NTLMSSP), SshFingerprinter (HASSH),
│   │   │                       # FaviconHasher (MMH3), HttpFingerprinter (cookies/URLs/errors),
│   │   │                       # IanaPenDatabase (SNMP sysObjectID), UdpProbeEngine (NetBIOS/SNMP/mDNS/SSDP+rootDesc),
│   │   │                       # OsFingerprinter (5-source), RoleClassifier (50+ ports, 100+ banners),
│   │   │                       # OuiDatabase (320+ MAC prefixes), VlanDetector (dynamic CIDR),
│   │   │                       # DrawIoExporter, ScanHistoryManager, KnowledgeBaseManager, CartographyModels
│   │   ├── Utilities/          # FileSize (shared formatting)
│   │   └── StateMachine/       # ConnectionStateMachine, ApplicationStatusMachine
│   │
│   ├── Heimdall.Ssh/           # SSH engine (net10.0)
│   │   ├── Pageant/            # PageantClient (Win32 IPC), PageantKeyWrapper, PageantHostAlgorithm
│   │   ├── Plink/              # PlinkTunnelRunner (fallback for Pageant-only auth)
│   │   ├── SshConnectionFactory, FailureClassifier, AuthPreflightChecker
│   │   ├── TunnelManager, GatewayChainResolver, HostKeyStore (TOFU)
│   │   ├── ServerHealthMonitor
│   │   └── SshShellSession, SshFailureCode (25 codes), TunnelInfo/Result/Session
│   │
│   ├── Heimdall.Rdp/           # RDP + Citrix engine (net10.0-windows, WPF+WinForms)
│   │   ├── ActiveX/            # RdpActiveXHost, ComInterfaces (IMsTscAx, IMsTscNonScriptable)
│   │   ├── Citrix/             # ConnectionService.Citrix.cs, EmbeddedCitrixView
│   │   ├── CredentialAutofill  # EnumWindows + EnumThreadWindows + UI Automation
│   │   ├── AspectRatioManager, RdpFileGenerator, RdpRedirectionOptions
│   │   └── IRdpSession, CredentialManagerHelper
│   │
│   ├── Heimdall.Sftp/          # SFTP + FTP engine (net10.0)
│   │   ├── IRemoteBrowser      # Common interface for SFTP and FTP browsers
│   │   ├── SftpBrowser         # SSH.NET native SFTP
│   │   ├── FtpBrowser          # FTP/FTPS file browser
│   │   ├── RemoteFileEditor    # FileSystemWatcher auto-upload
│   │   └── PathEscaper
│   │
│   ├── Heimdall.Terminal/      # Terminal engine (net10.0-windows)
│   │   ├── ConPty/             # ConPtySession, NativeMethods, SafePseudoConsoleHandle
│   │   ├── PipeModeSession     # Pipe mode for SSH (stdin/stdout, no ConPTY)
│   │   ├── TelnetSession       # Raw TCP + IAC negotiation for Telnet
│   │   ├── ITerminalSession    # Common interface
│   │   └── SmartPasteGuard     # Multi-line paste protection
│   │
│   └── Heimdall.App/           # WPF application (net10.0-windows)
│       ├── ViewModels/         # MainViewModel, ServerListVM, ConnectionVM, SettingsVM, SessionTabVM
│       │   └── Dialogs/        # ServerDialogVM, GatewayDialogVM, ProjectDialogVM, PinDialogVM, ScheduledTaskDialogVM
│       ├── Views/              # MainWindow, SessionPaneControl, SplitContainerControl,
│       │                       # EmbeddedRdpView, EmbeddedSshView, EmbeddedSftpView, EmbeddedCitrixView,
│       │                       # EmbeddedVncView, FloatingSessionWindow, LocalFileBrowserView
│       │   └── Dialogs/        # ServerDialog, GatewayDialog, ProjectDialog, PinDialog, InputDialog, ScheduledTaskDialog
│       ├── Services/           # ConnectionService (9 partial files: .Rdp, .Ssh, .Sftp, .Citrix, .Local, .Tunnel, .Vnc, .Telnet, .Ftp),
│       │                       # SplitService, EmbeddedSessionManager, MigrationService, DialogService,
│       │                       # NavigationService, SleepPrevention, TaskSchedulerService, MacroService,
│       │                       # EphemeralFileServer, X11ServerManager, WebSocketVncProxy
│       ├── Converters/         # BoolToVisibility, InvertBool, ConnectionState/Type geometry+color, FileSizeConverter, etc.
│       ├── Localization/       # TranslateExtension ({loc:Translate} markup), LocalizationSource (singleton bridge)
│       ├── Themes/             # CommonControls.xaml (1,760+ lines, Design Tokens, micro-animations), DarkTheme, LightTheme,
│       │                       # DialogCommonStyles.xaml, IconGeometries.xaml (Geo.* vector icons)
│       └── Theming/            # WindowThemeHelper (DWM dark mode API)
│
├── tests/
│   ├── Heimdall.Core.Tests/    # StateMachineTests
│   └── Heimdall.Ssh.Tests/     # FailureClassifier, AuthPreflight, HostKeyStore, Pageant, PlinkTunnel
│
├── config/                     # Factory defaults (settings.default.json, servers.default.json)
├── locales/                    # en.json, fr.json (~3,479 keys each)
├── docs/                       # ARCHITECTURE.md, CHANGELOG.md, SECURITY.md, TROUBLESHOOTING.md
├── Build.ps1                   # Portable build script (YYYY.MMDDxx versioning)
└── Dist/                       # Build output (gitignored)
```

### Key Module Responsibilities

| Module | Purpose |
|--------|---------|
| **ConfigManager** | Settings/servers JSON persistence, factory defaults, import/export |
| **MobaXtermImporter** | Parses MobaXterm .mxtsessions/.ini files into ServerProfileDto (6 protocols, path sanitization) |
| **GroupDefaultsDto** | Connection inheritance at project/group level |
| **ExternalToolDefinition** | Configurable external tool integration |
| **LocalizationManager** | JSON-based i18n with ~3,479 keys per locale |
| **FileLogger** | Daily rotation file logging |
| **ConnectionHistory** | Persisted log of connection events for audit and quick re-connect |
| **CredentialProtector** | DPAPI+HMAC encryption with legacy blob support |
| **ICredentialProvider** | Pluggable credential source interface |
| **CommandCredentialProvider** | External command credential retrieval (password manager CLI) |
| **NetworkScanner** | Legacy lightweight subnet scanner (ICMP + TCP port probes) |
| **CartographyEngine** | Full network cartography: ping sweep (TTL capture), port scan, banner grab, HTTP header extraction, TLS cert inspection, UDP probes (NetBIOS/SNMP/mDNS/SSDP), OS fingerprinting (5 sources), enriched role classification, VLAN detection, IP randomization, KB backfill, ARP refresh |
| **NtlmProbe** | SMB2 Negotiate + NTLMSSP Type 1/2 exchange: extracts hostname, domain, DNS forest, OS build, SMB dialect, signing policy, server GUID, uptime — no credentials required |
| **SshFingerprinter** | HASSH fingerprint (MD5 of SSH KEX_INIT algorithm lists) for precise SSH implementation identification |
| **FaviconHasher** | Shodan-compatible MurmurHash3 favicon hashing with 30+ known device hash lookup (FortiGate, ESXi, Synology, Freebox, TP-Link, Hikvision, Grafana, Jenkins...) |
| **HttpFingerprinter** | HTTP framework detection (12 cookie patterns), error page fingerprinting (7 regex), product URL probing (13 vendor-specific paths: Hikvision, Synology, MikroTik, FortiGate, ESXi...) |
| **IanaPenDatabase** | SNMP sysObjectID → vendor decode via 50+ IANA Private Enterprise Numbers (Cisco, Microsoft, Fortinet, MikroTik, Hikvision, Synology...) |
| **UdpProbeEngine** | Raw UDP packet construction for NetBIOS NBSTAT (name/domain/MAC), SNMPv2c GET (6 OIDs: sysDescr/sysName/sysLocation/sysObjectID/sysUpTime/sysServices), mDNS service discovery, SSDP M-SEARCH + rootDesc.xml fetch |
| **OsFingerprinter** | OS detection from 5 sources: ICMP TTL, SSH/HTTP banner (33 patterns), open port inference, SNMP sysDescr (19 patterns), NTLM build number. `MergeAll()` with multi-source confidence boosting |
| **RoleClassifier** | Heuristic role classification: 50+ port patterns, 100+ banner fingerprints, 6 conflict resolution rules, cert O=/OU= parsing, self-signed detection, manufacturer-based inference, randomized MAC detection |
| **OuiDatabase** | Embedded MAC OUI lookup (320+ manufacturer prefixes), locally administered MAC detection → "Private (Randomized MAC)" for smartphone/tablet identification |
| **VlanDetector** | Passive VLAN inference using scan profile CIDR prefix (dynamic subnet grouping) + Cisco `show vlan brief` parser |
| **ScanHistoryManager** | Scan snapshot persistence (JSON, atomic write, ACL, retention policy), historical comparison with typed `HostChange` diff |
| **KnowledgeBaseManager** | Persistent network knowledge base: per-field `Observation<T>` timestamps, merge-on-scan, TTL-based cache skip, host purge |
| **DrawIoExporter** | Network topology diagram generation (Draw.io XML with role-colored swimlanes, OS/NetBIOS/SNMP labels) |
| **WakeOnLan** | Magic packet transmission for remote machine wake-up |
| **ConnectionStateMachine** | 15-state connection lifecycle with validated transitions |
| **SshConnectionFactory** | SSH.NET connection creation with Pageant and key auth |
| **ServerHealthMonitor** | Background health/availability polling for server inventory |
| **TunnelManager** | Reference-counted SSH tunnel lifecycle management |
| **TelnetSession** | Raw TCP + IAC negotiation for Telnet connections |
| **FtpBrowser** | FTP/FTPS file browser implementing `IRemoteBrowser` |
| **SftpBrowser** | SSH.NET native SFTP implementing `IRemoteBrowser` |
| **ConnectionService** | Orchestrates all 8 protocol connections (9 partial files) |
| **TaskSchedulerService** | Scheduled/recurring connection automation |
| **MacroService** | Terminal macro recording, storage, and playback |
| **EphemeralFileServer** | Temporary local HTTP server for file transfer operations |
| **X11ServerManager** | Auto-detection of local X11 servers for X forwarding |
| **WebSocketVncProxy** | WebSocket-to-TCP bridge for noVNC VNC sessions |
| **EmbeddedSessionManager** | Session tab lifecycle, host control creation, split callbacks, detach/re-dock coordination |
| **SplitService** | Split/merge orchestration: async split with CancellationToken, merge with CanClose() check, deferred reconnect cleanup, per-session cancellation lifecycle, ConnectByProtocolAsync dispatch, SplitLayoutMemory ownership |
| **SplitLayoutMemory** | Thread-safe persistence of split server pair history in `config/split-layouts.json` (versioned schema, atomic save, ratio restoration on merge) |
| **SplitTreeHelper** | Static tree operations: `EnumerateLeaves`, `FindPane`, `FindParent`, `RemovePane`, `ReplacePane`, `CountLeaves` |
| **ToolRegistry** | Centralized registry for 33 built-in tools (ToolDescriptor, IToolView factory with CanClose(), categories, icons, palette aliases) |
| **DefaultPorts** | Named constants for well-known protocol ports (RDP, SSH, VNC, FTP, Telnet, HTTP, TFTP) |
| **FileSize** | Shared byte-to-human-readable size formatting utility |
| **SecureFileWriter** | Atomic file creation with restrictive ACL (TOCTOU-safe) |
| **InvertBoolConverter** | WPF value converter for boolean inversion |
| **PaletteActiveIndicatorConverter** | IMultiValueConverter: protocol-colored left rail brush for active sessions in Command Palette |
| **StringToBrushConverter** | Parses hex color strings (e.g., project colors) into `SolidColorBrush` for XAML binding |

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (C# 14) |
| UI framework | WPF (MVVM via CommunityToolkit.Mvvm) |
| DI | Microsoft.Extensions.DependencyInjection |
| SSH/SFTP | SSH.NET 2025.1.0 |
| Terminal rendering | WebView2 + xterm.js (pipe mode) |
| VNC | WebView2 + noVNC (WebSocket proxy) |
| RDP | ActiveX MsTscAx (WindowsFormsHost) |
| Crypto | System.Security.Cryptography.ProtectedData (DPAPI) |
| Testing | xUnit + Moq + FluentAssertions |
| Serialization | System.Text.Json |

## Build & Test

```bash
# Development
dotnet build
dotnet test
dotnet run --project src/Heimdall.App

# Debug build (portable, auto-increments build number)
powershell -File Build.ps1

# Release build (portable + zip archive)
powershell -File Build.ps1 -Mode Release

# Skip tests
powershell -File Build.ps1 -SkipTests
```

### Build Conventions

- **Build number format**: `YYYY.MMDDxx` (xx = sequential within day, starting at 01)
- **Debug builds**: `Dist/debug/Heimdall.Next_build.YYYY.MMDDxx/`
- **Release builds**: `Dist/release/Heimdall.Next_build.YYYY.MMDDxx/` + `.zip` archive
- **When user says "build"**: run `Build.ps1` (Debug mode, increments build number)
- **When user says "release"**: run `Build.ps1 -Mode Release` (Release mode + archive)
- Build runs tests before build (use `-SkipTests` to bypass)
- Version is written to `Heimdall.App.csproj`: `<Version>` (Win32-safe) and `<InformationalVersion>` (display)
- `Dist/` is gitignored

### CI/CD Pipeline

GitHub Actions (`.github/workflows/ci.yml`) runs on push to main/develop and PRs to main:
1. **Build**: `dotnet build` in Release configuration
2. **Test**: `dotnet test` (xUnit, 505 tests)
3. **Validate JSON locales**: Checks i18n key parity between en.json and fr.json (~3,479 keys)
4. **Lint**: Code quality checks

## Code Standards

- **License header**: Apache 2.0 with author "Julien Bombled" on all new files
- **Language**: All code, comments, and documentation in English
- **No hardcoding**: URLs, paths, magic numbers go in config; strings go in i18n locale files
- **Async by default**: Use async/await everywhere, no blocking calls on UI thread
- **MVVM**: Views in XAML, logic in ViewModels, no code-behind except minimal event wiring
- **Nullable reference types**: Enabled project-wide
- **i18n key convention**: `<Context><Element>` in CamelCase (e.g., `ErrorPlinkNotFound`, `BtnConnect`)

## Key Design Decisions & Patterns

### SSH: SSH.NET + Plink Fallback
- SSH.NET for programmatic auth (events, callbacks) and in-process tunnels
- Plink kept as fallback for Pageant-only auth (SSH.NET lacks built-in agent support)
- Custom `PageantClient` communicates via Win32 shared memory for SSH.NET key injection (with process owner verification)
- `CredentialProtector` unifies DPAPI+HMAC encryption with backward-compatible legacy blob support
- `AuthPreflightChecker` validates credentials before connection attempt
- `FailureClassifier` maps exceptions to 25 structured `SshFailureCode` values

### Terminal: Pipe Mode (NOT ConPTY)
- ConPTY double-converts VT sequences (VT -> Win32 key events -> VT), breaking arrow keys
- Pipe mode passes VT sequences raw: `xterm.js -> stdin pipe -> plink -t -> remote PTY`
- `-t` flag on plink forces remote PTY allocation (stdin is not a console in pipe mode)
- ConPTY (`ConPtySession`) exists but is reserved for local shell only
- Data transfer is binary-safe via base64 between C# and xterm.js

### RDP: ActiveX MsTscAx + Layout Flush
- `WindowsFormsHost` airspace: MUST flush WPF layout pipeline before `Connect()`
- `UpdateLayout()` + `DoEvents()` + `Dispatcher.Invoke(Render)` before every connect (2 flushes: pre-connect + post-handle)
- **Airspace overlay rule**: Any WPF UI that must render above a `WindowsFormsHost` (RDP, VNC) MUST use a `Popup` — the Popup creates its own HWND that the OS composites above the ActiveX surface. `Panel.ZIndex` has no effect against Win32 HWNDs. The Command Palette uses this pattern.
- Resolution updates blocked for 5s after `OnConnected` (prevents disconnect code 4360)
- COM dispose order: `Visibility=Collapsed` -> `Child=null` -> `Disconnect()` -> `DetachEventSink()` -> `Dispose()` (do NOT call `Marshal.ReleaseComObject` — let AxHost handle RCW cleanup)
- **Auto-reconnect**: `LoginComplete`, `AutoReconnecting`, `AutoReconnected` COM events with bounded retry (`MaxReconnectAttempts = 20`) and `CancelAutoReconnect` flag
- **Disconnect decoder**: `RdpActiveXHost.GetDisconnectReasonKey()` maps 24 MsTscAx codes to i18n keys
- **COM pre-warm**: Background STA thread creates/disposes a throwaway `RdpActiveXHost` at app startup, forcing mstscax.dll + 22 static dependencies into memory (~400ms saved on first connection)
- **DNS pre-resolution**: `Dns.GetHostEntryAsync()` fire-and-forget on server selection in tree view, warms OS DNS cache
- **TCP keep-alive**: `KeepAliveIntervalMs = 60_000` (named constant) for network break detection
- **Performance flags**: Per-server bitmask (wallpaper, themes, animations, drag, cursor shadow, composition) via `AdvancedSettings9.PerformanceFlags`
- **Disable UDP**: Per-server option to force TCP-only via `BandwidthDetection = false` + `NetworkConnectionType = 6` (LAN, no probing)

### WebView2 Focus Management
- WebView2 captures focus aggressively; toolbar must have `Panel.ZIndex=100`
- `ClipToBounds=True` on content Grid prevents WebView2 overflow into other tabs
- Focus set ONCE after xterm.js `ready:` message, never in GotFocus/PreviewMouseDown
- Session tabs live INSIDE Servers Grid (Column 2), never as global overlay

### Credential Autofill
- `EnumWindows` + `EnumThreadWindows` (CredUI dialogs from embedded ActiveX are thread-owned, not top-level)
- UI Automation for modern XAML dialogs, Win32 `SendMessage`/`BM_CLICK` fallback for classic
- `IMsTscNonScriptable` COM interface for embedded password injection + `ClearPassword()` after connect

### SFTP Embedded Panel
- SSH.NET native `SftpClient` (not psftp process) for file browsing and transfer
- Sudo fallback: standard SFTP first; on permission denied, falls back to `sudo cat` / `sudo tee` via SSH exec channel
- Pageant integration: `AGENT_COPYDATA_ID` must be `0x804e50ba`, RSA-SHA2 algorithm registration for modern servers, `Sign()` must return full SSH blob (not raw signature bytes)
- `RemoteFileEditor`: `FileSystemWatcher` + 2-second debounce for auto-upload on save; re-editing a file closes the previous editor session
- XAML gotcha: `CheckBox IsChecked="True"` fires the `Checked` event during `InitializeComponent()` — guard handlers with null checks on uninitialized fields
- Session tab right-click: SFTP tabs skip `MainWindow.OnSessionTabRightClick` context menu (SFTP view has its own dedicated context menu)

### Citrix: StoreBrowse Integration
- Citrix sessions use `storebrowse.exe` from the Citrix Workspace App to enumerate and launch published applications/desktops
- `ConnectionService.Citrix.cs` handles StoreFront authentication and ICA file generation
- `EmbeddedCitrixView` hosts the Citrix session in a similar tab pattern to RDP
- StoreBrowse path is auto-detected from `%ProgramFiles(x86)%\Citrix\ICA Client\SelfServicePlugin\` or configurable in settings

### Multi-Exec Broadcast
- Sends keystrokes simultaneously to multiple active SSH terminal sessions
- Broadcast mode toggled per-session via toolbar button
- Input is relayed from a single source terminal to all opted-in sessions via `PostWebMessageAsString`
- Each terminal can independently opt in/out of broadcast reception

### Recursive N-Pane Split System
- Binary tree model: `ISplitContent` → `SessionPaneModel` (leaf with PaneId/HostControl/ServerId/OriginalServerId) | `SplitContainerModel` (branch with First/Second/Orientation/SplitRatio)
- **Pane identity**: `ServerId` is session-scoped (assigned after connection, used as state machine key — empty during connection). `OriginalServerId` is stable (set at pane creation from inventory ID — used for reconnect, history, layout persistence)
- `SplitContainerModel` constants: `MinRatio` (0.1), `MaxRatio` (0.9), `DefaultRatio` (0.5), `SplitterThickness` (4) — `SplitRatio` auto-clamped in setter BEFORE PropertyChanged fires
- `SessionTabViewModel.RootContent` holds the tree root; up to 8 panes per tab
- `SplitTreeHelper`: `EnumerateLeaves`, `FindPane`, `FindPaneByServerId`, `FindPaneByHostControl`, `FindParent`, `FindSibling`, `RemovePane`, `ReplacePane`, `CountLeaves`, `FirstLeaf`
- Internal mutations use `bool`-returning helpers (`ReplacePaneRecursive`, `ReplaceContainer`) for short-circuit after first match
- `RemovePane` null subtree guard: promotes sibling instead of assigning null to container children
- **`SplitService`** (singleton, DI-registered): extracted from MainViewModel — owns all split/merge orchestration, per-session `CancellationTokenSource` lifecycle, `SplitLayoutMemory`, and `ConnectByProtocolAsync` (unified 8-protocol dispatch). Callbacks for ActiveSessions/StatusText wired by MainViewModel at construction
- WPF rendering via implicit `DataTemplate`s: `SessionPaneControl` (leaf) + `SplitContainerControl` (recursive container with `GridSplitter`)
- Both controls detach all event handlers in `Unloaded` (PropertyChanged, Click, DragCompleted, MouseDoubleClick) — no memory leaks
- Per-pane overlays: loading spinner during connection, disconnect overlay with Reconnect/Close buttons (accessible labels)
- **Min pane size**: `MinWidth="120" MinHeight="80"` on content presenters prevents splitter from collapsing panes
- **Double-click splitter**: resets ratio to 50/50; hover border on panes for active pane feedback
- Swap panes, toggle orientation (Ctrl+Shift+O), detach any pane to floating window, close individual pane
- **Symmetric session/tool splits**: sessions and built-in tools can be freely split and merged in any combination (e.g., SSH left + Network Cartography right)
- **`SplitSessionWithTool`**: docks a tool directly into a split pane via `CreateToolControl()` — synchronous, no loading overlay. Tool panes use `ConnectionType = "TOOL:<ID>"` and GUID-based `ServerId`
- **Context menu**: "Split..." → Horizontal | Vertical (submenu); "Merge with..." → session or tool → Horizontal | Vertical
- **Drag-to-split**: drag tab onto content area, orientation auto-detected from drop position, works on already-split targets
- **Split layout persistence**: `SplitLayoutMemory` (thread-safe, atomic save, versioned JSON schema) records server pairs in `config/split-layouts.json`, boosts in Command Palette; ratio restored on merge. Backward-compatible with legacy bare-array format
- **Palette split mode**: shows ALL servers from inventory (not limited to recent); active sessions and tool tabs at top for merge; selecting a tool from search docks it as a pane
- **Per-pane cleanup**: `ClosePane` and `CloseAllPanes` (centralized tab teardown) are type-aware — connection panes get disconnect history + tunnel release + state machine reset; tool panes check `IToolView.CanClose()` and skip state machine/tunnel teardown. Any busy tool pane blocks the tab close. `ConnectionViewModel.CloseSessionInternal` delegates entirely to `SplitService.CloseAllPanes`. **Fixed disposal order**: detach HostControl (null) → remove from tree → dispose (prevents RDP/ActiveX airspace issues)
- **Per-session cancellation**: `RegisterSession`/`CancelSession` on `SplitService` manages `CancellationTokenSource` per tab with deferred dispose (5s delay). `ConnectByProtocolAsync` passes `CancellationToken` to all `ConnectionService.Connect*Async` handlers. Async split/reconnect check token between config load and connection
- **Deferred reconnect cleanup**: `ReconnectPaneAsync` releases old tunnel/state machine via `ReleaseOldConnectionState` only AFTER new connection succeeds or fails (prevents state loss on reconnect failure). No longer creates self-referential LayoutMemory entries
- **MergeExistingSession** checks `CanClose()` on source tool panes before reparenting (busy tool blocks merge with user-visible status feedback). HostControl presence checked on all source leaves (not just primary shim)
- Backward-compat shim properties on `SessionTabViewModel` (`ServerId`, `Title`, `IsSplit`, `Secondary*`) delegate to tree leaves. `NotifyTreeDependentProperties()` shared method (DRY). `_emptyPane` is per-instance (not static) to prevent cross-session leakage
- Post-await guards: `!ActiveSessions.Contains(session)` + `FindPane` null check prevent orphaned connections
- `MergeExistingSession` uses `OriginalServerId` fallback for stable session lookup (tool tabs use `ServerId` directly)
- State machine entries preserved during merge (connections alive, just reparented); cleanup on tab close via `EnumerateLeaves`

### Quick Connect (Ctrl+K)
- Command palette implemented as a WPF `Popup` (own HWND) to render above `WindowsFormsHost` ActiveX surfaces (RDP, VNC airspace fix)
- **Two-line item layout**: Line 1: protocol icon + DisplayName + ConnectionTypeBadge. Line 2: Endpoint (host:port) + Username + ProjectName + Group with `·` separators
- **Responsive sizing**: `MinWidth="550" MaxWidth="700"`, `MaxHeight="450"` for results list
- **Active session indicator**: `PaletteActiveIndicatorConverter` (IMultiValueConverter) shows protocol-colored 3px left rail for non-Disconnected sessions
- **Endpoint caching**: `ServerItemViewModel.Endpoint` is a cached `[ObservableProperty]`, computed once via `FormatEndpoint()` from DTO (protocol-aware: SshPort for SSH/SFTP, FtpPort for FTP, VncPort for VNC, TelnetPort for Telnet, empty for Local/Citrix)
- **Protocol badge**: `ConnectionTypeBadge` returns short labels (RDP, SSH, TEL, CTX, SH, TOOL) with per-protocol color via `ConnectionTypeToBrushConverter`
- Supports RDP, SSH, SFTP, VNC, Telnet, FTP, Citrix, and Local Shell connection strings
- Parses `user@host:port` format with optional protocol prefix
- Bare IP/hostname input auto-proposes SSH and RDP connections (sets `Endpoint` alongside `RemoteServer`)
- Also used for split session server and tool selection (replaces ContextMenu for scalability)
- Split mode shows active sessions and tool tabs at the top for merge, then servers and tools for new panes
- Split mode forces Embedded connection mode (external processes cannot be docked in panes)
- **Split via context menu**: Right-click tab → "Split..." → Horizontal | Vertical → Command Palette with all servers and tools
- **Merge via context menu**: Right-click tab → "Merge with..." → session or tool → Horizontal | Vertical (uses `OriginalServerId` for stable lookup; tool tabs use `ServerId`)
- `PaletteInput.Focus()` deferred via `Dispatcher.BeginInvoke(Input)` — Popup content enters the visual tree asynchronously
- Win32 focus forced via P/Invoke (`SetForegroundWindow` + `SetActiveWindow` + `SetFocus`) on Popup open
- Recent connections persisted for quick re-use

### Tunnel Panel (Retractable)
- Dedicated side panel showing all active SSH tunnels with real-time status
- Retractable via toggle button to save screen space
- Displays local port, remote target, gateway chain, and traffic indicators
- Allows manual tunnel teardown without disconnecting the parent session

### Theme Switching (Runtime Dark/Light)
- Runtime theme switching without application restart
- `WindowThemeHelper` uses DWM API (`DwmSetWindowAttribute`) for title bar dark mode
- Theme resources merged via `ResourceDictionary` swap at runtime
- CommonControls.xaml provides 1,760+ lines of shared control styles, Design Tokens, and micro-animations

### AvalonEdit Embedded Editor
- AvalonEdit control embedded for viewing and editing remote files via SFTP
- Syntax highlighting for common file types
- Integrates with `RemoteFileEditor` for auto-upload on save

### VNC: noVNC + WebSocket Proxy
- VNC sessions rendered via noVNC (HTML5 VNC client) inside WebView2
- `WebSocketVncProxy` bridges between the noVNC WebSocket and the raw VNC TCP connection
- Proxy runs locally on an ephemeral port, translating WebSocket frames to RFB protocol
- Supports VNC authentication (password-based) and view-only mode
- `EmbeddedVncView` hosts the WebView2 control with noVNC loaded from embedded resources

### Telnet: Raw TCP + IAC Negotiation
- `TelnetSession` implements raw TCP socket communication with Telnet IAC (Interpret As Command) negotiation
- Handles WILL/WONT/DO/DONT option negotiation for terminal type, window size, and echo
- Rendered in xterm.js via the same WebView2 terminal infrastructure as SSH
- Supports configurable line endings (CR, LF, CRLF) and character encoding

### FTP: IRemoteBrowser Interface
- `FtpBrowser` implements the `IRemoteBrowser` interface, shared with `SftpBrowser`
- Common UI code in `EmbeddedSftpView` works with both SFTP and FTP browsers via the interface
- Supports FTP and FTPS (explicit TLS) connections
- File transfer progress reporting unified across both protocols

### Tab Detach: FloatingSessionWindow
- Any session tab or individual split pane can be detached into a standalone `FloatingSessionWindow`
- `DetachPaneToFloatingWindow(paneId)` extracts a pane from the split tree, promotes it to an independent tab, then detaches
- Detached windows can be re-docked back into the main window via reattach button
- Session state (connection, terminal buffer) is preserved during detach/re-dock
- Multiple floating windows supported simultaneously

### Tunnel Reference Counting
- `TunnelManager` tracks reference counts for shared tunnels across multiple sessions
- When multiple connections use the same gateway chain, a single tunnel is shared
- Tunnel is torn down only when the last referencing session disconnects
- Prevents orphaned tunnels and duplicate tunnel creation

### Connection Inheritance via GroupDefaultsDto
- `GroupDefaultsDto` defines default connection settings at the project/group level
- Server profiles inherit from their parent group defaults unless explicitly overridden
- Inheritable fields include credentials, gateway, port, color tag, and protocol-specific options
- Reduces repetitive configuration for servers sharing common attributes

### External Credential Provider Pattern
- `ICredentialProvider` interface allows plugging in external credential sources
- `CommandCredentialProvider` executes an external command (e.g., password manager CLI) to retrieve credentials at connection time
- Credentials are fetched on demand and never persisted to disk
- Supports timeout and cancellation for long-running credential lookups

### X11 Server Auto-Detection
- `X11ServerManager` detects running X11 servers (VcXsrv, Xming, Cygwin/X) on the local machine
- Automatically sets the `DISPLAY` environment variable for SSH sessions when an X server is found
- Falls back to configurable display settings if no server is auto-detected
- Enables X11 forwarding in SSH connections when a local X server is available

### Network Knowledge Base
- Persistent accumulated host data across scans (`config/network-kb.json`)
- `Observation<T>` wrapper with per-field timestamps and source tracking (gateway name or "local")
- Merge strategy: newest-non-null-wins per field, OS fingerprint uses highest confidence, services merged per-port
- TTL-based cache skip in `CartographyEngine.ScanAsync()`: ping (4h), ports (24h), banners (7d), UDP probes (7d), certs (30d)
- `CacheHitProgress` event for UI feedback during cache-accelerated scans
- Atomic write via temp-then-rename (no SecureFileWriter double-write)
- KB backfill: null OS/hostname/MAC fields populated from prior scan observations when re-scanning
- ARP table refresh post-scan captures MAC addresses populated by ping+TCP connections
- Manufacturer re-resolution for previously unresolved OUI prefixes
- Checkbox in Network Cartography UI to enable/disable cache usage; KB always enriched after scan

### Security Hardening
- **WebSocket Origin validation**: `WebSocketVncProxy` rejects non-local Origin headers (CSWSH prevention)
- **Atomic ACL file creation**: `SecureFileWriter.WriteAndProtect()` creates files with restrictive ACL from the start (no TOCTOU window)
- **Path traversal prevention**: `LocalFileBrowserView` validates rename/new folder inputs against `Path.GetInvalidFileNameChars()` and `..` sequences
- **TreatWarningsAsErrors**: Enabled globally via `Directory.Build.props`
- **Accessibility**: `AutomationProperties.Name` set on all interactive controls for screen reader support (385+ via code-behind `ApplyLocalization()`)

### Design System (CommonControls.xaml + DialogCommonStyles.xaml)
- **Typography tokens**: `FontSizeSmallCaption(11)`, `FontSizeCaption(12)`, `FontSizeBody(13)`, `FontSizeSubtitle(15)`, `FontSizeTitle(20)`, `FontSizeHeadline(24)` — minimum 11px per UX audit
- **Spacing tokens**: `SpacingXs(4)`, `SpacingSm(8)`, `SpacingMd(12)`, `SpacingLg(20)`, `SpacingXl(24)` — uniform `Thickness` resources for `Margin`/`Padding`
- **Micro-animations**: `FadeInPanelStyle` (150ms opacity fade-in on `Visibility=Visible`), `AnimationFast(150ms)`, `AnimationMedium(250ms)` — applied to Tools panel expand
- **Tool category brushes**: `ToolNetworkBrush` (blue), `ToolSecurityBrush` (amber), `ToolEncodingBrush` (purple), `ToolSystemBrush` (teal) — per-tool vector geometries + per-category colors in tree view
- **Badge/protocol brush parity**: `RdpBadgeBrush` = `ProtocolRdpBrush`, etc. (8 protocols, consistent across tree view and tabs)
- **EmptyStateStyle**: Reusable style for empty/onboarding states (centered, max-width 400px)
- **DialogCommonStyles.xaml**: 8 shared styles (label, section title, hint, card, form inputs) for dialog consistency
- **DataGrid**: global `ClipboardCopyMode="IncludeHeader"` enables native Ctrl+C copy
- **Asymmetric margins stay hardcoded**: XAML `Thickness` resources are uniform (e.g., `8,8,8,8`), so `Margin="0,0,8,0"` cannot use tokens — this is standard WPF practice

### Declarative i18n (`{loc:Translate}`)
- `TranslateExtension` (`src/Heimdall.App/Localization/`): WPF `MarkupExtension` for `{loc:Translate Key}` syntax — creates live `Binding` to `LocalizationSource.Instance[Key]`, auto-updates on runtime locale change
- `LocalizationSource`: Singleton bridge (`INotifyPropertyChanged`) between WPF bindings and `LocalizationManager` DI service — fires `PropertyChanged("Item[]")` on `LocaleChanged`
- Coexists with legacy `ApplyLocalization()` pattern — new views use `{loc:Translate}`, legacy views migrate incrementally
- PinDialog fully migrated as POC; all other views continue using `ApplyLocalization()`

### Two-Tier Icon System
- **Tier 1**: Vector geometries in `IconGeometries.xaml` — `Geo.Protocol.*` (8), `Geo.Status.*` (5), `Geo.Tool.*` (33), `Geo.Tree.*` (7). Consumed via `Path` + converters (`ConnectionTypeToGeometryConverter`, `ConnectionTypeToColorConverter`)
- **Tier 2**: Segoe MDL2 Assets inline in XAML for standard UI chrome (toolbar, navigation, menus)
- `ToolRegistry` stores `Geo.Tool.*` keys with `FrozenDictionary` lookups; converters resolve `TOOL:*` types via `GetGeometryKey()`/`GetCategoryBrushKey()`

### Protocol-Driven Add Server Flow
- **Step 1 — Protocol selection**: 8 large card buttons (Geo.Protocol.* icons + ProtocolXxxBrush colors) in UniformGrid. `SelectProtocolCommand` sets `ConnectionType` + `IsProtocolSelected`
- **Step 2 — Form fields**: Only fields relevant to the selected protocol are shown. `ShowFormFields` computed from `IsEditMode || IsProtocolSelected`
- **Edit mode**: `FromDto()` sets `IsProtocolSelected = true` + `IsEditMode = true`. Protocol badge shown read-only. ConnectionType dropdown removed
- **Back button**: `BackToProtocolSelectorCommand` resets to Step 1, calls `ClearValidationState()`
- **Advanced mode**: Animated ScaleY+Opacity transition (300ms/250ms) reveals TabControl. Mode persisted via `ConfigManager.MergeSettingAsync()`, NOT persisted when forced by validation focus

### Inline Validation (ServerDialog)
- `[Required]`/`[Range]`/`[NotifyDataErrorInfo]` on DisplayName, RemoteServer, all port properties
- `Validate()` → `ValidateAllProperties()` → per-protocol `ClearErrors()` for irrelevant fields → per-field error extraction via `GetLocalizedFieldError()`/`GetEndpointPortError()`
- Live re-validation: `OnXxxChanged` partial handlers call `ValidateProperty()` + update mirrored error + `RefreshValidationSummary()`
- Tab error badges: `HasTunnelingTabErrors`/`HasOptionsTabErrors` computed from error counts
- `FocusFirstInvalidField()`: maps `FirstInvalidField` to UI control, opens advanced mode + selects tab if needed, deferred focus via `Dispatcher.BeginInvoke(Input)`. Temporarily unhooks persistence handler to avoid saving forced advanced mode
- `FieldValidationErrorStyle` in DialogCommonStyles.xaml (ErrorTextBrush, FontSizeCaption, reusable)

### Keyboard Shortcuts
- `F1` shows keyboard shortcut cheat sheet
- `Ctrl+K` opens Command Palette (servers + ad-hoc connections + tools)
- `Ctrl+N` add server, `Ctrl+E` edit, `Ctrl+Del` delete
- `Ctrl+F` search, `Ctrl+B` toggle sidebar, `Ctrl+Shift+T` toggle tools panel, `F11` fullscreen
- `Ctrl+Shift+S` screenshot, `Ctrl+Shift+N` network scanner, `Ctrl+Shift+O` toggle split orientation

### Connection State Machine

States: `Disconnected`, `Initializing`, `ValidatingConfig`, `EstablishingTunnel`, `TunnelEstablished`, `LaunchingRdp`, `LaunchingSsh`, `LaunchingSftp`, `LaunchingVnc`, `LaunchingTelnet`, `LaunchingFtp`, `LaunchingCitrix`, `LaunchingLocal`, `Connected`, `Disconnecting`, `Error`

Application status states: `Initializing`, `Ready`, `Busy`, `Error`, `Shutdown`

Transitions are validated via `ConnectionStateMachine` before applying state changes.

### Version Number
- Win32 version fields limited to 65535; `<Version>1.0.MMDD.xx</Version>` for assembly
- `<InformationalVersion>YYYY.MMDDxx</InformationalVersion>` for display purposes

## Migration from Legacy Heimdall

- Config format compatible (same JSON schema for settings.json / servers.json)
- `MigrationService` reads DPAPI-encrypted fields from legacy config
- Import supports JSON, MobaXterm (.mxtsessions / .ini), mRemoteNG (.xml), RDCMan (.rdg), and .rdp files
- Export supports JSON

## Dependency Graph

```
Heimdall.App
  ├── Heimdall.Core
  ├── Heimdall.Ssh      -> Heimdall.Core
  ├── Heimdall.Rdp      -> Heimdall.Core
  ├── Heimdall.Sftp     -> Heimdall.Core, Heimdall.Ssh
  └── Heimdall.Terminal  -> Heimdall.Core
```

External: CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, SSH.NET, WebView2, noVNC, System.Security.Cryptography.ProtectedData.
