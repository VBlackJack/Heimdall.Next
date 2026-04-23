# Audit 2026-04-22 — Remediation Notes

Source audit: `docs/audit/audit-2026-04-22.md`
Remediation dates: 2026-04-22 to 2026-04-23
Commits produced: 8 (4 Batch 1 + 4 Batch 2), all on top of `origin/master`, unpushed at time of writing.
Test baseline: 4195 passing + 6 skipped -> 4233 passing + 6 skipped (+38 tests, 0 regression).
Build: clean throughout, zero warnings (`TreatWarningsAsErrors` active).

These notes are the raw material for future CHANGELOG / README / release notes. They preserve the *why* behind each fix, the decisions deliberately *not* taken, and the follow-up backlog.

---

## Batch 1 — Performance & Security Critical Path

Four commits focused on the highest user-visible issues: memory leaks in long-running sessions, a LAN-exposed HTTP server with no auth, a UI-thread blocking call at startup, and redundant disk I/O per SSH session.

### 14c746f — perf(sessions) - detach embedded WebView handlers symmetrically (PERF-01)

**Problem**: three event-handler subscriptions in WebView2-backed views were never removed. Each reconnect, tab close, or editor request piled up handlers that stayed alive until the subscriber itself was collected. Invisible on fresh starts, noticeable after hours of use with many sessions cycling through.

**Fix**: converted anonymous lambdas to named methods (`OnWebViewNavigationStarting`) with symmetric `+=` / `-=` in matching lifecycle slots (subscribe on attach, detach in `Unloaded` / `Dispose`). Applied to `EmbeddedVncView.xaml.cs`, `EmbeddedSshView.xaml.cs`, `EmbeddedSessionManager.cs`. Bonus: paired a nested `CloseRequested` subscription that shared the same lifecycle block.

**User impact**: reduced memory pressure in long-running sessions, especially in split layouts that reconnect frequently.

**Tests**: no new tests (symmetry is enforced by static grep: every `+=` has a matching `-=` in the same file). Baseline unchanged: 4195/6.

**Files**: 3 source files.

### c948754 — fix(fileshare) - require HTTP bearer token and gate TFTP behind opt-in (SEC-09)

**Problem**: the app-level File Share feature (`FileShareService` + `EphemeralFileServer`) exposed a directory over HTTP on `http://+:{port}/` (all LAN interfaces) with **zero authentication**. Anyone on the same network could read whatever the user had shared. TFTP was started automatically alongside HTTP, with no auth primitive in the protocol itself.

**Fix**:

- **HTTP**: generated a per-server 32-byte URL-safe base64 token via `RandomNumberGenerator.Fill`, enforced on every request (Bearer header or `?token=` query), compared with `CryptographicOperations.FixedTimeEquals`. Unauthorized requests return 401 with no token hint. Token lifetime = share session lifetime; new share cycle = new token.
- **URL redaction**: new `RedactToken(url)` helper replaces `token=<value>` in any logged URL with `token=<redacted>`. Case-insensitive.
- **TFTP**: new `AppSettings.FileShareEnableTftp` setting, default **false**. When disabled, the UDP listener does not start and the `tftp` command template is not published. New UI checkbox in the share panel with warning text ("TFTP has no authentication - only enable on trusted LANs"). i18n keys added to both `en.json` and `fr.json`.
- **UI**: copy button now includes the tokenized URL; command templates (`wget`, `curl`) carry the token.

**User impact**: LAN peers must now have the tokenized URL to access shared files. TFTP users who relied on auto-start must explicitly enable it in settings once (persisted).

**Tests**: +8. `EphemeralFileServerTests` extended with token format, missing/wrong/header/query token paths. New `FileShareServiceTests` covering `BaseUrl` tokenization and TFTP gating.

**Decision deliberately NOT taken**: tightening the HTTP bind from `http://+` to a specific IP. Would require a Windows URL ACL (`netsh http add urlacl`) or admin elevation, breaking the toggle UX for non-admin users. The token is the actual security boundary; the bind scope is a UX trade-off. Documented as an inline comment on the bind line.

**Files**: 9 source files (+ 2 test files).

### dc31112 — perf(startup) - remove App startup blocking and redundant task wrapper (PERF-05, PERF-07)

**Problem**: two async defects during app startup in `App.xaml.cs`:

