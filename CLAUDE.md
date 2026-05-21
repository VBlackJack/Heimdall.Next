# CLAUDE.md

## Project Overview

**Heimdall.Next** — .NET 10 + WPF secure Windows connection manager (RDP, SSH, SFTP, VNC, Telnet, FTP, Citrix, Local Shell). MobaXterm/mRemoteNG alternative. Ground-up rewrite of Heimdall (PowerShell 5.1 + WPF).

**Current build**: managed by `Build.ps1` auto-versioning (Debug; release version updates on next `Build.ps1 -Mode Release`)

## Repository Layout

- **Solution**: `Heimdall.slnx` — 9 source projects + 5 test projects (14 total)
- **Legacy**: `G:\_dev\SnapConnect\RDPManager` (maintained in parallel)

```
src/
├── Heimdall.Core/             # Models, Config, Security, Discovery, Localization, StateMachine, Logging, RdpResolutionMode/ProfileMigration
├── Heimdall.Ssh/              # SSH.NET + Pageant + Plink fallback, TunnelManager, HostKeyStore
├── Heimdall.Rdp/              # ActiveX MsTscAx + Citrix StoreBrowse, CredentialAutofill, RdpDisplayHelper
├── Heimdall.Sftp/             # SFTP + FTP browsers (IRemoteBrowser), RemoteFileEditor
├── Heimdall.Terminal/         # ConPty, PipeModeSession, TelnetSession, SmartPasteGuard
├── TwinShell.Core/            # Command/action models, services, localization abstractions
├── TwinShell.Persistence/     # SQLite persistence, repositories, EF Core
├── TwinShell.Infrastructure/  # Git sync, import/export, orchestration
└── Heimdall.App/              # WPF app: ViewModels, Views, Services, Themes, Converters, Localization
    ├── Services/              # ConnectionService (thin router), TunnelService, ConnectionHelpers,
    │                          # SplitService, EmbeddedSessionManager, MigrationService, DialogService,
    │                          # MacroService, EphemeralFileServer, X11ServerManager, WebSocketVncProxy,
    │                          # ToolRegistry (59 built-in tools), SecNumCloudAuditEngine, HtmlReportGenerator, CsvEvidenceExporter,
    │                          # HeimdallThemeService (ThemeForge wrapper + ThemeRevision + bridge refresh),
    │                          # TwinShellBootstrapper (DI + DB seed + localization/settings bridges for TwinShell),
    │                          # FullscreenShortcutRouter, LowLevelKeyboardHook, RdpDisconnectTeardownSequence,
    │                          # ResolutionPresetCatalog, ResolutionChoice
    │   ├── EmbeddedRdp/       # LetterboxLayoutCalculator (fixed-resolution centered host rect math)
    │   ├── Handlers/          # Per-protocol handlers (RdpHandler, SshHandler, SftpHandler, VncHandler,
    │   │                      #   TelnetHandler, FtpHandler, CitrixHandler, LocalShellHandler)
    ├── ViewModels/            # MainViewModel, ServerListViewModel, ConnectionViewModel, SettingsViewModel,
    │                          # SidebarToolsViewModels (SidebarToolCategoryViewModel + SidebarToolItemViewModel)
    ├── Converters/            # ConnectionType/State/StatusToBrush (dual IValue/IMultiValueConverter + ThemeRevision),
    │                          # ResourceKeyToBrushConverter (generic brush-key + ThemeRevision trigger),
    │                          # ResourceKeyToGeometryConverter, StringToBrushConverter, BoolToVisibility, NullToVisibility
    ├── Themes/                # HeimdallThemeBridge.xaml (74 app brushes on ThemeForge slots),
    │                          # CommonControls.xaml (Design Tokens, micro-animations, SidebarTabStyle),
    │                          # DialogCommonStyles.xaml, IconGeometries.xaml (Geo.* vector icons)
    └── Localization/          # TranslateExtension ({loc:Translate}), LocalizationSource (singleton bridge)
tests/
├── Heimdall.App.Tests/   # ThemeForge wrapper/bridge, SplitService, RDP display/letterbox/fullscreen/disconnect helpers, NotesStorage, EphemeralFileServer, WebSocketVncProxy tests
├── Heimdall.Core.Tests/  # StateMachine, Security (CredentialProtector, DPAPI, HMAC, InputValidator), ConfigManager, discovery tests
├── Heimdall.Rdp.Tests/   # CredentialAutofill broker selection tests
├── Heimdall.Ssh.Tests/   # FailureClassifier, AuthPreflight, HostKeyStore, Pageant, Plink, TunnelManager, security-event tests
└── Heimdall.App.UiTests/ # UIAutomation smoke-adjacent desktop tests
config/                  # Factory defaults (settings.default.json, servers.default.json,
                         #   hacker-simulator.scenarios.default.json, hacker-simulator.playlists.default.json)
locales/                 # en.json, fr.json (5,397 leaf keys each)
```

