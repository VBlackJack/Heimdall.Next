<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Changelog

All notable changes to Heimdall.Next are documented in this file.

## 2026-05-01 — RDP resolution, DPI, fullscreen, and lifecycle hardening

Two-phase RDP pass covering DPI correctness, per-server resolution profiles,
ActiveX lifecycle cleanup, and fullscreen usability.

### Phase 1 — RDP DPI plumbing

- Injects `DesktopScaleFactor` and `DeviceScaleFactor` before `Connect()` via
  direct QI on `IMsRdpExtendedSettings` (`ocx as IMsRdpExtendedSettings`) with
  an explicit `Marshal.QueryInterface` fallback. The dynamic
  `ax.ExtendedSettings` IDispatch path and `IServiceProvider.QueryService`
  path were both proven unreliable on real `MsTscAx.MsTscAx.10` installs.
- Tracks monitor DPI changes via `Window.DpiChanged` and reuses the guarded
  `UpdateSessionDisplaySettings` path for live display updates.
- Snaps RDP widths to a multiple of 4 before display updates.
- Adds the session-tab context-menu Resolution submenu with standard presets,
  Match Window, Custom, and Save as default for this server.
- Removes the previous global forced `SmartSizing = true`; current default
  behavior is preserved through explicit initialization instead.

### Phase 2 — Resolution profiles, fullscreen UX, lifecycle hardening

- Adds per-server `RdpResolutionMode` schema (`FitWindow`, `Fixed`,
  `SmartSizing`, `Multimon`) with migration from legacy
  `RdpFixedResolutionWidth` / `RdpFixedResolutionHeight` and
  `RdpMultiMonitor` fields. Legacy JSON property names remain readable.
- Adds the ServerDialog "Resolution profile" section with mode-specific field
  visibility, validation ranges, snap-to-4 acceptance for fixed widths, and
  EN/FR localization parity.
- Adds centered letterbox sizing for `Fixed + SmartSizing=off`, positioning the
  `WindowsFormsHost` with explicit `Margin` / `Width` / `Height` inside a
  themed host surface instead of relying on WPF transforms.
- Migrates `UseMultimon` from the fragile `AdvancedSettings9` path to the
  documented `IMsRdpClientNonScriptable5` QI path.
- Harmonizes RDP disconnect teardown across tab close, toolbar disconnect,
  context-menu disconnect, and reconnect/failed-session cleanup through
  `RdpDisconnectTeardownSequence`.
- Improves fullscreen UX with a themed auto-hiding exit chip, top-edge reveal,
  universal F11 toggle, Esc exit, Ctrl+Shift+F11 toggle, and layered keyboard
  routing (`PreviewKeyDown`, `ThreadPreprocessMessage`, low-level
  `WH_KEYBOARD_LL` hook, foreground-process filter).

Test baseline after this pass: **5,030 passing + 6 skipped**, zero warnings.

## 2026-04-25 — SSH audit follow-up (Pageant DACL, known_hosts DoS caps, lifecycle)

Four-commit hardening pass on the SSH/SFTP/Tunnel surface following a
multi-pass audit. Previous-pass findings vetted, two false positives dropped,
one self-introduced regression caught and fixed in the same pass.

- **Pageant IPC** — `PageantClient.SendMessage` now creates the shared file
  mapping with a self-only DACL (`D:P(A;;FA;;;<currentUserSid>)`) and a
  cryptographically random suffix in the mapping name (64 bits of entropy via
  `RandomNumberGenerator.GetHexString(16)`). The new
  `SecurityAttributesScope` allocates `SECURITY_ATTRIBUTES` and the security
  descriptor under a try/catch that releases both pointers on any failure
  between alloc and ownership transfer.
- **known_hosts parsing** — `KnownHostsParser` enforces a per-line cap of
  64 KB and exposes a streaming `TextReader` overload; `KnownHostsImporter`
  refuses files larger than 50 MB, streams via `StreamReader`, and degrades
  to an empty report (with `FileLogger.Warn`) on I/O / decoding failures
  instead of bubbling exceptions to the UI.
- **Constant-time fingerprint compare** — `HostKeyStore.Verify` now uses
  `CryptographicOperations.FixedTimeEquals` after a length-equality guard
  (safe because OpenSSH host-key fingerprints are fixed-length).
- **Plink stderr redaction and lifecycle** — `PlinkTunnelRunner.SanitizeForLog`
  redacts password / passphrase / token / bearer assignments and `-pw` /
  `-pwfile` flags via compiled regexes; `Stop()` cancels and joins the stderr
  drain task (with a 500 ms timeout) before killing the process, so the
  background reader cannot outlive the pipe.
- **SSH agent and shell** — `SshShellSession` links the read-loop CTS to the
  caller's cancellation token (and now throws ahead of the link if the
  caller-supplied token is already cancelled). `OpenSshPipeAgent.SendRequest`
  is rebuilt on `PipeOptions.Asynchronous` + `ReadExactlyAsync` with a
  linked timeout token, replacing the best-effort `ReadTimeout` that
  `NamedPipeClientStream` silently ignores in some modes.
- **Tunnel allocation** — `TunnelManager.AllocatePort` distinguishes
  `AddressAlreadyInUse` from other socket failures and logs the fallback
  to ephemeral.
- **SFTP** — `SftpBrowser.DeleteDirectoryRecursive` is now an iterative
  post-order traversal capped at 256 levels, eliminating the stack-overflow
  risk on hostile remote filesystems. `RemoteFileEditor.AutoUploadAsync`
  re-throws `HostKeyRejectedException` instead of folding it into a generic
  upload failure, surfacing host-key changes to the UI as a security event.
  `RemoteFileEditor.LaunchEditor` now uses `ProcessStartInfo.ArgumentList`
  for the local path.
- **ServerHealthMonitor** — Start/Stop/Stop sequencing is serialized via an
  internal `Lock` and the cts/poll-task pair is snapshotted under the lock
  before any blocking await, so an immediately-following `StartAsync` sees a
  clean state.
- **OpenSshConfigImporter** — `MakeUniqueName` caps its suffix loop at 1000
  and falls back to a guid-tagged name beyond that.
- **AuthPreflightChecker** — emits an aggregated warning when every
  configured agent failed to enumerate identities, distinguishing
  "no agent has keys" from "every agent crashed".
- **FtpBrowser** — warns at connect time when TLS is disabled (credentials
  in clear text) and rejects LIST entries whose filename exceeds 4 KB.
- **i18n / configuration** — new `PlinkTunnelRunnerOptions` record (timings as
  named values rather than positional ints) and `SshLocalizationKeys` const
  class consumed by `SshHandler` + `TunnelService`, so a typo in an i18n key
  fails to compile rather than silently surfacing the literal key in the UI.

Test count after this pass: **421 SSH + 2 625 Core (~Ssh subset 212) +
1 348 App + 96 UI = 4 490 passing + 6 skipped**, zero warnings under
`TreatWarningsAsErrors`.

Deferred (tracked, not implemented in this pass): mktemp-based portable
remote temp dir for `RemoteFileEditor` (requires per-host probe cache),
typed `FailureClassifier` properties (depends on SSH.NET surface),
`SshHandler.cs` 834-LOC refactor (separate task).

## 2026-04-25 — SSH runtime validation fixes

- Hardened embedded SSH terminal shutdown: late disconnect/output callbacks now stop posting to WebView2 once the terminal surface or dispatcher is disposed, preventing `TerminalWebView` object-disposed popups during app exit.
- App shutdown now closes active sessions through the silent cleanup path instead of invoking the user-facing "close all sessions" confirmation while WPF is already shutting down.
- Documented the `Heimdall-TestEnv` gateway setup split: imported server profiles reference gateway ids, but gateway definitions must exist in the runtime build's `config\settings.json` (`AppSettings.SshGateways`). Added smoke-test and troubleshooting notes for running `Inject-Gateway.ps1` against the exact Debug/Release build being launched.
- Baseline after this pass: **4,454 passing + 6 skipped** tests.

## 2026-04-24 — SSH hardening roadmap (lots 1-9)

Full roadmap addressing the SSH review recommendations. Nine lots plus one follow-through patch, each landed as independently-green commits on `master`.

