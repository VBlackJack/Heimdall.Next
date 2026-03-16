# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

**Heimdall.Next** is a ground-up rewrite of Heimdall (PowerShell 5.1 + WPF) as a modern .NET 10 + WPF application. It is a secure Windows RDP/SSH/SFTP connection manager designed to be a MobaXterm alternative with superior security and modern UX.

**Current build**: v2026.031647 (2026-03-16)

## Repository Layout

- **Working directory**: `G:\_dev\SnapConnect\Heimdall.Next`
- **Solution file**: `Heimdall.slnx`
- **Legacy project**: `G:\_dev\SnapConnect\RDPManager` (PowerShell version, maintained in parallel)

## Codebase Size

- **97 C# source files** (~18,600 LOC)
- **12 XAML files** (~3,800 LOC)
- **6 test files** (~1,860 LOC), 218 xUnit tests
- **1,412 i18n keys** per locale (EN/FR)
- **1,700+ lines** of theme XAML (CommonControls + Dark/Light)

## Architecture (6 Projects)

```
Heimdall.slnx
├── src/
│   ├── Heimdall.Core/          # Shared foundation (net10.0)
│   │   ├── Configuration/      # AppSettings, ConfigManager, SchemaValidator, DTOs
│   │   ├── Localization/       # LocalizationManager (JSON-based i18n)
│   │   ├── Logging/            # FileLogger (daily rotation)
│   │   ├── Models/             # RdpServer, SshGateway, Project, TunnelSession, Enums
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
│   ├── Heimdall.Rdp/           # RDP engine (net10.0-windows, WPF+WinForms)
│   │   ├── ActiveX/            # RdpActiveXHost, ComInterfaces (IMsTscAx, IMsTscNonScriptable)
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
│       ├── Views/              # MainWindow, EmbeddedRdpView, EmbeddedSshView
│       │   └── Dialogs/        # ServerDialog, GatewayDialog, ProjectDialog, PinDialog, InputDialog
│       ├── Services/           # ConnectionService, EmbeddedSessionManager, MigrationService
│       │                       # DialogService, NavigationService, SleepPrevention
│       ├── Converters/         # BoolToVisibility, ConnectionState/Type brushes, etc.
│       ├── Themes/             # CommonControls.xaml (1,599 lines), DarkTheme, LightTheme
│       └── Theming/            # WindowThemeHelper (DWM dark mode API)
│
├── tests/
│   ├── Heimdall.Core.Tests/    # StateMachineTests
│   └── Heimdall.Ssh.Tests/     # FailureClassifier, AuthPreflight, HostKeyStore, Pageant, PlinkTunnel
│
├── config/                     # Factory defaults (settings.default.json, servers.default.json)
├── locales/                    # en.json, fr.json (1,412 keys each)
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
