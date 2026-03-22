# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

**Heimdall.Next** is a ground-up rewrite of Heimdall (PowerShell 5.1 + WPF) as a modern .NET 10 + WPF application. It is a secure Windows connection manager supporting 8 protocols (RDP, SSH, SFTP, VNC, Telnet, FTP, Citrix, Local Shell), designed as a MobaXterm and mRemoteNG alternative with superior security and modern UX.

**Current build**: v2026.032203 (Release)

## Repository Layout

- **Working directory**: `G:\_dev\SnapConnect\Heimdall.Next`
- **Solution file**: `Heimdall.slnx`
- **Legacy project**: `G:\_dev\SnapConnect\RDPManager` (PowerShell version, maintained in parallel)

## Codebase Size

- **~198 C# source files** (~55,000 LOC)
- **~58 XAML files** (~13,000 LOC)
- **40 test files** (~14,000 LOC), 1,305 xUnit tests
- **~3,061 i18n keys** per locale (EN/FR)
- **33 built-in sysops tools** (ToolRegistry with IToolView interface, cross-tool navigation, Network Cartography with deep fingerprinting)
- **1,760+ lines** of theme XAML (CommonControls + Dark/Light, Design Tokens, micro-animations)

## Architecture (6 Projects)

```
Heimdall.slnx
├── src/
│   ├── Heimdall.Core/          # Shared foundation (net10.0)
│   │   ├── Configuration/      # AppSettings, ConfigManager, SchemaValidator, ServerProfileDto, GroupDefaultsDto, ExternalToolDefinition
│   │   ├── Localization/       # LocalizationManager (JSON-based i18n)
│   │   ├── Logging/            # FileLogger (daily rotation), ConnectionHistory
│   │   ├── Models/             # RdpServer, SshGateway, Project, TunnelSession, ISessionResult, ServerProfileDto, TerminalMacro, DefaultPorts, Enums
│   │   ├── Security/           # DpapiProvider, HmacIntegrity, CredentialProtector, PinManager, InputValidator, AclEnforcer,
│   │   │                       # NetworkScanner, WakeOnLan, CommandCredentialProvider, ICredentialProvider, SecureFileWriter
│   │   ├── Discovery/          # CartographyEngine, UdpProbeEngine (NetBIOS/SNMP/mDNS), OsFingerprinter,
│   │   │                       # RoleClassifier, OuiDatabase (300+ MAC prefixes), VlanDetector,
│   │   │                       # DrawIoExporter, ScanHistoryManager, CartographyModels
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
│       │   └── Dialogs/        # ServerDialogVM, GatewayDialogVM, ProjectDialogVM, PinDialogVM
│       ├── Views/              # MainWindow, EmbeddedRdpView, EmbeddedSshView, EmbeddedSftpView, EmbeddedCitrixView,
│       │                       # EmbeddedVncView, FloatingSessionWindow, LocalFileBrowserView
│       │   └── Dialogs/        # ServerDialog, GatewayDialog, ProjectDialog, PinDialog, InputDialog
│       ├── Services/           # ConnectionService (9 partial files: .Rdp, .Ssh, .Sftp, .Citrix, .Local, .Tunnel, .Vnc, .Telnet, .Ftp),
│       │                       # EmbeddedSessionManager, MigrationService, DialogService,
│       │                       # NavigationService, SleepPrevention, TaskSchedulerService, MacroService,
│       │                       # EphemeralFileServer, X11ServerManager, WebSocketVncProxy
│       ├── Converters/         # BoolToVisibility, InvertBool, ConnectionState/Type brushes, FileSizeConverter, etc.
│       ├── Themes/             # CommonControls.xaml (1,760+ lines, Design Tokens, micro-animations), DarkTheme, LightTheme
│       └── Theming/            # WindowThemeHelper (DWM dark mode API)
│
├── tests/
│   ├── Heimdall.Core.Tests/    # StateMachineTests
│   └── Heimdall.Ssh.Tests/     # FailureClassifier, AuthPreflight, HostKeyStore, Pageant, PlinkTunnel
│
├── config/                     # Factory defaults (settings.default.json, servers.default.json)
├── locales/                    # en.json, fr.json (~3,061 keys each)
├── docs/                       # TROUBLESHOOTING.md, ARCHITECTURE.md
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
| **LocalizationManager** | JSON-based i18n with ~3,061 keys per locale |
| **FileLogger** | Daily rotation file logging |
| **ConnectionHistory** | Persisted log of connection events for audit and quick re-connect |
| **CredentialProtector** | DPAPI+HMAC encryption with legacy blob support |
| **ICredentialProvider** | Pluggable credential source interface |
| **CommandCredentialProvider** | External command credential retrieval (password manager CLI) |
| **NetworkScanner** | Legacy lightweight subnet scanner (ICMP + TCP port probes) |
| **CartographyEngine** | Full network cartography: ping sweep (TTL capture), port scan, banner grab, HTTP header extraction, TLS cert inspection, UDP probes (NetBIOS/SNMP/mDNS), OS fingerprinting, enriched role classification, VLAN detection |
| **UdpProbeEngine** | Raw UDP packet construction for NetBIOS NBSTAT (name/domain/MAC), SNMPv2c GET (sysDescr/sysName/sysLocation), mDNS service discovery (26 service types) |
| **OsFingerprinter** | OS detection via ICMP TTL analysis + SSH/HTTP banner pattern matching (33 patterns), multi-source merge |
| **RoleClassifier** | Heuristic role classification: 46+ port patterns, 96+ banner fingerprints, multi-source `ClassifyEnriched()` consuming ports+banners+OS+NetBIOS+SNMP+mDNS+HTTP headers |
| **OuiDatabase** | Embedded MAC OUI lookup (300+ manufacturer prefixes: enterprise, IoT, ISP, industrial, mobile, media) |
| **VlanDetector** | Passive VLAN inference from /24 subnets + Cisco `show vlan brief` parser |
| **ScanHistoryManager** | Scan snapshot persistence (JSON), historical comparison with typed `HostChange` diff |
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
| **EmbeddedSessionManager** | Session tab lifecycle, detach/re-dock coordination |
| **ToolRegistry** | Centralized registry for 33 built-in tools (ToolDescriptor, IToolView factory with CanClose(), categories, icons, palette aliases) |
| **DefaultPorts** | Named constants for well-known protocol ports (RDP, SSH, VNC, FTP, Telnet, HTTP, TFTP) |
| **FileSize** | Shared byte-to-human-readable size formatting utility |
| **SecureFileWriter** | Atomic file creation with restrictive ACL (TOCTOU-safe) |
| **InvertBoolConverter** | WPF value converter for boolean inversion |

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
3. **Validate JSON locales**: Checks i18n key parity between en.json and fr.json (~3,061 keys)
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
- `UpdateLayout()` + `DoEvents()` + `Dispatcher.Invoke(Render)` before every connect
- Resolution updates blocked for 5s after `OnConnected` (prevents disconnect code 4360)
- COM dispose order: `Visibility=Collapsed` -> `Child=null` -> `Disconnect()` -> `DetachEventSink()` -> `Dispose()`

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

### Quick Connect (Ctrl+K)
- Command palette overlay for ad-hoc connections without saving a server profile
- Supports RDP, SSH, SFTP, VNC, Telnet, FTP, Citrix, and Local Shell connection strings
- Parses `user@host:port` format with optional protocol prefix
- Bare IP/hostname input auto-proposes SSH and RDP connections
- Also used for split session server selection (replaces ContextMenu for scalability)
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
- Any session tab can be detached into a standalone `FloatingSessionWindow`
- Detached windows can be re-docked back into the main window via drag or context menu
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

### Security Hardening
- **WebSocket Origin validation**: `WebSocketVncProxy` rejects non-local Origin headers (CSWSH prevention)
- **Atomic ACL file creation**: `SecureFileWriter.WriteAndProtect()` creates files with restrictive ACL from the start (no TOCTOU window)
- **Path traversal prevention**: `LocalFileBrowserView` validates rename/new folder inputs against `Path.GetInvalidFileNameChars()` and `..` sequences
- **TreatWarningsAsErrors**: Enabled globally via `Directory.Build.props`
- **Accessibility**: `AutomationProperties.Name` set on all interactive controls for screen reader support (385+ via code-behind `ApplyLocalization()`)

### Design System (CommonControls.xaml)
- **Typography tokens**: `FontSizeCaption(11)`, `FontSizeBody(12)`, `FontSizeSubtitle(14)`, `FontSizeTitle(18)`, `FontSizeHeadline(24)` — use `{StaticResource FontSizeBody}` instead of hardcoded `FontSize="12"`
- **Spacing tokens**: `SpacingXs(4)`, `SpacingSm(8)`, `SpacingMd(12)`, `SpacingLg(16)`, `SpacingXl(24)` — uniform `Thickness` resources for `Margin`/`Padding`
- **Micro-animations**: `FadeInPanelStyle` (150ms opacity fade-in on `Visibility=Visible`), `AnimationFast(150ms)`, `AnimationMedium(250ms)`
- **Tool category brushes**: `ToolNetworkBrush` (blue), `ToolSecurityBrush` (amber), `ToolEncodingBrush` (purple), `ToolSystemBrush` (teal) — per-tool glyphs + per-category colors in tree view
- **DataGrid**: global `ClipboardCopyMode="IncludeHeader"` enables native Ctrl+C copy
- **Asymmetric margins stay hardcoded**: XAML `Thickness` resources are uniform (e.g., `8,8,8,8`), so `Margin="0,0,8,0"` cannot use tokens — this is standard WPF practice

### Keyboard Shortcuts
- `F1` shows keyboard shortcut cheat sheet
- `Ctrl+K` opens Command Palette (servers + ad-hoc connections)
- `Ctrl+N` add server, `Ctrl+E` edit, `Ctrl+Del` delete
- `Ctrl+F` search, `Ctrl+B` toggle sidebar, `F11` fullscreen
- `Ctrl+Shift+S` screenshot, `Ctrl+Shift+N` network scanner

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