- **Lot 1 — Host-key deadlock removed, trust path fail-closed.** Interactive host-key decisions now happen before the real `Connect()` via a dedicated pre-authentication probe (`SshConnectionFactory.ProbeHostKeyAsync` with `NoneAuthenticationMethod`). Real connections use a strict, synchronous `PinnedFingerprintVerifier` that accepts only the pre-resolved fingerprint. SSH.NET's `HostKeyReceived` callback is guaranteed to be pure-synchronous — no async, no UI dispatch, no sync-over-async from inside it. Production runtime paths no longer fall back to `AutoAcceptHostKeyVerifier.Instance` when `HostKeyStore` is provided without an `IHostKeyVerifier`: they fail closed with a clear exception. `ToolGatewayConnector` refuses to route tool traffic through a gateway that has no pinned fingerprint yet; the user must complete a normal interactive SSH session first.
- **Lot 2 — Key passphrase separated from login password.** New `SshKeyPassphrase` field on `SshConnectionParams`, persisted encrypted via `SshKeyPassphraseEncrypted` alongside `SshPasswordEncrypted` in `servers.json`. The Server dialog now exposes two distinct `PasswordBox` fields; the passphrase field is visible only when a key path is configured. Password can now serve as a true fallback auth method when a key is also present, not as a silently-repurposed passphrase. Legacy profiles (key path set + password set, no `sshKeyPassphraseEncrypted` field) are kept read-only on disk; legacy mapping is applied at runtime (password tried both as passphrase and as password fallback, strictly more permissive than before), and an info log is emitted. Auto-migration happens only when the user saves from the UI. Plink fallback fails fast with a descriptive error when a passphrase is set, since plink cannot prompt for it.
- **Lot 3 — Public SSH.NET resize API.** Removed the private-field reflection previously used in `SshShellSession.Resize()` and switched to SSH.NET 2025.1.0's public `ShellStream.ChangeWindowSize(uint columns, uint rows, uint width, uint height)`. A strict signature regression test guards future SSH.NET upgrades.
- **Lot 4 — Windows OpenSSH Agent support.** New `ISshAgent` / `ISshAgentKey` abstraction with two implementations: `PageantAgent` (existing Pageant IPC refactored behind the interface) and `OpenSshPipeAgent` (new, named pipe `\\.\pipe\openssh-ssh-agent` per draft-ietf-sshm-ssh-agent). `SshAgentRegistry` enumerates agents in a user-configurable priority order via new `AppSettings.SshAgentPreference` (default: Windows OpenSSH first, Pageant second). RSA keys are advertised with their SHA2 variants (`rsa-sha2-256` flag 0x02, `rsa-sha2-512` flag 0x04, plus legacy `ssh-rsa`) so modern servers with `ssh-rsa` disabled still accept cached keys. Agent IPC handles are never kept alive across requests (no handle leaks on Windows).
- **Lot 5 — `HostKeyTrustService` and known_hosts synchronization.** New centralized orchestration layer above `HostKeyStore`, with enriched `HostKeyEntry` metadata (`FirstSeen`, `LastSeen`, `Algorithm`, `Source`, `PublicKeyBase64`). `LastSeen` is updated on every successful verification, outside the SSH.NET callback. Added explicit import/export against OpenSSH `~/.ssh/known_hosts`, including parse support for hashed entries (HMAC-SHA1 per OpenSSH `HashKnownHosts`). Persistence schema bumped to `trustedHostKeysV2`; legacy `trustedHostKeys` is read without modification or deletion so downgrades remain safe. New `AppSettings.SyncKnownHostsAtStartup` (opt-in, off by default) runs the importer non-blocking at startup. CA-signed host keys (`@cert-authority`) and revoked lines (`@revoked`) are parsed and skipped with diagnostics; full CA support is a future lot.
- **Lot 5B — UI import metadata propagation.** The pre-existing `ImportKnownHostsDialogViewModel` path now routes through `HostKeyTrustService.Import()` so imported entries carry `Source=ImportedKnownHosts` and a `PublicKeyBase64` blob, enabling round-trip export from the UI.
- **Lot 6 — ProxyJump import.** `OpenSshConfigParser` now maps OpenSSH `ProxyJump` directives (single-hop and multi-hop chains) to `SshGatewayDto` gateways linked via `ParentGatewayId`, consumable by the existing `GatewayChainResolver` unchanged. `ProxyJump none` is accepted as "no proxy". Unsupported forms are explicitly rejected with localized diagnostics: `ProxyCommand` (any form), mixed `ProxyJump+ProxyCommand`, `%h`/`%p`/`%r` token substitution, quoted/malformed syntax, and cycles. Reuse rules: inside the same import batch `(host, port, user, keyPath)` identity; against existing Heimdall gateways, `(host, port, user)` — in both cases, no mutation of the existing gateway, just reference sharing. Cycle detection rejects the entire chain, never a partial import.
- **Lot 7 — TunnelManager refactor.** Extracted shared helpers (`ResolvePinnedVerifierAsync`, `ConnectSshClientWithCancellationAsync`, `WireFinalForwardedPorts`, `BuildTunnelInfo`, `RegisterTunnelSession`, `ClassifyAndBuildFailureResult`) into a partial class `TunnelManager.Build.cs` with a `TunnelBuildContext` holder. `OpenTunnelAsync` and `OpenChainedTunnelAsync` became thin orchestrators sharing 100 % of post-connect logic. `TunnelManager.cs` trimmed from ~825 to 462 lines (-44 %). Nine characterization tests added in a dedicated prior commit; the `tests/` diff between the characterization commit and the refactor commit is empty, proving no behavior change.
- **Lot 8 — Trusted host keys UI.** New sub-panel under `Settings > SSH & SFTP` exposing the `HostKeyTrustService` data. Dense grid with columns Host:Port, Algorithm, Source (localized label — no raw enum leaks to XAML), First seen, Last seen, truncated fingerprint, row actions. Sortable columns, substring filter on Host:Port. Row actions: copy full fingerprint, details modal (full fingerprint and public key base64), remove with a confirmation dialog that discourages habitual removal. Global actions: import from `~/.ssh/known_hosts`, export to it, refresh. Conflict resolution on import goes through a dedicated modal with per-row "Keep existing" default and explicit opt-in "Replace with imported"; the grid itself has no replace action by design, so a host-key mismatch always stays a notable decision.
- **Lot 9 — Local bind retry.** `ForwardedPortLocal.Start()` and `ForwardedPortDynamic.Start()` calls now retry up to 3 times with 50 ms spacing on `SocketException(AddressAlreadyInUse)` only, closing the TOCTOU window between `AllocatePort` and the actual bind. `RemotePortForward.Start()` is not retried (server-side bind, different race surface). Chained-tunnel intermediate local ports also covered. The retry helper accepts an injectable sleep delegate so unit tests stay deterministic.

Invariants preserved across all nine lots and verified at every merge:

- Zero `AutoAcceptHostKeyVerifier.Instance` occurrences in production code paths at any point.
- No sync-over-async introduced in any host-key or auth code path (grep guard: `.Result`, `.Wait(`, `GetAwaiter().GetResult`).
- SSH.NET `HostKeyReceived` handler is pure-synchronous from lot 1 onwards.
- Host-key persistence schema migrations are strictly additive on disk (lot 5 keeps `trustedHostKeys` intact alongside `trustedHostKeysV2`, mirroring the non-destructive `servers.json` migration pattern of lot 2).

Baseline after the roadmap: **4,448 passing + 6 skipped** (`4,454` discovered), **59 built-in tools**, **5,185 locale keys** per locale (EN and FR at strict parity enforced by CI). Zero build warnings, zero skipped changes in the 6-skip count across all 10 commits.

## 2026-04-24 — SSH/RDP security audit remediation
- Hardened SSH host-key trust across SSH.NET and Plink: Plink now consumes pinned fingerprints from `HostKeyStore`, first-use and mismatch decisions route through `IHostKeyVerifier`, and the themed `HostKeyPromptDialog` handles deliberate acceptance or rejection.
- Added explicit host-key mismatch diagnostics, localized user messages, and persistence semantics where `HostKeyEvent` fires only from `Trust()`.
- Refactored `TunnelManager` cleanup through single cleanup helpers for partial simple and chained tunnel setup failures.
- Switched `ServerHealthMonitor` command execution to SSH.NET APM async execution with concurrent CPU/RAM/disk probes and cancellation coverage.
- Tightened RDP credential broker autofill so broker windows require a host-title match before password injection.
- Added root `SECURITY.md` with the current threat model, known limitations, and security test entry points.
- Current baseline after this audit line: **4,318 passing + 6 skipped** (`4,324` discovered), **59 built-in tools**, and **~5,118 locale keys** per locale.

## 2026-04-23 — release 2026.042302 — audit remediation patch release
- Version bump from `2026.042301` to `2026.042302` (`InformationalVersion`).
- Packages the full 2026-04-22 audit-remediation line already merged on `master`: session/WebView handler leak cleanup, File Share bearer-token hardening with TFTP opt-in, startup async de-blocking, terminal asset caching, subprocess argument hardening, MVVM cleanup, UI polish, accessibility fixes, and repository housekeeping.
- Follow-up release patch after the first publish:
  - aligns the formatting gate with the repository's expected CRLF / `using` order
  - relaxes one `TcpPingViewModelTests` timeout under code coverage so GitHub Actions stays stable without changing runtime behavior
- Current baseline for this release line: **4,233 passing + 6 skipped** in CI, **59 built-in tools**, and **5,105 locale keys** per locale.