### Dependency Graph

```
Heimdall.App → Core, Ssh, Rdp, Sftp, Terminal, TwinShell.Core, TwinShell.Persistence, TwinShell.Infrastructure
Heimdall.Ssh → Core    |  Heimdall.Rdp → Core
Heimdall.Sftp → Core, Ssh  |  Heimdall.Terminal → Core
TwinShell.Infrastructure → TwinShell.Core, TwinShell.Persistence
TwinShell.Persistence → TwinShell.Core
```

External: CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, SSH.NET, WebView2, noVNC, ProtectedData, EF Core SQLite, LibGit2Sharp, Polly.

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

**Batch shortcuts** (double-click): `Run.bat` (build+launch), `Test.bat` (tests), `Build.bat` (debug build), `Release.bat` (full release pipeline)

```bash
dotnet build && dotnet test && dotnet run --project src/Heimdall.App
powershell -File Build.ps1                              # Debug build
powershell -File Build.ps1 -Mode Release                # Release + installers
powershell -File Build.ps1 -Mode Release -Publish       # Release + GitHub publish
powershell -File Build.ps1 -Mode Release -DryRun        # Simulated publish (safe)
powershell -File Build.ps1 -Mode Release -Version 2026.033101  # Force version
powershell -File Build.ps1 -SkipTests
```

Test baseline: `dotnet test Heimdall.slnx --no-build` discovers 5,578 tests — 5,578 passing, 0 skipped. Partial per-project TRX files can report smaller counts or hide `NotExecuted`; always run the aggregated command for a correct baseline.

**Gotcha — `Build.ps1 -SkipTests` + `dotnet test --no-build`**: `Build.ps1 -SkipTests` skips the test pass but also skips the build of test assemblies. Pairing it with `dotnet test --no-build` afterwards runs stale test binaries (or fails to find them outright). When iterating on tests after a `-SkipTests` build, run an explicit `dotnet build Heimdall.slnx -c Debug -p:nodeReuse=false` before `dotnet test`.

- **"build"** = `Build.ps1` (Debug), **"release"** = `Build.ps1 -Mode Release`, **"publish"** = add `-Publish`
- Version: `<Version>1.0.MMDD.xx</Version>` (Win32 limit 65535), `<InformationalVersion>YYYY.MMDDxx</InformationalVersion>`
- `-Version` flag overrides auto-increment to avoid version inflation on repeated builds
- Output: `Dist/debug/` or `Dist/release/` (gitignored), installers in `Dist/installers/`
- CI: GitHub Actions — build, test, i18n key parity, lint

## Workflow — Pair Architect Mode

When working on complex multi-file changes (refactoring, features, bug hunts), the preferred workflow is **supervised mode**:

- **Claude (Cowork)** acts as architect/supervisor — explores the codebase, plans the approach, produces detailed implementation prompts one at a time
- **The user** passes each prompt to **Claude Code** (CLI agent), then reports back the result
- **Claude** analyzes the report and produces the next prompt
- Communication: **French** between Claude and the user, **English** in prompts for Claude Code and all code/comments/docs
- **No `Co-Authored-By`** or AI attribution in commits — AI usage stays invisible in repo history
- Each prompt must be self-contained (Claude Code has no memory of previous prompts)
- Every code-modifying prompt ends with build/test verification
- After UI changes, request screenshots before writing the next prompt
- The skill `.claude/skills/pair-architect/SKILL.md` has the full methodology reference

## Code Standards

