<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Changelog

All notable changes to Heimdall.Next are documented in this file.

## [Unreleased] - 2026-03-28

### Comprehensive audit — security, i18n, accessibility, and robustness across 49 files

#### Security
- Centralize shell escaping in `InputValidator`: `EscapeShellArg()`, `EscapeForDoubleQuotedString()`, `ValidateDomain()`, `SanitizeCsvCell()`
- Add input validation + shell escaping on all `CreateCommand()` calls across 16 tool views (CWE-78 prevention)
- CSV formula injection prevention via `SanitizeCsvCell()` in 10 exporters + generic `ToolContextMenuHelper`
- CRLF sanitization on raw HTTP Host header construction
- IIS CVE predicates: proper version checks replacing always-true predicates

#### Fixed
- SslStream disposal in 7 files (try/finally + DisposeAsync + leaveInnerStreamOpen)
- SemaphoreSlim disposal in 6 files
- RSA/ECDSA crypto key disposal in 3 files (using var)
- X509Certificate disposal after clone, CTS disposal in finally
- Process kill-on-cancellation for DNS processes
- OperationCanceledException propagation at 40+ catch sites
- Blocking async converted to proper await (TlsAuditView certificate retrieval)
- Dead code removal (TlsAuditView cipher enumeration)
- Race condition on CTS lifecycle (Interlocked.Exchange)
- Password cleared on Dispose (PasswordAuditView)
- DKIM success message showing DMARC wording
- Punycode/IDN hostname validation (allow -- mid-label)

#### Internationalization
- Extract ~170 i18n keys from SecNumCloudAuditEngine, HtmlReportGenerator, and tool views
- SecNumCloudAuditEngine: `Func<string, string> localize` constructor parameter
- HtmlReportGenerator: `localize` parameter on `Generate()`
- Locale count: ~4,290 keys (EN/FR parity)

#### Accessibility
- AutomationProperties.Name on all interactive controls across 17+ XAML files
- Hardcoded English accessibility labels replaced with runtime-localized SetName() pattern

#### Data Model
- AuditScope.Targets: `List<string>` -> `IReadOnlyList<string>`

### UX audit — a11y, design tokens, i18n, and interaction across 49 tools

Three-pass cross-audit covering all 49 built-in tools (64 files, +809/-417 lines).

#### Accessibility
- 565 AutomationProperties.Name annotations in XAML (49/49 tool files covered)
- 592 AutomationProperties.SetName() calls in code-behind (49/49 files)
- 11 unnamed buttons given x:Name for a11y (ChmodCalculator presets, PasswordGenerator quick-lengths)

#### Design Tokens
- New `ToolContentMaxWidth` (700) token — 20 files migrated from hardcoded MaxWidth values
- New `PaddingButtonToolbar` (8,4) token — 17 buttons migrated (DiagramEditor, NotesToolView)
- ~90 buttons migrated to padding tokens (PaddingButtonCopy, PaddingButtonPreset, PaddingButtonPrimary, PaddingButtonToolbar, PaddingButtonHelp)
- Hardcoded `CornerRadius="3"` replaced with CornerRadiusXs token (SnmpWalker, CveLookup)
- Hardcoded `Foreground="White"` replaced with TextOnAccentBrush (SshKeyAudit, CveLookup)
- Hardcoded `FontSize="12"` / `FontSize="16"` replaced with FontSizeCaption / IconSizeMedium tokens

#### Interaction
- 8 tools now handle Enter key on input fields (UUID, SshKeyGen, CertGen, FirewallTester, NetworkCalc, SshConfigGen)
- 2 ProgressBars added (CronJobManager, ServiceStatus) for async loading feedback
- UUID BtnGenerate promoted from SecondaryButtonStyle to PrimaryButtonStyle

#### Internationalization
- FirewallTester placeholder moved from hardcoded XAML Tag to locale keys
- 6 new locale keys added (en.json + fr.json): ToolFwTestHostsPlaceholder, ToolCronJobA11yLoading, ToolServicesA11yLoading

---

## [v2026.032701] - 2026-03-27

### Comprehensive tool audit — robustness, accessibility, and UX (15 tools, 26 files)

#### Password Generator overhaul
- **3 generation modes**: Random, Syllable (CV/CVC), and Passphrase with per-mode presets
- **Optional clipboard auto-clear** (30s): checkbox in Advanced section, visual hint after copy
- **Custom presets filtered by mode**: only presets matching the current mode are shown
- **Title vs WordCase differentiated**: Title capitalizes first group only, WordCase capitalizes every group
- **Strength hidden when empty**: no more "Critical (0 bits)" on blank output
- **Quick-length highlight** now updates correctly after preset application
- **TextBox guards**: MaxLength on separator (4) and custom specials (64) inputs
- **Preset cache**: avoids disk I/O on every mode change
- **try/finally** on ApplyCustomPreset to prevent flag freeze on exception

#### Cross-tool robustness (12 files)
- **Clipboard.SetText protection**: 21 unprotected calls across 12 tools wrapped in `try/catch(ExternalException)` to handle locked clipboard gracefully (Base64, CertGenerator, Chmod, Crontab, Json, JWT, SshConfig, TextDiff, HostsFile, Notes, PasswordGenerator)
- **try/finally on boolean flags**: HackerSimulator (`_isRunning`, `_typingInProgress`, `_cursorVisible`), PingTool (`_isRunning`), PortScanner (`_isScanning`) — prevents UI freeze if setup code throws
- **CanClose()** added to ServiceStatus and CronJobManager to prevent close during async operations

#### Accessibility
- **LiveSetting="Polite"** added to 9 dynamic output elements across 7 tools (PasswordGenerator, ServiceStatus, CronJobManager, SshConfigGenerator, UUID, NetworkCalculator, LogViewer, NetworkCartography)
- **Focusable="True"** on PasswordGenerator output TextBox for keyboard navigation

#### i18n
- 2 new locale keys: `ToolPwdGenClipboardAutoClear`, `ToolPwdGenClipboardClearHint` (EN + FR)
- Total: 3,654 keys per locale

---

## [v2026.032606] - 2026-03-26

### Security Audit tool overhaul

#### Extensible scenario system
- **25 scenarios** across 6 categories (Visual, Attack, Deployment, Hardening, Incident, Identity) and 3 realism levels (Demo, Ops, Enterprise)
- **External JSON scenario packs**: template engine with `{{pick:...}}`, `{{number:min-max}}`, `{{hex:N}}`, `{{ip}}`, `{{mac}}` variables — add custom scenarios without recompiling
- **Playlist system**: ordered scenario sequences with 5 built-in playlists (Client Demo, SOC, DevOps, Compliance, Red Team)
- **Favorites**: star/unstar scenarios, filter by favorites
- **Toolbar redesign**: scenario picker, category/realism filters, text search, speed slider, playlist selector

#### New infrastructure scenarios (JSON-driven)
- Ansible Rolling Deployment, Multi-Hop Server Chain, Role Rollout / Hardening
- Vault Secret Rotation, HAProxy Blue/Green Promotion, Linux Patch Window
- AWX Job Template Rollout, Helm / Kubernetes Upgrade, PKI / Certificate Renewal

#### Playback features
- **Seed-based deterministic replay**: same seed reproduces identical scenario output
- **Transcript export**: text and Markdown format with per-scenario sections
- **Vintage CRT mode**: scanline overlay with flicker animation

#### Settings persistence
- Favorites, last scenario, playlist, random mode, vintage monitor state saved to `settings.json`
- 5 new `HackerSimulator*` properties in `AppSettings`

#### Code quality (post-review cleanup)
- 35 UI chrome strings extracted from inline `Tx()` to locale files (CI key-parity enforced)
- 9 redundant C# scenario builders removed (JSON-only, no dead code)
- Blocking `GetAwaiter().GetResult()` replaced with proper async
- 4 bare `catch {}` blocks narrowed to `catch (Exception)`
- 10 magic numbers extracted to named constants
- Duplicated `JsonSerializerOptions` consolidated to `static readonly` field

---

## [v2026.032605] - 2026-03-26

### Diagram Editor audit and embed protocol fixes

#### Diagram Editor (P1)
- **Empty diagram loading**: Canvas now initializes automatically on open (previously blocked on "Loading" until user clicked New)
- **Native autosave**: Replaced custom polling autosave (manual graph serialization via mxCodec) with draw.io's native `autosave`/`save` embed events — preserves full .drawio context
- **External link relay**: Help menu and external links now open in the default browser via `openLink` embed event
- **Menu bar hidden**: draw.io's built-in menu bar (File/Edit/View/Arrange/Extras/Help) disabled — `mxPopupMenu` dropdowns cannot open inside a WebView2 iframe due to pointer event routing limitations; Heimdall's own toolbar provides New/Open/Save/Export PNG

#### Architecture constraint documented
- draw.io embed mode requires `(window.opener || window.parent) != window` — iframe is mandatory (direct WebView2 load bypasses `initializeEmbedMode`)

#### CLAUDE.md
- Condensed from 495 to 170 lines (~65% reduction) — removed content derivable from code, kept all bug-prevention gotchas

---

## [v2026.032601] - 2026-03-26

### Comprehensive UX audit and Codex audit implementation

