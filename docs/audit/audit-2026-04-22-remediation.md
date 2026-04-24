# Audit 2026-04-22 — Remediation Notes

Source audit: `docs/audit/audit-2026-04-22.md`
Remediation dates: 2026-04-22 to 2026-04-23 (Batches 1-2), 2026-04-23 (Batches 3A, 3B, and 3C).
Commits produced: 16 (4 Batch 1 + 4 Batch 2 + 3 Batch 3A + 3 Batch 3B + 2 Batch 3C). Batches 1-2 pushed to `origin/master`; Batch 3 (8 commits) staged for a single push at end of batch with this updated documentation.
Test baseline: 4195 passing + 6 skipped -> 4301 passing + 6 skipped (+106 tests, 0 regression).
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

## Batch 3A — Subprocess Hardening (continued)

Three commits closing the 8 NEEDS-AUDIT / UNKNOWN subprocess sites flagged in Batch 2's SEC-02 follow-up. Pure security focus: argv assembly, import boundary, privileged launch channel. Diagnostic-first workflow (read-only audit prompt produced a structured FIX / SAFE / NEEDS-DESIGN matrix before any code change), 5 NEEDS-DESIGN escalations dispatched by the architect (4 trusted-by-design with documented contracts, 1 escalated to a defense-in-depth fix at the import boundary).

### 7460acd — fix(security) - harden subprocess argv assembly in DNS, package manager, file browser (Batch 3A)

**Problem**: three subprocess sites had raw `Arguments` concatenation that bypassed boundary validation when reached from non-UI paths:

1. `DnsSecurityService` (`QueryDnsLocalAsync`, `QueryDnsViaTunnelAsync`): the only domain validation lived in `DnsSecurityCheckerViewModel`. A non-UI caller could spawn `nslookup` (or compose a tunnel command) with attacker-controlled domain.
2. `PackageManagerService` (TwinShell.Infrastructure): regex strip removed shell metacharacters but allowed option-shaped tokens like `--help`, `-?`, `-Force` as standalone argv tokens — tool-level option injection on `winget`/`choco`.
3. `LocalFileBrowserView` external-editor launch: `EditorPath` came from app settings as free text. The "quote the file path once" contract was reinterpretable when `EditorPath` pointed to a shell host (cmd, powershell, wsl, .ps1, .bat, etc.).

**Fix**:

- **DnsSecurityService**: service-local pre-spawn validation (rejects empty/control-char/oversized domains). Record-type allow-list `{TXT, CAA, DNSKEY, RRSIG, MX}` enforced at the service boundary. New `CreateNslookupStartInfo` helper using `ArgumentList`. Tunnel path uses `InputValidator.EscapeShellArg` on every interpolated value, mirroring the SEC-02 pattern.
- **PackageManagerService**: rejects values whose first non-whitespace character is `-` or `/` (no strip — explicit `ArgumentException`). Argv built deterministically with `ArgumentList`. End-of-options `--` delimiter inserted before data tokens for `winget` and `choco`.
- **LocalFileBrowserView**: `InputValidator.IsShellTarget(editorPath)` rejection with new localized message key `EditorRejectedShellTarget` (added to `en.json` + `fr.json`). External-editor launch migrated to `ArgumentList`. File-association open (`UseShellExecute=true`), `rundll32` Open-As, and `explorer /select` paths intentionally left unchanged (filesystem-derived paths, narrow validation sufficient).

**User impact**: invalid/malicious inputs now fail at the service boundary before subprocess spawn. Existing valid use cases preserved — including DKIM/DMARC queries (DNS validator allows underscore-prefixed labels that `InputValidator.ValidateDomain` would reject) and the full set of DNS record types currently exercised by the app.

**Tests**: +19. `DnsSecurityServiceTests` (validation matrix, ArgumentList composition, tunnel escaping). `PackageManagerServiceTests` (leading-dash rejection, deterministic argv with `--` delimiter, both `winget` and `choco`). `LocalFileBrowserViewTests` (shell-target rejection, ArgumentList composition, regression for spaces in paths).

**Files**: 3 source files + 3 test files + 2 locale files.

**Implementation notes** (architect-validated mid-execution):

- DNS allow-list expanded from the originally-prompted `{TXT, CAA}` to `{TXT, CAA, DNSKEY, RRSIG, MX}` — the existing `DnsSecurityService` already used these types on HEAD, restricting to TXT/CAA would have broken existing behavior. The list is still bounded and explicit.
- Domain validation implemented locally in `DnsSecurityService` instead of `InputValidator.ValidateDomain` — `ValidateDomain` rejects underscore labels (RFC 5891 hostname rule), but DNS records explicitly need them (`_dmarc`, `_domainkey`, `_tlsa`, etc.). Long-term cleanup tracked as separate backlog item.

### 4437c2c — fix(security) - close Citrix import injection vector and harden -L/-S launch (Batch 3A)