1. A DI factory for `NotesStorageService` called `configManager.LoadSettingsAsync().GetAwaiter().GetResult()` - blocking the UI thread on first singleton resolution. The hang was visible on cold starts with disk/network latency.
2. `await Task.Run(() => TwinShellBootstrapper.InitializeAsync(_serviceProvider))` wrapped an already-async method in a thread-pool hop, producing a fragile `Task<Task>` shape prone to silent breakage under refactors.

**Fix**:

- Pre-computed the notes storage path from already-loaded settings and cached it on the `App` instance. The DI factory now reads the cached path instead of re-loading settings synchronously.
- Dropped the `Task.Run` wrapper: direct `await TwinShellBootstrapper.InitializeAsync(_serviceProvider)`. `OnStartup` was already `async void`, so the inner async method is awaited correctly without the wrapper.
- Added `DispatcherUnhandledException` + `AppDomain.CurrentDomain.UnhandledException` handlers routing through a shared `ShowUnhandledException` helper. Startup exceptions now surface as a visible dialog instead of a silent crash.
- Extracted `PersistTrustedHostKeyAsync` to isolate an intentional fire-and-forget TOFU persistence path, replacing a prior `Task.Run(async ...)` shape.

**User impact**: noticeably faster cold start (removed sync I/O on UI thread during DI build). Startup errors are now user-visible instead of silent.

**Tests**: +4. New `AppStartupTests` covering `ResolveNotesStoragePath` (pure function) and `PersistTrustedHostKeyAsync` (non-blocking contract + exception swallowing).

**Files**: 1 source file + 1 test file. All changes scoped to `App.xaml.cs`.

### 55052d5 — perf(terminal) - cache embedded xterm assets via lazy loader (PERF-06)

**Problem**: `EmbeddedSshView.xaml.cs` read 5 terminal boot assets from disk on **every** SSH session creation - `terminal.html` (17 KB), `xterm.min.css` (3 KB), `xterm.min.js` (290 KB), `addon-fit.min.js` (2 KB), `addon-webgl.min.js` (101 KB). Total ~413 KB re-read per session, per reconnect, per split pane.

**Fix**: new `TerminalAssetsLoader` static class in `src/Heimdall.App/Services/`. Each asset exposed as a property backed by `Lazy<string>` with `LazyThreadSafetyMode.ExecutionAndPublication`. First access triggers one sync disk read; subsequent accesses return the cached string. Per-session interpolation (font, theme, `convertEol`) is unchanged - only the static pieces are cached.

**User impact**: faster second-session and split-pane creation (no disk I/O after first session). One-time cold read on first SSH use.

**Tests**: +3. `TerminalAssetsLoaderTests` covering non-empty content, same-reference semantics (caching proof), and concurrent first-access consistency.

**Files**: 1 new source file, 1 modified view, 1 new test file.

---

## Batch 2 — Structural Hygiene & UX Polish

Four commits addressing MVVM boundary violations, subprocess argument safety, UI consistency issues, and documentation drift.

### af39f2d — fix(security) - harden subprocess argument assembly for nslookup and plink (SEC-02)

**Problem**: three subprocess-invocation sites injected attacker-controlled values into command-line arguments without escaping:

1. `DnsLookupService.cs:209-211` - `nslookup` hostname and DNS server concatenated into `ProcessStartInfo.Arguments`.
2. `SshHandler.cs:215` - SSH key path wrapped in raw double-quotes without escaping embedded `"`.
3. `PlinkTunnelRunner.cs:264` - same raw-quote pattern for `-i`, `-hostkey`, `-pwfile`.

Attacker input like `example.com; calc.exe` or a key path containing `"` would inject commands/arguments.

**Fix**:

- **DnsLookupService**: migrated to `ProcessStartInfo.ArgumentList` (auto-quoted by .NET). Added `TryValidateLookupInputs` that requires `ValidateDomain` on hostname, and `ValidateDomain` OR `IPAddress.TryParse` on optional DNS server. Rejects null bytes.
- **SshHandler**: hybrid strategy. Direct `Process.Start` sites migrated to `ArgumentList` (`BuildPuttyStartInfo`, host-key probe). Pipe-mode fallback kept string-based (would have required refactoring `PipeModeSession`, out of scope) but gated through `TryValidateKeyPath` and `InputValidator.EscapeForDoubleQuotedString`. Defense-in-depth: validation amont + escape aval.
- **PlinkTunnelRunner**: migrated entirely to `ArgumentList` via new `BuildArguments` + `ValidateConnectionInputs` + `ValidateKeyPath`.