#### WCAG Contrast Fixes (P0)
- **Dark ErrorColor**: #FF5555 → #FF6B6B (5.13:1 on primary background)
- **Dark BorderColor**: #6272A4 → #7B8EC4 (4.41:1)
- **Dark TextDisabledColor**: #9298B0 → #A8AECA (4.17:1 on surface)
- **Light BorderColor**: #94A3B8 → #708090 (3.72:1)
- **Dark SurfaceColor**: #44475A → #4A4D64 (improved card/background separation)

#### Accessibility (P0-P1)
- **14 empty AutomationProperties.Name** replaced with declarative `{loc:Translate}` in MainWindow
- **Keyboard context menu**: Shift+F10 / Apps key opens context menu on TreeView
- **LiveSetting="Polite"**: SSH/RDP/VNC status text announced by screen readers
- **Icon button a11y**: Overlay reconnect/close buttons labeled in all embedded views
- **59 decorative MDL2 icons**: Hidden from screen readers via `AutomationProperties.Name=""`
- **Tab focus ring**: Navigation tabs show FocusIndicatorBrush on keyboard focus

#### ServerDialog Redesign (Codex Critique)
- **Auth fields in basic mode**: Username, password, SSH key now visible without Advanced toggle
- **Protocol-specific sections**: RDP/SSH/SFTP/VNC/FTP/Telnet/Local/Citrix each show relevant auth fields
- **Advanced mode reduced**: Only Connection diagram, Tunneling, Options, Info, Gateway Auth remain behind toggle

#### Scheduled Task Dialog (Codex Elevated)
- **New ScheduledTaskDialog**: Replaces two sequential InputDialogs with structured form
- **Server ComboBox**: Searchable dropdown from server inventory
- **Schedule type**: Daily (time picker) or Interval (minutes) with live validation
- **Next run preview**: "Next execution: tomorrow at 09:30" shown in real-time
- **Edit support**: Edit button + double-click on DataGrid row
- **Dirty state guard**: Warns on close with unsaved changes

#### Command Palette Safety (Codex P1)
- **Click = select only**: Single click highlights without executing
- **Enter / double-click = execute**: Prevents accidental connection launches
- **Ctrl+Enter = split**: Unchanged

#### Server Detail Panel Enrichment (Codex P2)
- **6 new metadata rows**: Project (with color dot), Username, Gateway, Auth summary, Tags, Favorite star
- **Auth summary**: Per-protocol (e.g., "SSH Key + Password", "Agent", "Prompt")
- **Gateway name resolution**: Resolved from inventory map

#### Settings Improvements (Codex Elevated)
- **Layout widened**: MaxWidth 600px → 900px for better desktop utilization
- **Sticky action bar**: Save/Reset/Import/Export pinned at top with border separator
- **Explicit Browse buttons**: "..." replaced with folder icon + "Browse" label on all 5 buttons
- **Search filter**: TextBox filters sub-tabs by keyword (bilingual EN/FR matching)

#### Filter Enrichment (Codex Medium)
- **8-field search**: Sidebar filter + Command Palette now search DisplayName, RemoteServer, Group, Username, ConnectionType, Environment, Tags, ProjectName

#### Validation Consistency (Codex Medium)
- **GatewayDialog**: Per-field inline errors (NameError, HostError, PortError, UserError)
- **ProjectDialog**: Per-field inline errors (NameError, DescriptionError)
- **Live re-validation**: Both dialogs re-validate on keystroke after first save attempt
- **Focus on dialog open**: First field auto-focused in GatewayDialog and ProjectDialog

#### Dirty State Guards (P1)
- **ServerDialog**: IsDirty tracking with _isInitializing guard, confirm on Cancel
- **GatewayDialog**: Same pattern with per-property tracking
- **ProjectDialog**: Same pattern

#### Typography & Visual Hierarchy (Codex Medium)
- **Scale widened**: Caption 11→12, Body 12→13, Subtitle 14→15, Title 18→20
- **SpacingLg**: 16→20 for more section breathing room
- **Section title margin**: Added top/bottom spacing in DialogCommonStyles
- **OpacityDisabled**: 0.55 → 0.60 for better dark theme distinction

#### Keyboard & Navigation
- **InputGestureText**: Ctrl+E, Ctrl+Del, Ctrl+N shown on context menu items
- **Tooltip shortcut hints**: Ctrl+Del, Ctrl+K added to toolbar buttons
- **Scroll position restore**: TreeView scroll offset saved/restored on tab switch
- **Discoverability hints**: Visible "Ctrl+N · Ctrl+K · F1" in empty state, detail panel, status bar

#### Additional Improvements
- **Last-used gateway**: Pre-selects in Add Server dialog (persisted in AppSettings)
- **SFTP cancel**: Icon button with mid-transfer cancellation via progress callback
- **LocalFileBrowserView**: Dynamic Name column sizing
- **MessageDialog**: Button order normalized (Cancel → Primary), resizable
- **InputDialog**: SizeToContent instead of fixed height
- **Button MinWidth**: Standardized to 80px across all dialogs

#### i18n
- 3,566 keys (EN/FR parity) — +87 keys
- `StringToBrushConverter` for project color dots in detail panel

#### Tests
- **1,586 tests** (1,196 Core + 283 SSH + 107 App), all passing

---

## [v2026.032508] - 2026-03-25

### Full UX audit implementation (P0-P2)

#### WCAG Contrast Fixes (P0)
- **FileIconColorConverter theme adaptation**: Replaced 6 hardcoded Dracula RGB brushes with theme-aware resources (FileScriptBrush, FileConfigBrush, etc.) — Light theme file icons now legible (was 1.5:1, now 4.5:1+)
- **Dark theme ErrorColor**: #FF6E6E → #FF5555 (4.2:1 → 4.6:1, meets WCAG AA)
- **Dark theme TelnetBadgeBrush**: #A0A0B0 → #B0B4C8 (4.5:1 → 5.2:1)
- **Light theme BorderColor**: #CBD5E1 → #94A3B8 (1.5:1 → 3.2:1, meets WCAG 2.1 § 1.4.11 non-text)

#### Data Loss Prevention (P1)
- **Unsaved settings warning on tab switch**: Save/Discard/Cancel dialog when leaving Settings tab with pending changes
- **Unsaved settings warning on app exit**: Same dialog in Window.Closing handler
- **3-button MessageDialog**: New `ShowThreeWay()` method + `BtnTertiary` for Save/Discard/Cancel pattern
- **Window size/position persistence**: Saves Width, Height, Left, Top, WindowState to AppSettings on close; restores on load with virtual screen bounds validation

#### Accessibility (P1)
- **Reduced-motion support**: Respects Windows "Show animations" setting (`SystemParameters.MenuAnimation`) — animation durations overridden to 0ms when disabled (WCAG 2.1 § 2.3.3)

#### Keyboard Shortcuts (P2)
- **Ctrl+W**: Close current session tab (with confirmation if connected)
- **Ctrl+Tab / Ctrl+Shift+Tab**: Cycle between session tabs (next/previous)
- **F1 help updated**: New shortcuts documented in EN/FR
- **Tooltip shortcut hints**: Toggle sidebar tooltip now includes "(Ctrl+B)"

#### UX Guards (P2)
- **Double-click connect guard**: `_connectingServerIds` HashSet prevents duplicate concurrent connections to the same server from rapid clicks

#### i18n
- 3,501 keys (EN/FR parity confirmed) — +2 keys (BtnDiscard)

#### Tests
- **1,586 tests** (1,196 Core + 283 SSH + 107 App), all passing

---

## [v2026.032506] - 2026-03-25

### UX audit phase 2: validation, palette redesign, protocol-driven add server

#### Server Dialog — Protocol-Driven Flow
- **Protocol selector**: New Step 1 with 8 large card buttons (vector icons + protocol colors) replaces the connection type dropdown in add mode
- **Contextual fields**: Form fields adapt to selected protocol — Local Shell shows only name, SSH shows host+port, etc.
- **Edit mode**: Read-only protocol badge, form pre-populated, protocol selector bypassed
- **Back button**: Returns to protocol selector in add mode without losing form data

#### Server Dialog — Inline Validation
- **Per-field errors**: Inline error messages below DisplayName, Server, Port, LocalPort, AudioMode, ColorDepth
- **Live re-validation**: Errors clear in real-time as user corrects fields (ValidateProperty per keystroke)
- **Tab error badges**: Red count badges on Tunneling and Options tabs when they contain errors
- **Auto-focus**: First invalid field receives focus on save, with automatic tab/advanced mode expansion
- **Protocol-aware validation**: Only relevant fields validated per protocol; HasErrors stays consistent via ClearErrors per-protocol cleanup
- **VNC port validation**: Added [Range] validation with i18n support
- **Reusable style**: FieldValidationErrorStyle in DialogCommonStyles.xaml

#### Command Palette (Ctrl+K) — Redesign
- **Two-line layout**: Line 1: protocol icon + name + badge; Line 2: host:port + username + project + group
- **Responsive width**: 550-700px (MinWidth/MaxWidth) instead of fixed 550px, MaxHeight 450px
- **Active session indicator**: Protocol-colored left rail on connected sessions
- **Protocol badge**: Short labels (RDP, SSH, TEL, CTX, SH, TOOL) with per-protocol colors
- **Correct endpoint per protocol**: SSH/SFTP use SshPort, FTP uses FtpPort, VNC uses VncPort, Telnet uses TelnetPort
- **FTP/Telnet usernames**: Palette now shows credentials for all protocols, not just SSH/RDP