**Problem**: `CitrixLaunchCommandLine` on `ServerProfileDto` (described as "pre-authenticated SelfService.exe launch arguments from cache XML") was publicly serializable. `ImportJsonAsync` in `SettingsViewModel` deserialized `ServerProfileDto` wholesale with no field stripping; `SchemaValidator.ValidateServer` was declared but never called from `ConfigManager`'s load path. A crafted `servers.json` could carry an arbitrary `CitrixLaunchCommandLine`, then `CitrixHandler.ConnectAsync` forwarded it raw to `Process.Start("SelfService.exe", attackerString)` — arbitrary args delivered to a process that itself launches more processes. Independently, the `-L/-S` StoreFront launch branch composed argv via manual quoting of `CitrixAppName` and `CitrixStoreFrontUrl`, with naive metachar strip as the only defense.

**Fix**:

- **Import boundary**: new `ImportedProfileSanitizer` in `Heimdall.Core/Configuration/` — single-purpose static helper that strips fields meant to be populated only by local scanners. Today: nulls `CitrixLaunchCommandLine` on every imported profile. Single call point in `SettingsViewModel` between the import switch and confirm-message construction covers all five importers (`.mxtsessions`/`.ini`/`.mobaconf`, `.rdp`, `.rdg`, `.xml`, `.json`).
- **`-L/-S` launch hardening**: argv composition migrated to `ArgumentList`. `Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)` + `(uri.Scheme == http || uri.Scheme == https)` validation on `StoreFrontUrl`. New localized message key `CitrixInvalidStoreFrontUrl` (added to `en.json` + `fr.json`).
- **Cache-launch trust contract documented**: code comment on the cache-launch branch explicitly names the import sanitizer as the live gate, points to the `SchemaValidator` wiring backlog item, and warns against adding a third validator with subtly different rules.

**User impact**: an imported `servers.json` can no longer carry a pre-authenticated Citrix command-line payload — the field is nulled at the import boundary, forcing a fresh local scanner run on the receiving machine. Existing scanner-populated entries continue to work unchanged. Invalid `StoreFrontUrl` values surface as a clear error instead of being passed raw.

**Tests**: +16. `CitrixHandlerTests` (URI validation matrix, argv composition, regression for quoted/spaced `CitrixAppName`). `ImportedProfileSanitizerTests` (sanitizer coverage on mixed lists, empty list, defensive null-element handling).

**Files**: 1 new (`ImportedProfileSanitizer.cs`) + 1 modified handler (`CitrixHandler.cs`) + 1 modified VM (`SettingsViewModel.cs`, 2-line change) + 2 locale files + 2 test files = 7 files.

**Decision deliberately NOT taken**: wiring `SchemaValidator.ValidateServer` into `ConfigManager.LoadServersAsync`. Requires a real architectural decision on failure-mode policy at load time (drop invalid entries, throw, warn-and-mark-disconnected) and per-field validation rules. Mixing into a security commit muddies the narrative. Tracked as backlog item.

**Iso-behavioral preservation**: the `-L/-S` argv ordering (SSO mode `[-L, -S, appName, url]`, non-SSO `[-L, appName, url]`) was preserved verbatim from the pre-commit code. The structure is internally inconsistent and may not match Citrix `storebrowse` documented CLI grammar, but a functional fix would be out of scope for an iso-behavioral security commit. Tracked as separate audit backlog item; the test asserts current behavior, not intent.

### 17ed8a0 — fix(security) - encode target argv across PrivilegeLauncher self-elevation hop (Batch 3A)

**Problem**: `PrivilegeLauncher` self-elevation channel relaunched the Heimdall binary with `--privlaunch <level> <exe> [args...]` and re-parsed the user's `[args...]` in the elevated copy via `HandlePrivilegeLaunchArgs`. Two issues:

1. Round-trip fidelity: arguments containing spaces, embedded quotes, or trailing whitespace could be re-tokenized differently by the elevated process.
2. Parsing boundary: the elevated process re-parsed an attacker-influenceable string. A latent surface — a target args value of `--privlaunch admin other.exe` could in principle be misread as a re-entry of the control flag if the parser was not strict about position.

**Fix**:

- Target launch encoded as base64-of-JSON (`{"exe": "...", "args": ["...", "..."]}`) carried via a single `--payload` token. The relaunch invocation becomes `Heimdall.exe --privlaunch <level> --payload <base64>`.
- Strict positional parsing on the elevated side: `argv[0]="--privlaunch"`, `argv[1]=<level>` (validated against the existing enum), `argv[2]="--payload"`, `argv[3]=<base64>`. Anything else → reject with no recovery, no partial-data dispatch.
- Boundary validation of decoded payload (non-empty exe, no null byte, existence check for fully-qualified paths). Decoded `args` are forwarded to `ArgumentList` verbatim.
- Test seam: `EncodeLaunchPayload`, `DecodeLaunchPayload`, `TryValidateLaunchPayload`, `ParseArguments`, `BuildSelfElevationArguments`, `BuildCommandLine` exposed as `internal` (`InternalsVisibleTo` already configured).