- **License**: Apache 2.0, author "Julien Bombled" on new files
- **Language**: English only (code, comments, docs)
- **No hardcoding**: Strings → locales; URLs/paths/magic numbers → config
- **Async by default**: No blocking calls on UI thread
- **MVVM**: Logic in ViewModels, no code-behind except minimal event wiring
- **Nullable reference types**: Enabled project-wide
- **TreatWarningsAsErrors**: Enabled via `Directory.Build.props`
- **i18n key convention**: `<Context><Element>` CamelCase (e.g., `ErrorPlinkNotFound`, `BtnConnect`)
- **Core namespace collision guard**: Before creating a new sub-namespace under `Heimdall.Core.*`, check the chosen name against BCL top-level namespaces (`System`, `IO`, `Net`, `Threading`, `Linq`, `Text`, `Collections`, `Diagnostics`, `Security`, `Runtime`, `Globalization`, etc.). If collision, use a disambiguated name (e.g., `SystemInfo` instead of `System`, `NetDiag` instead of `Net`) AND align the folder path to match the disambiguated namespace. Reference: `Heimdall.Core.SystemInfo` with folder `src/Heimdall.Core/SystemInfo/` — posed in b29, realigned in b31.

## Test discipline

When extending test coverage, two non-negotiable rules apply:

- **Producer-first** — A mapping test must cite file + line + trigger of the real producer. If the producer cannot be located in `src/`, the test goes back to investigation before it is written.
- **Architecture-first** — Do not introduce a refactor solely to make a test reachable. If the test requires a new seam, decide the refactor explicitly as an architecture change on its own merits, not as a side effect of test coverage.

## i18n Patterns

- New XAML: `{loc:Translate Key}` markup extension (live-updates on locale change via `LocalizationSource` singleton)
- Legacy `ApplyLocalization()` coexists; new views use `{loc:Translate}`, migration is incremental
- 5,489 leaf keys per locale (EN/FR), CI enforces key parity

## Built-in Tools (59 tools)

### Network (17 tools)
Ping, DNS Lookup, Certificate Inspector (multi-port scan), Port Scanner, Subnet Calculator, IP Converter, HTTP Status Codes, Whois, HTTP Header Analyzer, Banner Grabber, TCP Traceroute, SNMP Walker, ARP Monitor, Firewall Rule Tester, Network Cartography, Network Calculator, TCP Ping

### Security (15 tools)
Hash Generator, HMAC Generator, Password Generator, SSH Key Generator, Certificate Generator, JWT Parser, TOTP Generator, Password Policy Checker, SSH Key Auditor, SSL/TLS Auditor, DNS Security Checker (SPF/DKIM/DMARC), SMB Enumerator, Default Credential Scanner, CVE Lookup, **SecNumCloud Audit** (orchestration)

### Encoding (6 tools)
Base64, URL Encoder, JSON Formatter, Regex Tester, Text Diff, Text Case Converter

### System (15 tools)
Chmod Calculator, DateTime Converter, UUID Generator, Crontab Builder, Log Viewer, Hosts File Editor, SSH Config Generator, Cron Job Manager, Service Status Dashboard, Notes (Markdown), Diagram Editor, Hacker Simulator, **Command Library** (TwinShell integration), Privilege Launcher

### External (6 native tools + auto-detected third-party)
Wake-on-LAN (UDP magic packet), Open Ports (P/Invoke GetExtendedTcpTable), Network Interfaces (.NET NetworkInterface API), Route Table, DNS Batch Resolver, Wi-Fi Networks

### Tool Infrastructure
- `ToolRegistry.cs`: single source of truth — one `Entry()` per tool (ID, category, i18n keys, aliases, factory, icon)
- `IToolView` interface: `Initialize(ToolContext?, LocalizationManager?)`, `CanClose()`, `Dispose()`
- `ToolDescriptor.DescriptionKey`: optional explicit i18n key for tool description (default convention: `ToolDesc{Id}`)
- Gateway routing: most network tools support SSH tunnel via `ToolGatewayConnector.Connect()` + `CmbRouteVia` ComboBox
- Icons: `Geo.Tool.*` geometries in `IconGeometries.xaml`
- **Sidebar panel**: tabbed sidebar with Sessions / Tools toggle (`SidebarTabStyle` RadioButtons, Ctrl+Shift+T). Tools tab shows a full-height `TreeView` with collapsible categories (`SidebarToolCategoryViewModel` → `SidebarToolItemViewModel`), filter by name + aliases, single-click to launch. See **Sidebar (Sessions / Tools Tabs)** gotcha section below for the tab-toggle edge case.
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
- **Context menu**: detected tools appear in session right-click → "Detected Tools" submenu, grouped by provider
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