## 2026-04-22 — sessions diagnostics, NotesTool cleanup, and docs sync
- Introduced a shared `SessionDiagnostic` / `SessionFailureStage` contract and surfaced pane-scoped SSH failure diagnostics end-to-end, including a `Details` disclosure in `SessionPaneControl`.
- Wired RDP diagnostics on both pre-tab failure branches (`RdpHandler`) and mid-session host events (`RdpActiveXHost.Disconnected` / `FatalError`) while retiring the legacy local detail text block in `EmbeddedRdpView`.
- Kept failed-session panes interactive by suppressing the tab-loading overlay when diagnostics already exist, and compacted the failure-overlay Reconnect / Close buttons for narrow panes.
- Modernized `NotesTool` with `{loc:Translate}` migration, ViewModel-owned Confluence/HTML export payload generation, declarative tag-chip binding via `ItemsControl`, and a denser Obsidian-like explorer presentation.
- Refreshed README / architecture / smoke documentation to match the current gate (**4195 passing + 6 skipped**, `4201` discovered) and locale catalog size (**5,102 keys per locale**).

## 2026-04-21 — release 2026.042102 — ARP Monitor refactor + locale/parser fixes
- Version bump from 2026.042101 to 2026.042102 (`InformationalVersion`).
- Fixes the JWT Parser locale-switch crash by marshaling `OnLocaleChanged` back to the WPF dispatcher when locale changes originate from a non-UI thread.
- Aligns the UI test harness locale-switch path with the production `LocalizationSource` bridge, restoring clone-clean UI smoke stability (`93/93` on repeated runs).
- Fixes the French `netsh wlan` parser ambiguity between `Type de réseau` and `Type de radio`, so `RadioType` is now populated correctly on FR output.
- Refactors ARP Monitor in three phases without behavior drift:
  - extracts `ArpTableParser` into `Heimdall.Core.Network` with dedicated Windows/Linux/macOS parser tests,
  - introduces `IArpTableReader` + `ArpMonitorViewModel` + extracted `ArpEntry`,
  - migrates ARP alerting, vendor lookup, and TSV copy payload generation into the ViewModel while leaving only non-bindable UI effects in the view.
- Closes the `#52` audit follow-up by centralizing the duplicated `IsCollapsed(AutomationElement?)` helper into `UiTestBase`.
- Housekeeping: tests now stand at **4156 passing + 6 skipped** (`4162` discovered), with clean local build/test gates before release.

## 2026-04-21 — release 2026.042101 — Remote access audit package
- Version bump from 2026.042003 to 2026.042101 (`InformationalVersion`).
- Consolidates batches 55.1, 58, and 59 (see entries below dated 2026-04-19).
- Adds the remote-access audit package:
  - `docs/audit-connection-sequences-2026-04-19.md`
  - `docs/audit-gap-rdp-2026-04-19.md`
  - `docs/audit-gap-ssh-terminal-sftp-2026-04-19.md`
  - `docs/audit-roadmap-remote-access-2026-04-19.md`
- No new runtime feature in this release.

## 2026-04-19 — batch 55.1 — Remove legacy workspace path

- Deleted `WorkspaceService` + `WorkspaceDto`/`WorkspaceSessionDto`.
- Deleted the orphan `SessionCoordinator.RestoreWorkspaceAsync` method.
- Retired the `EnableSessionPersistence` UI toggle; session snapshot save/restore
  (b55) is now unconditional. The property stays on `AppSettings` for backward
  deserialization compatibility but has no runtime effect.
- Removed four locale keys (`WorkspaceRestoring`, `LogWorkspaceRestored`,
  `SettingsWorkspaceRestore`, `A11ySettingsWorkspaceRestore`) from `en.json`/`fr.json`.
- Removed the legacy `EnableSessionPersistence = $false` line from the
  sidebar-favorites smoke script.

## 2026-04-19 — batch 58 — Post-connect Command Library linkage

- Extended `PostConnectStep` with optional `CommandLibraryId` and
  `CommandLibraryParams` so SSH embedded sessions can resolve Command Library
  actions at run time while preserving the dormant literal `Input` for unlink.
- Added `CommandLibraryStepResolver` and the `Broken` post-connect status so
  missing actions, Windows-only templates, and invalid parameters surface as
  configuration errors instead of silent fallbacks or runtime failures.
- Added a modal `CommandLibraryPickerDialog` to `ServerDialog` so operators can
  link or unlink post-connect steps without editing the Command Library itself.
- Kept the scope SSH-embedded only; Plink, Telnet, and Local Shell post-connect
  flows remain unchanged in this batch.

## 2026-04-19 — batch 59 — Post-connect parameter auto-prefill

- Added one-shot auto-prefill for linked Command Library parameters in the
  picker, using server-profile host/port/user aliases captured at open time.
- Added a `Change...` path for already linked steps so operators can re-open the
  picker with their existing values preserved.
- Kept prefill snapshot-only, with no live binding back to the server fields.
- Structurally blacklisted secrets (`password`, `token`, `secret`, etc.) from
  any auto-prefill path.

## [Unreleased] - 2026-04-14

### UX — session-tree move-to-group parity + sidebar favorites

- **Session tree move-to-group unified**: context-menu and drag-drop now converge on a single `ServerListViewModel` core move path, preserving in-memory expansion state by avoiding the previous `LoadServers` rebuild after drag-drop
- **Drag/drop destination policy aligned**: drag-over and drop validate against the same project-scoped target set as the context menu, and the session tree now exposes an explicit no-group drop zone for drag-to-root parity
- **Sidebar Favorites section added**: the sidebar Tools tree now shows an always-present localized Favorites category at index 0, populated from `AppSettings.FavoriteToolIds` and sorted alphabetically by localized display name
- **Cross-surface favorite sync**: `MainViewModel.ToggleFavoriteToolAsync` now raises `FavoritesChanged`, and `SidebarViewModel` applies targeted add/remove mutation so the sidebar stays in sync with both the sidebar ContextMenu and the full-page Tools tab pin button
- **Right-click no longer launches sidebar tools**: a `_suppressSidebarLaunch` guard blocks the `SelectedItemChanged` launch path during right-click targeting, and the redundant sidebar double-click launcher was removed to avoid duplicate tabs on context/network tools
- **Durable UIA smoke added**: `scripts/smoke/move-to-group-smoke.ps1` and `scripts/smoke/sidebar-favorites-smoke.ps1` were added to the repo harness, with WPF ContextMenu-specific gaps explicitly marked as skipped and delegated to human smoke

### Refactor — MainWindow + MainViewModel decomposition (Phases 1–4)

**`MainWindow.xaml.cs`: 3,490 → 2,123 LOC (−39%)**

- **Phase 1** — Extract 3 isolated low-risk domains:
  - `OnboardingFlowViewModel` (first-launch 3-step overlay, resolved by `MainWindow` via DI)
  - `FileShareService` (ephemeral HTTP/TFTP folder sharing, event-based API, `IAsyncDisposable`)
  - `WindowUIState` POCO + `MainWindow.WindowUI.cs` partial (fullscreen, sidebar toggle, tree scroll persistence, folder expand/collapse memory, window-bounds save/restore)

- **Phase 2** — Extract keyboard + sidebar + tools tab:
  - `KeyboardShortcutService` (18 shortcuts, fluent registration, `canExecute` gating) replaces the monolithic `OnPreviewKeyDown` switch
  - `SidebarViewModel` with XAML bindings (Sessions/Tools toggle, tool filter, lazy population, Ctrl+Shift+T toggle)
  - `ToolsTabViewModel` (full-page Tools browser VM state — favorites, recents, filter; section rendering still in `ToolsTabPopulationService` via Panel injection)
  - **Fix**: remove dead `OnWindowDeactivated` Command Palette auto-close handler that had been closing the palette on every open (pre-existing bug)

- **Phase 3** — Extract session/tree/tab interactions:
  - `TreeInteractionState` POCO + `MainWindow.TreeInteractions.cs` partial (session TreeView drag-drop, filter box, inline rename)
  - `TabInteractionState` POCO + `MainWindow.TabInteractions.cs` partial (tab drag-to-reorder, drag-to-detach, drop target resolution, hover tracking)
  - `SessionTabContextMenuFactory` + `ISessionTabContextCallbacks` (335-LOC menu builder, 19 conditional items)
  - `SessionSplitService` (detach/split/merge/unsplit orchestration, `SplitPaletteRequested` event)
  - Initial `ServerListViewModel.MoveServerToGroupAsync` extraction for the tree drag-drop write path (later unified with the context-menu path in the move-to-group parity pass)

**`MainViewModel.cs`: 1,917 → 628 LOC (−67%)**