**User impact**: target exe + args round-trip byte-exact across the elevation hop. The Heimdall self-elevation hop's command line in process listings now shows only `--privlaunch <level> --payload <base64>` — the target exe name and clear-text args are no longer visible there (only on the final target's own command line, as expected).

**Tests**: +10. Encode/decode round-trip with quoted, spaced, leading-dash, equals-sign, and trailing-whitespace args. Validator accept/reject matrix. Boundary checks.

**Files**: 1 source file + 1 test file.

**Encoding choice**: base64 JSON (option i) over temp-file/DPAPI (option ii) — simpler, no temp-file lifecycle, byte-exact round-trip, payload safe by construction (base64 has no shell metacharacters).

**Implementation note**: `Verb="runas"` requires `UseShellExecute=true`, which on .NET means the relaunch hop consults `Arguments` (raw string), not `ArgumentList`. The relaunch composition keeps a `Arguments` string built from discrete tokens; safe because the payload token is base64.

**Discovered during smoke (not introduced by this commit)**: the `System` / `TrustedInstaller` launch path round-trips args correctly through the new `--payload` encoding, and the intermediate `--privlaunch` child runs as Administrator, but the FINAL target process spawned via `LaunchWithTokenProbe` / `CreateProcessWithTokenW` still ends up running as the original user. Pre-existing semantic bug in the token-acquisition path. Tracked as separate backlog item.

---

## Batch 3A Aggregate

- **3 commits** (`7460acd`, `4437c2c`, `17ed8a0`), bisect-friendly, narratively coherent.
- **Test baseline**: 4233 -> 4278 (+45 tests across 3A, zero regression, zero skipped delta).
- **Build**: clean (0 warning, 0 error throughout).
- **Smoke checkpoints**: 2 manual smokes (after commit B, after commit C). Commit C smoke used an end-to-end harness rather than UI to observe argv exactly received by the target — more probative than the originally-proposed UI path.
- **Original Batch 3 Backlog item 1 closure** (8 subprocess sites): 4 closed mechanically (DNS / Package / FileBrowser / Citrix `-L/-S`), 1 closed via import-boundary defense (Citrix cache-launch via `ImportedProfileSanitizer`), 1 closed via encoded-payload hop (PrivilegeLauncher), 1 confirmed SAFE on current HEAD (`SecNumCloudAuditEngine`), 4 documented as trusted-by-design (`LocalShellHandler`, `ExternalToolLaunchService` configured-tools surface, `CommandExecutionService` TwinShell shell executor, `PipeModeSession` generic transport). All NEEDS-DESIGN escalations dispatched.

---

## Batch 3B — UI Dispatcher Decoupling

Three commits migrating 8 of the 9 remaining `Application.Current.Dispatcher` couplings to the `IUiDispatcher` abstraction introduced in Batch 2 (`3c2c598`). Diagnostic-first workflow (read-only audit prompt cataloged the 10 call sites by pattern, dispatch shape, DI readiness, and migration complexity before any fix prompt) — same model as Batch 3A. SplitService intentionally deferred (see "Deliberate non-fix" below).

### 25a124b — refactor(mvvm) - migrate ServerList, Sidebar, NotesTool, ToolContext to IUiDispatcher (Batch 3B)

**Problem**: four sites flagged TRIVIAL by the diagnostic — three VMs and one service, each calling `Application.Current?.Dispatcher` directly with mixed null-safe / CheckAccess patterns:

1. `ServerListViewModel.OnConnectionStateChanged` (line 1702): `BeginInvoke`, always queued.
2. `SidebarViewModel.OnFavoritesChanged` (line 261): `CheckAccess` + fallback.
3. `NotesToolViewModel.RunOnUiAsync` (line 711): `CheckAccess` + fallback.
4. `ToolContextProvider.OnLocaleChanged` (line 110): `CheckAccess` + fallback.

All four reachable from non-UI threads (event handlers, locale-change callbacks). The null-safe `?.` access silently no-ops in test environments, masking dispatcher-routing assumptions.

**Fix**:

- Constructor injection of `IUiDispatcher` into all four types. `SidebarViewModel` is constructor-threaded from `MainViewModel` (which already holds a dispatcher field at line 53). `ToolContextProvider` is a singleton DI service — automatic resolution. The other two are DI-resolved.
- Direct dispatcher calls replaced with abstraction calls. Three sites preserve the `CheckAccess` fast-path (locale / favorites callbacks where re-entry into binding updates is acceptable). `ServerListViewModel` deliberately does NOT add a fast-path — the always-queued semantics protect against re-entry into selection / binding update paths. Documented inline.
- Shared `FakeUiDispatcher` extracted to `tests/Heimdall.App.Tests/TestHelpers/` (was previously inline in `SessionCoordinatorTests`). Now consumed by 5 test sites.

**User impact**: zero user-visible change. All four sites continue to dispatch to UI thread; threading semantics preserved exactly.