#### Settings
- **Unsaved changes indicator**: Orange dot on Settings tab when IsDirty, with localized tooltip
- **Theme revert on discard**: Live theme preview reverts to saved theme when user discards changes
- **Locale key fix**: Unsaved settings prompt now uses correct i18n keys

#### Bug Fixes
- **ServerDialog crash**: Fixed LayoutTransform storyboard using `FrameworkElement` instead of `UIElement` (runtime BAML error)
- **Scrollbar inversion**: Added `IsDirectionReversed="True"` to vertical Track in custom ScrollBar template
- **Telnet port loss on edit**: Telnet connections now load TelnetPort (not RemotePort) and skip default port reset in edit mode
- **Focus persistence**: FocusFirstInvalidField no longer permanently changes user's advanced-mode preference
- **Application.Current null check**: PaletteActiveIndicatorConverter safe during shutdown

#### Detail Panel
- **Edit/Delete buttons**: Added to server detail panel alongside Connect for better discoverability
- **Accessibility**: AutomationProperties.Name on all new interactive controls

#### Empty State
- **"No selection" enriched**: Segoe MDL2 icon + hint text + Ctrl+K quick connect tip when servers exist but none is selected

#### i18n
- 51+ new keys with full EN/FR parity (validation, protocol cards, hints, palette)
- 3,478+ keys per locale

---

## [v2026.032507] - 2026-03-25

### Complete UX audit implementation (19/20 items from triple-audit: Claude, Codex, Gemini)

#### Accessibility & Tooltips
- **Tooltip campaign**: Added localized tooltips to all icon-only buttons across MainWindow, EmbeddedRdp/Ssh/Sftp/Vnc/Citrix views, LocalFileBrowser, NotesToolView, PasswordGenerator, SessionPaneControl, SplitContainerControl (~47 buttons)
- **AutomationProperties localized**: Moved 45 hardcoded English `AutomationProperties.Name` from XAML to code-behind `ApplyLocalization()` using i18n keys (`A11y*` pattern)
- **Minimum font size**: Raised `FontSizeSmallCaption` from 9px to 11px for better readability on dense exploitation screens

#### Zero Hardcoding compliance
- **ComboBoxItems extracted to i18n**: Terminal color schemes (5), PowerShell execution policies (5), shell executables (5), SSH key algorithms (3), certificate algorithms (2), file encodings (4), HMAC formats (2), ping intervals (5) — all use `Tag` for stored value, `Content` set via `ApplyLocalization()`
- **Hardcoded ToolTip="Copy" removed** from PasswordGenerator history button (now localized via `Loaded` event handler)

#### Theme & Contrast
- **Scrollbar thumb contrast fixed**: Dark theme #7B8298 → #A8B0CC (2.8:1 → 4.2:1), Light theme #C0C0C0 → #999999 (1.8:1 → 4.8:1) — meets WCAG 2.1 non-text contrast minimum
- **Badge/protocol brush consolidation**: 5 new badge brushes (VNC, FTP, Citrix, Telnet, Local) + RDP/SSH/SFTP badge colors aligned with protocol accent brushes for visual consistency
- **Toolbar ghost button pressed state**: Changed from TextSecondaryBrush (poor contrast) to HighlightBrush

#### Discoverability & Navigation
- **Tools panel visible by default**: 33 built-in tools now shown on first launch instead of hidden behind collapsed toggle
- **Ctrl+Shift+T documented in F1 help** (EN/FR)
- **Wording "server-first" updated**: StatusReady, EmptyStateSelectServer, SearchPlaceholder now reference tools and Ctrl+K — not just servers
- **Command Palette mode indicator**: Shows "Split Mode" / "Merge Mode" label when palette opens in split/merge context
- **Command Palette auto-close**: Closes on sidebar tab change and window deactivation (preserves StaysOpen=True for ActiveX airspace compatibility)

#### Design System
- **EmptyStateStyle**: New reusable style in CommonControls.xaml for empty/onboarding states
- **DialogCommonStyles.xaml**: Extracted 8 shared styles (label, section title, hint text, section card, form inputs) from ServerDialog/GatewayDialog/ProjectDialog into shared resource dictionary
- **FadeIn animation**: Applied to ToolsQuickPanel for smooth expand transition
- **Notes dirty indicator**: Header shows "Unsaved changes" warning via `ToolNotesUnsaved` key when editor has pending changes

#### Network Cartography
- **Scan progress indicator**: Real-time "Scanning: X/Y hosts..." TextBlock in scan toolbar, updated from `HostDiscoveryProgress` event

#### Progressive Disclosure (ServerDialog)
- **Simple/Advanced mode**: Essential fields (Name, Host, Port, Type, Project, Gateway) always visible; 5 advanced tabs hidden behind animated toggle with ScaleY + Opacity transition (300ms ease-out open, 250ms ease-in close)
- **Mode persistence**: Advanced mode preference saved to AppSettings via `ConfigManager.MergeSettingAsync()`

#### Declarative i18n (loc:Translate markup extension)
- **TranslateExtension**: WPF `MarkupExtension` enabling `{loc:Translate Key}` syntax in XAML — auto-updates on runtime language switch via `INotifyPropertyChanged` on indexer
- **LocalizationSource**: Singleton bridge between WPF binding system and `LocalizationManager` DI service
- **PinDialog migrated**: Full POC — all 7 manual localization calls replaced with declarative XAML bindings, code-behind reduced to focus logic only

#### Icon System Unification
- **BitmapImage system removed**: Deleted `ConnectionTypeToIconConverter`, `ConnectionStateToIconConverter`, `IconResources.xaml`, and 37 PNG files
- **Two-tier icon architecture**: Vector geometries (`Geo.*` in IconGeometries.xaml) for domain icons + Segoe MDL2 Assets for standard UI chrome
- **TreeView rewrite**: Replaced ~180 lines of MDL2 DataTriggers with 2 converter bindings (`TypeToGeoConverter` + `TypeToColorConverter`)
- **ToolRegistry updated**: All 33 tools reference `Geo.Tool.*` geometry keys with `FrozenDictionary` lookups
- **Documented conventions**: Comprehensive header in IconGeometries.xaml describing naming pattern and extension procedure

#### i18n
- 3,457 keys (EN/FR parity confirmed) — +111 keys (tooltips, A11y, ComboBox content, empty states, palette modes, scan progress, disclosure, etc.)

#### Tests
- **1,586 tests** (1,196 Core + 283 SSH + 107 App), all passing

---

## [v2026.032506] - 2026-03-25

### Notes audit fixes, template i18n, Tools panel UX

#### Notes tool — bug fixes from multi-model audit (Codex + Gemini)
- **P1 — Milkdown fallback**: `TryInitializeMilkdownAsync` now checks `MilkdownEditorControl.IsHostInitialized` after `InitializeAsync()` — machines without WebView2 runtime correctly fall back to AvalonEdit instead of showing a non-functional Milkdown host
- **P1 — camelCase settings mismatch**: `CreateStorageService()` and `LoadSidebarWidth()` now read camelCase property names (`notesDirectory`, `notesSidebarWidth`) matching `ConfigManager`'s `JsonNamingPolicy.CamelCase` serialization — configurable `NotesDirectory` path was silently ignored after any settings round-trip
- **P2 — Sidebar width persistence race**: replaced ad-hoc `settings.json` direct write with `ConfigManager.MergeSettingAsync()` — prevents concurrent TOFU host key writes or other settings updates from being silently overwritten
- **P2 — Wiki-link accent regression**: `Slugify()` now strips diacritics via Unicode normalization (`FormD` decomposition + `NonSpacingMark` removal) — `Procédure` slugifies to `procedure`, and `FindNotePathAsync()` uses accent-insensitive title fallback so `[[Procedure]]` resolves `# Procédure`
- **Sync save in CanClose/Dispose**: new `NotesStorageService.SaveNote()` synchronous method avoids `.GetAwaiter().GetResult()` sync-over-async pattern
- **`_pendingReadOnly` nullable**: `MilkdownEditorControl` uses `bool?` to correctly handle `SetReadOnly(false)` before editor ready

#### Notes tool — Zero Hardcoding compliance
- **Template factory i18n**: all 26 hardcoded template strings extracted to locale files (`ToolNotesTpl*` keys) — `NotesTemplateFactory.Create()` accepts optional `LocalizationManager` parameter, propagated from view → storage → factory
- **French translations**: templates fully localized (Objectifs, Chronologie, Résumé, Étapes, Retour arrière, etc.)

#### Tools panel UX refonte
- **Removed redundant header**: deleted the "Tools ▾" panel header and its close button — the toggle button at the bottom is the sole open/close control
- **Chevron state indicator**: toggle button shows `▲` when panel is closed, `▼` when open
- **Category headers with colored accent**: each category section now displays a 3px colored bar (Network=blue, Security=amber, Encoding=purple, System=teal) with uppercase label in matching color
- **Alphabetical sort**: tools within each category sorted alphabetically by localized name

#### Infrastructure
- `ConfigManager.MergeSettingAsync(Action<AppSettings>)`: atomic load-mutate-save under write lock for targeted property updates
- `App.Services` public accessor for DI service resolution from tool views
- `NotesTemplateFactory.RemoveDiacritics()`: reusable Unicode diacritics stripping