### Command Library (TwinShell Integration)
- **Source**: bundled TwinShell projects under `src/TwinShell.Core/`, `src/TwinShell.Persistence/`, and `src/TwinShell.Infrastructure/`, all referenced via ProjectReference and targeting `net10.0-windows`
- **Database**: SQLite at `%LOCALAPPDATA%\TwinShell\twinshell.db` (shared with TwinShell standalone if installed)
- **Seed data**: 514 pre-configured PowerShell/Bash commands from `data/seed/actions/*.json`, seeded on first launch during splash screen (`await`, not fire-and-forget)
- **Bootstrapper**: `TwinShellBootstrapper.cs` registers all TwinShell services in Heimdall DI — includes `HeimdallLocalizationBridge` (JSON locales → `ILocalizationService`) and `HeimdallSettingsBridge` (AppSettings → `ISettingsService`)
- **Tool ID**: `CMDLIB` in `ToolRegistry`, category System
- **Features**: fuzzy search with relevance ranking (HashSet O(1) filter), platform/category/risk filters with `X/Y` counter, parameterized command generation with inline validation (Required `*`, type tooltips, host prefill from `ToolContext.TargetHost`), Windows/Linux template switcher, notes/examples/links panel, favorites (★ toggle + filter), command history (auto-record on Copy/Send), import/export (TwinShell-compatible JSON), Git Sync (LibGit2Sharp via `IGitSyncService`)
- **CRUD**: `CommandActionDialog` modal with conditional platform sections (`ShowWindowsSection`/`ShowLinuxSection` bound to Platform), parameter editor with Name/Label/Type headers + Default/Required metadata, `WatermarkTextBoxStyle` on DefaultValue fields
- **Send to Terminal**: `ToolContext.SendCommandAction` delegate wired by `EmbeddedSessionManager.CreateToolControl()`, walks `SplitTreeHelper.EnumerateLeaves()` to find sibling `EmbeddedSshView`
- **Git Sync settings**: `AppSettings.CmdLibGitSync*` (URL, Token DPAPI-encrypted, Branch, Author, OnStartup, AutoPush) — configurable in Settings tab, token status indicator + clear button
- **System action protection**: Edit/Delete buttons hidden for non-user-created (seed) actions; import skips system actions in merge mode
- **Layout**: 7-row grid (Header, Help, Filters, LoadingBar, ActionList, Generator, History). Generator and History panels mutually exclusive (selecting action closes History, toggling History closes Generator). Generator uses inner Grid with fixed action buttons row outside ScrollViewer. Filter bar uses Grid+WrapPanel (search full-width Row 0, combos wrap in Row 1). HistoryList shares themed ListViewItem template (SurfaceBrush hover, CardBrush select) with ActionList

## Key Design Gotchas