**Tests**: +7. `ServerListViewModel` direct dispatch test (1). `SidebarViewModelTests` (new file, fast-path + queued-path = 2). `NotesToolViewModelTests` extended (fast-path + queued-path = 2). `ToolContextProviderTests` (new file, fast-path + queued-path = 2). All existing INDIRECT tests for `ServerListViewModel` updated to inject `FakeUiDispatcher`.

**Files**: 13 files (4 source + `MainViewModel` thread + 1 shared test helper + 4 test files updated/created + glue).

**Implementation note**: optional `CheckAccess + InvokeAsync` helper extension on `IUiDispatcher` deliberately NOT added. Each site has minor body differences; a helper would have introduced noise without removing meaningful duplication.

### c99e80f — refactor(mvvm) - migrate EmbeddedSftp, ArpMonitor, JwtParser VMs to IUiDispatcher (Batch 3B)

**Problem**: three VMs flagged MODERATE by the diagnostic — each constructed by hand in their hosting view's code-behind (not via DI), each carrying a cached `Dispatcher` field or a static helper that fell back to `Dispatcher.CurrentDispatcher`:

1. `EmbeddedSftpViewModel` (constructor line 52, `RunOnUiAsync` line 1013): cached `Dispatcher` field, fallback to `Dispatcher.CurrentDispatcher` when constructed without explicit dispatcher.
2. `ArpMonitorViewModel.RunOnUiAsync` (line 333): static helper with `CheckAccess` + fallback, used by polling loop and refresh / error paths.
3. `JwtParserViewModel.OnLocaleChanged` (line 266): `CheckAccess` + fallback.

**Fix**:

- Constructor injection of `IUiDispatcher` into all three VMs. The `Dispatcher.CurrentDispatcher` fallback removed entirely (would have masked test-environment bugs).
- `ArpMonitorViewModel`: static helper converted to instance method (helper is private to the VM, dispatcher kept local; rationale: avoids threading another parameter through every call site).
- View construction sites updated to resolve `IUiDispatcher` from DI and pass it to the new VM constructors: `EmbeddedSftpView.xaml.cs`, `ArpMonitorView.xaml.cs`, `JwtParserView.xaml.cs`.
- All existing direct tests for these VMs updated to inject `FakeUiDispatcher`.

**User impact**: zero user-visible change. SFTP, ARP polling, and JWT locale-switch behavior preserved (smoke validated against live Rebex SFTP demo server, real WPF-hosted ARP polling cycle, and existing pilot test for JWT locale switch).

**Tests**: +5. `EmbeddedSftpViewModelTests` (new file, off-thread post + null-arg = 2). `ArpMonitorViewModelTests` extended (off-thread post = 1). `JwtParserViewModelTests` extended (fast-path + queued-path = 2).

**Files**: 9 files (3 VMs + 3 views + 3 test files).

### 6d6adcb — refactor(mvvm) - add IUiDispatcher Func<Task> overload and migrate TaskSchedulerService (Batch 3B)

**Problem**: `TaskSchedulerService.OnTickAsync` (lines 127-145 and 156-164) used `Application.Current.Dispatcher.InvokeAsync(async () => { ... })` twice. The existing `IUiDispatcher` abstraction exposed only `InvokeAsync(Action)`, which cannot model "post async lambda to UI thread and await its full inner Task completion" — a naive `Action` wrapper would either fire-and-forget the inner Task (breaking persistence ordering) or require a TCS dance at the call site (uglier than the original, harder to test). The tick-guard contract depends on the inner work fully completing before the guard releases.

**Fix**:

- New `IUiDispatcher.InvokeAsync(Func<Task>)` overload: returns a `Task` that completes when the inner `Func<Task>`'s `Task` fully completes (not when dispatch hands off). Action overload semantics unchanged. No `Func<Task<T>>` generic variant (no current consumer needs return values).
- `WpfUiDispatcher` implementation: forwards to the WPF Application dispatcher's `InvokeAsync(Func<Task>)`, awaits the `DispatcherOperation<Task>`, then awaits the inner `Task` to ensure full inner completion. (The `CurrentDispatcher` symbol used in this implementation is a private property on `WpfUiDispatcher` that resolves to `Application.Current?.Dispatcher` — naming overlaps visually with `System.Windows.Threading.Dispatcher.CurrentDispatcher` but is unrelated; the existing Action overload uses the same pattern from `3c2c598`.)
- `FakeUiDispatcher` (shared test helper): new overload with separate call counter (`InvokeAsyncFuncCallCount`) and optional inner-completion wrapper for tick-guard regression tests.
- `TaskSchedulerService`: constructor injection of `IUiDispatcher` (threaded from `MainViewModel` line 332 → `ScheduledTasksViewModel` line 73 → `TaskSchedulerService`). Both `OnTickAsync` dispatch sites migrated to `await _uiDispatcher.InvokeAsync(async () => { ... })`. Inline comment on each site documents the tick-guard contract.