Validation rules: hostname/host via `ValidateDomain` or `IPAddress.TryParse`; keyPath must be rooted, no null bytes, no `"`, must exist; user regex; port range.

**User impact**: no user-visible change on valid inputs. Invalid/malicious inputs now surface as validation errors before subprocess launch.

**Tests**: +11. 4 for DNS (injection, null byte, garbage server, valid args), 4 for Plink (quote injection, relative path, missing file, valid args), 3 for SSH (quote injection, Putty ArgumentList, pipe-mode quoting).

**Files**: 3 source files, 3 test files (1 new).

**Follow-up backlog**: 8 other `Process.Start` sites identified as NEEDS AUDIT / UNKNOWN during diagnosis, not fixed in this commit. See "Batch 3 Backlog" below.

### 3c2c598 — refactor(mvvm) - inject UI dispatcher and decouple ARP entry presentation (CQ-I1, ARCH-I1, ARCH-I2)

**Problem**: three structural-hygiene issues in the ViewModel/Model layer:

1. `PostConnectSequenceRunner.cs:107` silently swallowed exceptions inside the per-step loop. Production failures in auto-run commands, banner display, session labelling vanished with no log trace.
2. `SessionCoordinator.cs` called `Application.Current.Dispatcher.Invoke(...)` directly at 3 sites - a ViewModel-layer class reaching into WPF statics, un-testable from non-UI threads.
3. `ArpEntry.cs` held a `System.Windows.Media.Brush` property - a presentation artifact polluting a VM/Model, breaking serialization symmetry and theme-switching correctness.

**Fix**:

- **CQ-I1**: typed `catch (Exception ex)` with `FileLogger.Error(...)` log including step name, exception type, and message. Continuation contract preserved: log + continue by default, stop only when `step.OnFailure == Stop`. Inline comment explaining the semantics.
- **ARCH-I1**: new `IUiDispatcher` abstraction in `src/Heimdall.App/Services/` with minimal surface (`Invoke(Action)`, `Invoke<T>(Func<T>)`, `InvokeAsync(Action)`, `CheckAccess()`). Implementation `WpfUiDispatcher` wraps `Application.Current.Dispatcher` - the **one** class allowed to reach into WPF statics. Registered as singleton in DI, threaded through `MainViewModel` into `SessionCoordinator`. Three direct `Application.Current.Dispatcher.Invoke` calls replaced with `InvokeOnUi(...)` / `ClearPostConnectStateOnUiThread(...)` helpers.
- **ARCH-I2**: removed `Brush` from `ArpEntry`, added `ArpEntryState` enum (`New`, `Changed`, `Gone`, `Stable`). Moved color resolution to new `ArpEntryStateToResourceKeyConverter` + `MultiBinding` in `ArpMonitorView.xaml`. Colors preserved: `New -> SuccessBrush`, `Changed -> WarningBrush`, `Gone -> ErrorBrush`, `Stable -> TextSecondaryBrush`. Theme-token-driven, not hardcoded.

**User impact**: zero user-visible change. The ARP monitor renders identically. Post-connect failures are now logged (visible in `FileLogger` output).

**Tests**: +6. `PostConnectSequenceRunnerTests` adds log-and-continue test. `SessionCoordinatorTests` (new) verifies dispatcher injection. `ArpEntryStateToResourceKeyConverterTests` (new) covers 4 enum cases. Existing `ArpMonitorViewModelTests` tightened to assert new semantic state.

**Files**: 10 source files (2 new), 4 test files (3 new or tightened).

**Follow-up backlog**: 9 other VMs/services still use `Application.Current.Dispatcher` directly (`EmbeddedSftpViewModel`, `ServerListViewModel`, `SidebarViewModel`, `ArpMonitorViewModel`, `JwtParserViewModel`, `NotesToolViewModel`, `SplitService`, `TaskSchedulerService` (2 hits), `ToolContextProvider`). One residual `Brush` in `CommandLibraryActionEntry:85`. Scope was held tight; Batch 3 migration candidate.