### RDP ActiveX (Critical)
- **Layout flush before Connect()**: `UpdateLayout()` + `DoEvents()` + `Dispatcher.Invoke(Render)` — 2 flushes required (pre-connect + post-handle)
- **Airspace rule**: WPF UI above `WindowsFormsHost` MUST use `Popup` (own HWND). `Panel.ZIndex` has no effect against Win32 HWNDs. Command Palette uses this pattern
- **Resolution updates blocked 10s** after `OnConnected` (prevents disconnect code 4360). Configurable via `AppSettings.RdpResizeEnableDelayMs` (default 10000 ms).
- **Per-profile/global RDP setting resolution**: settings with both a per-profile DTO field and a global `AppSettings` counterpart resolve as `profile` when non-null → `global` → hardcoded default. Keep the resolver pure, static, and total over `int?`. Negative profile values clamp to `0` at runtime while schema/dialog validation rejects them; negative global values fall back to the hardcoded default with a Warning log. Reference implementation: `EmbeddedSessionManager.ResolveRdpResizeEnableDelayMs`.
- **IMsRdpExtendedSettings acquisition**: do not use `dynamic ax.ExtendedSettings`; the AxHost-marshalled `System.__ComObject` does not expose `ExtendedSettings` through IDispatch on real `MsTscAx.MsTscAx.10` installs. Use direct QI on the OCX (`ocx as IMsRdpExtendedSettings`) with a `Marshal.QueryInterface` fallback. `IServiceProvider.QueryService` returns `E_NOINTERFACE` for sibling COM interfaces here.
- **DPI scale factors**: set `DesktopScaleFactor` / `DeviceScaleFactor` pre-`Connect()` via `IMsRdpExtendedSettings.Property`; dynamic session resizing goes through `UpdateSessionDisplaySettings` with current monitor DPI-derived values. Widths sent to RDP are snapped to a multiple of 4.
- **FitWindow uses SmartSizing**: `RdpDisplayResolver` resolves `RdpResolutionMode.FitWindow` to `smartSizing: true` (`reason: explicit-fit-window-scaled`). This is the architectural answer to MsTscAx's non-client scrollbar leak in non-smart modes; stripping `WS_HSCROLL | WS_VSCROLL` at the Win32 layer is unwinnable because MsTscAx re-applies the bits every layout pass. Fixed and Multimon modes intentionally keep `smartSizing: false` for pixel-perfect rendering; the HWND-strip plumbing in `RdpActiveXHost` remains as defense in depth for those modes.
- **External vs embedded Auto parity**: embedded Auto is the reference contract. External `.rdp` Auto mode must write `smart sizing:i:1`, force `use multimon:i:0` regardless of the profile flag, use `screen mode id:i:1`, and write deterministic primary working-area dimensions snapped to a multiple of 4 via `RdpDisplayHelper`.
- **UseMultimon**: `IMsRdpClientNonScriptable5` is the documented owner of `UseMultimon` (IID `4F6996D5-D7B1-412C-B0FF-063718566907`). The old `AdvancedSettings9` path is fragile; current code uses direct QI with a `Marshal.QueryInterface` fallback before `Connect()`.
- **Multimon topology validation**: connect-time topology validation lives in pure `RdpDisplayResolver.ValidateMultimon`. If requested multimon cannot be honored by the current host, fall back to single-monitor, log at Warning with structured topology context, and surface the localized message through the existing reconnect/status text channel (`EmbeddedRdpView.StatusTextBlock`), never a modal.
- **Per-monitor selection**: `selectedmonitors` is a documented RDP property but does NOT appear on `IMsRdpClientNonScriptable5` in the published Microsoft typelib. Pattern used by `RdpActiveXHost.TrySetSelectedMonitors`: primary path `MsRdpClientShell.SetRdpProperty("selectedmonitors", "0,2")` (documented), with a best-effort fallback to `IMsRdpClientNonScriptable5.SelectedMonitors` (typelib-inferable but not contractually documented). Apply this `SetRdpProperty + non-scriptable fallback` pattern any time a documented RDP property has no first-class C# binding on the COM interface — set the property via the documented path first, fall back only if the call returns failure or NotImplemented.
- **Fullscreen keyboard escape**: `Window.PreviewKeyDown` and `ComponentDispatcher.ThreadPreprocessMessage` are bypassed when `MsTscAx` owns focus. Reliable fullscreen shortcuts require `WH_KEYBOARD_LL` plus a foreground-process filter (`GetForegroundWindow()` → `GetWindowThreadProcessId()` vs cached `Environment.ProcessId`) so Heimdall does not absorb keys while another app is foreground.
- **Disconnect teardown order**: centralized in `RdpDisconnectTeardownSequence`: `Visibility=Collapsed` → `Child=null` → `Disconnect()` → `DetachEventSink()` → `Dispose()` (never `Marshal.ReleaseComObject`). All four paths (tab close, toolbar disconnect, context menu disconnect, reconnect/failed-session cleanup) route through `EmbeddedSessionManager.DisconnectSessionAsync`.
- **Letterbox fixed resolution**: `RdpResolutionMode.Fixed + RdpInitialSmartSizing=false` sizes and centers the `WindowsFormsHost` with `Margin`/`Width`/`Height` inside a `{DynamicResource SurfaceBrush}` host surface. Do not use `Stretch="Uniform"` or WPF transforms on `WindowsFormsHost`; airspace rules make transforms unreliable over Win32 child HWNDs.
- **COM pre-warm**: Background STA thread creates/disposes throwaway `RdpActiveXHost` at startup (~400ms saved)
- **Auto-reconnect**: Bounded retry (`MaxReconnectAttempts = 20`) with `CancelAutoReconnect` flag
- **Disconnect decoder**: `RdpActiveXHost.GetDisconnectReasonKey()` maps 24 MsTscAx codes to i18n keys