**User impact**: zero user-visible change. Scheduled tasks tick, fire callbacks on the UI thread, and persist exactly as before. End-to-end smoke validated tick + persistence (`LastWriteTimeUtc` on `settings.json` advances after each tick, `LastRun` / `NextRun` updated correctly) and re-entrance protection (a deliberately-blocked first tick prevented a parallel second tick from re-entering until release; counters: `taskDueCount=3`, `persistCount=3`, `InvokeAsync(Func<Task>)=6`).

**Tests**: +3. `TaskSchedulerServiceTests` (new file): `FakeUiDispatcher` overload counter + inner-completion behavior (1), routing through `OnTickAsync` (1), tick-guard regression (1 — blocks inner Task via TCS, asserts second tick is held until release, then asserts persistence runs after release).

**Files**: 7 files (`IUiDispatcher`, `WpfUiDispatcher`, `TaskSchedulerService`, `MainViewModel` thread, `ScheduledTasksViewModel` thread, `FakeUiDispatcher` extension, new test file).

**Decision deliberately NOT taken**: WpfUiDispatcher real-WPF-context test for the new overload skipped (no `Application` bootstrap in `Heimdall.App.Tests` — `FakeUiDispatcher` + tick-guard regression test cover the critical semantics; real-dispatcher path validated by smoke).

---

## Batch 3B Aggregate

- **3 commits** (`25a124b`, `c99e80f`, `6d6adcb`), bisect-friendly. The interface-extension commit (`6d6adcb`) bundles the `Func<Task>` overload addition with its only immediate consumer (`TaskSchedulerService`) — no orphan interface change in history.
- **Test baseline**: 4278 -> 4293 (+15 tests across 3B, 0 regression, 0 skipped delta).
- **Build**: clean throughout (0 warning, 0 error).
- **Smoke checkpoints**: 2 manual smokes (after 3B-B with live SFTP / ARP / JWT exercise via Rebex demo server + real WPF host; after 3B-C with end-to-end scheduler harness validating tick + persistence + re-entrance protection).
- **Original Batch 3 Backlog item 2 closure**: 8 of 9 sites migrated to `IUiDispatcher`. `SplitService` intentionally deferred — see "Deliberate non-fix" below.

**Deliberate non-fix — SplitService**:

`SplitService.SwapSplitPanesAsync` (line 594) and `AwaitVisualTreeAsync` (line 804) depend on specific `DispatcherPriority` semantics (`Loaded` then `ContextIdle`) to stabilize WebView2 / ActiveX reparenting during split-pane operations. The `IUiDispatcher` abstraction does not model `DispatcherPriority`; a mechanical migration would either lose the priority semantics (risking a regression of the WebView2/ActiveX reparenting races stabilized in earlier work) or require extending the abstraction with priority/yield surface that does not belong on a clean UI-marshalling interface. Tracked as backlog item — needs a parallel `IUiYieldScheduler` (or equivalent) abstraction design pass.

---

## Batch 3C — Final Cleanup

Two commits closing the residual Batch 3 backlog: a targeted MVVM refactor mirroring ARCH-I2, and a cleanup bundle for 6 Minor findings. Final batch of the audit remediation workstream.

### d0f7346 — refactor(mvvm) - eliminate residual Brush in CommandLibraryActionEntry (ARCH-I2 residual)

**Problem**: `CommandLibraryActionEntry.RiskBrush` (line 85) — same exact pattern ARCH-I2 fixed for `ArpEntry` in `3c2c598`, but in a different VM that the original audit had spotted as a residual. Three issues: WPF `Brush` type pollution in the VM, hardcoded theme-blind fallback colors (Gray, Orange, Red), Brush resolved at evaluation time so theme switches did not refresh bound visuals.

**Fix**: removed `RiskBrush` and the now-unused `ResolveBrush` helper. Added `RiskBrushKey` string property that returns the resource key name for the current `CriticalityLevel`. `CommandLibraryView.xaml` updated to bind `RiskBrushKey` through `ResourceKeyToBrushConverter` via MultiBinding with `DataContext.ThemeRevision` — mirrors `ArpMonitorView.xaml:195` exactly. Live theme-switch correctness restored.

Simpler than ARCH-I2: no new enum needed (`CriticalityLevel` already exists), no new converter created (existing `ResourceKeyToBrushConverter` reused).

**User impact**: risk badges in the Command Library tool now refresh colors immediately on theme switch, rather than staying frozen until the entry is rebuilt.

**Tests**: +4. `CommandLibraryActionEntryTests` covers the `CriticalityLevel → RiskBrushKey` mapping for all four cases (Info, Run, Dangerous, default).

**Files**: 3 (1 VM + 1 view + 1 test file).

**Adjacent surface verified clean**: `FavoritesFilterBrushKey` in `CommandLibraryViewModel` is already key-based; no other Brush properties spotted in the Command Library surface.

### a07f5147 — chore(cleanup) - close residual Minor findings (SEC-M4, DEVOPS-M1/M2, A11Y-05, UX-04, UI-04)

**Problem**: 6 small Minor findings clustered in one cleanup commit. Listed in priority order — SEC-M4 first because it touches a security surface, even though the actual fix size is comparable to the others.

