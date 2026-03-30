# CLAUDE.md

## Project Overview

**Heimdall.Next** — .NET 10 + WPF secure Windows connection manager (RDP, SSH, SFTP, VNC, Telnet, FTP, Citrix, Local Shell). MobaXterm/mRemoteNG alternative. Ground-up rewrite of Heimdall (PowerShell 5.1 + WPF).

**Current build**: v2026.033005 (Release)

## Repository Layout

- **Solution**: `Heimdall.slnx` — 6 projects
- **Legacy**: `G:\_dev\SnapConnect\RDPManager` (maintained in parallel)

```
src/
├── Heimdall.Core/       # Models, Config, Security, Discovery, Localization, StateMachine, Logging
├── Heimdall.Ssh/        # SSH.NET + Pageant + Plink fallback, TunnelManager, HostKeyStore
├── Heimdall.Rdp/        # ActiveX MsTscAx + Citrix StoreBrowse, CredentialAutofill
├── Heimdall.Sftp/       # SFTP + FTP browsers (IRemoteBrowser), RemoteFileEditor
├── Heimdall.Terminal/   # ConPty, PipeModeSession, TelnetSession, SmartPasteGuard
└── Heimdall.App/        # WPF app: ViewModels, Views, Services, Themes, Converters, Localization
    ├── Services/        # ConnectionService (9 partial files: .Rdp/.Ssh/.Sftp/.Citrix/.Local/.Tunnel/.Vnc/.Telnet/.Ftp),
    │                    # SplitService, EmbeddedSessionManager, MigrationService, DialogService,
    │                    # MacroService, EphemeralFileServer, X11ServerManager, WebSocketVncProxy,
    │                    # ToolRegistry (49 tools), SecNumCloudAuditEngine, HtmlReportGenerator, CsvEvidenceExporter
    ├── Themes/          # CommonControls.xaml (Design Tokens, micro-animations), Dark/LightTheme,
    │                    # DialogCommonStyles.xaml, IconGeometries.xaml (Geo.* vector icons)
    └── Localization/    # TranslateExtension ({loc:Translate}), LocalizationSource (singleton bridge)
tests/
├── Heimdall.Core.Tests/ # StateMachine tests
└── Heimdall.Ssh.Tests/  # FailureClassifier, AuthPreflight, HostKeyStore, Pageant, Plink
config/                  # Factory defaults (settings.default.json, servers.default.json,
                         #   hacker-simulator.scenarios.default.json, hacker-simulator.playlists.default.json)
locales/                 # en.json, fr.json (~4,685 keys each)
```

### Dependency Graph

```
Heimdall.App → Core, Ssh, Rdp, Sftp, Terminal
Heimdall.Ssh → Core    |  Heimdall.Rdp → Core
Heimdall.Sftp → Core, Ssh  |  Heimdall.Terminal → Core
```