### Sidebar (Sessions / Tools Tabs)
- **Sidebar smart truncation**: `SidebarDisplayNameFormatter` truncates only when the trimmed name exceeds `MaxLength = 40` chars and has a trailing parenthesized suffix. Strategy: preserve the full head (identifier), truncate the suffix with Unicode `\u2026`, and drop the suffix entirely if the head alone is near the budget. WPF `TextTrimming` handles further clipping, tooltips always show the full `DisplayName`, and the default sidebar width is 320 px.
- **Ctrl+Shift+T tab toggle**: grouped `RadioButton.IsChecked = !IsChecked` can leave both sidebar tabs unchecked. `ToggleSidebarTab()` must explicitly check the target sibling so the Sessions / Tools content containers never collapse together.

### Terminal: Pipe Mode (NOT ConPTY)
- ConPTY double-converts VT sequences, breaking arrow keys
- Pipe mode: `xterm.js → stdin pipe → plink -t → remote PTY` (raw VT passthrough)
- ConPTY reserved for local shell only; data transfer binary-safe via base64

### WebView2 Focus
- Captures focus aggressively; toolbar needs `Panel.ZIndex=100`
- `ClipToBounds=True` on content Grid prevents overflow into other tabs
- Focus set ONCE after xterm.js `ready:` message, never in GotFocus/PreviewMouseDown
- Session tabs live INSIDE Sessions Grid (Column 2), never as global overlay
- **AcceleratorKeyPressed routing**: WebView2 SDK intercepts keys via `CoreWebView2Controller.AcceleratorKeyPressed` and raises synthetic WPF `KeyDown` with `e.OriginalSource = WebView2 control`. This is the only reliable way to detect terminal-originated keys — `Keyboard.FocusedElement` stays stale on the TreeView, and `GetFocus()` Win32 is redirected during `TranslateAccelerator`. Guard: `e.OriginalSource is Microsoft.Web.WebView2.Wpf.WebView2`

### SSH & Pageant
- SSH.NET for programmatic auth + in-process tunnels; Plink fallback for Pageant-only
- `PageantClient`: Win32 shared memory, `AGENT_COPYDATA_ID = 0x804e50ba`, RSA-SHA2 registration, `Sign()` must return full SSH blob (not raw signature bytes). Do not strip or re-wrap the blob returned by `PageantClient.SignData()`.
- `FailureClassifier`: 29 structured `SshFailureCode` values, including `HostKeyUnavailable` for fail-closed Plink paths where no Heimdall-trusted gateway key can be resolved.
- Host-key dependencies are non-nullable on production SSH/SFTP/tunnel/sudo entry points. `RejectingHostKeyVerifier` is the safe fail-closed verifier; `AutoAcceptHostKeyVerifier` is for explicit tests only.
- `PlinkHostKeyDecider` + `IPlinkHostKeyProbe`: Plink must not launch without a stored or safely probed fingerprint that Heimdall can pass as `-hostkey`; never fall back to PuTTY/Plink's cache.
- `TunnelManager`: Reference-counted (shared tunnels, teardown only on last disconnect). Reuse identity includes remote target, forwarding mode, and `GatewayChainKey` (stable gateway IDs + collision-safe chain hash).
- Mid-session host-key rejection flows through `SshSessionFailureDispatcher` and `SshSessionSecurityEvent`; SSH auto-reconnect must remain blocked on MITM signals.