**Fix per item**:

- **SEC-M4** (`PlinkTunnelRunner.cs:150`): Plink stderr lines were logged verbatim. Plink stderr can include SSH banner content sent by the remote server BEFORE authentication completes — i.e., attacker-controlled bytes if connecting to a malicious server. Risk: log injection (control characters / ANSI escapes disrupt log file structure when reviewing logs), large binary content polluting logs. Added `SanitizeForLog(line)` helper: strips control chars (replaces with `?`, preserves tabs), caps length at 256 chars (truncates with ` [...]`). Log entry now tagged `untrusted` to make the source explicit.
- **DEVOPS-M1** (`.gitignore`): removed duplicate `CLAUDE.md` entry. Kept the entry in the Claude Code section with a one-line comment explaining why it is gitignored (local-only file per project convention).
- **DEVOPS-M2** (`Build.ps1:272`): silent skip of installer creation when `ISCC.exe` is not found, replaced with a 3-line prominent warning block. Bonus: added `HEIMDALL_ISCC_PATH` env var override (the architect-marked-optional addition; Codex implemented it). Existing fallback to the hardcoded path is preserved.
- **A11Y-05** (`PortScannerView.xaml`): stats bar previously relied on `SuccessTextBrush` / `ErrorTextBrush` to differentiate counts (color-only encoding, marginal under extreme zoom or for users with color vision deficiency). Now renders self-contained textual stats as `Open: N`, `Closed: N`, `Total: N`. Color brushes preserved as a secondary visual cue.
- **UX-04**: investigated and found already-resolved on current HEAD. The audit's "icon `?` glyph vs Segoe MDL2 inconsistency in tool headers" was closed by `498ade1` (UI-I1), which centralized `ToolHelpButtonStyle` across 60 tool views. No code change needed in this commit.
- **UI-04** (`SecNumCloudAuditView.xaml`): the audit flagged ≥11 ad-hoc margins. Investigation found that none of the values map cleanly onto the existing `SpacingXS / S / M / L / RowGap` token set — they are asymmetric values driven by visual alignment with neighboring elements. Resolution: 5 margin clusters annotated with explanatory XAML comments documenting why the values are non-token; 0 actual token replacements. Partial close — the deeper issue (spacing token system lacks granularity for this view) is tracked as a new backlog item.

**User impact**: Plink stderr no longer pollutes logs with raw banner content. PortScanner stats are usable without color discrimination. Missing-installer build failure is no longer silent. No user-visible change for the others.

**Tests**: +4. `PlinkTunnelRunnerTests` covers `SanitizeForLog` (control char strip, length cap, tab preservation, null/empty handling).

**Files**: 7 (`.gitignore`, `Build.ps1`, `PortScannerView.xaml` + code-behind, `SecNumCloudAuditView.xaml`, `PlinkTunnelRunner.cs`, new test file).

**Out of scope (flagged, not fixed)**:
- `ReadStderrSafeAsync` in `PlinkTunnelRunner.cs:443` is dead code (no callers anywhere in the solution). Not part of SEC-M4. Worth a future single-commit removal.
- Ad-hoc margins in views OTHER than `SecNumCloudAuditView` — out of scope, not assessed.

---

## Batch 3C Aggregate