### 498ade1 — fix(ui) - add PortScanner stop and unify help button chrome (UX-I1, UI-I1, UI-I2, A11Y-I1)

**Problem**: four presentation-layer issues:

1. `PortScannerView` had no visible Stop control during an active scan. The Start button was repurposed instead of a dedicated cancel path.
2. Help/close-help buttons across **60 tool views** (audit estimated ~38; actual count higher) used inline `Width="24" Height="24"`, below the 32x32 baseline used elsewhere. Hard to hit on high-DPI, inconsistent feel.
3. `MainWindow.xaml:2469` hardcoded `#AA1E1F29` for the RDP drop overlay, bypassing theme tokens. Looked off under non-default Dracula variants.
4. `Mw_SettingsCmdLibSyncTokenClear` icon-only button had no `AutomationProperties.Name`. Screen readers announced it as "button" with no context.

**Fix**:

- **UX-I1**: new `BtnStop` in `PortScannerView`, visible only during `IsScanning`, wired to existing `CancelScan()` via `OnStopScanClick`. Start button hidden during scan via `RefreshUiFromVm`. No new cancellation mechanism introduced.
- **UI-I1**: centralized `ToolHelpButtonStyle` and `ToolCloseHelpButtonStyle` in `CommonControls.xaml` at 32x32. Removed inline `Width="24" Height="24"` from 60 tool views. Pattern-match sweep caught an additional `CommandLibraryView` help button that lacked `x:Name` but followed the same pattern.
- **UI-I2**: replaced hardcode with the existing `DragDropOverlayBackground` theme resource (already defined in all 7 Dracula variants - zero theme-file edits needed).
- **A11Y-I1**: `AutomationProperties.Name="{loc:Translate SettingsCmdLibSyncTokenClear}"` on the target button. Adjacent sweep added the same to the close-tunnel button with key `A11yCloseTunnel`. Both keys pre-existed in locales; zero new i18n keys.

**User impact**: Stop button now available during port scans. Help buttons feel more tangible and consistent. RDP drop overlay harmonizes with the active Dracula theme. Screen reader users get context on the settings token clear button.

**Tests**: +1. `PortScanViewModelTests.Cancel_DoesNotShowError` covers the Stop path. UI changes rely on static greps for regression protection.

**Files**: 64 files total (1 VM, 1 view+codebehind, 1 style file, ~60 tool views, 1 main window, 1 test).

**Residual (deliberately kept)**: `CommandLibraryView.xaml:273` favorites-toggle kept at 24x24 with inline comment - dense list-row context, parent row provides hit area. Documented as intentional.

### 258a476 — chore(housekeeping) - align tool docs and tighten audit cleanup (DEVOPS-I1, DOC-M1)

**Problem**: five small housekeeping items:

1. `.gitignore` didn't cover all `TestResults/` variants or coverage artifacts; potential for future accidental commits.
2. `README.md` claimed 49 built-in tools, `CLAUDE.md` claimed 58. Reality: `ToolRegistry` registers 59.
3. `CLAUDE.md` listed `QuoteProcessArgument` as an `InputValidator` member - method does not exist.
4. `RedactToken` coverage was only wired to the 401 log path in `EphemeralFileServer`. Other log sites needed verification.
5. `CommandLibraryView.xaml:273` favorites toggle 24x24 needed intentional-keep documentation.

**Fix**:

- **`.gitignore`**: added `**/TestResults/`, `**/TestResults/**`, `**/*.trx`, `**/coverage.cobertura.xml`, `**/.coverage`, `**/TestResults.*.xml`.
- **Tool count**: `README.md` and `CLAUDE.md` aligned to 59 built-in tools (source of truth: `ToolRegistry.Entry(` count).
- **`InputValidator` doc**: corrected to list actual methods (`Validate`, `ValidatePortRange`, `GetPattern`, `GetPatternNames`, `ValidateDomain`, `SanitizeCsvCell`, `EscapeShellArg`, `EscapeForDoubleQuotedString`, `IsShellTarget`, `IsValidExecutionPolicy`). Note: `CLAUDE.md` is git-ignored in this repo - correction lives in the local working copy only.
- **RedactToken coverage**: audited all log sites. `EphemeralFileServer.cs:299` already routed through RedactToken (safe). `FileShareService.cs:141` emits non-tokenized URL (safe). Helper kept in-place in `EphemeralFileServer` - no shared extraction needed until a second vulnerable site appears.
- **Bind-intent comment**: added on the `http://+` prefix in `EphemeralFileServer` explaining the deliberate wildcard (token is the security boundary; tightening would require URL ACL).
- **Favorites toggle**: inline XAML comment on `CommandLibraryView.xaml:273` documenting the intentional 24x24 in dense list-row context.