### Tool Gateway Routing (`/dev/tcp` pattern)
- 5 tools support "Route via" SSH gateway: Network Cartography, Port Scanner, Banner Grabber, Firewall Tester, Default Credential Scanner
- `ToolGatewayConnector.Connect()` returns an `SshClient` for remote command execution (no SOCKS/port-forward)
- **Per-probe timeout required**: bare `(echo >/dev/tcp/HOST/PORT)` blocks 20-127s on filtered ports (kernel TCP retransmit timeout). Always wrap: `timeout 2 bash -c "echo >/dev/tcp/HOST/PORT"`
- **Explicit `bash -c`**: `/dev/tcp` is a bash built-in, not available in `dash`/`sh`. SSH exec channels may use `/bin/sh` depending on the gateway's login shell
- **Zombie prevention**: without `timeout`, SSH.NET `CommandTimeout` kills the channel but the remote bash process lingers. `timeout` sends SIGTERM for clean cleanup
- **NetworkCartography tunnel scan**: 3-phase approach — (1) batch ping sweep + ARP table for host discovery, (2) batch reverse DNS, (3) parallel background `/dev/tcp` probes per host with `sleep 5; kill $(jobs -p); wait` fence
- Shell escaping: `InputValidator.EscapeShellArg(ip)` produces `'10.0.0.1'` — safe inside `bash -c "..."` (single quotes are literal inside double quotes, inner bash removes them)

### SFTP
- SSH.NET native `SftpClient`, not psftp process
- Sudo fallback: `sudo cat`/`sudo tee` via SSH exec only on typed permission-denied exceptions (`SftpPermissionDeniedException`, local `UnauthorizedAccessException`). Do not reintroduce substring matching on generic `Failure` messages.
- Sudo upload cleanup uses separate write/cleanup commands via `SudoUploadCommands`; cleanup intentionally ignores the user cancellation token so `/tmp/.heimdall_*` files are removed even when the upload is cancelled.
- `RemoteFileEditor`: `FileSystemWatcher` + 2s debounce auto-upload; re-edit closes previous session. Upload tasks are tracked per `EditSession`, cancellation is propagated through `CloseEdit`/`Dispose`, and faults are observed.
- Sudo edit sessions cache the session `PinnedFingerprintVerifier`; host-key rotation during auto-upload raises `HostKeyRotatedDuringUpload` and closes the edit session.
- XAML gotcha: `CheckBox IsChecked="True"` fires `Checked` during `InitializeComponent()` — guard with null checks

### FTP
- `FtpBrowser` implements `IRemoteBrowser` on top of .NET `FtpWebRequest`; migration rationale for FluentFTP lives in `docs/audit/ftp-fluentftp-migration.md`.
- `FtpHandler` validates host and port before connect, using `InputValidator.ValidateDomain`, `IPAddress.TryParse`, and `InputValidator.ValidatePortRange`.
- `ConnectionResult.Warning` carries non-blocking protocol warnings. Credentialed FTP without TLS sets `WarnFtpCleartext`; route it to status text, not a modal.
- Parser helpers (`NormalizePath`, `ResolvePath`, `ParseListLine`, `ParseUnixDate`) are `internal static` test seams; keep them pure.

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

### Bulk server actions
- Five persisted-mutation bulk helpers (`DeleteServersCoreAsync`, `MoveServersToGroupCoreAsync`, `MoveServersToProjectCoreAsync`, `DuplicateServersCoreAsync`, `EditPasswordServersCoreAsync`) delegate to a shared template `ExecutePersistedBulkMutationAsync` + declarative record `BulkMutationPlan` in `ServerListViewModel.Bulk.cs`
- The plan builder mutates the local DTO list only; the template is the sole writer of `_allServers`, `SelectedItems`, `SelectedServer`, and filter state, and does so only after `SaveServersAsync` succeeds (rollback-by-construction)
- **VM identity preservation**: update existing view-models via `ServerItemViewModel.UpdateFromDto(...)`, not by constructing a new VM and swapping `_allServers[i]`; swapping breaks the references held by `SelectedItems` and fails single-item contract tests
- **Filter before selection**: call `ApplyFilter(...)` first, then `ApplySelection(...)`; selection normalizes against the filtered `Servers` collection, and reversing the order silently empties the selection when the targets are not yet in view
- **Bulk password**: `EditPasswordServersCoreAsync` encrypts the plaintext once via `CredentialProtector.Protect()` then `SetEditablePassword` routes the ciphertext to the protocol-specific field (`RdpPasswordEncrypted`, `SshPasswordEncrypted`, `FtpPasswordEncrypted`, `TelnetPasswordEncrypted`, `VncPassword`) based on `ConnectionType`. Dialog uses double `PasswordBox` (no data binding — code-behind pushes to ViewModel).
- `ConnectServersBulkCoreAsync` is explicitly **not** part of this family — its cycle runs sessions, not persisted mutations, and it must not reference the template or the plan record