- **2 commits** (`d0f7346`, `a07f5147`), bisect-friendly.
- **Test baseline**: 4293 -> 4301 (+8 tests across 3C, 0 regression, 0 skipped delta).
- **Build**: clean throughout.
- **Smoke checkpoints**: final consolidated smoke after 3C-B covering the 3C surface (Command Library live theme switch with risk-badge color refresh, PortScanner textual stats prefixes, SecNumCloudAuditView visual sanity) plus a cross-batch sweep (cold start, multi-tool open/close, live SFTP connect via Rebex demo server).
- **Original Batch 3 Backlog item 3 closure**: residual Brush in `CommandLibraryActionEntry` migrated to key-based binding.
- **Original Batch 3 Backlog item 4 closure**: 6 Minor findings closed (5 implemented, 1 found already-resolved by UI-I1's `ToolHelpButtonStyle` centralization).

---

## Aggregate Summary

- **16 commits** across 5 sub-batches (4 Batch 1 + 4 Batch 2 + 3 Batch 3A + 3 Batch 3B + 2 Batch 3C), chronological order, bisect-friendly.
- **Test baseline**: +106 tests (4195 -> 4301), zero regression, zero skipped delta.
- **Build**: clean throughout with `TreatWarningsAsErrors` active.
- **Findings closed**:
  - **Batches 1-2**: 11 Important (PERF-01, SEC-09, PERF-05, PERF-07, PERF-06, SEC-02, CQ-I1, ARCH-I1, ARCH-I2, UX-I1, UI-I1, UI-I2, A11Y-I1) + 2 Minor (DEVOPS-I1, DOC-M1). Note: UI-I1 covers 60 tool views in one commit.
  - **Batch 3A**: original "8 subprocess sites" Batch 3 backlog item + related Minors SEC-M1 (PrivilegeLauncher args) and SEC-M3 (LocalFileBrowserView quoting).
  - **Batch 3B**: 8 of 9 sites in original Batch 3 backlog item 2 (`Application.Current.Dispatcher` migrations). SplitService deferred with documented rationale.
  - **Batch 3C**: residual Brush in `CommandLibraryActionEntry` (ARCH-I2 residual) + 6 Minor findings (SEC-M4, DEVOPS-M1, DEVOPS-M2, A11Y-05, UX-04 found already-closed by UI-I1, UI-04 partially closed via annotations).
- **Deliberate non-fixes** (documented in commits/code/notes): EphemeralFileServer bind tightening (bearer token suffices, URL ACL cost unjustified); TFTP kept reachable as opt-in (auth-less by protocol); `CommandLibraryView` favorites toggle 24x24 kept (dense list context); `SchemaValidator.ValidateServer` not wired into `ConfigManager` load path during 3A (architectural decision deferred); `SplitService` not migrated to `IUiDispatcher` during 3B (relies on `DispatcherPriority` semantics that the abstraction does not model); `SecNumCloudAuditView` ad-hoc margins kept with annotations rather than token-replaced (token system lacks the granularity).

## Batch 3 Backlog (status after Batch 3 closure)

1. **8 subprocess sites still needing shell-escape audit** — **CLOSED by Batch 3A** (3 commits).

2. **9 VMs/services still using `Application.Current.Dispatcher` directly** — **CLOSED (8 of 9) by Batch 3B** (3 commits). `SplitService` deferred with documented rationale (relies on `DispatcherPriority` semantics; needs parallel design pass). Tracked as new backlog item below.

3. **Residual `Brush` in `CommandLibraryActionEntry.cs:85`** — **CLOSED by Batch 3C** (commit `d0f7346`).

4. **Remaining audit Minor findings** — partially addressed:
   - **CLOSED by Batch 3C** (commit `a07f5147`): SEC-M4, DEVOPS-M1, DEVOPS-M2, A11Y-05, UI-04 (partial — annotations rather than token replacement, full close tracked as new backlog).
   - **Found already-closed during 3C**: UX-04 (resolved earlier by UI-I1 in `498ade1`).
   - **Still pending** (out of Batch 3 scope, not closed): refactor-class Minors `CQ-03`, `CQ-04`, `ARCH-08`, `ARCH-09` (all `SecNumCloudAuditEngine` extraction — separate effort), `SEC-M2` (`WebSocketVncProxy` accepts any local WS after Origin), `SEC-M5` (PIN max length + filesystem-resettable lockout), `ARCH-04` (intentional factory contract), `ARCH-10` (TwinShell.* missing Apache 2.0 headers — licence decision), `DEVOPS-M3` (CI coverage threshold gate — policy decision), `DOC-02` (sparse XML doc on internal helpers — style decision).

5. **Pre-release smoke not performed on**:
   - Full SSH split + disconnect/reconnect cycles (no remote SSH server during smoke).
   - WPF cold start visual verification (done by maintainer, not automated; covered by `ShellLaunchTests` in 3C smoke).
   - Real Citrix `storebrowse` / `SelfService.exe` end-to-end connect (no Citrix Workspace installed in smoke environment — covered by unit tests for argv composition + URI validation; functional ordering question tracked in "New Backlog discovered during Batch 3A" below).
   - Real Plink stderr exposure to a server emitting binary banners (covered by `SanitizeForLog` unit tests; behavior under live untrusted server not exercised).

## New Backlog (discovered during Batch 3)

Items surfaced during sub-batch diagnostics, smokes, or scope-creep avoidance. All deliberately deferred past Batch 3.

1. **Wire `SchemaValidator.ValidateServer` into `ConfigManager` load path** — currently declared but never called. Requires architectural decision on failure-mode policy at load time (drop invalid entries / throw / warn-and-mark-disconnected). Once wired, add per-field rules (e.g., `CitrixLaunchCommandLine` control-char + length cap) as defense-in-depth for manual `servers.json` edits.

2. **External detected tools structured-argv refactor** — Sysinternals / NirSoft / NanaRun providers ship static raw-string templates. `ExternalToolLaunchService.LaunchDetected` launches with raw `Arguments`. Placeholder values are sanitized but option-injection at the tool level remains possible. Provider-abstraction refactor.

3. **`InputValidator.ValidateDnsName` variant for underscore-prefixed labels** — `ValidateDomain` rejects underscore labels (RFC 5891 hostname rule). `DnsSecurityService` had to implement local validation to support `_dmarc`, `_domainkey`, `_tlsa`, etc. Add `ValidateDnsName` to `InputValidator`, then refactor `DnsSecurityService` to use it. Avoids two parallel domain validators.

4. **Audit `CitrixHandler` `.ica` direct-open branch** — uses `Process.Start` with `UseShellExecute=true` on `CitrixIcaFilePath`. Path string can carry URL semantics under ShellExecute. Field is not stripped by `ImportedProfileSanitizer` today. Decide whether to strip on import and / or validate as a local existing `.ica` file before launch.

5. **Audit Citrix `storebrowse` argv ordering vs official documentation** — Batch 3A preserved the legacy argv layout verbatim (`-L -S "appName" "url"` in SSO mode, `-L "appName" "url"` in non-SSO). Structure is internally inconsistent and may not match the documented CLI grammar. Test currently asserts current behavior, not intent — add behavior-snapshot comment when fixing.

6. **Investigate flaky `DateTimeConverterSmokeTests` and `NetworkCartographyViewModelTests`** — both flaked on intermediate runs during Batch 3A commit C verification, passed on rerun. Likely WPF/UI environmental flakiness (timing, dispatcher, async setup). Worth stabilizing before 3B starts changing dispatcher behavior.

7. **Fix end-to-end semantics of `PrivilegeLauncher` `System` / `TrustedInstaller` launch** — pre-existing bug surfaced during 3A commit C smoke. Args round-trip correctly through the new `--payload` encoding, intermediate `--privlaunch` child runs as Administrator, but the final target spawned via `LaunchWithTokenProbe` / `CreateProcessWithTokenW` still ends up running as the original user. Risk: user believes target is running with elevated SYSTEM context and may make security decisions on that assumption. Investigate token acquisition path (`WTSQueryUserToken`, `SeIncreaseQuotaPrivilege`, `SeAssignPrimaryTokenPrivilege`).

8. **Design priority-aware UI dispatch abstraction for `SplitService` reparenting** — discovered during 3B diagnostic. `SplitService.SwapSplitPanesAsync` and `AwaitVisualTreeAsync` rely on specific `DispatcherPriority` semantics (`Loaded` then `ContextIdle`) for WebView2 / ActiveX reparenting stability. The clean `IUiDispatcher` abstraction does not (and should not) model `DispatcherPriority`. Either introduce a parallel `IUiYieldScheduler` interface that exposes priority-aware yields, or accept that `SplitService` is a deliberate exception that keeps its direct WPF dispatcher coupling. Decide before any future split / reparenting refactor.

9. **Stabilize 4 known flaky tests** — accumulated across Batch 3 verification runs:
   - `DateTimeConverterSmokeTests` (UI test project, surfaced during 3A commit C)
   - `NetworkCartographyViewModelTests` (surfaced during 3A commit C)
   - `PortScanViewModelTests.Scan_CompletesSuccessfully_PopulatesResults` (surfaced during 3B-A)
   - `ShellLaunchTests.MainWindow_Launches_AndTitleContainsHeimdall` (surfaced during 3B-A; passed reliably during 3C-A and 3C-B verifications, used as cold-start sanity check in final smoke)
   All four passed on rerun and on final full runs throughout Batch 3, so they did not block any sub-batch — but they are signal of WPF / async / dispatcher timing fragility that will become noisier as more dispatcher-touching code lands. Investigate root cause (likely environmental: WPF Application bootstrap timing, dispatcher idle, async setup races) and stabilize. Should be addressed before the next major UI-touching feature batch.

10. **Spacing token system granularity for asymmetric layouts** — discovered during 3C-B UI-04 cleanup. `SecNumCloudAuditView.xaml` had ≥11 ad-hoc margins; on inspection none mapped cleanly onto the existing `SpacingXS / S / M / L / RowGap` tokens. Codex annotated 5 clusters with explanatory XAML comments rather than invent new tokens for one view. Either (a) expand the spacing token system to cover asymmetric / visual-alignment use cases (risk: token sprawl, harder to keep consistent), or (b) accept asymmetric per-view margins as legitimate exceptions and codify the annotation pattern. Decide before the next significant view-building effort.

11. **Remove dead code: `ReadStderrSafeAsync` in `PlinkTunnelRunner.cs:443`** — surfaced during 3C-B SEC-M4 work. The method is defined but has zero callers in the entire solution. Single-commit removal candidate.

12. **End-of-Batch-3 push** — Batch 3 produced 8 commits (`7460acd` through `a07f5147`) that are staged for a single push at end of batch (per architect-validated push strategy "c"). All 8 commits sit on top of `origin/master` with no rebase or fixup needed. Push at the same time as this updated `audit-2026-04-22-remediation.md`.

## How to Use These Notes

- **CHANGELOG.md / release notes**: pick sections per audience (user-facing: Batch 1 + UX-I1 + A11Y-I1; dev-facing: full remediation).
- **README.md updates**: already applied (tool count, `InputValidator` surface).
- **PR / merge description**: paste the "Aggregate Summary" block.
- **Future audit pass**: start from the "Batch 3 Backlog" section.

## Metadata

- Audit methodology: 10-category project-audit skill, 5 parallel Explore subagents.
- Remediation workflow: Pair Architect (Cowork architect + Codex 5.4 executor).
- Session record: 2 batches, 8 prompts, 2 commit-split prompts, smoke checkpoint between batches.