- **Phase 4** — `MainViewModel` decomposition into 4 sub-VMs (constructor-composed, not DI-registered; `IDisposable` for event-subscription cleanup):
  - `CommandPaletteViewModel` (14 methods: fuzzy search ranking, tool-command parsing, ad-hoc `user@host:port` parsing with protocol inference, connect/split flows, `SplitLayoutMemory` pairing)
  - `TunnelsViewModel` (tunnel panel + tab, `ResolveRoute(sessionId)` for session header display)
  - `ScheduledTasksViewModel` (`TaskSchedulerService` ownership, idempotent `_started` flag)
  - `SessionCoordinator` (8 external wire-ups — 5 `Split.*` providers/setters + 3 `EmbeddedSessionManager` callbacks; broadcast cluster; `OnSessionReady` / `OnReconnectRequestedAsync` / `AutoOpenSftpAsync`)

### Refactor — Declarative i18n migration (Phase 5, in progress)

- **Phase 5A** — Navigation + toolbar imperative labels → `{loc:Translate}` (58 sites). `ApplyNavigationLocalization` / `ApplyToolbarLocalization` now empty stubs pending Phase 5D cleanup
- **Phase 5B** — Accessibility pass → `AutomationProperties.Name="{loc:Translate}"` (39 sites). `ApplyAccessibilityLocalization` deleted entirely
- Phase 5C (Tunnel/Scheduled/Settings/About apply helpers) and Phase 5D (format-args + computed properties + composite strings) pending

### Refactor — Command Library ViewModel extraction

- `CommandLibraryViewModel` extracted from `CommandLibraryView` code-behind with XAML bindings migration (fuzzy filter, platform/category/risk filters, parameter editor, favorites, history, Git Sync). View code-behind now limited to WebView2 and dispatcher-bound glue

### Fixed

- Ctrl+K Command Palette no longer closes immediately on open (dead `OnWindowDeactivated` handler removed; pre-existing bug)
- Filter box `TextChanged` handler no longer duplicates on locale switch (`Mw_FilterBox.TextChanged` subscription moved from `ApplyLocalization` to the `MainWindow` constructor)
- `App.OnExit` service provider disposal now routes through `IAsyncDisposable.DisposeAsync` to properly dispose async-only services (`FileShareService`)
- `MainViewModel` no longer leaks `CollectionChanged` + `PropertyChanged` handlers on session-tab teardown

### Housekeeping

- Tests: **1,775 passing** (unchanged) + 6 skipped (WPF `Application` context gating)
- Build: clean, 0 warnings, 0 errors
- i18n: 4,855 keys (EN/FR parity maintained, no changes this round)

---

### Post-v2026.041301 audit follow-up (2026-04-13) — code-behind split, observability, assets diet

#### Code organization — MainWindow code-behind split (Chantier 1)
- **`MainWindow.xaml.cs` shrunk from 4,895 → 3,490 lines** (−1,405 lines, −29%) via three structural extractions. Zero behavior change — pure file splits verified by build + full test suite
- **`Services/ContextMenuFactory.cs`** (647 lines, new) — builds the four session `TreeView` context menus (server, folder, tool, empty area) and the "Detected Tools" submenu from `ExternalToolProviderService`. Constructor-injected via DI; reached from MainWindow through a small `IContextMenuCallbacks` interface so the menu builder never touches window-scoped state directly
- **`Services/ToolsTabPopulationService.cs`** (605 lines, new) — owns the full-page Tools tab rebuild (Favorites / Recents / categories / 280px cards with search filter), the sidebar Tools `TreeView` data + filter logic, and the pure helpers `GetCategoryBrushKey` / `GetInheritedToolTargetHost` / `CreateInheritedToolContext` / `ResolveToolTabTitle`. Tool card click/pin callbacks are plain `Action<T>` delegates (no interface ceremony for two callbacks). Uses `Application.Current.FindResource` for theme tokens so the service stays decoupled from any specific `FrameworkElement`. `PopulateToolsTab` itself stayed in `MainWindow.xaml.cs` as a thin wrapper because it writes to named header elements (`Mw_ToolsTabTitle`, `Mw_ToolsTabCount`) that are tightly coupled to the XAML tree
- **`MainWindow.Localization.cs`** (519 lines, new partial class) — holds the 8 `Apply*Localization` methods (`ApplyLocalization` orchestrator + Navigation / Toolbar / Tunnel / Scheduled / Settings / About / Accessibility) and the three helpers that are only ever called from `ApplySettingsLocalization` (`PopulateCredProvPresets`, `PopulateExtToolPlaceholderList`, `UpdateExtToolPreview`). `UpdateExternalToolProviderStatus` and `UpdateTokenStatus` stayed in the main code-behind because they have additional callers (external-tool rescan, Git sync token save/clear handlers)
- All three extractions were carried out as pure structural moves with no logic change, no rename, and no signature change. `ContextMenuFactory` and `ToolsTabPopulationService` are registered as singletons in `App.xaml.cs` DI