### ServerDialog
- Protocol-driven two-step flow: Step 1 = protocol card selection, Step 2 = protocol-specific fields
- Advanced mode: animated reveal, persisted via `ConfigManager.MergeSettingAsync()` — NOT persisted when forced by validation focus (temporarily unhooks persistence handler)
- Inline validation: `[Required]`/`[Range]`/`[NotifyDataErrorInfo]`, tab error badges, `FocusFirstInvalidField()` with deferred focus

### Security
- DPAPI + HMAC-SHA256: `CredentialProtector` with legacy blob backward compat
- `InputValidator` centralized security: `Validate()`, `ValidatePortRange()`, `GetPattern()`, `GetPatternNames()`, `ValidateDomain()`, `SanitizeCsvCell()`, `EscapeShellArg()`, `EscapeForDoubleQuotedString()`, `IsShellTarget()`, `IsValidExecutionPolicy()`
- **Credential diagnostic logging**: never log username, domain, password-presence, password length, or any credential field in RDP/SSH/SFTP/credential code paths. Connect log lines may reference target host and protocol only. Broker enumeration diagnostics may include OS window titles, handles, PIDs, and process names, but never edit-field content. Canonical example: `CredentialAutofill.cs`.
- Shell injection prevention: `EscapeShellArg()` on all SSH tunnel and tool `CreateCommand()` calls (CWE-78)
- Context-aware placeholder sanitization: `IsShellTarget()` detects shell interpreters (cmd, powershell, bash, wsl, cscript, mshta + script extensions .bat/.cmd/.ps1/.vbs/.js/.wsf/.hta); shell targets → strict stripping, regular .exe → relaxed (preserves `()`, `'`, `%`)
- CSV formula injection prevention: `SanitizeCsvCell()` in 10 exporters + `ToolContextMenuHelper`
- CRLF sanitization on raw HTTP Host header construction
- `SecureFileWriter.WriteAndProtect()`: atomic ACL file creation (no TOCTOU)
- Path traversal: `LocalFileBrowserView` validates against `GetInvalidFileNameChars()` + `..`
- WebSocket Origin validation in `WebSocketVncProxy` (CSWSH prevention)
- Accessibility: `AutomationProperties.Name` on all interactive controls across all views, runtime-localized via `SetName()` pattern
- SSH/SFTP audit controls: compile-time non-null host-key dependencies, Plink `HostKeyUnavailable` fail-closed path, gateway-aware tunnel reuse identity, typed mid-session security events, typed sudo permission-denied checks, cached sudo edit verifier, and non-blocking cleartext FTP warnings

### Theming
- **ThemeForge package**: Heimdall consumes the private `ThemeForge.Theme` NuGet package from GitHub Packages. It exposes 16 canonical themes; the app default is `Drakul`.
- **`HeimdallThemeService`** (singleton, DI): app wrapper around `ThemeForge.Theme.ThemeService`. It preserves Heimdall's compatibility surface (`ApplyTheme(string?)`, `CurrentTheme`, `ThemeRevision`, `event Action<string> ThemeChanged`) while delegating palette swaps to ThemeForge. Invalid persisted theme names fall back to `Drakul` and are persisted through `ConfigManager.MergeSettingAsync`.
- **Bridge dictionary**: `Themes/HeimdallThemeBridge.xaml` re-expresses Heimdall's 74 app brush keys on ThemeForge color slots via `DynamicResource`. `HeimdallThemeService.RefreshHeimdallBridge` re-merges this dictionary after every ThemeForge swap because a shared `SolidColorBrush` resource does not live-update its `DynamicResource` `Color`.
- **Kept app dictionaries**: `CommonControls.xaml`, `DialogCommonStyles.xaml`, and `IconGeometries.xaml` remain app-owned. The old `ThemeService` and 15 legacy `*Theme.xaml` palette files were removed.