External: CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, SSH.NET, WebView2, noVNC, ProtectedData.

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (C# 14), WPF (MVVM via CommunityToolkit.Mvvm), DI |
| SSH/SFTP | SSH.NET 2025.1.0 |
| Terminal | WebView2 + xterm.js (pipe mode); VNC via noVNC (WebSocket proxy) |
| RDP | ActiveX MsTscAx (WindowsFormsHost) |
| Crypto | DPAPI + HMAC-SHA256 |
| Testing | xUnit + Moq + FluentAssertions |

## Build & Test

```bash
dotnet build && dotnet test && dotnet run --project src/Heimdall.App
powershell -File Build.ps1                # Debug, auto-increments YYYY.MMDDxx
powershell -File Build.ps1 -Mode Release  # Release + zip archive
powershell -File Build.ps1 -SkipTests
```

- **"build"** = `Build.ps1` (Debug), **"release"** = `Build.ps1 -Mode Release`
- Version: `<Version>1.0.MMDD.xx</Version>` (Win32 limit 65535), `<InformationalVersion>YYYY.MMDDxx</InformationalVersion>`
- Output: `Dist/debug/` or `Dist/release/` (gitignored)
- CI: GitHub Actions — build, test, i18n key parity, lint

## Code Standards

- **License**: Apache 2.0, author "Julien Bombled" on new files
- **Language**: English only (code, comments, docs)
- **No hardcoding**: Strings → locales; URLs/paths/magic numbers → config
- **Async by default**: No blocking calls on UI thread
- **MVVM**: Logic in ViewModels, no code-behind except minimal event wiring
- **Nullable reference types**: Enabled project-wide
- **TreatWarningsAsErrors**: Enabled via `Directory.Build.props`
- **i18n key convention**: `<Context><Element>` CamelCase (e.g., `ErrorPlinkNotFound`, `BtnConnect`)

## i18n Patterns

- New XAML: `{loc:Translate Key}` markup extension (live-updates on locale change via `LocalizationSource` singleton)
- Legacy `ApplyLocalization()` coexists; new views use `{loc:Translate}`, migration is incremental
- ~4,685 keys per locale (EN/FR), CI enforces key parity

## Built-in Tools (52 tools)

### Network (16 tools)
Ping, DNS Lookup, Certificate Inspector (multi-port scan), Port Scanner, Subnet Calculator, IP Converter, HTTP Status Codes, Whois, HTTP Header Analyzer, Banner Grabber, TCP Traceroute, SNMP Walker, ARP Monitor, Firewall Rule Tester, Network Cartography, Network Calculator

### Security (15 tools)
Hash Generator, HMAC Generator, Password Generator, SSH Key Generator, Certificate Generator, JWT Parser, TOTP Generator, Password Policy Checker, SSH Key Auditor, SSL/TLS Auditor, DNS Security Checker (SPF/DKIM/DMARC), SMB Enumerator, Default Credential Scanner, CVE Lookup, **SecNumCloud Audit** (orchestration)

### Encoding (6 tools)
Base64, URL Encoder, JSON Formatter, Regex Tester, Text Diff, Text Case Converter

### System (12 tools)
Chmod Calculator, DateTime Converter, UUID Generator, Crontab Builder, Log Viewer, Hosts File Editor, SSH Config Generator, Cron Job Manager, Service Status Dashboard, Notes (Markdown), Diagram Editor, Hacker Simulator

### External (3 native tools + auto-detected third-party)
Wake-on-LAN (UDP magic packet), Open Ports (P/Invoke GetExtendedTcpTable), Network Interfaces (.NET NetworkInterface API)

### Tool Infrastructure
- `ToolRegistry.cs`: single source of truth — one `Entry()` per tool (ID, category, i18n keys, aliases, factory, icon)
- `IToolView` interface: `Initialize(ToolContext?, LocalizationManager?)`, `CanClose()`, `Dispose()`
- `ToolDescriptor.DescriptionKey`: optional explicit i18n key for tool description (default convention: `ToolDesc{Id}`)
- Gateway routing: most network tools support SSH tunnel via `ToolGatewayConnector.Connect()` + `CmbRouteVia` ComboBox
- Icons: `Geo.Tool.*` geometries in `IconGeometries.xaml`
- **Sidebar panel**: mini-cards (badge + icon + name + desc), MaxHeight=350, header with close button
- **Tools tab**: dedicated full-page browser with favorites (persisted), recents, search, 280px cards with pin/unpin
- **Onboarding**: 3-step first-launch overlay, persisted via `AppSettings.OnboardingCompleted`
- **Launch flow**: `OpenToolTabAsync` cleans up orphaned tabs on `CreateToolControl` failure
- **Design token gotcha**: `SpacingRowGap` is `sys:Double`; for `RowDefinition.Height` use `SpacingRowGapGrid` (`GridLength`)

### External Tool Provider (NirSoft / Sysinternals)
- **Architecture**: `IExternalToolProvider` → `SysinternalsToolProvider` (16 tools) + `NirSoftToolProvider` (16 tools)
- **Service**: `ExternalToolProviderService` (singleton) scans at startup, detects installed tools by checking PATH + standard directories
- **Registration**: `ToolRegistry.RegisterExternalTools()` adds detected tools dynamically as `ToolCategory.External` entries
- **Wrapper**: `ExternalToolWrapperView` — generic view that launches any CLI tool, captures stdout, displays as DataGrid (CSV) or TextBox (text)
- **Elevation**: tools with `RequiresElevation` use `cmd /c "tool args > tempfile"` with `Verb=runas` (stdout redirect workaround)
- **Context menu**: detected tools appear in server right-click → "Detected Tools" submenu, grouped by provider
- **Settings**: `AppSettings.SysinternalsPath` / `NirSoftPath` — user-configurable directories + Rescan button
- **Licensing**: NirSoft and Sysinternals tools **cannot** be redistributed — detect & wrap only, user must install them
- **Naming**: external tool IDs use `EXT:PROVIDER:TOOLID` format (e.g. `EXT:SYSINTERNALS:PSEXEC`)

### SecNumCloud Audit Engine
- `SecNumCloudAuditEngine` (in `Heimdall.App/Services/`): orchestrates 15 checks across 4 SecNumCloud v3.2 chapters
- Constructor accepts `Func<string, string> localize` parameter for runtime i18n of all audit messages
- `HtmlReportGenerator.Generate()` accepts `localize` parameter for report content localization
- Calls Core APIs directly: `CartographyEngine`, `NtlmProbe`, `UdpProbeEngine`, `SshFingerprinter`, `HttpFingerprinter`
- Progress via events: `PhaseProgress`, `StatusChanged`, `CheckCompleted`
- Exports: HTML standalone report (`HtmlReportGenerator`), CSV evidence (`CsvEvidenceExporter`), Draw.io diagram (`DrawIoExporter`)

## Key Design Gotchas

### RDP ActiveX (Critical)
- **Layout flush before Connect()**: `UpdateLayout()` + `DoEvents()` + `Dispatcher.Invoke(Render)` — 2 flushes required (pre-connect + post-handle)
- **Airspace rule**: WPF UI above `WindowsFormsHost` MUST use `Popup` (own HWND). `Panel.ZIndex` has no effect against Win32 HWNDs. Command Palette uses this pattern
- **Resolution updates blocked 5s** after `OnConnected` (prevents disconnect code 4360)
- **COM dispose order**: `Visibility=Collapsed` → `Child=null` → `Disconnect()` → `DetachEventSink()` → `Dispose()` (never `Marshal.ReleaseComObject`)
- **COM pre-warm**: Background STA thread creates/disposes throwaway `RdpActiveXHost` at startup (~400ms saved)
- **Auto-reconnect**: Bounded retry (`MaxReconnectAttempts = 20`) with `CancelAutoReconnect` flag
- **Disconnect decoder**: `RdpActiveXHost.GetDisconnectReasonKey()` maps 24 MsTscAx codes to i18n keys

### Terminal: Pipe Mode (NOT ConPTY)
- ConPTY double-converts VT sequences, breaking arrow keys
- Pipe mode: `xterm.js → stdin pipe → plink -t → remote PTY` (raw VT passthrough)
- ConPTY reserved for local shell only; data transfer binary-safe via base64

### WebView2 Focus
- Captures focus aggressively; toolbar needs `Panel.ZIndex=100`
- `ClipToBounds=True` on content Grid prevents overflow into other tabs
- Focus set ONCE after xterm.js `ready:` message, never in GotFocus/PreviewMouseDown
- Session tabs live INSIDE Servers Grid (Column 2), never as global overlay

### SSH & Pageant
- SSH.NET for programmatic auth + in-process tunnels; Plink fallback for Pageant-only
- `PageantClient`: Win32 shared memory, `AGENT_COPYDATA_ID = 0x804e50ba`, RSA-SHA2 registration, `Sign()` must return full SSH blob (not raw signature bytes)
- `FailureClassifier`: 25 structured `SshFailureCode` values
- `TunnelManager`: Reference-counted (shared tunnels, teardown only on last disconnect)

### SFTP
- SSH.NET native `SftpClient`, not psftp process
- Sudo fallback: `sudo cat`/`sudo tee` via SSH exec on permission denied
- `RemoteFileEditor`: `FileSystemWatcher` + 2s debounce auto-upload; re-edit closes previous session
- XAML gotcha: `CheckBox IsChecked="True"` fires `Checked` during `InitializeComponent()` — guard with null checks

### Credential Autofill
- `EnumWindows` + `EnumThreadWindows` (CredUI from embedded ActiveX are thread-owned, not top-level)
- UI Automation for modern XAML, Win32 `SendMessage`/`BM_CLICK` for classic
- `IMsTscNonScriptable` COM for embedded password injection + `ClearPassword()` after connect

### Split System (N-Pane Binary Tree)
- `ISplitContent` → `SessionPaneModel` (leaf) | `SplitContainerModel` (branch); up to 8 panes/tab
- **Pane identity**: `ServerId` = session-scoped (state machine key, empty during connect). `OriginalServerId` = stable (inventory ID, for reconnect/history/layout)
- **SplitService** (singleton): owns split/merge, per-session `CancellationTokenSource`, `SplitLayoutMemory`, `ConnectByProtocolAsync`
- **Disposal order**: detach HostControl (null) → remove from tree → dispose (prevents RDP airspace issues)
- **Post-await guards**: `!ActiveSessions.Contains(session)` + `FindPane` null check prevent orphans
- **Tool panes**: `ConnectionType = "TOOL:<ID>"`, GUID `ServerId`; `CanClose()` blocks close/merge if busy
- **Deferred reconnect**: old state released only AFTER new connection succeeds/fails
- Controls detach event handlers in `Unloaded`; `_emptyPane` is per-instance (not static)

### Command Palette (Ctrl+K)
- WPF `Popup` (own HWND) for airspace fix above ActiveX surfaces
- Parses `user@host:port` with optional protocol prefix; bare IP proposes SSH+RDP
- Split mode forces Embedded connection mode (external processes cannot be docked)
- Focus: `Dispatcher.BeginInvoke(Input)` + P/Invoke `SetForegroundWindow`/`SetActiveWindow`/`SetFocus`

### ServerDialog
- Protocol-driven two-step flow: Step 1 = protocol card selection, Step 2 = protocol-specific fields
- Advanced mode: animated reveal, persisted via `ConfigManager.MergeSettingAsync()` — NOT persisted when forced by validation focus (temporarily unhooks persistence handler)
- Inline validation: `[Required]`/`[Range]`/`[NotifyDataErrorInfo]`, tab error badges, `FocusFirstInvalidField()` with deferred focus

### Security
- DPAPI + HMAC-SHA256: `CredentialProtector` with legacy blob backward compat
- `InputValidator` centralized security: `EscapeShellArg()`, `EscapeForDoubleQuotedString()`, `ValidateDomain()`, `SanitizeCsvCell()`, `IsShellTarget()`
- Shell injection prevention: `EscapeShellArg()` on all SSH tunnel and tool `CreateCommand()` calls (CWE-78)
- Context-aware placeholder sanitization: `IsShellTarget()` detects shell interpreters (cmd, powershell, bash, wsl, cscript, mshta + script extensions .bat/.cmd/.ps1/.vbs/.js/.wsf/.hta); shell targets → strict stripping, regular .exe → relaxed (preserves `()`, `'`, `%`)
- CSV formula injection prevention: `SanitizeCsvCell()` in 10 exporters + `ToolContextMenuHelper`
- CRLF sanitization on raw HTTP Host header construction
- `SecureFileWriter.WriteAndProtect()`: atomic ACL file creation (no TOCTOU)
- Path traversal: `LocalFileBrowserView` validates against `GetInvalidFileNameChars()` + `..`
- WebSocket Origin validation in `WebSocketVncProxy` (CSWSH prevention)
- Accessibility: `AutomationProperties.Name` on all interactive controls across all views, runtime-localized via `SetName()` pattern

### Design System (CommonControls.xaml)
- Typography tokens: `FontSizeSmallCaption(11)` → `FontSizeHeadline(24)`, min 11px
- Spacing tokens: `SpacingXs(4)` → `SpacingXl(24)` — uniform `Thickness` only
- Padding tokens: `ToolHeaderPadding(12,8)`, `ToolFooterPadding(12,6)`, `PaddingButtonCopy`, `PaddingButtonPrimary` — all tool headers/footers use these tokens
- Asymmetric margins stay hardcoded (WPF `Thickness` resources are uniform-only)
- Icons: `Geo.*` vectors in IconGeometries.xaml + Segoe MDL2 for UI chrome
- Tool category brushes: Network (blue), Security (amber), Encoding (purple), System (teal)

### State Machines
- **Connection**: Disconnected → Initializing → ValidatingConfig → EstablishingTunnel → TunnelEstablished → Launching{Protocol} → Connected → Disconnecting → Error
- **App status**: Initializing → Ready → Busy → Error → Shutdown
- Transitions validated via `ConnectionStateMachine` before applying

## Migration & Import

- Config-compatible with legacy (same JSON schema)
- Import: JSON, MobaXterm (.mxtsessions/.ini), mRemoteNG (.xml), RDCMan (.rdg), .rdp
- Export: JSON