#### Observability — empty catch blocks (CQ-01)
- **`FileLogger.Debug(string)` + `FileLogger.Debug(string, Exception)`** added to `Heimdall.Core.Logging.FileLogger` — mirrors the existing `Error` overloads, emits at level `DEBUG` through the same queue-and-flush pipeline
- **`TunnelManager.cs`** — 21 empty `catch {}` blocks now log at Debug level: 18 `dispose?.Dispose()` pairs in the tunnel-establishment error handlers (`Dynamic port dispose suppressed` / `Remote port forward dispose suppressed`), plus 3 lambda `Disconnect()` calls wired to `CancellationToken.Register` (`Client disconnect on cancel suppressed` / `Root client` / `Hop client`). Inner catches use a local `cleanupEx` variable to avoid shadowing the outer exception dispatch
- **`SshShellSession.cs`** — single `_client.Disconnect()` cancellation lambda now logs `SSH disconnect cleanup suppressed` at Debug level
- Rationale: these sites are defensible (cleanup paths shouldn't throw) but silent failures hid any surprising exception at runtime. Logging at Debug is cheap, observable through the dev-console trace, and doesn't change behaviour

#### Performance — NotesStorageService dispatcher starvation (PERF-03)
- **`NotesStorageService.SaveNote()`** — the synchronous save path called from `IToolView.CanClose()` / `IDisposable.Dispose()` now waits on its `SemaphoreSlim` with a 2-second timeout instead of blocking indefinitely. On timeout it logs `SaveNote timed out waiting for write lock` via `FileLogger.Warn` and returns without writing — far better than stalling the WPF dispatcher if an async `SaveNoteAsync` is in flight

#### Testing — SplitService unit tests (TEST-01)
- **`tests/Heimdall.App.Tests/SplitServiceTests.cs`** (+14 tests) covering `SplitService`'s synchronous, self-contained methods — the service had zero direct coverage despite being the central owner of pane lifecycle
- **Category A — CancellationTokenSource lifecycle (5 tests)**: `RegisterSession` token creation, `CancelSession` cancels a previously captured token, unknown-session `GetSessionToken` returns `CancellationToken.None`, unknown-session `CancelSession` no-op safety, idempotent re-register (second `TryAdd` keeps original)
- **Category B — `CloseAllPanes` tool-pane blocking (4 tests)**: empty tree, single closable tool pane (disposed + host control cleared), single blocking tool pane (host control preserved, no dispose), mixed tree with one blocker (pre-check means neither pane is disposed)
- **Category D — `ToggleSplitOrientation` (3 tests)**: Horizontal↔Vertical both directions plus unsplit no-op
- **Category E — `SplitSessionWithTool` guards (2 tests)**: unknown tool id short-circuit + max-panes (8) cap with `SetStatusText` callback capture
- **All 7 `SplitService` dependencies are `sealed`** — Moq cannot mock them. The fixture uses real instances for `ConfigManager` (temp dir), `LocalizationManager` (unlocalized, keys return verbatim), and `ToolRegistry` (built-in registry), and passes `null!` for `ConnectionStateMachine` / `TunnelManager` / `EmbeddedSessionManager` / `ConnectionService` because every tested code path was verified to never dereference them. A code comment in the fixture documents this rationale. **Moq was NOT added to the project** despite the initial plan suggesting it
- **`SwapSplitPanesAsync` intentionally untested** — it early-returns when `System.Windows.Application.Current?.Dispatcher` is null, which is always the case in xUnit. Standing up a WPF `Application` + STA dispatcher pump is the same blocker that keeps `ThemeServiceTests` at `[Skip]` and is out of scope here

#### Assets diet (Chantier 3 — PERF-04 + PERF-05)
- **Orphaned PNGs removed (−9.6 MB)**: `Assets/Icons/app/icon-flat.png` (4.19 MB), `icon-rays.png` (3.18 MB), `logo.png` (1.85 MB). Reference audit (`git grep` across source + XAML + csproj + installer scripts) turned up only historical `docs/CHANGELOG.md` mentions and unrelated `/logo.png` references inside `drawio/js/*.min.js` (which point at `Assets/drawio/images/logo.png`, a different file). The real app icon `src/Heimdall.App/app.ico`, wired via `<ApplicationIcon>` in the csproj, is untouched
- **Draw.io locales pruned (−2.95 MB)**: `Assets/drawio/resources/` went from 59 files / 3.1 MB down to 4 files / 149 KB. Kept `dia.txt` (base / English fallback — draw.io's loader uses this name for English), `dia_fr.txt`, `dia_i18n.txt` (auto-generated key manifest), and `README.md`. Removed 55 other `dia_*.txt` locale files — Heimdall is English/French only and draw.io falls back to `dia.txt` for any missing locale
- **`Assets/drawio/VENDORED.md`** updated with three new sections documenting what was pruned, what is a candidate for further pruning *with a runtime test plan* (viewer bundles ~5.6 MB, `shapes-14-6-5.min.js` vs `shapes.min.js` duplication ~1.4 MB, clipart `img/` categories up to ~8 MB), and what is intentionally kept
- **Total on-disk savings: ~12.55 MB** from source control. The `Assets\**\*` glob in `Heimdall.App.csproj` means removed files simply drop out of the deploy — no csproj edit required

#### Housekeeping
- Tests: **1,775 passing** (was 1,761) + 6 skipped (WPF Application context gating — intentional)
- Build: clean, 0 warnings, 0 errors
- i18n: 4,855 keys (EN/FR parity maintained, no changes this round)
- `MainWindow.xaml.cs`: 4,895 → 3,490 lines across Chantier 1 (Step 1 extracted `ContextMenuFactory`, Step 2 extracted `ToolsTabPopulationService`, Step 3 extracted `MainWindow.Localization.cs`)

---

## [v2026.041301] - 2026-04-13

### Sessions rename + full project audit pass

#### UX — Servers → Sessions rename
- **Wholesale rename** of all user-facing "Servers" labels to "Sessions" across navigation tabs, sidebar tabs, dialog titles, status bar, tooltips, error messages, accessibility names, onboarding steps, and tree/empty-state hints — better reflects that Heimdall manages local shells (PowerShell, CMD, WSL) alongside remote SSH/RDP/VNC/SFTP/FTP/Citrix sessions
- **XAML element renames**: `TabServers → TabSessions`, `SidebarTabServers → SidebarTabSessions`, `SidebarServersContent → SidebarSessionsContent`, `ServerTreeView → SessionTreeView`, `ServerTreeColumn → SessionTreeColumn`, `ServerDetailPanel → SessionDetailPanel`, `Mw_AddMenuServer → Mw_AddMenuSession`, `Mw_EmptyBtnAddServer → Mw_EmptyBtnAddSession`, `Mw_EmptySelectServer → Mw_EmptySelectSession`
- **MainViewModel**: `IsServersTabSelected → IsSessionsTabSelected`, `_selectedTab` / `_previousTab` defaults `"Servers" → "Sessions"`, all tab-routing string literals updated
- **Event handlers**: `OnServersTabChecked → OnSessionsTabChecked`, `OnSidebarTabServersChecked → OnSidebarTabSessionsChecked`
- **Preserved as-is** (intentional): `ServerListViewModel`, `ServerItemViewModel`, `ServerDialog`, `ServerProfileDto`, `ServerId` / `OriginalServerId` model properties, `EphemeralFileServer`, `X11ServerManager`, `servers.default.json` filename, and every `server` reference in tool help text that means an actual remote machine (HTTP / DNS / SMB / FTP / VNC / TLS / SSH server, host key verification, etc.)

#### UX — Sidebar tab persistence (PERF-99 / DOC-03)
- **Bidirectional persistence**: new `PersistSidebarTabChoice(bool isTools)` writes the choice via `ConfigManager.MergeSettingAsync(s => s.ShowToolsPanel = isTools)` whenever either RadioButton is checked. Previously only the onboarding flow set `ShowToolsPanel = true`; manually switching back to Sessions never wrote `false`, so every subsequent launch defaulted to Tools
- **`_sidebarTabRestored` startup guard**: prevents `InitializeComponent()`'s default `IsChecked="True"` from clobbering the persisted preference before the `Loaded` handler can restore it. The `OnSidebarTabSessionsChecked` / `OnSidebarTabToolsChecked` handlers no-op until the flag is set in the Loaded handler, immediately after the restore block
- **Onboarding cleanup**: removed the dead in-memory `vm.CurrentSettings.ShowToolsPanel = true` assignment that never actually persisted (the subsequent `MergeSettingAsync(s => s.OnboardingCompleted = true)` reloads from disk and only mutates that one field). Now the RadioButton check naturally routes through the new persist helper

#### Performance
- **PERF-05 (critical) — Async/await replaces blocking `.GetAwaiter().GetResult()`** in 4 sites:
  - `RestoreWindowBounds`: signature changed to `(AppSettings settings)`, settings now passed from the Loaded handler (already loaded by `LoadCommand.ExecuteAsync`)
  - `OnClosing`: converted to `protected override async void` with a deferred-close pattern (`_closeConfirmed` guard). Cancels the close, awaits `ShowSaveDiscardCancelAsync`, then re-invokes `Close()` — previously deadlocked on the dispatcher when the dialog tried to post back
  - `EphemeralFileServer.StartHttpServer` / `StartTftpServer` → renamed to `StartHttpServerAsync` / `StartTftpServerAsync` with `await StopHttpServerAsync()` / `await StopTftpServerAsync()` for the double-start path. Caller (`OnShareFolderClick`) converted to `async void`
- **PERF-01 — Event cleanup in `MainWindow.OnClosed`**: stored 4 long-lived event handler delegates in fields (`_connectionPropertyChangedHandler`, `_serverListPropertyChangedHandler`, `_externalToolsChangedHandler`, `_localeChangedHandler`) so they can be unsubscribed via `-=` on close. Without this, the captured-`this` lambdas kept the window rooted past `Close()`
- **PERF-07 — Draw.io excluded from Debug builds**: `Heimdall.App.csproj` `<Content Include="Assets\drawio\**">` now wrapped in `Condition="'$(Configuration)' != 'Debug'"`. Saves ~48 MB / 2258 files copied to `bin/Debug/` on every iterative dev build. `DiagramEditorView.InitializeWebViewAsync` shows a localized "Release-only" fallback panel (new key `DiagramEditorDebugOnly`) when the directory is missing instead of crashing
- **PERF-09 — Lossless PNG re-compression**: `icon-flat.png` 4.41 → 4.19 MB (-224 KB), `icon-rays.png` 4.55 → 3.18 MB (-1.37 MB) via Pillow `optimize=True compress_level=9` with byte-perfect pixel verification. `logo.png` and `splash-screen.png` left untouched (already encoded by a stronger optimizer; Pillow output was *larger*). ~1.6 MB saved on disk

#### Code Quality
- **CQ-08 — `sealed` modifier** added to **225 non-inherited declarations** (170 classes + 55 records) across 9 projects: TwinShell.Core (60), TwinShell.Infrastructure (48), Heimdall.Core (45), TwinShell.Persistence (33), Heimdall.App (20), Heimdall.Ssh (9), Heimdall.Sftp (5), Heimdall.Rdp (3), Heimdall.Terminal (2). Audit script applied skip rules for inherited types (built a cross-codebase derived-name index), WPF view bases (`Window`/`UserControl`/`Page`/`Control`/`MarkupExtension`), classes containing the `virtual` keyword in their body, and an explicit blocklist for COM event sinks (`MsTscAxEventSink`). Zero build errors, zero rollbacks
- **CQ-06 — Empty `catch {}` blocks fixed in `BackupService.cs`** (3 sites): temp directory cleanup, backup metadata read, backup metadata write — all replaced with `catch (Exception ex) { _logger.LogWarning(ex, ...) }` using the existing injected `ILogger<BackupService>`. Other bare catches across the codebase already had inline rationale comments (`/* best effort */`, `/* already exited */`, etc.) and were left untouched

#### Accessibility
- **A11Y-04** — `LogViewerView` `BtnTail` `ToggleButton` now sets a descriptive `AutomationProperties.Name` (new key `A11yLogViewerTailToggle` — EN: "Toggle live tail mode" / FR: "Activer/désactiver le mode tail temps réel") via the existing code-behind localization pattern. The previous "Tail" label was non-descriptive for screen reader users. Every other tool-view `ToggleButton` was audited — this was the only gap

#### UI
- **Settings toolbar button truncation** — replaced fixed `Width="130"` / `Width="160"` with `MinWidth` on 4 buttons (`Mw_SettingsResetBtn`, `Mw_SettingsExportBtn`, `Mw_SettingsImportBtn`, `Mw_SettingsCitrixBtn`). French translations now auto-size instead of clipping mid-word. `SecondaryButtonStyle` already defines `Padding="16,8"`, so no inline padding override needed

#### i18n
- **fr.json mojibake repair (1170 substitutions across 631 lines)**: fixed double-UTF-8 encoding affecting all French accented characters (ô è é à ç î ê ù â É À) via a two-pass codec round-trip — pass 1 (latin-1) for accented lowercase forms, pass 2 (CP1252) for the uppercase `É` / `À` whose smart-punctuation second char (`‰` U+2030, `€` U+20AC) sits outside the latin-1 0x80-0xBF continuation range. `WindowTitle` now correctly displays "Centre de Contrôle d'Accès Distant"
- **Stale Heimdall-profile values cleaned** (13 keys × 2 locales): `ErrorEmergencyResolveServers`, `ErrorEmergencySaveServers`, `ErrorRestoreServersFailed`, `SettingsApplyModeToAll`, `ConfirmDeleteGatewayDetailMessage`, `ToolSshConfigGenerateAllHint`, `AccessSearchFilter`, `SearchResultCount`, `AccessDetailConnect`, `AccessEmptyImport`, `A11ySearchAndFilter`, `OnboardingStep1Title`, `OnboardingStep1Desc` — all updated from "server(s)" to "session(s)" where the term refers to a saved profile, not an actual remote machine
- **Sessions rename** (locale value updates for the wholesale UX rename): `TabSessions`, `SidebarTabSessions`, `A11ySidebarTabSessions`, `A11ySessionsTab`, `StatusBarSessions`, `EmptyStateBtnAddSession`, `EmptyStateSelectSession`, `AddMenuSession`, plus 119 value-only updates across status messages, dialog titles, confirmations, tooltips, tree/empty-state hints, error messages, scheduled task labels, and accessibility names. Dropped the duplicate `NavTabServers` (`NavTabSessions` already existed)
- **+2 keys** (EN/FR): `DiagramEditorDebugOnly`, `A11yLogViewerTailToggle`. Final state: 4855 keys per locale, parity verified, JSON valid

#### DevOps
- **DEVOPS-02** — `dotnet list package --vulnerable --include-transitive` step added to `.github/workflows/ci.yml` after the test step. Emits a `::warning::` instead of failing the build (vulnerability databases occasionally have false positives or no upgrade path; informational only). Implemented in `pwsh` to match the runner's default shell

#### Documentation
- **DOC-03** — `CLAUDE.md` updated for the Sessions rename: 3 stale "Servers" references in the sidebar/tools description and Session-Grid airspace section. The 6 remaining "server" hits are all preserved C# class/file/property identifiers (`EphemeralFileServer`, `X11ServerManager`, `ServerListViewModel`, `servers.default.json`, `ServerId`, `### ServerDialog` section header)

#### Testing
- **+37 new tests** in `tests/Heimdall.App.Tests/` covering services with previously zero coverage:
  - `ThemeServiceTests` (10 — 4 active + 6 `[Skip]` for WPF Application context): `AvailableThemes` enumeration, constructor defaults, `ThemeRevision` initial value, no-throw under no-Application. Migration / idempotence / event / canonical-casing scaffolds wait for a future WPF fixture
  - `MigrationServiceTests` (13): `DetectLegacyInstallation` positive/negative/null path, `ImportFromLegacyAsync` round-trip with valid settings + server inventory, empty arrays, malformed JSON, missing-file failure mode
  - `EphemeralFileServerTests` (14): HTTP/TFTP lifecycle, argument validation, idempotent stop-when-not-running, **PERF-05 double-start regression guard**, `Dispose`/`DisposeAsync` cleanup, `GetLocalIpAddress` static helper. Each test uses a distinct port in the IANA dynamic range (49510-49514) and silently skips when port acquisition fails for restricted CI environments
- All new tests follow the existing project pattern: xUnit only, `IDisposable` cleanup with temp directories, no mocking library

#### Housekeeping
- Tests: **1,761 passing** (was 1,724) + 6 skipped (intentional WPF scaffolds)
- i18n: 4,855 keys (EN/FR parity maintained, +2 net)
- Build: clean, 0 warnings, 0 errors

---

## [v2026.041202] - 2026-04-12

### Theme system overhaul — centralized ThemeService, 7 Dracula variants only

#### ThemeService (single owner of the theme swap)
- **`Services/ThemeService.cs`**: singleton DI service with `ApplyTheme(string?)` as the only code path that replaces the theme `ResourceDictionary` in `Application.Resources.MergedDictionaries`
- **Idempotent swap**: no-op when the requested theme is already active; searches the existing dictionary via `Source.OriginalString.Contains("Theme.xaml")`
- **Legacy migration**: settings containing `"Dark"` or `"Light"` are silently migrated to `DraculaPro` and persisted via `ConfigManager.MergeSettingAsync`
- **`ThemeRevision`**: monotonic counter bumped *before* the `ThemeChanged` event fires, used by XAML `MultiBinding` triggers
- **DWM integration**: every open `Window` gets its dark-mode title bar flag refreshed via `WindowThemeHelper.ApplyCurrentTheme` after each successful swap
- **Duplication removed**: `App.xaml.cs` and `MainViewModel.cs` no longer contain their own theme switch statements (the previous duplication was the root cause of commit `0d3d9c0`, where `ApplyThemeFromSettings` only knew Dark/Light)

#### Themes removed
- **Deleted**: `src/Heimdall.App/Themes/DarkTheme.xaml`, `src/Heimdall.App/Themes/LightTheme.xaml`
- **Kept**: 7 Dracula variants — `DraculaProTheme` (default), `AlucardTheme`, `BladeTheme`, `BuffyTheme`, `LincolnTheme`, `MorbiusTheme`, `VanHelsingTheme`
- `App.xaml` default merged dictionary → `Themes/DraculaProTheme.xaml`
- `config/settings.default.json`, `AppSettings.DefaultTheme`, `SettingsViewModel._defaultTheme`, `SchemaValidator.ValidThemes` all updated to `DraculaPro` / the 7-variant set
- Settings theme `ComboBox` in `MainWindow.xaml` cleaned up (removed `Mw_ThemeDark` and `Mw_ThemeLight` items + their localization hooks)

#### Theme reactivity — converters, code-behind, editor
- **Brush-resolving converters** (`ConnectionTypeToColorConverter`, `ConnectionTypeToBrushConverter`, `ConnectionStateToBrushConverter`, `ServerStatusToColorConverter`) implement both `IValueConverter` *and* `IMultiValueConverter` with a shared `ResolveBrush` helper. XAML sites route them through `MultiBinding [value, DataContext.ThemeRevision]` so WPF re-runs the converter on each swap. `ElementName=MainWindowRoot` required (not `RelativeSource AncestorType=Window`) so the binding resolves from inside Command Palette `Popup` content
- **Generic resource-key converters**: `ResourceKeyToBrushConverter` (dual `IValue`/`IMulti`, used by the sidebar Tools `TreeView`) and `ResourceKeyToGeometryConverter` (simple `IValue`, resolves `Geo.Tool.*` keys)
- **Code-built UI in `MainWindow.xaml.cs`** (`PopulateToolsTab`, `RefreshToolsTabSections`, `CreateToolsTabCard`, `UpdateToolLaunchContextLabels`): `element.SetResourceReference(<DP>, "BrushKey")` instead of caching `Brush` instances from `FindResource`. Hover-state toggles call `SetResourceReference` with a conditional key rather than flipping pre-cached brushes
- **`EmbeddedEditorView`**: reads AvalonEdit chrome colors (`Background`, `Foreground`, `LineNumbersForeground`, `SelectionBrush`, `CurrentLineBackground/Border`) via `ResolveColor("BrushKey", fallback)` — no more Dark/Light branches. Subscribes to `ThemeService.ThemeChanged` in `Loaded`, unsubscribes in `Unloaded`. Syntax token palette stays fixed Dracula (shared across all variants)
- **Hardcoded hex cleanup in `MainWindow.xaml`**: `ContentDropZone` background → `{DynamicResource DragDropOverlayBackground}`, broadcast-mode `DataTrigger` → `{DynamicResource BroadcastActiveBrush}`

### Sidebar UX redesign — tabbed Servers / Tools panel

- **Tabbed sidebar**: two `RadioButton`s (`SidebarTabServers` / `SidebarTabTools`, `GroupName=SidebarTabs`) replace the collapsible `ToolsQuickPanel` (`MaxHeight=350`, bottom-docked). Both tabs now share the full sidebar height; `Visibility` of `SidebarServersContent` / `SidebarToolsContent` is bound to each RadioButton's `IsChecked`
- **`SidebarTabStyle`** (`CommonControls.xaml`): flat `RadioButton` template with accent underline on `IsChecked`, `HighlightBrush` on hover, `FocusIndicatorBrush` on keyboard focus — all colors via `DynamicResource`
- **Servers tab**: unchanged — toolbar (search, add, expand/collapse) + `ServerTreeView`
- **Tools tab**: filter `TextBox` + context label + full-height `TreeView` with collapsible categories. Data model:
  - `SidebarToolCategoryViewModel` (ObservableObject): `CategoryName`, `BrushKey`, `Tools`, `VisibleCount`, `IsExpanded`, `IsVisible`
  - `SidebarToolItemViewModel`: `Id`, `Name`, `BrushKey`, `IconGeometryKey`, pre-lowercased `Searchable` blob (`name + aliases`)
- **Lazy populate**: `BuildSidebarToolsData()` reads `ToolRegistry.All`, groups by `Category`, sorts alphabetically per group — invoked on first `SidebarTabTools.Checked` and rebuilt when `ToolRegistry.ExternalToolsChanged` fires
- **Filter**: `Searchable.Contains(filterLower)` per item, auto-expand matching categories, empty-state label when no results
- **Launch flow**: `LaunchSidebarTool(item)` reuses the same primitives as the full-page Tools tab (`CreateInheritedToolContext` / `ResolveToolTabTitle` / `vm.OpenToolTabAsync` / `vm.TrackRecentTool`)
- **Ctrl+Shift+T**: toggles the active sidebar tab. Gotcha: setting `RadioButton.IsChecked = false` on a grouped button does NOT auto-check the sibling; `ToggleSidebarTab()` explicitly assigns `IsChecked = true` on the target
- **Persistence**: reuses the existing `ShowToolsPanel` bool setting (`true` = Tools tab active at startup)

### Locales
- +4 keys (EN/FR): `SidebarTabServers`, `SidebarTabTools`, `A11ySidebarTabServers`, `A11ySidebarTabTools`

### Removed
- `Themes/DarkTheme.xaml`, `Themes/LightTheme.xaml`
- `ToolsQuickPanel`, `BtnToggleToolsPanel`, `ToolsToggleChevron`, `Mw_ToolsToggleLabel`, `Mw_ToolsPanelHeaderLabel`, `Mw_ToolsPanelNoResults`, `Mw_ToolsPanelContextText`, `Mw_ToolsScanIndicator`, `ToolsCategoryStack`, `ToolsPanelScroll`, `ToolsPanelScrollHint`
- `MainWindow.ToggleToolsPanel()`, `PopulateToolsPanel()`, `CreateToolCard()`, `PersistToolsPanelState()`, `OnToolsFilterChanged`, `OnToolsPanelScrollChanged`, `_toolsPanelPopulated`
- `App.xaml.cs::ApplyThemeFromSettings()` and `MainViewModel::OnThemeChanged()` — both switch statements moved into the centralized `ThemeService`

### CI fix — SDK 10.0.201 overload resolution
- `dotnet format` on SDK 10.0.201 mis-inferred `var queryLower = query.ToLowerInvariant()` as `int` in 3 specific sites with lambda / nested `var` contexts, routing `string.Contains(string, StringComparison)` to the `char` overload (CS1503). Replaced `var` with explicit `string` types in `MainWindow.OnSettingsSearchTextChanged` and `OnSidebarToolsFilterChanged` — 67 other call sites in the codebase were unaffected
- `dotnet format` pass applied in a separate commit to fix ENDOFLINE / CHARSET / IMPORTS drift that had accumulated across recent PRs

### Housekeeping
- Tests: 1,730 passing
- i18n: parity maintained EN/FR (+4 keys)
- CI build: .NET 10.0.x runner

---

## [Unreleased] - 2026-04-02

### Terminal keyboard fix — Delete key no longer triggers server deletion

- **Root cause**: WebView2 SDK routes keys via `AcceleratorKeyPressed` → synthetic WPF `KeyDown`, but `Keyboard.FocusedElement` stays stale on the TreeView. The previous fallback (`FindAncestor<TreeView>` exclusion) was self-defeating in the most common scenario (user clicks TreeView then terminal).
- **Fix**: Check `e.OriginalSource is WebView2` in the `OnKeyDown` handler — the SDK always sets `OriginalSource` to the WebView2 control for terminal-originated keys. Removed the unreliable `ActiveSession.ConnectionType` + `TreeView` exclusion fallback.

## [Unreleased] - 2026-04-01

### Command Library UX audit — layout, responsiveness, feedback, performance

#### Layout
- **Generator panel sticky buttons**: Copy/Send/Edit/Delete action buttons moved outside the ScrollViewer into a fixed Grid row — always visible regardless of parameter count, notes, or examples in the scrollable area
- **Generator ↔ History mutual exclusion**: selecting an action auto-closes the History panel; toggling History auto-closes the Generator — prevents both panels from crushing the action list on 1080p split panes
- **Responsive filter bar**: replaced DockPanel with Grid+WrapPanel — search TextBox always gets full width (own row), filter ComboBoxes wrap gracefully on narrow panes instead of crushing the search input
- **HistoryList themed hover/select**: added ControlTemplate with SurfaceBrush (hover) and CardBrush (select) matching the ActionList visual treatment

#### Feedback
- **Loading indicator**: ToolLoadingBarStyle ProgressBar shown during initial data load with `finally` block for guaranteed cleanup on error
- **Example click clears stale validation**: clicking a pre-built example now clears any previous parameter validation error

#### Performance
- **O(1) search filtering**: replaced `_searchResults.Any(r => r.Id == ...)` (O(n) per item) with a `HashSet<string>` lookup in `FilterPredicate`

#### Dialog
- **DefaultValue watermark**: both Windows and Linux parameter DefaultValue TextBoxes now use `WatermarkTextBoxStyle` — placeholder text ("Default value") visible when empty and unfocused

---

## [v2026.033108] - 2026-03-31

### Fix tunnel scan — host discovery, per-probe timeout, zombie prevention

#### Network Cartography (critical fix)
- **Root cause**: scanning via SSH tunnel found only 1 host (the gateway itself) instead of the full subnet. Two bugs: (1) no host discovery phase (ping sweep, ARP) — only hosts with open ports on the scanned list were returned, (2) sequential `/dev/tcp` probes with no per-probe timeout — a single filtered port blocked the entire scan per IP for 20-127 seconds (kernel TCP retransmit timeout), causing `CommandTimeout` to kill the command before most ports were tested
- **Phase 1 — Host discovery**: batch ping sweep via SSH (all IPs as parallel background jobs in a single `CreateCommand`), ARP table read (`/proc/net/arp`) for ICMP-blocked hosts, automatic fallback to full-subnet scan when ping is restricted on the gateway
- **Phase 2 — Batch reverse DNS**: single SSH command for all alive hosts (was one command per IP)
- **Phase 3 — Parallel port probes**: all ports for a host run as background bash jobs simultaneously (`(echo >/dev/tcp/IP/$p && echo $p) &`), bounded by `sleep 5; kill $(jobs -p); wait` fence — no single filtered port can block the scan
- **Explicit `bash -c`**: ensures `/dev/tcp` support regardless of the gateway's login shell (`dash`/`sh` lack it)
- All alive hosts now included in results (even those with no open ports), matching direct scan behavior

#### Port Scanner, Banner Grabber, Firewall Tester, Default Credential Scanner
- **Same `/dev/tcp` fix**: all four tools' tunnel probe functions wrapped with `timeout 2 bash -c` to prevent filtered ports from leaving zombie bash processes on the gateway
- `CommandTimeout` raised from 2-3s to 5s as a safety net (per-probe timeout is now the primary mechanism)

#### i18n
- +3 keys (EN/FR): `ToolNetMapTunnelPingSweep`, `ToolNetMapTunnelDiscovered`, `ToolNetMapTunnelScanningHost`

#### Housekeeping
- Tests: 1,714 passing (unchanged)
- i18n: 4,688 keys (EN/FR parity maintained)

---

## [v2026.033006] - 2026-03-30

### UX audit remediation — Dispose memory leaks, i18n format strings

#### Memory Leak Fixes
- **18 tool views**: added event handler unsubscription (`-=`) in `Dispose()` for all subscriptions (`+=`) made in constructors — prevents views from being retained in memory after tab closure
- Affected views: ArpMonitor, Base64, CertInspector, CrontabBuilder, DateTimeConverter, HackerSimulator, HttpStatusCodes, IpConverter, NetworkCalculator, NetworkCartography, Notes, Ping, PortScanner, ServiceStatus, SubnetCalculator, TextCaseConverter, TextDiff, MilkdownEditor
- Timer cleanup: `Tick -= handler` added before `Stop()` on all `DispatcherTimer` fields (Arp, Ping, ServiceStatus, HackerSimulator, TextDiff, DateTimeConverter)
- WebView2 cleanup: `NavigationStarting` and `WebMessageReceived` unsubscribed with null guard in MilkdownEditorControl

#### i18n
- **DefaultCredentialView**: replaced string concatenation (`service + " " + L(key)`) with proper `string.Format(L(key), service)` for RTL-safe formatting
- Updated locale keys `ToolDefCredDetailAccepted` and `ToolDefCredDetailRejected` to use `{0}` placeholder (EN + FR)

#### Housekeeping
- Tests: 1,714 passing (unchanged)
- i18n: 4,685 keys (EN/FR parity maintained)

---

## [v2026.033005] - 2026-03-30

### Security audit remediation — context-aware sanitization, external tools, a11y

#### Security
- **Context-aware placeholder sanitization**: `InputValidator.IsShellTarget()` detects shell interpreters (cmd.exe, PowerShell, bash, sh, zsh, wsl, cscript, wscript, mshta) and script extensions (.bat, .cmd, .ps1, .vbs, .js, .wsf, .hta). Shell targets get strict metacharacter stripping; regular .exe targets get relaxed stripping that preserves `()`, `'`, `%` in legitimate values (double quotes always stripped for MSVC CRT safety)
- Applied to both `ExternalToolDefinition.ResolveArguments()` (user-defined tools) and `CommandCredentialProvider.ExpandTemplate()` (credential provider CLI)
- **VNC WebSocket Origin validation**: replaced `StartsWith` with exact `Uri` host matching to prevent CSWSH subdomain bypass
- **Command palette tool shadowing**: external tools now always searched alongside native tool prefix matches (previously hidden when a native tool prefix matched first)
- **External tools config validation**: save blocked on empty name/path or duplicate names with inline error via `ValidationSummary`; `ExternalToolItemViewModel` uses `[Required]` + `[NotifyDataErrorInfo]`
- **Credential provider soft failures surfaced**: `ShowWarning()` dialog when `GetCredentialAsync()` returns null (empty output or non-zero exit) instead of silent fallthrough
- **ServerDialog async**: removed `.GetAwaiter().GetResult()` blocking calls, replaced with async `Loaded` handler
- **RunHidden alignment**: `CreateNoWindow = true` added to context menu launch path (was only on palette path)

#### UX
- **External tools editor**: Browse button for working directory; structured placeholder help panel; live command preview with resolved placeholders from selected server; Test button to launch from Settings; binary existence validation on save
- **Credential provider setup**: preset dropdown (KeePassXC, Bitwarden CLI, 1Password CLI, pass); database path browse button; Test button with inline feedback (success/no result/timeout/error); placeholder hint below command field
- **Onboarding interactive**: each step now navigates to the relevant UI area (Step 1 → Servers tab, Step 2 → Settings, Step 3 → enables Tools panel); keyboard a11y (Escape, Tab cycle, focus, synced AutomationProperties.Name)
- **Configurable external tool timeout**: `ExternalToolTimeoutMs` in Settings > Advanced (default 60s, range 5s–600s), replaces hardcoded 60s in ExternalToolWrapperView
- **Tool scan indicator**: "Scanning..." label on Tools panel header during background third-party tool detection

#### Previous (v2026.033005-pre)
- **External tool placeholder resolution**: `{Port}` now resolves to the protocol-specific port (SSH→22, FTP→21, VNC→5900, Telnet→23) instead of the generic RDP port; `{KeyFile}` placeholder now populated from server SSH key path
- **Process timeout cleanup**: external tool wrapper kills the process tree on timeout/cancel in both standard and elevated (UAC) code paths
- **Credential provider stderr deadlock**: stderr is now drained concurrently to prevent 4KB pipe buffer deadlock on Windows
- **Settings dirty flag**: inline edits to external tool properties now correctly mark Settings as dirty
- **ServerDialog i18n**: 44 new keys (EN/FR) covering port labels, help text, session kinds, mode summaries, tunnel descriptions, and gateway captions for all 8 protocols
- **Ctrl+K palette**: external tool placeholders resolved against selected server when available

#### Housekeeping
- `InternalsVisibleTo` added to `Heimdall.Core.csproj` for `Heimdall.Core.Tests` (ExpandTemplate testing)
- `VENDORED.md` manifests added for Assets/Tools (plink 0.83, gsudo 2.5.1), Assets/vnc (noVNC 1.5.0, pako 1.0.3), Assets/drawio (26.0.9) — upstream versions, licenses, and review dates
- i18n: +189 keys (4,685 total, EN/FR parity)
- Tests: 1,714 passing (+81 new: IsShellTarget, context-aware sanitization, ExpandTemplate relaxed/strict paths)

---

## [v2026.033004] - 2026-03-30

### Network Cartography — multi-probe discovery, new columns, SNMP OID classifier

#### Discovery Pipeline
- ARP table seeded before ping sweep: hosts known to the OS bypass ICMP
- Multi-probe fallback for undiscovered IPs: reverse DNS + NetBIOS Name Service + TCP connect on 5 key ports (22, 80, 443, 445, 3389)
- Filter empty hosts: `HostScanResult.HasMeaningfulData` removes IPs with no ports, hostname, role, or metadata from both display and CSV export

#### DataGrid & Export
- New **MAC Address** column (after IP)
- New **Latency** column (after OS, shows ping round-trip in ms)
- CSV export filters out empty hosts (no more 238 phantom rows on a /24)

#### SNMP Enterprise OID Classifier
- Cisco: routers (1.3.6.1.4.1.9.1), Catalyst switches (9.5), switches (9.6) — confidence 80-85%
- Juniper, MikroTik, Fortinet, Palo Alto, VMware, Microsoft OID branches
- Boosts role classification confidence on OID match

#### Housekeeping
- i18n: +2 keys (4,452 total, EN/FR parity)
- Tests: 1,610 passing

---

## [v2026.033003] - 2026-03-30

### UX audit fixes, CIDR auto-detection, and scan timeout resilience

#### Accessibility & Keyboard
- AutomationProperties.Name on MainWindow navigation tabs (Servers, Tunnels, Scheduled, Settings, About) via `{loc:Translate}`
- TabIndex keyboard navigation added to 5 tool views: HackerSimulator, CronJobManager, PasswordAudit, SshKeyAudit, DiagramEditor
- GridSplitter accessibility label in SplitContainerControl
- WCAG contrast fix: replace Opacity="0.6" on settings unit suffixes with TextDisabledBrush

#### Visual
- Fix tool card hover: remove default WPF button chrome (bare ContentPresenter template), use HighlightBrush for hover background
- Fix SecNumCloudAuditView CornerRadius cast error (`CornerRadius` resource was cast as `Double`)

#### Network Tools
- Ping Monitor: add gateway routing via SSH (`CmbRouteVia` selector, tunneled ping via SSH exec)
- SecNumCloud Audit: auto-detect local CIDR on init, detect remote CIDR on gateway selection
- Extract shared `SubnetDetector` helper from NetworkCartography (reusable across tools)

#### Critical Bug Fix — Scan Timeout Resilience
- Fix scans silently aborting when per-operation timeouts fire: `CancellationTokenSource(timeout)` + linked token `OperationCanceledException` was indistinguishable from user cancellation
- 13 catch sites fixed across 7 files: CartographyEngine (ProbePortAsync, InspectTlsWithHttpAsync), SecNumCloudAuditEngine (6 check methods), NetworkScanner, HttpFingerprinter, FaviconHasher, BannerGrabberView, CertInspectorView
- Fix pattern: `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` absorbs per-operation timeouts without aborting the entire scan

#### Housekeeping
- i18n: +11 keys (4,450 total, EN/FR parity)
- Tests: 1,610 passing (283 SSH + 131 App + 1,196 Core)

---

## [v2026.032903] - 2026-03-29

### Comprehensive UX audit — accessibility, async guards, empty states, keyboard across 49 tools

#### Accessibility
- Explicit `TabIndex` on 45 tool views (top-to-bottom, left-to-right visual order)
- 15 new empty state panels with localized icon + hint text
- 24 empty states migrated to shared `ToolEmptyStateIconStyle`/`ToolEmptyStateTextStyle`
- Watermarks added: PasswordAudit, SshKeyAudit, ServiceStatus
- DiagramEditor: tooltips on 13 toolbar buttons

#### Async & Keyboard
- SshKeyGenerator/CertificateGenerator: key generation moved to `Task.Run` (unblocks UI thread)
- TextDiffView: double-click guard + input disable during comparison
- Enter key wired: ArpMonitor, TextCaseConverter (`Ctrl+Enter`), CrontabBuilder, DateTimeConverter
- Focus on load: CrontabBuilder, ServiceStatus, DiagramEditor, HackerSimulator
- DiagramEditor: toolbar disabled until WebView2 initialization completes

#### Code Quality
- `DefaultPorts`: extended with 22 named constants, replacing magic numbers across presets
- `ToolAsyncStateController`: fix primary constructor redundant field re-declaration
- `ToolPickerDialog`: input validation via `InputValidator.Validate()`, trigger ordering fix
- `NetworkToolPresets`: DNS server labels localized, `DnsServerPreset` nested in class
- Remove dead `showUnpin` parameter from `CreateToolsTabCard`
- Fix regex false positives in `ToolXamlInputHardcodingTests`
- Fix fragile attribute-order assumption in `DenseToolTabOrderTests`

#### Housekeeping
- Remove 6 obsolete docs (UX_GITHUB_ISSUES.md, network-discovery research, 4 audit screenshots)
- i18n: +24 keys (4,453 total, EN/FR parity)
- Tests: 1,610 passing (283 SSH + 131 App + 1,196 Core)

---

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