#### i18n
- 3,346 keys (EN/FR parity confirmed) — +48 keys (26 template sections + 22 existing updates)

#### Tests
- **1,586 tests** (1,196 Core + 283 SSH + 107 App), all passing — +10 new (3 sync save, 3 diacritics, 2 accent-insensitive wiki-link, 2 template i18n)

---

## [v2026.032505] - 2026-03-25

### Notes tool enhancements and swap panes fix

#### Notes: sidebar toggle, context menu, Dracula theme
- **Sidebar toggle**: collapsible TreeView panel via hamburger button in header bar — saves/restores width across toggles
- **Editor right-click context menu**: 17 Markdown formatting actions (Bold, Italic, Strikethrough, Inline Code, Code Block, Link, Image, Note Link, Headings 1–3, Bullet/Numbered/Task List, Blockquote, Table, Horizontal Rule) — works in both Milkdown (JS) and AvalonEdit (WPF) editors with localized labels (EN/FR)
- **Dracula theme**: full Dracula palette for Milkdown dark mode (#282a36 bg, #f8f8f2 fg, #bd93f9 purple accents, #8be9fd cyan links, #ff79c6 pink inline code) via native Crepe `--crepe-*` CSS tokens (removed legacy `@milkdown/theme-nord` import). AvalonEdit syntax highlighting colors updated to match

#### Fix: swap panes freeze
- **Async two-phase handoff**: `SwapSplitPanesAsync` detaches host controls, awaits visual tree stabilization (`AwaitVisualTreeAsync` at Loaded + ContextIdle priority), swaps model references, awaits again, then restores controls — prevents UIElement single-parent race between old and new `SessionPaneControl` instances
- **`SessionPaneControl` lifecycle guards**: `SyncContent()` and `UpdateOverlays()` gated by `IsLoaded`; `HostPresenter.Content` cleared in both `OnUnloaded` and `OnDataContextChanged`; PropertyChanged subscription only while loaded — prevents disconnected controls from stealing WebView2/ActiveX children

---

## [v2026.032404] - 2026-03-24

### Notes tool — Obsidian-style Markdown editor with Milkdown

#### New tool: Notes (#34 NOTES)
- **Milkdown WYSIWYG editor** via WebView2 (ProseMirror-based, MIT licensed) with AvalonEdit + syntax highlighting fallback
- **TreeView file explorer** mirroring filesystem hierarchy with folder icons, drag-and-drop between folders, and folder creation
- **4 templates**: Blank, Daily, Incident, Procedure — with contextual server metadata pre-fill
- **`[[wiki-link]]` support**: click navigation, back/forward history, auto-completion popup on `[[` keystroke
- **Tag filtering**: `> tags: infra, prod` metadata line, dynamic filter buttons
- **Export**: Confluence Storage Format XML (copy/export), HTML standalone
- **Drag-and-drop import** of external `.md` files
- **Context menu**: New/Daily/Incident/Procedure, New Folder, Rename, Duplicate, Delete, Open in Explorer
- **Autosave** with 850ms debounce, path traversal protection, atomic writes
- **Configurable storage path** via `NotesDirectory` in settings.json

#### Integration
- Server right-click → "Notes" submenu with all templates (pre-filled ToolContext)
- Command Palette: `Ctrl+K → notes`
- Dedicated `Geo.Tool.Notes` icon

#### Infrastructure
- New `Heimdall.App.Tests` project: **97 tests** (SimpleMarkdownConverter, ConfluenceStorageConverter, NotesTemplateFactory, NotesStorageService)
- Session tab context menu exclusion for tool TreeViews (prevents Split/Merge menu from intercepting tool-owned context menus)
- `WebView2Loader.dll` copied to bin root for `dotnet run` compatibility
- `PlaceholderRegex` fix: removed `^$` anchors that prevented inline placeholder restoration

#### i18n
- 3,298 keys (EN/FR parity confirmed)

#### Tests
- **1,576 tests** (1,196 Core + 283 SSH + 97 App), all passing

## [v2026.032403] - 2026-03-24

### Split/Merge audit — 7 fixes (bugs, robustness, cleanup dedup)

#### Bug fixes
- **CancellationTokenSource leak**: `CancelSession` now disposes the CTS after a 5-second delay (deferred dispose) — previously cancelled but never disposed, leaking one CTS per tab close
- **GridSplitter cursor**: cursor now updates dynamically (`SizeNS` for Horizontal, `SizeWE` for Vertical) in `ApplyLayout()` — previously hardcoded `SizeWE` regardless of orientation
- **Reconnect self-referential LayoutMemory**: `ReconnectPaneAsync` no longer calls `LayoutMemory.Record` (was recording the same server as both primary and secondary, polluting palette suggestions)
- **MergeExistingSession HostControl check**: now checks all source tree leaves via `EnumerateLeaves().Any(p => p.HostControl is not null)` instead of the primary shim — split tabs with a disconnected primary were incorrectly blocked from merging

#### Robustness
- **CancellationToken propagation**: `ConnectByProtocolAsync` now passes `ct` to all `ConnectionService.Connect*Async` protocol handlers — closing a tab during a slow tunnel or SSH handshake now actually cancels the connection attempt
- **Merge blocked feedback**: `MergeExistingSession` now shows a status bar message (`SplitMergeBlockedByTool`) when a busy tool pane prevents the merge — previously returned silently with no user feedback

#### Cleanup deduplication
- **`CloseAllPanes` extracted to SplitService**: centralized tab teardown (CanClose gate, cancellation, disconnect history, tunnel release, state reset, disposal) — `ConnectionViewModel.CloseSessionInternal` now delegates entirely to `SplitService.CloseAllPanes`, eliminating 30 lines of duplicated cleanup logic
- **ConnectionViewModel slimmed**: removed 3 unused DI dependencies (`ConnectionStateMachine`, `TunnelManager`, `ConfigManager`) and their imports after cleanup extraction

#### i18n
- Added `SplitMergeBlockedByTool` key (EN/FR)

#### Tests
- **1,479 tests** (1,196 Core + 283 SSH), all passing

## [v2026.032402] - 2026-03-24

### SplitService extraction + race condition fixes

#### Architecture
- **SplitService extracted**: All split/merge orchestration (`SplitSessionWithServerAsync`, `SplitSessionWithTool`, `MergeExistingSession`, `ClosePane`, `ReconnectPaneAsync`, `SwapSplitPanes`, `ToggleSplitOrientation`) moved from `MainViewModel` to dedicated `SplitService` singleton (~500 lines extracted, ~350 lines removed from MainViewModel)
- **Unified protocol dispatch**: `ConnectByProtocolAsync` helper deduplicates the 8-protocol switch statement that was duplicated between split and reconnect flows
- **Callback wiring pattern**: `SplitService` uses the same callback property injection as `EmbeddedSessionManager` for access to `ActiveSessions`, `ActiveSession`, `HasActiveSessions`, and `StatusText`
- **DI registration**: `SplitService` registered as singleton in `App.xaml.cs`, injected into both `MainViewModel` and `ConnectionViewModel`

#### Race condition fixes
- **Per-session CancellationToken**: `RegisterSession`/`CancelSession` lifecycle on `SplitService` creates per-session `CancellationTokenSource`. Async split/reconnect methods check cancellation between config load and connection, and in post-await guards. `CloseSessionInternal` calls `CancelSession` before pane cleanup to abort in-flight operations
- **Deferred state machine cleanup in ReconnectPaneAsync**: Old tunnel reference and state machine entry are now released AFTER the new connection succeeds or definitively fails (via `ReleaseOldConnectionState` helper). Previously, old state was reset before reconnection, causing state loss on reconnect failure
- **Fixed disposal order**: `ClosePane` and `CloseSessionInternal` now detach HostControl from visual tree (set null) BEFORE removing from tree and disposing. Prevents RDP/ActiveX airspace issues during disposal
- **OriginalServerId set at pane creation**: `SplitSessionWithServerAsync` now sets `OriginalServerId` on the new pane immediately (was empty until post-connection finalization). Enables proper disconnect history and tunnel cleanup if pane is closed during async connection
- **MergeExistingSession CanClose check**: Now verifies `IToolView.CanClose()` on all source tree tool panes before merging. A busy tool (e.g., scan in progress) blocks the merge
- **SafeDispose enhanced**: Now logs unexpected exceptions (non-`ObjectDisposedException`) via `FileLogger.Warn` instead of silently swallowing them

#### UX improvements
- **Minimum pane size**: `SplitContainerControl` content presenters now enforce `MinWidth="120" MinHeight="80"` to prevent splitter from collapsing panes to unusable size
- **Double-click splitter reset**: Double-clicking the `GridSplitter` resets split ratio to 50/50 (`SplitContainerModel.DefaultRatio`)
- **NaN/Infinity guard**: `OnSplitterDragCompleted` now guards against `NaN`/`Infinity` ratios from collapsed panes (falls back to `DefaultRatio`)
- **Hover border on panes**: `SessionPaneControl` now shows a subtle 1px border on `IsMouseOver` (in addition to the existing 2px accent border on `IsKeyboardFocusWithin`) for better active pane feedback in split views
- **Splitter cursor**: `Cursor="SizeWE"` set on `GridSplitter` for visual feedback

#### Code quality
- **NotifyTreeDependentProperties**: Shared method replaces duplicated 12-line `OnPropertyChanged` blocks in both `OnRootContentChanged` and `NotifyShimPropertiesChanged` (DRY)
- **_emptyPane per-instance**: Changed from `static readonly` to instance field — prevents cross-session state leakage if fallback pane properties are modified
- **CTS lifecycle**: `CancelSession` no longer immediately disposes the CTS (just cancels). In-flight operations holding token references remain valid for guard checks
- **Diagnostic logging**: Added `FileLogger` calls at all guard points: pane not found, max panes reached, session cancelled, orphaned pane cleanup, double-close detection, tool CanClose blocked, reconnect skip (already in progress)

#### Schema versioning
- **SplitLayoutMemory**: `config/split-layouts.json` now uses versioned format `{ "version": 1, "entries": [...] }`. Load is backward-compatible with legacy bare-array format (auto-migrates on next save)

#### Tests
- **1,479 tests** (1,196 Core + 283 SSH), all passing — zero regressions from refactoring

## [v2026.032403] - 2026-03-24

### Symmetric split/merge between sessions and tools

#### New features
- **Mixed session + tool splits**: sessions and built-in tools can now be freely split and merged in any combination (e.g., SSH terminal left + Network Cartography right)
- **`SplitSessionWithTool`**: new method docks a built-in tool directly into a split pane without requiring a network connection — tool creation is synchronous, no loading overlay needed
- **Command Palette split mode**: tool tabs now appear as merge candidates alongside sessions; selecting a tool from search results in split mode docks it as a pane
- **Context menu merge**: "Merge with..." submenu now lists both sessions and tool tabs

#### Cleanup hardening
- **Per-pane cleanup in `CloseSessionInternal`**: refactored from early-exit tool check to per-pane handling in the recursive leaf loop — mixed splits (session + tool in same tab) now clean up correctly: tool panes respect `CanClose()` and skip state machine/tunnel teardown, while connection panes get full disconnect/tunnel/state-machine cleanup
- **`ClosePane` tool awareness**: closing a tool pane in a split tree now checks `IToolView.CanClose()` (e.g., blocks close during active scan) and skips state machine/tunnel operations
- **Busy tool blocks tab close**: if any tool pane in a split tree has `CanClose() == false`, the entire tab close is blocked (consistent with standalone tool tab behavior)

#### Routing
- `ExecutePaletteSelection`: added `tool-*` branch before generic server split path
- `ConnectFromPaletteAsync`: added `tool-*` branch in split mode routing
- `ConnectSplitFromPaletteAsync`: tools now split into active session pane instead of opening a new tab

## [v2026.032402] - 2026-03-24

### Split/Merge system hardening

#### Bug fixes
- **`ReplacePane` short-circuit**: extracted `ReplacePaneRecursive` with `bool` return — stops traversing after first match instead of processing both children
- **`RemovePane` null subtree**: when recursive removal empties a subtree, promotes the sibling instead of assigning `null` to `First`/`Second` (prevented potential `NullReferenceException`)
- **`ReplaceContainer` short-circuit**: converted from `void` to `bool` return for early exit after match
- **`MergeExistingSession` lookup**: added `OriginalServerId` fallback — context menu and palette merge no longer silently fail if `ServerId` is empty during connection
- **`OnSplitterDragCompleted` orientation guard**: explicit `SplitOrientation.Vertical` check prevents fallthrough to column calculation when horizontal grid is misconfigured

#### Memory leak fixes
- **`SessionPaneControl`**: added `Unloaded` handler — detaches `PropertyChanged`, `Button.Click`, `DataContextChanged`, `Loaded` subscriptions
- **`SplitContainerControl`**: added `Unloaded` handler — detaches `PropertyChanged`, `DragCompleted`, `DataContextChanged`, `Loaded` subscriptions

#### Thread-safety & I/O hardening
- **`SplitLayoutMemory`**: all public methods (`Record`, `FindPartner`, `FindAllPartners`) synchronized via `lock`; constructor `Load()` also under lock
- **Atomic save**: unique temp file per write (`Guid`-suffixed) with `finally` cleanup on failure — prevents corruption on concurrent writes or crash

#### Zero-hardcoding cleanup
- `SessionPaneControl.xaml`: replaced `Background="#B0000000"` → `{DynamicResource OverlayBackground}`, `FontSize="28"` → `{StaticResource FontSizeHeadline}`, `Foreground="#AAAAAA"/"White"` → theme brushes, removed English `FallbackValue`
- `SessionPaneControl.xaml.cs`: `"Disconnected"`/`"Error"` magic strings → `nameof(ConnectionState.Disconnected)`/`.Error`
- `SessionPaneModel.cs`: default `_status` changed from `"Connecting"` to `""` (set by caller via i18n)
- `SplitContainerModel.cs`: named constants `MinRatio` (0.1), `MaxRatio` (0.9), `DefaultRatio` (0.5), `SplitterThickness` (4)
- `SplitContainerControl.xaml.cs`: all magic numbers replaced with model constants; removed redundant `SetRowSpan/SetColumnSpan(1)` calls
- `SplitLayoutMemory.cs`: extracted `FileName` constant

#### Model improvements
- **`SplitRatio` auto-clamping**: `OnSplitRatioChanged` partial method clamps to `[MinRatio, MaxRatio]` — view no longer double-clamps
- **Merge ratio restoration**: `MergeExistingSession` consults `SplitLayoutMemory` for prior ratio when merging a previously-paired server pair
- **`SyncContent` optimization**: `ReferenceEquals` check prevents unnecessary `ContentPresenter.Content` reassignment

#### Menu restructure
- **"Split..." submenu**: replaced two top-level items with nested submenu (Split... → Horizontal | Vertical), matching "Merge with..." pattern
- **Palette split mode**: shows ALL servers from inventory (previously limited to 10 recent)
- New i18n keys: `SplitMenu`, `OrientationHorizontal`, `OrientationVertical` (EN + FR)

#### Accessibility
- `GridSplitter`: added `AutomationProperties.Name="Split pane resizer"`
- Disconnect icon: added `AutomationProperties.Name="Disconnected"`
- Overlay buttons: added `AutomationProperties.Name` for Reconnect/Close

#### Tests
- 5 new unit tests: deep `ReplacePane` (3+ levels), non-existent pane, short-circuit verification, deep `RemovePane` subtree promotion, `SplitRatio` clamping
- Total: **1,469 tests** (1,186 Core + 283 SSH), all passing

## [v2026.032401] - 2026-03-24

### Recursive N-Pane Split System

#### Architecture overhaul
- **Recursive split tree**: replaced flat `Secondary*` properties with binary tree model (`ISplitContent` → `SessionPaneModel` | `SplitContainerModel`)
- Up to **8 panes per tab** in any layout: 2x2, L-shape, 3 side-by-side, deeply nested splits
- All operations addressed by `PaneId` (GUID) — split, merge, swap, close, reconnect, detach
- WPF rendering via implicit `DataTemplate` resolution: `SessionPaneControl` (leaf) + `SplitContainerControl` (recursive container with `GridSplitter`)
- `SplitTreeHelper` static utilities: `EnumerateLeaves`, `FindPane`, `FindParent`, `FindSibling`, `RemovePane`, `ReplacePane`, `CountLeaves`, `FirstLeaf`
- 37 new unit tests for tree operations

#### New split features
- **Swap panes**: right-click → "Swap Panes" exchanges primary and secondary content
- **Toggle orientation**: Ctrl+Shift+O switches split between horizontal and vertical
- **Detach any pane**: extract any individual pane from a split tree into a floating window
- **Drag-to-split**: drag a tab onto the content area of another tab to merge (works on already-split targets for 3+ panes, orientation auto-detected from drop position)
- **Per-pane loading overlay**: spinner shown during connection with server title and status
- **Per-pane disconnect overlay**: Reconnect and Close buttons when a pane disconnects
- **Splitter ratio memory**: each pane's splitter position preserved across tab switches
- **Split layout persistence**: `SplitLayoutMemory` records server pairs in `config/split-layouts.json`, boosts previously paired servers in Command Palette

#### Context menu improvements
- "Merge with..." uses nested submenu per session (Session Name → Horizontal | Vertical)
- Split actions (Swap, Toggle Orientation, Close Secondary, Detach Secondary) shown when split is active
- "Detach Secondary" disabled while pane is still connecting

#### Safety and cleanup
- Post-await guard: `!Connection.ActiveSessions.Contains(session)` prevents orphaned connections when tab is closed during async split
- `CleanupOrphanedSecondary()` exposed for code-behind to clean up state machine/tunnel entries
- Close confirmation checks all panes in the tree (not just primary)
- State machine reset and tunnel reference release in `ClosePane` for each individual pane
- MergeExistingSession preserves state machine entries (connections are alive, just reparented)
- Anti-double-reconnect guard via `HostControl is null` check
- Layout coalescing: `_layoutDirty` flag prevents redundant grid rebuilds

#### Backward compatibility
- `SessionTabViewModel` exposes shim properties (`ServerId`, `Title`, `Status`, `HostControl`, `IsSplit`, `SplitOrientation`, `SplitRatio`, `Secondary*`) delegating to tree leaves
- `NotifyShimPropertiesChanged()` for in-place tree mutations (swap)
- Legacy `CloseSecondaryPane` and `ReconnectSecondaryAsync` relay commands preserved

#### Files added
- `Heimdall.Core/Models/ISplitContent.cs`, `SessionPaneModel.cs`, `SplitContainerModel.cs`, `SplitTreeHelper.cs`
- `Heimdall.App/Views/SessionPaneControl.xaml/.cs`, `SplitContainerControl.xaml/.cs`
- `Heimdall.Core/Configuration/SplitLayoutMemory.cs`
- `Heimdall.Core.Tests/SplitTreeHelperTests.cs`

#### Files removed
- `Heimdall.App/Views/SplitPaneHost.xaml/.cs` (replaced by `SessionPaneControl` + `SplitContainerControl`)

## [v2026.032312] - 2026-03-23

### Network Cartography — Deep Fingerprinting Engine

#### OS fingerprinting overhaul
- **Port-based OS inference**: RDP/WinRM → Windows, SSH-only → Linux, Kerberos+LDAP → Windows Server
- **SNMP sysDescr OS detection**: 19 patterns (VMware ESXi, Cisco IOS, Ubuntu, Debian, Red Hat, Windows, FreeBSD, etc.)
- **NTLM OS build mapping**: Extracts exact Windows version from SMB2 NTLM challenge (e.g., "Windows Server 2022 Build 20348")
- **MergeAll()**: Combines 5 sources (TTL, banner, ports, SNMP, NTLM) with multi-source confidence boosting

#### New probe modules
- **NtlmProbe**: SMB2 Negotiate + NTLMSSP Type 1/2 exchange — extracts hostname, domain, DNS forest, OS build, SMB dialect, signing policy, server GUID, uptime without credentials
- **SshFingerprinter**: HASSH fingerprint (MD5 of KEX_INIT algorithm lists) — identifies SSH implementation precisely
- **FaviconHasher**: Shodan-compatible MurmurHash3 favicon fingerprinting with 30+ known device hashes (FortiGate, VMware ESXi, Synology, Grafana, Jenkins, Freebox, TP-Link, Hikvision...)
- **HttpFingerprinter**: Cookie detection (12 frameworks), error page regex (7 patterns), product URL probing (13 vendor-specific paths: Hikvision, Synology, QNAP, MikroTik, FortiGate, ESXi...)
- **IanaPenDatabase**: SNMP sysObjectID → vendor decode via 50+ IANA Private Enterprise Numbers

#### Role classification improvements
- 4 new role definitions: LDAP Directory, Syslog Server (TLS/6514), HTTP Proxy (3128), Windows Server
- 6 conflict resolution rules: LDAP suppresses SSH, Windows Server suppresses generic RDP, AD suppresses partial roles
- Removed 3 dead UDP-only role definitions (Syslog/514, DHCP/67, UPnP/1900) unreachable via TCP scan
- Manufacturer-based role inference: Arlo → IP Camera, Verisure → Alarm System, Hikvision/Dahua → IP Camera
- Randomized MAC detection → "Smartphone/Tablet" role for devices with privacy MAC
- Certificate enrichment: issuer O=/OU= parsing, self-signed + 10yr validity → appliance default cert detection
- Chromecast confidence raised (70 base) to outrank generic "Web Server (HTTPS-Alt)"

#### SNMP enhancements
- 3 additional OIDs: sysObjectID (vendor/model), sysUpTime (uptime), sysServices (OSI layer bitmask)
- ASN.1 OID and TimeTicks decoders for response parsing
- NetBIOS parser bounds hardening: qdCount cap, strict offset validation

#### UPnP / SSDP deep discovery
- Fetch rootDesc.xml from SSDP LOCATION URL
- Parse friendlyName, manufacturer, modelName, modelNumber, serialNumber, presentationURL
- SsdpInfo extended with 3 new optional fields

#### OUI database expansion
- Added: Hikvision (BCBAC2, 4CF5DC, 54C4A5, C4A36E), Free/Freebox (DC00B0), Arlo Technologies (B8060D, 9C7B6B), Securitas Direct/Verisure (0023C1), Samsung (58B568)
- Locally administered MAC detection → "Private (Randomized MAC)" for smartphone/tablet identification

#### Knowledge base & scan engine
- KB persistence fixed: removed SecureFileWriter double-write that could corrupt the file
- AreUdpProbesFresh: null observations use LastSeen as proxy instead of being treated as "fresh"
- ARP table refresh post-scan (ping+TCP populates ARP cache during scan)
- Manufacturer re-resolution post-scan when MAC exists but OUI was previously unresolved
- KB backfill: null OS/hostname fields populated from prior scan observations
- IP probe order randomization (Fisher-Yates shuffle) to reduce IDS triggering

#### UX improvements
- Progress bar shows IsIndeterminate animation immediately on scan start
- ProgressPanel stays visible after scan when status message is displayed (0-hosts warning no longer vanishes)
- "No hosts responded" message with Skip Ping / gateway suggestion
- Gateway tunnel scan: batched port probes (single SSH command per host instead of per-port, ~24x faster)
- Cross-thread fix: UI checkbox state captured before ConfigureAwait(false)

#### VlanDetector
- Dynamic subnet grouping from scan profile CIDR instead of hardcoded /24
- Proper uint mask computation for edge cases (prefix ≥ 32)

#### CSV export
- 6 new columns: SNMP_ObjectID, NTLM_DNS, NTLM_Domain, NTLM_Build, SSH_HASSH, Favicon_Hash (27 total)
- SSDP column enriched with FriendlyName/Manufacturer/Model/Server

#### Tooltip enrichment
- SMB: dialect version, signing policy, server GUID, calculated uptime
- NTLM: DNS computer/domain/forest, OS build
- SSH: HASSH fingerprint
- Favicon: hash value + known device name lookup
- HTTP: detected framework + product identification
- UPnP: friendlyName, manufacturer, model, model number, serial number

## [v2026.032309] - 2026-03-23

### Split & Merge Sessions + Airspace Fix + RDP Improvements

#### Session merge (new feature)
- Right-click tab → **"Merge with..."** submenu lists all active sessions with horizontal/vertical orientation
- Merges the selected session into the current tab's split pane without reconnecting — the live connection is reparented instantly
- Unsplit restores the merged session as an independent tab
- Split palette also shows active sessions at the top for merge via keyboard (Enter)

#### Airspace fix (Command Palette over RDP/VNC)
- Command Palette converted from WPF Grid overlay to `Popup` (own HWND) — renders above WindowsFormsHost/ActiveX surfaces
- Win32 focus forced via `SetForegroundWindow`/`SetActiveWindow`/`SetFocus` P/Invoke on Popup open
- Keyboard navigation via `PreviewKeyDown` on Border parent (intercepted before TextBox consumes arrows)
- Click item resolved from `ListBoxItem.DataContext` via `PreviewMouseLeftButtonDown`

## [v2026.032304] - 2026-03-23

### Split Session Fix + RDP Improvements

#### Airspace fix (Command Palette over RDP/VNC)
- **Fix**: Command Palette (Ctrl+K) was invisible over RDP sessions due to WPF airspace issue — `WindowsFormsHost` HWND always rendered above WPF overlay content
- Replaced the `Grid` overlay with a WPF `Popup` that creates its own HWND, rendering above all Win32 surfaces
- Drop shadow and proper `PlacementTarget` for consistent positioning
- Deferred focus via `Dispatcher.BeginInvoke` (Popup content enters visual tree asynchronously)
- Dismiss on outside click via `PreviewMouseDown` on the main Window

#### Split session
- **Fix**: split session was silently failing because default RDP/SSH mode was "External" — embedded mode is now the default
- Force embedded mode for split pane connections (external mstsc.exe cannot be docked)
- Add missing VNC, FTP, Citrix protocol cases in split session switch

#### RDP ActiveX enhancements
- Auto-reconnect events (`LoginComplete`, `AutoReconnecting`, `AutoReconnected`) with bounded retry count and cancel support
- Disconnect reason decoder with localized messages (24 reason codes)
- UPN credential format support (`user@domain.com`)
- USB device redirection, bandwidth auto-detect, network connection type
- Performance flags and DisableUdp options in `.rdp` file generation
- Fix `AudioCaptureRedirectionMode` COM property type (int, not bool)
- Fix COM dispose — let AxHost handle RCW cleanup (prevents "COM object separated" errors)

#### Settings
- Default connection mode changed from "External" to "Embedded" for both RDP and SSH
- "Apply to all servers" button for bulk SSH/RDP mode switching

## [v2026.032303] - 2026-03-23

### Network Cartography — Knowledge Base + Security Hardening

#### Knowledge Base (persistent host data across scans)
- New `KnowledgeBaseManager` with per-field `Observation<T>` timestamps and source tracking
- Merge-on-scan: every scan enriches the persistent KB (`config/network-kb.json`)
- TTL-based cache acceleration: ping (4h), ports (24h), banners (7d), UDP probes (7d), certs (30d)
- `CacheHitProgress` event for real-time UI feedback during cache-accelerated scans
- KB stats in footer (host count + time-ago), Clear KB button with confirmation dialog
- Checkbox to enable/disable cache usage per scan; KB always enriched regardless
- `PurgeStaleHosts()` for automatic cleanup of old entries
- `ToScanResult()` round-trip conversion for cached data
- 28 unit tests covering merge, confidence, serialization round-trip, purge, TTL

#### Security hardening (audit-driven)
- Shell injection prevention: `IPAddress.TryParse()` + port range validation before SSH `/dev/tcp` and `host` commands (CWE-78)
- Process timeout: `WaitForExit(5000)` + `Kill()` on ARP table process (Windows + macOS)
- TLS callback documented as intentional (scanner inspecting certs, not trusting connections)
- Atomic writes: temp-file-then-rename for scan snapshots and KB persistence
- ACL enforcement: `SecureFileWriter.WriteAndProtect()` on scan history and KB files (Windows)
- Path traversal prevention: `Path.GetFileName()` + `..` rejection + `scan_` prefix whitelist in `LoadSnapshot()`
- Scan snapshot retention policy: max 20 files, oldest auto-deleted

#### Performance optimizations
- Compiled regex cache: `ServerHeaderRegex`, `TitleTagRegex`, 7 HTTP header regexes (static readonly + `RegexOptions.Compiled`)
- `RoleClassifier.CnRegex`: compiled static regex for X.500 CN extraction
- Concurrent collections: `ConcurrentBag<HostScanResult>`, `ConcurrentDictionary` for ping results (eliminates lock contention)
- Ping sweep respects `MaxConcurrency` (`Math.Min(64, profile.MaxConcurrency)`)
- `GetProbeStrategy()` called once per port (was called twice)
- Layout flush reduced from 3 to 2 in `EmbeddedRdpView.BeginConnect()`

#### RDP connection performance
- COM pre-warm: background STA thread creates/disposes throwaway `RdpActiveXHost` at app startup (~400ms saved on first connection)
- DNS pre-resolution: `Dns.GetHostEntryAsync()` fire-and-forget on server selection in tree view
- TCP keep-alive: `KeepAliveIntervalMs = 60_000` named constant via `AdvancedSettings9.KeepAliveInterval`
- Performance flags: per-server bitmask (wallpaper, themes, animations, drag, cursor shadow, composition) via `AdvancedSettings9.PerformanceFlags`
- Disable UDP: per-server TCP-only option via `TransportSettings3` (avoids UDP probe timeout behind firewalls)
- ServerDialog UI: new "Experience" expander with 7 checkboxes + bitmask recomposition on save

#### UI and i18n
- Scan error feedback: `ToolNetMapErrorScanFailed` key with error message in status bar
- 21 new i18n keys (KB UI, cache hit, RDP experience, scan errors) in EN + FR
- 7 `AutomationProperties.SetName()` on RDP experience checkboxes (accessibility)
- 13 `AutomationProperties.SetName()` on Network Cartography controls

#### Tests
- 93 new tests: KnowledgeBaseManager (28), VlanDetector (16), ScanHistoryManager (16), DrawIoExporter (10), RdpRedirectionOptions (20), CartographyEngine round-trip (3)
- Total: 1,417 xUnit tests (was 1,324)

---

## [v2026.032302] - 2026-03-23

### Local Shell Elevation — ElevationMode + AdminByRequest Compatibility

#### Elevation Mode (replaces checkbox)
- New `ElevationMode` enum: `None`, `Auto`, `Gsudo`, `Runas`
- `Auto` mode: tries gsudo with `--direct` flag first (bypasses ServiceHelper), falls back to external elevated window on failure
- `Gsudo` mode: gsudo only (embedded terminal, fails if gsudo is blocked)
- `Runas` mode: ShellExecute `runas` verb in external window (compatible with AdminByRequest, CyberArk, BeyondTrust)
- Server Dialog: checkbox replaced with "Elevation" dropdown ComboBox
- Backward compatible: existing `LocalShellElevated=true` maps to `Auto` via `EffectiveElevationMode`

#### gsudo + Endpoint Privilege Manager Fix
- Added `--direct` flag to all gsudo invocations (bypasses `ServiceHelper.StartService` crash caused by AdminByRequest invalidating process handles)
- Graceful fallback chain in `Auto` mode: gsudo `--direct` → external elevated window → clear error message
- UAC cancellation (Win32 error 1223) handled with user-friendly message
- External elevated sessions show info panel in tab ("Elevated shell launched in external window")

## [v2026.032301] - 2026-03-23

### Tools UX Harmonization & Network Cartography Remote Subnet Detection

#### Design System
- Add `PaddingButtonHelp`, `PaddingButtonCopy`, `PaddingButtonPrimary`, `PaddingButtonPreset`, `PaddingInput` tokens in CommonControls.xaml
- 181 hardcoded padding values replaced with design tokens across all 33 tool views
- All tools now use consistent tokenized spacing (global change via a single file)

#### Tool Views (33 tools) — Structural Harmonization
- Unified header Border: `Padding="12,8"`, no extra margin, across all 33 tools
- Unified title TextBlock `x:Name="HeaderTitle"` (was split between `HeaderTitle` and `TitleText`)
- Added `VerticalAlignment="Center"` on all title TextBlocks
- Apache 2.0 licence headers added to 17 XAML files that were missing them
- Copy button padding standardized to `PaddingButtonCopy` token

#### Watermark Localization (i18n)
- 24 watermark placeholder strings extracted from XAML `Tag` attributes into i18n locale files
- 17 code-behind files updated to set `Tag` via `L()` helper in `ApplyLocalization()`
- Full EN/FR translations for all watermark placeholders

#### Empty State Panels
- Added `ToolEmptyStateStyle` panels with Segoe MDL2 icons to 8 tool views: Whois, Cert Inspector, Subnet Calculator, SSH Config Generator, Service Status, Cron Job Manager, Log Viewer, Regex Tester
- Panels shown before first action, hidden when results appear

#### Accessibility (a11y)
- `AutomationProperties.LiveSetting="Polite"` added to 15 tool result areas (was 5)
- Screen readers now notified of dynamic result updates across all major tools

#### Tools Panel (Sidebar)
- Category-based fallback icons (Segoe MDL2 glyphs) when tool vector/bitmap icon is missing
- Scroll-more indicator (chevron) at bottom of panel when content overflows

#### Tab Busy Indicator
- New `IsBusy` property on `SessionTabViewModel` with pulsing accent dot in tab header
- `SetBusyAction` callback in `ToolContext` for tools to signal long-running operations
- Wired on Ping, Port Scanner, Network Cartography (pulse visible during active scans)

#### Network Cartography — Remote Subnet Auto-Detection
- Selecting an SSH gateway in "Route via" now auto-detects remote subnets
- SSH connection to gateway, runs `ip -4 addr show` (Linux), `ifconfig` (Unix/macOS), `ipconfig` (Windows)
- Parses non-loopback IPv4 CIDRs, normalizes to network addresses, pre-fills TxtSubnet
- Multiple detected subnets accessible via tooltip on the subnet field
- Localized status messages (EN/FR) during detection

## [v2026.032210] - 2026-03-22

### Comprehensive UX Audit — WCAG AA, Design Tokens, Accessibility

#### Design System (40 tokens, WCAG AA compliant)
- Add `ContentAreaMargin`, `SessionHeaderPadding`, `ToolHeaderPadding`, `ToolFooterPadding` spacing tokens
- Add `FontFamilyMonospace` token for path boxes and code editors
- Add `FocusIndicatorBrush` (cyan on dark, blue on light) for keyboard focus on all button styles
- PrimaryButton foreground changed to `TextOnAccentBrush` (white on accent surfaces)
- 19 themed control styles with complete hover/pressed/focused/disabled states
- DataGrid column header, cell, and row styles now applied globally (fixes unthemed DataGrid in tools)

#### WCAG AA Contrast Fixes
- Dark theme: AccentColor adjusted for 4.53:1 contrast with white text (was 2.41:1)
- Dark theme: TextSecondary and TextDisabled colors lightened for better readability on card surfaces
- Light theme: AccentColor darkened for stronger contrast
- Light theme: TextDisabled darkened to 4.51:1 (was 2.88:1)
- Light theme: ProtocolSsh and ProtocolSftp brushes darkened to meet AA on white backgrounds

#### Tool Views (33 tools)
- Help button ("?") added to all 21 tools that were missing it (33/33 complete)
- Help keys follow UPPERCASE convention (e.g., `ToolHelpBASE64`)
- Hardcoded `Margin="16,0,16,16"` replaced with `ContentAreaMargin` token in 6 tools
- CrontabBuilder `Foreground="Red"` replaced with `ErrorTextBrush`
- DiagramEditor header padding unified to `12,8` (was `8,6`)

#### Views and Dialogs
- Unique protocol glyphs in TreeView: Local (`E770`), Telnet (`E968`), FTP (`E896`)
- `Background="Black"` replaced with theme-aware `BackgroundBrush` in RDP and Citrix views
- Session header strips use `SessionHeaderPadding` token (RDP, SSH, VNC, Citrix, SFTP)
- `FontFamilyMonospace` token applied to SFTP, LocalFileBrowser, and Editor path boxes
- Focus vs Selected states distinguished in ListView items (`FocusIndicatorBrush`)
- Status bar height increased from 28px to 36px
- Dialog buttons: `Width` changed to `MinWidth` across all dialogs (Gateway, Project, Pin, Server, Message)
- PinDialog buttons right-aligned (was centered)
- Hardcoded placeholder text removed (code-behind i18n binding)

#### App Icon
- Rebuilt from clean ARGB source (`icon-flat.png`) with proper transparency
- No more white haze/shadow on dark taskbar backgrounds

#### Documentation
- ARCHITECTURE.md: rewritten design system section with 40 tokens, WCAG AA, help system
- README.md: updated test count, tool count, design system description, i18n key count

## [v2026.032204] - 2026-03-22

### Network Cartography — Enhanced Device Detection
- OS fingerprinting via ICMP TTL analysis (Windows/Linux/Network Equipment) and banner pattern matching (33 patterns)
- NetBIOS NBSTAT probe (UDP 137): computer name, domain/workgroup, MAC address extraction
- SNMPv2c GET probe (UDP 161): sysDescr, sysName, sysLocation with raw ASN.1/BER encoding
- mDNS/Bonjour service discovery (multicast UDP 5353): 26 service types (AirPlay, HomeKit, Chromecast, printers, etc.)
- HTTP header deep analysis: Server, X-Powered-By, WWW-Authenticate, X-Frame-Options, HSTS extraction
- HTTPS header extraction: TLS handshake + HTTP GET over SSL for HTTPS-only endpoints (443/8443/9443)
- Expanded OUI database from 101 to 300+ manufacturer prefixes (IoT, enterprise, ISP routers, industrial/SCADA, mobile, media)
- Enhanced role classification (`ClassifyEnriched`): multi-source evidence from ports + banners + OS + NetBIOS + SNMP + mDNS + HTTP headers
- 20 new banner fingerprints (Shelly, Tasmota, Jenkins, GitLab, Portainer, etc.) and 4 new role definitions (UPS, CI/CD, GitLab, Container Registry)
- Ping latency capture (was hardcoded to 0)
- New DataGrid columns: OS, Details (compact NB/SNMP/mDNS summary)
- Row tooltip with full enrichment data on hover (localized labels)
- CSV export expanded to 20 columns with localized headers
- Draw.io export enriched with OS, NetBIOS name, SNMP sysName in node labels
- History diff detects OS, NetBIOS, and manufacturer changes (typed `HostChange` model)
- Enrichment progress display in status bar during NetBIOS/SNMP phase
- Cross-platform ARP table: Windows (`arp -a`), Linux (`/proc/net/arp`), macOS (`arp -a` with regex)
- Debug logging on UDP probe failures (NetBIOS, SNMP, mDNS)
- 92 new xUnit tests covering OsFingerprinter, UdpProbeEngine (including realistic NBSTAT payloads), RoleClassifierEnriched, OuiDatabase, CartographyEngine (TLS port classification, CIDR parsing, typed diff model)

## [v2026.032203] - 2026-03-22

### UX Audit (6 passes)
- Gateway diagram: Viewbox auto-scaling prevents truncation
- ServerDialog: tabs stay visible but disabled (not hidden), with tooltip explanation
- 33 tool icons: 4 category colors + per-tool glyphs replace uniform wrench
- Ctrl+K palette: protocol icons, status dots, endpoint hints
- VNC session parity: Split, Reconnect, overlay — fully wired in EmbeddedSessionManager
- Settings bar: WrapPanel, Save button separated from secondary actions
- SFTP: bookmark overflow menu, optimized column widths
- Broadcast button: icon + localized label replaces cryptic "B"
- Session loading overlay: semi-transparent with progress bar + status
- Empty states: DNS, PortScanner, NetworkCartography show guidance before first query
- Error text wrapping on all 10 tool error TextBlocks
- Merged duplicate search fields into single sidebar filter
- Project dialog: multi-line description, inline color name label
- MessageDialog DWM dark mode, removed 6 empty ToolTip flashes
- FloatingSessionWindow: connection status displayed

### Design System
- Typography tokens: FontSizeCaption/Body/Subtitle/Title/Headline
- Spacing tokens: SpacingXs/Sm/Md/Lg/Xl
- 506 hardcoded FontSize values migrated across 45 files
- Micro-animations: FadeInPanelStyle (150ms) on 4 overlays
- DataGrid: global Ctrl+C copy via ClipboardCopyMode
- TextBox IsReadOnly: triple visual signal (background + border + opacity)

### Accessibility
- 385+ AutomationProperties.SetName via code-behind
- Keyboard focus indicators on TreeView/ListBox items
- Disabled tab tooltips, BtnGoPath/PaletteInput labels
- Toolbar tooltips with keyboard shortcuts

### Developer
- IToolView.CanClose() default interface method
- ToolContextMenuHelper: CopyAll + ExportCSV for DataGrid tools
- Build.ps1: regex fix for suffixed folders, GitHub release collision check
- CI: nuget.org source for offline-first NuGet.Config

## [v2026.032012] - 2026-03-20

### Features
- 21 built-in sysops tools as session tabs (Ping, DNS, Cert Inspector, Port Scanner, Subnet Calculator, IP Converter, Password Generator, SSH Key Generator, Hash, HMAC, Base64, URL Encoder, JWT Parser, Chmod Calculator, Crontab Builder, JSON Formatter, Regex Tester, Text Diff, DateTime Converter, UUID Generator, HTTP Status Codes)
- Tools accessible via Ctrl+K palette, "+" menu, right-click context menu, and TreeView double-click
- Enhanced Password Generator: 3 modes (Random/Syllable/Passphrase), 7 case options, 6 presets, CLI-safe mode, custom specials, exclude ambiguous, NATO phonetic, AZERTY/QWERTY layout, 5-level strength with mode-aware issues
- Wordlists expanded to 525 EN / 513 FR words with validation

### Security
- Unbiased random generation (modulo bias eliminated)
- CLI-safe fallback bypass fixed
- XXE protection on all XML importers
- Citrix command injection validation
- Password file TOCTOU eliminated

### UX
- Tool tabs integrate with TreeView (icons, double-click, edit, context menu)
- Detail panel shows "Open" for tools, hides connection info
- Copy feedback "✓" on all tool copy buttons
- Input validation with error messages on network tools
- Large payload protection (JSON/Base64 5MB, Regex 500 cap)
- AutomationProperties localized on all controls

### Architecture
- ToolContext record, CreateToolControl factory, TOOL:* ConnectionType prefix
- Tool type list shared constant, no duplication
- Preset suspension flag prevents multi-regeneration

## [v2026.032002] - 2026-03-20

### Security
- Remove password file TOCTOU fallback (fail hard if SecureFileWriter fails)
- Add Unix file mode 0600 on Plink password files
- Add XXE protection (DtdProcessing.Prohibit) on all XML importers
- Validate CitrixLaunchCommandLine against shell metacharacters
- Wrap async void event handlers with try-catch

### Performance
- Reduce Task.Wait() timeouts from 2-3s to 500ms (4-5x faster session close)
- Parallelize health monitor SSH commands via Task.WhenAll (3x faster)
- Increase health poll interval from 5s to 15s (66% less SSH traffic)
- Cache FolderViewModel.ServerCount with auto-invalidation

### Architecture
- Split ApplyLocalization() into 7 sub-methods
- Extract ImportConfigAsync() into 6 format-specific helpers
- Eliminate CloseAllSessions() code duplication
- Extract CredentialTarget record for credential resolution
- Replace all Debug.WriteLine with FileLogger (77 occurrences)
- Consolidate duplicate DefaultPorts constants
- Extract WebView2 message protocol constants
- Convert async void OpenFile() to async Task

### Tests
- Add 508 tests across 20 new test files (505 to 1013 total)
- Cover: CredentialProtector, DpapiProvider, SecureFileWriter, AclEnforcer
- Cover: RdcManImporter, MRemoteNgImporter, RdpFileImporter, SchemaValidator
- Cover: TunnelManager, RdpFileGenerator, AspectRatioManager
- Cover: LocalizationManager, FileLogger, ConnectionHistory, CommandCredentialProvider

## [v2026.032001] - 2026-03-20

### UX
- 117 fixes across 5 audit passes
- Add 47 i18n keys (2086 EN/FR in perfect parity)
- Add AutomationProperties.Name on all interactive controls (20+)
- Add keyboard focus indicators on PrimaryButtonStyle and SecondaryButtonStyle
- Add TextTrimming on all dynamic TextBlocks
- Add HorizontalScrollBarVisibility="Disabled" on form dialogs
- Localize MessageDialog, SSH status strings, filter placeholders
- Replace all Debug.WriteLine with FileLogger in App layer (31 occurrences)
- Add IsBusy on ImportConfigAsync
- Add CanExecute guards on SettingsViewModel commands
- WebView2 DefaultBackgroundColor now theme-aware

## [v2026.031917] - 2026-03-19

### Initial Release
- 8 protocol support: RDP, SSH, SFTP, VNC, Telnet, FTP, Citrix, Local Shell
- Embedded sessions via ActiveX (RDP), WebView2+xterm.js (SSH/Telnet), noVNC (VNC)
- DPAPI+HMAC credential encryption with external vault integration
- Pageant SSH agent via native Win32 IPC
- Multi-gateway SSH tunnel chaining with ref-counting
- SFTP browser with sudo elevation fallback
- Quick Connect (Ctrl+K), Network Scanner, Macro Recorder
- Dark/Light themes, bilingual EN/FR interface
- Import from MobaXterm, mRemoteNG, RDCMan, .rdp files
- Tab detach to floating windows, split pane sessions
- 505 xUnit tests