**User impact**: none (documentation and housekeeping).

**Tests**: +4. `RedactToken_ReplacesTokenValue_InQueryString`, `RedactToken_IsCaseInsensitive_OnKey`, `RedactToken_LeavesOtherQueryParams_Intact`, `RedactToken_HandlesNoQueryString`.

**Files**: 3 root files (`.gitignore`, `README.md`, `CLAUDE.md` local-only), 2 source files, 1 test file, 1 view.

---

## Aggregate Summary

- **8 commits** across 2 batches (4 Batch 1 + 4 Batch 2), chronological order, bisect-friendly.
- **Test baseline**: +38 tests (4195 -> 4233), zero regression, zero skipped delta.
- **Build**: clean throughout with `TreatWarningsAsErrors` active.
- **Findings closed**: 11 Important (PERF-01, SEC-09, PERF-05, PERF-07, PERF-06, SEC-02, CQ-I1, ARCH-I1, ARCH-I2, UX-I1, UI-I1, UI-I2, A11Y-I1) + 2 Minor (DEVOPS-I1, DOC-M1). Note: UI-I1 covers 60 tool views in one commit.
- **Deliberate non-fixes** (documented in commits/code/notes): EphemeralFileServer bind tightening (bearer token suffices, URL ACL cost unjustified); TFTP kept reachable as opt-in (auth-less by protocol); `CommandLibraryView` favorites toggle 24x24 kept (dense list context).

## Batch 3 Backlog (not addressed in 2026-04 remediation)

Tracked in session tasks but not implemented. Candidates for a future audit remediation pass:

1. **8 subprocess sites still needing shell-escape audit** (from Prompt 5 diagnosis):
   - NEEDS AUDIT: `CitrixHandler.cs`, `DnsSecurityService.cs`, `SecNumCloudAuditEngine.cs`.
   - UNKNOWN: `LocalShellHandler.cs`, `ExternalToolLaunchService.cs`, `PrivilegeLauncher.cs`, `PipeModeSession.cs`, `CommandExecutionService.cs`, `PackageManagerService.cs`, `LocalFileBrowserView.xaml.cs`.

2. **9 VMs/services still using `Application.Current.Dispatcher` directly** (from Prompt 6 diagnosis):
   - `EmbeddedSftpViewModel`, `ServerListViewModel`, `SidebarViewModel`, `ArpMonitorViewModel`, `JwtParserViewModel`, `NotesToolViewModel`, `SplitService`, `TaskSchedulerService` (2 hits), `ToolContextProvider`.
   - Infrastructure is now in place (`IUiDispatcher` + `WpfUiDispatcher`). Migration is mechanical, one file at a time.

3. **Residual `Brush` in `CommandLibraryActionEntry.cs:85`** - same pattern as ARP-I2, different VM.

4. **Remaining audit Minor findings** not addressed in this remediation (~13 items). See source audit report for the full list.

5. **Pre-release smoke not performed on**:
   - Full SSH split + disconnect/reconnect cycles (no remote SSH server during smoke).
   - WPF cold start visual verification (done by maintainer, not automated).

## How to Use These Notes

- **CHANGELOG.md / release notes**: pick sections per audience (user-facing: Batch 1 + UX-I1 + A11Y-I1; dev-facing: full remediation).
- **README.md updates**: already applied (tool count, `InputValidator` surface).
- **PR / merge description**: paste the "Aggregate Summary" block.
- **Future audit pass**: start from the "Batch 3 Backlog" section.

## Metadata

- Audit methodology: 10-category project-audit skill, 5 parallel Explore subagents.
- Remediation workflow: Pair Architect (Cowork architect + Codex 5.4 executor).
- Session record: 2 batches, 8 prompts, 2 commit-split prompts, smoke checkpoint between batches.
