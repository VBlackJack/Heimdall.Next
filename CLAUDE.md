# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

**Heimdall.Next** is a ground-up rewrite of Heimdall (PowerShell 5.1 + WPF) as a modern .NET 10 + WPF application. It is a secure Windows RDP/SSH/SFTP/Citrix connection manager designed to be a MobaXterm alternative with superior security and modern UX.

**Current build**: v2026.031703 (Release)

## Repository Layout

- **Working directory**: `G:\_dev\SnapConnect\Heimdall.Next`
- **Solution file**: `Heimdall.slnx`
- **Legacy project**: `G:\_dev\SnapConnect\RDPManager` (PowerShell version, maintained in parallel)

## Codebase Size

- **~120 C# source files** (~26,000 LOC)
- **~18 XAML files** (~6,000 LOC)
- **12 test files** (~3,500 LOC), 385 xUnit tests
- **~1,730 i18n keys** per locale (EN/FR)
- **1,700+ lines** of theme XAML (CommonControls + Dark/Light)

## Architecture (6 Projects)

```
Heimdall.slnx
├── src/
│   ├── Heimdall.Core/          # Shared foundation (net10.0)
│   │   ├── Configuration/      # AppSettings, ConfigManager, SchemaValidator, ServerProfileDto
│   │   ├── Localization/       # LocalizationManager (JSON-based i18n)
│   │   ├── Logging/            # FileLogger (daily rotation)
│   │   ├── Models/             # RdpServer, SshGateway, Project, TunnelSession, ISessionResult, ServerProfileDto, Enums
│   │   ├── Security/           # DpapiProvider, HmacIntegrity, PinManager, InputValidator, AclEnforcer
│   │   └── StateMachine/       # ConnectionStateMachine, ApplicationStatusMachine
│   │
│   ├── Heimdall.Ssh/           # SSH engine (net10.0)
│   │   ├── Pageant/            # PageantClient (Win32 IPC), PageantKeyWrapper, PageantHostAlgorithm
│   │   ├── Plink/              # PlinkTunnelRunner (fallback for Pageant-only auth)
│   │   ├── SshConnectionFactory, FailureClassifier, AuthPreflightChecker
│   │   ├── TunnelManager, GatewayChainResolver, HostKeyStore (TOFU)
│   │   └── SshShellSession, SshFailureCode (25 codes), TunnelInfo/Result/Session
│   │
│   ├── Heimdall.Rdp/           # RDP + Citrix engine (net10.0-windows, WPF+WinForms)
│   │   ├── ActiveX/            # RdpActiveXHost, ComInterfaces (IMsTscAx, IMsTscNonScriptable)
│   │   ├── Citrix/             # ConnectionService.Citrix.cs, EmbeddedCitrixView
│   │   ├── CredentialAutofill  # EnumWindows + EnumThreadWindows + UI Automation
│   │   ├── AspectRatioManager, RdpFileGenerator, RdpRedirectionOptions
│   │   └── IRdpSession, CredentialManagerHelper
│   │
│   ├── Heimdall.Sftp/          # SFTP engine (net10.0)
│   │   ├── SftpBrowser         # SSH.NET native SFTP
│   │   ├── RemoteFileEditor    # FileSystemWatcher auto-upload
│   │   └── PathEscaper
│   │
│   ├── Heimdall.Terminal/      # Terminal engine (net10.0-windows)
│   │   ├── ConPty/             # ConPtySession, NativeMethods, SafePseudoConsoleHandle
│   │   ├── PipeModeSession     # Pipe mode for SSH (stdin/stdout, no ConPTY)
│   │   ├── ITerminalSession    # Common interface
│   │   └── SmartPasteGuard     # Multi-line paste protection
│   │
│   └── Heimdall.App/           # WPF application (net10.0-windows)
│       ├── ViewModels/         # MainViewModel, ServerListVM, ConnectionVM, SettingsVM, SessionTabVM
│       │   └── Dialogs/        # ServerDialogVM, GatewayDialogVM, ProjectDialogVM, PinDialogVM
│       ├── Views/              # MainWindow, EmbeddedRdpView, EmbeddedSshView, EmbeddedSftpView, EmbeddedCitrixView
│       │   └── Dialogs/        # ServerDialog, GatewayDialog, ProjectDialog, PinDialog, InputDialog
│       ├── Services/           # ConnectionService (6 partial files: .Rdp, .Ssh, .Sftp, .Citrix, .Local, .Tunnel),
│       │                       # EmbeddedSessionManager, MigrationService, DialogService,
│       │                       # NavigationService, SleepPrevention
│       ├── Converters/         # BoolToVisibility, ConnectionState/Type brushes, FileSizeConverter, etc.
│       ├── Themes/             # CommonControls.xaml (1,700+ lines, incl. GridViewColumnHeader), DarkTheme, LightTheme
│       └── Theming/            # WindowThemeHelper (DWM dark mode API)
│
├── tests/
│   ├── Heimdall.Core.Tests/    # StateMachineTests
│   └── Heimdall.Ssh.Tests/     # FailureClassifier, AuthPreflight, HostKeyStore, Pageant, PlinkTunnel
│
├── config/                     # Factory defaults (settings.default.json, servers.default.json)
├── locales/                    # en.json, fr.json (~1,730 keys each)
├── docs/                       # TROUBLESHOOTING.md, ARCHITECTURE.md
├── Build.ps1                   # Portable build script (YYYY.MMDDxx versioning)
└── Dist/                       # Build output (gitignored)
```

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (C# 14) |
| UI framework | WPF (MVVM via CommunityToolkit.Mvvm) |
| DI | Microsoft.Extensions.DependencyInjection |
| SSH/SFTP | SSH.NET 2025.1.0 |
| Terminal rendering | WebView2 + xterm.js (pipe mode) |
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
2. **Test**: `dotnet test` (xUnit, 385 tests)
3. **Validate JSON locales**: Checks i18n key parity between en.json and fr.json (~1,730 keys)
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
- Custom `PageantClient` communicates via Win32 shared memory for SSH.NET key injection
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
- Supports RDP, SSH, SFTP, and Citrix connection strings
- Parses `user@host:port` format with optional protocol prefix
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
- CommonControls.xaml provides 1,700+ lines of shared control styles

### AvalonEdit Embedded Editor
- AvalonEdit control embedded for viewing and editing remote files via SFTP
- Syntax highlighting for common file types
- Integrates with `RemoteFileEditor` for auto-upload on save

### Version Number
- Win32 version fields limited to 65535; `<Version>1.0.MMDD.xx</Version>` for assembly
- `<InformationalVersion>YYYY.MMDDxx</InformationalVersion>` for display purposes

## Migration from Legacy Heimdall

- Config format compatible (same JSON schema for settings.json / servers.json)
- `MigrationService` reads DPAPI-encrypted fields from legacy config
- Import/export supports same formats (JSON, CSV, MobaXterm, mRemoteNG)

## Dependency Graph

```
Heimdall.App
  ├── Heimdall.Core
  ├── Heimdall.Ssh      -> Heimdall.Core
  ├── Heimdall.Rdp      -> Heimdall.Core
  ├── Heimdall.Sftp     -> Heimdall.Core, Heimdall.Ssh
  └── Heimdall.Terminal  -> Heimdall.Core
```

External: CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, SSH.NET, WebView2, System.Security.Cryptography.ProtectedData.
