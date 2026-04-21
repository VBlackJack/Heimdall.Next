# Heimdall.Next — Roadmap Synthesis (SSH + Terminal + SFTP + RDP)

**Date**: 2026-04-19
**Batch**: b54 (synthesis and prioritized roadmap)
**Consolidates**: `docs/audit-gap-ssh-terminal-sftp-2026-04-19.md` (b52) + `docs/audit-gap-rdp-2026-04-19.md` (b53).
**Target audience (resolved)**: admin / power user migrating from a mix of **PuTTY + MobaXterm + mstsc / mRemoteNG**. This is the anchor for every priority call below.
**Scope**: cross-protocol prioritization. Does not re-document gaps (see b52 / b53); does commit to an ordering.

---

## 0. Executive summary

Heimdall.Next ships a protocol core that is already ahead of every reference product on a handful of axes (typed failure taxonomy, multi-hop gateway chains, reference-counted tunnels, RDP 41-code decoder, temp `.rdp` hardening, DPI-aware resize, sudo-fallback SFTP editing). What it lacks — and what prevents a PuTTY/MobaXterm/mstsc user from migrating on the spot — is **not** more protocol depth but:

1. **Migration ergonomics**: in-app key generation (PuTTYgen), `.rdp` bulk import, `~/.ssh/config` import, session-tree round-trip.
2. **Daily reliability**: auto-reconnect on drop, session-state restore on next launch.
3. **Paper-cut settings**: configurable terminal scrollback, regex search, cursor shape, bell, per-server keep-alive, RD Gateway UI field.
4. **Power-user throughput**: SFTP transfer queue with recursive upload and resume, per-monitor selection, `.rdp` export.

The roadmap therefore has three waves:

- **Now** — 6 batches that close the migration-blocker set (mstsc `.rdp` import, PuTTYgen, RD Gateway UI, session-state restore plumbing, auto-reconnect on SSH drop, terminal paper-cuts bundle).
- **Next** — 8–10 batches that close the "daily power-user" gaps (SFTP transfer engine, OpenSSH config import, RDP multi-monitor refinement, hot-toggle redirections, connection sequences pre/post, terminal color-scheme editor).
- **Later** — items intentionally deferred per the defaults Julien fixed (vault, Azure AD / AVD, embedded webcam, Serial, X11 end-to-end, scripted macros, session replay).

With Now complete, Heimdall.Next can credibly claim migration-readiness for PuTTY and mstsc users. With Now + Next complete, it is credibly better than both PuTTY and MobaXterm on the primary user journeys (launch, security, transfer, resilience, session management) while retaining the strengths already above every reference.

---

## 1. Method

### 1.1 Inputs
- `docs/audit-gap-ssh-terminal-sftp-2026-04-19.md` (107 gaps: S1–S31 + T1–T38 + F1–F38; 9 clusters A–I).
- `docs/audit-gap-rdp-2026-04-19.md` (71 gaps: L + G + D + Rd + Rc + M; 9 clusters RA–RI).

### 1.2 Prioritization framework
- **Migration impact** per reference: mstsc, PuTTY, MobaXterm, mRemoteNG.
- **Effort** per the rubric in b52 §1.4 (S ≤ 1 day / M ≤ 1 week / L 1–3 weeks / XL > 3 weeks).
- **Strategic weight** given the target audience (admin / power user).
- **Shared plumbing**: gaps that share infrastructure (e.g. SSH session resume + terminal session restore + RDP tab restore all share a session-state snapshot mechanism) are bundled into a single batch when possible.

### 1.3 Applied defaults (locked by Julien, 2026-04-19)
| Decision | Value applied | Consequence |
|---|---|---|
| Target audience | Admin / power user from PuTTY + MobaXterm + mstsc/mRemoteNG | Orders the matrix against four baselines at once. |
| Credential vault (G14) | Not immediate priority | Pushed to Later; does not gate any Now/Next batch. |
| Azure AD / AVD (G5) | Secondary | Pushed to Later; requires backend swap discussion. |
| Embedded webcam (Rd11) | Low priority | Pushed to Later; external-mode webcam already covers 90 % of demand. |
| Hot-toggle redirections (Rd13) | Medium | Included in Next, not Now. |
| Session-state restore (M9 + T31 + S24) | High priority | Included in Now as shared plumbing. |
| Vault vs connection sequences | Connection sequences first | Cluster RH (M10) before Cluster RI (G14). |

---

## 2. Top 10 global gaps (cross-protocol ranking)

Ranked by **migration-blocker weight × inverse effort**. Cross-reference column points to the originating gap in b52 / b53.

| Rank | Gap | Protocol | Source | Impact | Effort | Why here |
|---|---|---|---|---|---|---|
| 1 | **Session-state restore on launch** | All | S24 + T31 + M9 | Blocker (MobaXterm / mRemoteNG) | M (shared) | Single biggest "I feel at home" cue for mRemoteNG / MobaXterm users; one infrastructure, four protocols benefit. |
| 2 | **`.rdp` bulk import + drag-drop + file association + export** | RDP | L1 + L2 + L3 + M19 | Blocker (mstsc) | S × 4 | Parser exists, tests exist — pure UI wiring. Highest ROI of the entire backlog. |
| 3 | **In-app SSH key generation + `.ppk` ↔ OpenSSH conversion** | SSH | S6 + S7 | Blocker (PuTTY) | M × 2 | Without it, PuTTY users stay on PuTTYgen forever; audit engine already parses all target formats. |
| 4 | **Auto-reconnect on SSH drop** | SSH | S23 | Blocker (MobaXterm) | M | `SshFailureCode` already tells us recoverable-vs-fatal; retry loop + state-machine wiring is the work. |
| 5 | **RD Gateway UI field in `ServerDialog` (embedded mode)** | RDP | G7 | Blocker (enterprise) | S | Data model + `.rdp` generator already handle it. One dialog field. |
| 6 | **SFTP transfer queue with recursive upload and resume** | SFTP | F12 + F10 + F14 | Blocker (MobaXterm) | L (anchor) + M + M | Foundational for every SFTP throughput gap (F11 / F13 / F17 depend on F12). |
| 7 | **Configurable terminal scrollback + regex search + cursor shape + bell + bracketed paste** | Terminal | T4 + T6 + T13 + T17 + T21 | High (PuTTY / MobaXterm) | S × 5 | Five symbolic paper-cuts, all single-line settings; one bundled batch. |
| 8 | **Per-monitor selection** | RDP | D7 | Blocker (power user multi-display) | M | mstsc's "Choose monitors" is table stakes for anyone with asymmetric displays. |
| 9 | **OpenSSH config import (`~/.ssh/config`)** | SSH | S26 | High (any Linux-aware user) | M | Round-trip with the existing exporter; bulk-onboard the same way `.rdp` import will for mstsc estates. |
| 10 | **Per-server keep-alive + max-retries override (RDP and SSH)** | RDP + SSH | Rc2 + Rc7 + S21 (already) | High (daily reliability tuning) | S × 2 | Removes the two hard-coded constants in `RdpActiveXHost.cs` (60 s keep-alive, 20 attempts); per-server override. |

**Not in the top 10 but explicitly close**: SFTP persisted bookmarks (F33), fullscreen keyboard shortcut (D9), inline tab rename (M4), host-key management UI (S16), redirection-failure notification (Rd15), quick-connect confirmation UI (L6), gateway same-creds-as-remote toggle (G8). All land in Now or the first half of Next.

---

## 3. Top 10 strengths to preserve

Capabilities that are already superior to **every** reference product. Any refactor, rewrite, or dependency upgrade must preserve them — they are the differentiators.

| # | Strength | Where | Why it matters |
|---|---|---|---|
| 1 | **Typed SSH failure taxonomy — 26 codes** | `SshFailureCode` + `FailureClassifier` | No reference product exposes structured errors. Enables recoverable-vs-fatal logic for auto-reconnect, better telemetry, better UI copy. |
| 2 | **RDP disconnect-reason decoder — 41 codes + i18n** | `RdpActiveXHost.GetDisconnectReasonKey()` | mstsc shows generic text; Royal TS / mRemoteNG show numeric codes. Heimdall localizes both concept and language. |
| 3 | **Multi-hop gateway chains with TOFU per hop** | `GatewayChainResolver` + `HostKeyStore` | MobaXterm jump-host is single-level in practice; PuTTY's `ProxyCommand` requires manual shell plumbing. Heimdall is native, typed, cycle-safe. |
| 4 | **Reference-counted tunnel reuse** | `TunnelManager` | Correctness-level differentiator. Multiple sessions share the same live tunnel; teardown is automatic when the last consumer disconnects. |
| 5 | **SmartPasteGuard — 16 destructive-pattern regexes** | `src/Heimdall.Terminal/SmartPasteGuard.cs` | Paste `rm -rf /` into MobaXterm or PuTTY — it runs. Heimdall asks. A safety surface worth advertising. |
| 6 | **Temp `.rdp` hardening — CRLF + ACL + memory zeroing** | `RdpFileGenerator` + `SecureFileWriter` | CWE-93 mitigation + per-file ACL + plaintext password zero-after-use. No reference product does this. |
| 7 | **CredentialAutofill with FR/EN UI Automation + WM_SETTEXT** | `CredentialAutofill` + `CredentialManagerHelper` | Replaces the rigid CredMan-only handoff of mstsc / Royal TS. Handles multiple dialog classes, foreign-language Windows, multi-dialog contention. |
| 8 | **DPI-aware physical pixel conversion (RDP)** | `PresentationSource.CompositionTarget.TransformToDevice` in `EmbeddedRdpView` | Correct rendering at 125 / 150 / 200 % scale out of the box. mRemoteNG notoriously struggles here. |
| 9 | **Sudo-fallback SFTP editing (`sudo cat` / `sudo tee` pipeline)** | `RemoteFileEditor` | Edit a root-owned file on a server where your user cannot `chmod` — works transparently. MobaXterm requires a shell detour; PSFTP doesn't attempt it. |
| 10 | **SSH-tunneled RDP with ref-counted tunnel reuse** | `EmbeddedSessionManager` + `TunnelManager` | RDP over an SSH jump, with the tunnel shared across multiple sibling sessions. mRemoteNG requires a separate PuTTY session; mstsc has no equivalent. |

Runners-up also worth preserving explicitly: **auth preflight check** (S29), **dual-path `UpdateResolution`** (`UpdateSessionDisplaySettings` → `Reconnect` fallback), **COM pre-warm** (~400 ms saved on first RDP connect), **AspectRatioManager with even-pixel enforcement** (5 modes, unique across references), **auto-open SFTP on SSH** tighter than MobaXterm's fixed left dock.

---

## 4. Roadmap in waves

### 4.1 Wave `Now` — migration-blocker clear (≈ 6 batches)

**Goal**: after Now, a user coming from PuTTY, MobaXterm, mstsc, or mRemoteNG can migrate without looking back. No "I need to open PuTTYgen real quick" moments, no "I can't find where to import my `.rdp` files" moments, no "it disconnected and I lost my tabs" moments.

| # | Batch | Scope | Primary gaps | Effort | Depends |
|---|---|---|---|---|---|
| N1 | **Session-state snapshot infrastructure** | Persist open tabs + split layouts + per-protocol state at a graceful shutdown and on a heartbeat; restore-on-launch with per-session reconnect-or-confirm prompt (defaults to "confirm", configurable). | S24 + T31 + M9 + Rc8 | M | — |
| N2 | **`.rdp` bulk migration** | Import button in ServerDialog, drag-drop handler on the main window, file-association registration (per-user), "Export as `.rdp`" action on a server profile. | L1 + L2 + L3 + M19 | S × 4 | — |
| N3 | **PuTTYgen-equivalent key tool** | In-app key generation (RSA / ECDSA / Ed25519), passphrase, export OpenSSH + `.ppk`, comment edit, fingerprint display; host-key management UI as a companion. | S6 + S7 + S16 | M + M + S | — |
| N4 | **RD Gateway UI + gateway-creds toggle + keep-alive knobs** | RD Gateway hostname field in `ServerDialog`, "use same credentials" toggle, per-server RDP keep-alive interval, per-server RDP max-retries override. | G7 + G8 + Rc2 + Rc7 | S + S + S + S | — |
| N5 | **SSH auto-reconnect on drop** | Bounded retry with exponential back-off on any `SshFailureCode` classified recoverable; visible state machine progression (Connecting → Retrying → Disconnected); cancellable from the reconnect overlay. | S23 + Rc10 (RDP back-off curve by analogy) | M | >> N1 (state snapshot for the restart case) |
| N6 | **Terminal paper-cuts bundle** | Configurable scrollback, regex search, cursor shape, cursor blink toggle, bell (audio / visual / taskbar), bracketed-paste negotiation, fullscreen keyboard shortcut (RDP side). | T4 + T6 + T13 + T14 + T17 + T21 + D9 | S × 7 | — |

Total **Now** effort ≈ 1 M + 8 S + 3 M + 4 S + 1 M + 7 S = roughly **5 weeks** of pair-architect cadence (our per-batch mean is 3–4 days including validation). Reasonable as a focused quarter sprint.

### 4.2 Wave `Next` — power-user depth (≈ 8–10 batches)

**Goal**: after Next, Heimdall.Next is unambiguously better than PuTTY and MobaXterm on the primary journeys — launch, authentication, transfer, resilience, multi-screen, session management.

| # | Batch | Scope | Primary gaps | Effort | Depends |
|---|---|---|---|---|---|
| X1 | **SFTP transfer queue foundation** | Queue model (FIFO + cancellable), queue view with per-item progress, pause/resume/cancel semantics, persistence across disconnections. | F12 (anchor) | L | — |
| X2 | **Recursive upload + resume** | Walk local tree, create remote dirs, upload files; resume partial transfers via SFTP range semantics. | F10 + F14 | M + M | >> X1 |
| X3 | **Parallel transfers + speed indicator** | Configurable concurrency limit, shared progress widget, EWMA bytes-per-second computation. | F13 + F17 | M + S | >> X1 |
| X4 | **SFTP browser completeness** | Persisted bookmarks, remote recursive find, drag-drop both ways including folders. | F33 + F27 + F8 + F9 | S + M + M + M | >> X1 for drag-in |
| X5 | **Per-monitor selection + DPI-override-at-connect** | RDP "Choose monitors" UI listing physical displays with id/resolution/position + ticks; optional override of auto-detected DPI scale. | D7 + D11 | M + S | — |
| X6 | **OpenSSH config import (round-trip with S27)** | Parse `~/.ssh/config`, preview diff, bulk-import as server profiles and gateways, preserve comments; export-and-rewrite round trip check. | S26 | M | — |
| X7 | **RDP hot-toggle redirections** | Toggle clipboard / drives / printers / audio at runtime; COM-level re-apply where supported, soft reconnect fallback where not; notification when a redirection is blocked or fails. | Rd13 + Rd15 | L + S | — |
| X8 | **Inline tab rename + session notes + session search** | Explicit click-to-rename on tab label, per-session notes persisted with the profile, cross-tree search refinement. | M4 + M11 + M18 | S + S + S | — |
| X9 | **Connection sequences (pre/post actions)** | Pre-connect actions (open file, run script), post-connect actions (send initial commands, focus pane), failure-path actions. Includes per-protocol external-tool integration. | M10 + M14 | L + M | — |
| X10 | **Terminal custom color-scheme editor + char-set override** | JSON-backed scheme, editor view under Settings, hot-reload into xterm.js; per-session character-set override for legacy CJK / Latin-1 servers. | T3 + T36 | M + S | — |

Total **Next** effort ≈ 1 L + 2 M + 2 M + 1 S + 3 M + 1 S + 1 M + 1 L + 1 S + 3 S + 1 L + 1 M + 1 S = **2–3 months** of pair-architect cadence.

### 4.3 Wave `Later` — deferred by intent

Documented so future audits don't re-litigate. Each item has an explicit reason to wait.

| Item | b52 / b53 code | Why deferred |
|---|---|---|
| Credential vault (team-shareable, rotation, scoped) | G14 | Per Julien's default: not immediate priority. Keeps Heimdall.Next positioned as single-user-first. Revisit when team-deployment signal surfaces. |
| Azure AD / AVD auth | G5 | Secondary per default. Requires backend swap from MsTscAx to MSRDC / freerdp — XL architectural decision, not a batch. Revisit when cloud-migrated-estate demand is confirmed. |
| Embedded-mode webcam redirection | Rd11 | Low priority per default. External-mode already covers 90 % of webcam demand. COM surface (`IMsRdpClientNonScriptable7.CameraRedirConfigCollection`) is complex; cost not justified today. |
| Serial / COM port | T32 | Hardware users are a distinct segment. Revisit if demand signal emerges; do not pre-build. |
| X11 forwarding end-to-end (bundled X-server) | S25 | Linux-centric users are a minority of our target audience. Current half-step (flag sent, channel unclear) may already suffice for users running their own VcXsrv; revisit if complaints escalate. |
| Mosh / eternal terminal | T33 | Niche. Do not invest until there is a concrete user request. |
| Scripted macros (Python / JS / Lua) | T30 | XL, niche, SecureCRT territory. Keep the existing keystroke-replay macro system as the answer for now. |
| In-app text editor | F30 | External-editor pivot already works with the sudo fallback. Building a built-in editor is XL and out of connection-manager scope. |
| Session replay (VT-faithful) | T10 | Transcript recording already covers the common forensic need. Replay is a niche power feature. |
| Web-based RDP (HTML5) | — (b53 Appendix A) | Out of scope for a desktop connection manager. |
| Plugin / extension API for protocol handlers | — (b53 Appendix A) | No identified user request; preserve the option, don't pre-build. |
| Mobile companion app | — (b53 Appendix A) | Out of scope. |
| RDP performance-profile presets ("Modem / Satellite / LAN") | — (b53 Appendix A) | Rarely touched on modern networks; the raw `RdpPerformanceFlags` field already supports manual tuning. |

---

## 5. Concrete batch candidates for b55+

One batch = one prompt, one supervised Codex run, one closure report. Sized to the usual 1–2-files + tests cadence, with a validation step tied to the existing test baseline (3750 passing + 6 skipped). Items below are ordered; dependencies are called out.

### b55 — Session-state snapshot + restore-on-launch
**Scope**: new `Heimdall.Core.SessionState.SessionSnapshotService` (singleton, persisted JSON under `%LOCALAPPDATA%\Heimdall.Next\sessions\`). On `MainWindow.Closing` or a 30 s heartbeat, capture open tabs, split tree, pane protocols, pane state. On startup, detect a non-empty snapshot, open a "Restore previous session?" modal with per-session checkboxes (default all-checked) and a "Don't restore" button. Connect confirmed sessions in parallel-limited batches.
**Files**: new `SessionSnapshotService.cs`, `SnapshotRestoreDialog.xaml(.cs)`, bindings in `MainViewModel`, persistence hook in `App.xaml.cs`.
**Tests**: snapshot round-trip JSON, restore with 0/1/many sessions, cancelled restore preserves snapshot.
**Validation**: full suite green; manual 3-session restore; snapshot file ACL checked.

### b56 — `.rdp` import button + drag-drop handler
**Scope**: "Import `.rdp`" button in the server list toolbar, wired to the existing `RdpFileImporter`. Multi-select file dialog, parse each file, show a preview grid (host / user / gateway / detected port), "Import N" button. Drag-drop handler on `MainWindow` accepting `DataFormats.FileDrop` with `.rdp` extension filter, routing to the same flow.
**Files**: `ServerListView.xaml(.cs)` (add button + handler), new `RdpImportPreviewDialog.xaml(.cs)`, `App.xaml.cs` drop-target wiring.
**Tests**: `RdpFileImporterTests` already cover parser; add `RdpImportPreviewDialogTests` for preview + confirm flow.
**Validation**: import 5 representative `.rdp` files (plain / with gateway / with redirections / with domain / malformed).

### b57 — `.rdp` file association + "Export as `.rdp`" action
**Scope**: per-user file association via `HKCU\Software\Classes\.rdp` + `ProgID` entry, one-time registration on first launch (gated by `AppSettings.RdpFileAssociationRequested`), user-confirmable via Settings. Context-menu "Export as `.rdp`" on server list items, routing to `RdpFileGenerator` + `SaveFileDialog`.
**Files**: new `FileAssociationService.cs`, Settings page addition, `ServerListView` context menu addition.
**Tests**: `FileAssociationService` unit tests with a registry mock; export round-trip with `RdpFileImporter` of the generated file.
**Validation**: file association appears in Windows "Open with" dialog; round-trip import-export preserves all fields.

### b58 — PuTTYgen-equivalent key generation tool
**Scope**: new `ToolRegistry` entry `SSH_KEY_GEN` alongside `SSH_KEY_AUDIT`. UI with algorithm picker (RSA 2048/3072/4096, ECDSA P-256/384/521, Ed25519), passphrase field with confirm, generate button, preview of fingerprint + public key + private key; export buttons (OpenSSH, `.ppk`). Backed by SSH.NET + BouncyCastle for `.ppk` serialization.
**Files**: new `src/Heimdall.Ssh/SshKeyGenerator.cs`, new `src/Heimdall.App/Views/Tools/SshKeyGenerator*.xaml(.cs)`, `ToolRegistry` entry, `IconGeometries.xaml` addition.
**Tests**: `SshKeyGeneratorTests` round-trip generate → serialize → reload for each algorithm; `.ppk` v3 format validation.
**Validation**: generated keys load in PuTTY, OpenSSH, and Heimdall itself.

### b59 — `.ppk` ↔ OpenSSH format conversion + host-key management UI
**Scope**: extend the generator tool with an "Import key" tab that accepts a file, detects format, optionally re-exports to the other format (with passphrase change option). Separate host-key management view listing `HostKeyStore.GetAllTrusted()` entries with remove / pin actions.
**Files**: extend `SshKeyGeneratorView`, new `HostKeyManagerView.xaml(.cs)`, `ToolRegistry` entry for host-key manager.
**Tests**: import-export round-trip for each format pair; host-key store mutation tests already exist — extend for "pinned" attribute.
**Validation**: convert a PuTTY-generated `.ppk` to OpenSSH and back, fingerprint unchanged.

### b60 — RD Gateway UI + gateway same-creds toggle
**Scope**: new RD Gateway section in `ServerDialog` RDP Step 2 (hostname field with `InputValidator.ValidateDomain`, checkbox "use same credentials as remote host", tooltip explaining the equivalence to mstsc's gateway tab). Binding to `ServerProfileDto.RdpGateway.*` (existing model).
**Files**: `ServerDialog.xaml(.cs)`, `ServerDialogViewModel.cs`.
**Tests**: `ServerDialogViewModelTests` round-trip gateway fields.
**Validation**: saved profile produces a `.rdp` file with correct `gatewayhostname:s:` and `gatewaycredentialssource:i:`.

### b61 — Per-server keep-alive + max-retries overrides (RDP + SSH already done)
**Scope**: replace hard-coded `RdpActiveXHost.KeepAliveInterval = 60000` and `MaxReconnectAttempts = 20` with bindings to `ServerProfileDto.RdpKeepAliveIntervalSeconds` (nullable, falls back to `AppSettings.RdpDefaultKeepAlive`) and `ServerProfileDto.RdpMaxReconnectAttempts`. ServerDialog Advanced tab exposes both. SSH keep-alive is already per-server; expose it in ServerDialog if not already surfaced.
**Files**: `RdpActiveXHost.cs`, `ServerProfileDto.cs`, `ServerDialog.xaml` Advanced section.
**Tests**: `RdpActiveXHostTests` for keep-alive propagation (add if not present); `ServerProfileDtoTests` serialization.
**Validation**: set keep-alive = 10 s, observe faster disconnect detection.

### b62 — SSH auto-reconnect on drop (with back-off and cancel)
**Scope**: new `SshReconnectPolicy` class that consumes an `SshFailureCode`, decides recoverable-vs-fatal, and drives a back-off loop (1 s → 2 s → 5 s → 15 s → 30 s, capped). `SshHandler` wires the policy to `SshShellSession` disconnection. Reconnect overlay (analog of RDP's) with attempt counter + cancel button.
**Files**: new `src/Heimdall.Ssh/SshReconnectPolicy.cs`, `SshHandler.cs`, new `SshReconnectOverlay` in `EmbeddedSshView.xaml`.
**Tests**: `SshReconnectPolicyTests` for each `SshFailureCode` category; cancellation honored at each back-off step.
**Validation**: disconnect the test SSH host mid-session, verify reconnect loop progresses and can be cancelled.

### b63 — Terminal paper-cuts bundle (T4 + T6 + T13 + T14 + T17 + T21)
**Scope**: six single-setting additions, one batch:
- `AppSettings.TerminalScrollback` (int, -1 = unlimited → maps to 1,000,000).
- Regex toggle in the search bar.
- `AppSettings.TerminalCursorStyle` (Block / Bar / Underline) + `TerminalCursorBlink`.
- `AppSettings.TerminalBellMode` (None / Beep / Visual / Taskbar) + OSC `\x07` handler.
- `\x1b[?2004h` emission on PTY open and magic-stripping on paste.
**Files**: `AppSettings.cs`, `terminal.html`, `EmbeddedSshView.xaml.cs`, `MessageBridge` bindings.
**Tests**: add `TerminalSettingsRoundTripTests`; manual QA for bell + cursor in each mode.
**Validation**: full suite green; manual bell + cursor + scrollback + regex check.

### b64 — Fullscreen keyboard shortcut (RDP) + inline tab rename
**Scope**: bind `Ctrl+Alt+Break` (mstsc-compat) to `SetFullscreen` toggle in `EmbeddedRdpView`. Add explicit click-to-rename on `SessionTab` header with `TextBox` overlay + Enter-commits / Esc-cancels.
**Files**: `EmbeddedRdpView.xaml.cs`, `SessionTab.xaml(.cs)`, `SessionTabViewModel.cs`.
**Tests**: `SessionTabViewModelTests` rename semantics; keyboard-shortcut routing test.
**Validation**: Ctrl+Alt+Break toggles fullscreen; tab rename survives restart (requires b55 snapshot for the session-scope case; permanent rename requires profile mutation).

### b65 — SFTP transfer queue foundation
**Scope**: new `src/Heimdall.Sftp/TransferQueue.cs` (FIFO of `TransferItem` records with status enum + cancellation + progress event). New `TransferQueueView` pane attached to SFTP sessions. Convert `EmbeddedSftpView` upload / download calls to enqueue. Single-worker initially (parallelism in b66).
**Files**: new `TransferQueue.cs`, new `TransferQueueView.xaml(.cs)`, rewire `EmbeddedSftpView.xaml.cs` upload/download entry points.
**Tests**: `TransferQueueTests` for ordering, cancellation, failure propagation.
**Validation**: queue five simultaneous uploads, cancel #3, observe correct state.

### b66 — Recursive upload + transfer resume
**Scope**: recursive enumerator for local folders, remote-dir creation, per-file enqueue. Resume via `SftpClient.Open(FileMode.Append)` + offset-from-remote-size.
**Files**: extend `TransferQueue`, extend `SftpBrowser`, `EmbeddedSftpView.xaml.cs`.
**Tests**: recursive upload of a nested tree; resume after forced disconnect mid-file.
**Validation**: upload a 3-level folder, interrupt, resume, verify byte-identical result.

### b67 — SFTP parallel transfers + speed indicator
**Scope**: concurrency setting (`AppSettings.SftpMaxParallelTransfers` default 3), token-bucket semaphore in `TransferQueue`. EWMA bytes/sec computed per transfer + aggregate header.
**Files**: extend `TransferQueue`, extend `TransferQueueView`.
**Tests**: concurrency bound honored; speed EWMA within tolerance.
**Validation**: 10-file upload with concurrency = 3, observe queue throughput vs sequential.

### b68 — SFTP browser completeness (bookmarks + recursive find + drag-drop folders)
**Scope**: `AppSettings.SftpBookmarks` (list of `{label, host, path}`), dropdown in `EmbeddedSftpView`. Remote recursive `find` invocation with results grid. Extend drag-drop to accept folders (recursively enqueue). Reverse drag-drop remote → Windows Explorer via `DoDragDrop` + `FileGroupDescriptor` stream.
**Files**: extend `EmbeddedSftpView`, `AppSettings.cs`, `SftpBrowser`.
**Tests**: bookmark persistence; recursive-find parsing.
**Validation**: drag a folder from Explorer onto the remote pane, observe recursive enqueue; drag a remote file to Explorer, observe download to the drop target.

### b69 — Per-monitor selection (RDP) + DPI override at connect
**Scope**: new dialog listing `Screen.AllScreens` with bounds / primary flag / working area / detected DPI; ticks persist per-server as `ServerProfileDto.RdpSelectedMonitorIds`. `RdpFileGenerator` emits `selectedmonitors:s:` (comma-separated mstsc IDs). Embedded mode passes the same via `IMsRdpClientNonScriptable5.DesktopLayout` if the COM surface allows; otherwise falls back to external mode with a soft warning. DPI override exposes a dropdown (100 / 125 / 150 / 175 / 200 % + Auto).
**Files**: new `MonitorPickerDialog.xaml(.cs)`, `ServerProfileDto.cs`, `RdpFileGenerator.cs`, `EmbeddedRdpView.xaml.cs`.
**Tests**: `MonitorPickerDialog` round-trip; `RdpFileGenerator` emits correct `selectedmonitors:s:`.
**Validation**: select 2 of 3 displays, connect, verify correct monitor coverage.

### b70 — OpenSSH config import
**Scope**: parse `~/.ssh/config` (pattern matching, Host blocks, `ProxyJump`, `IdentityFile`, `User`, `Port`, `HostName`). Import preview dialog with diff-against-existing-profiles, bulk-import button. Gateway chains recreated using existing `ParentGatewayId`.
**Files**: new `src/Heimdall.Ssh/OpenSshConfigParser.cs`, new `OpenSshImportDialog.xaml(.cs)`.
**Tests**: parse matrix of real-world configs; round-trip with existing exporter.
**Validation**: import a 30-entry `~/.ssh/config`, verify gateways and identity files.

### b71–b72 — Hot-toggle redirections + redirection notifications + connection sequences pre/post

These two batches close Cluster RF and kick off Cluster RH. Scope them when we're inside the Next wave and can size based on what we learned in Now.

**Open holds for future prompts**: b73 terminal color-scheme editor (T3 + T36), b74 session notes + search refinement (M11 + M18), b75 gateway MFA forwarding (G13), b76 cipher/MAC/KEX selection (S17).

---

## 6. Answer — "qu'est-ce qu'il manque à Heimdall.Next pour être crédiblement meilleur que PuTTY / MobaXterm sur les parcours principaux ?"

**Three axes. Close them in Now + Next and the answer is "nothing that matters to the target audience".**

### Axis 1 — Migration ergonomics
PuTTY and MobaXterm users arrive with existing estates: `~/.ssh/config`, `.rdp` files, `.ppk` keys, session trees. Heimdall.Next today imports none of them natively. Once the following four are in (all in Now / Next):

1. `.rdp` bulk import (**b56–b57**)
2. PuTTYgen-equivalent with `.ppk` ↔ OpenSSH conversion (**b58–b59**)
3. OpenSSH config import (**b70**)
4. Session-tree export compatible with MobaXterm `.mxtsessions` or mRemoteNG XML (**future — medium, not blocking**)

…the "minute-one migration" experience is equal to or better than what PuTTY/MobaXterm offer, because Heimdall also **retains and relays** the typed failure model, TOFU, multi-hop chains, and temp `.rdp` hardening that the source products never had.

### Axis 2 — Daily reliability
PuTTY has no auto-reconnect; MobaXterm has auto-reconnect and session-restore. The second is the migration test most users run the day after installing: "did it remember where I was?"

1. Session-state snapshot + restore-on-launch (**b55**)
2. SSH auto-reconnect with exponential back-off (**b62**)
3. Per-server keep-alive + max-retries (**b61**)

Once those three ship, Heimdall.Next matches MobaXterm on the "I closed my laptop, opened it back up, everything is there" user story, while keeping the RDP 41-code decoder and SSH 26-code taxonomy that turn a reconnect into actionable information instead of a silent retry.

### Axis 3 — Daily touchpoints (scrollback, search, keep-alive, knobs)
PuTTY users judge a terminal by cursor shape, bell, regex search, scrollback size. MobaXterm users judge a transfer tool by transfer queue, resume, recursive upload. Both sets of judgments happen in the first twenty minutes. Today:

- Scrollback is hard-coded 10 000 (T4). Fix in **b63**.
- Search is substring-only (T6). Fix in **b63**.
- Cursor is block, always blinking (T13 + T14). Fix in **b63**.
- Bell is absent (T17). Fix in **b63**.
- SFTP sends files one at a time, no resume, no folder upload, no queue. Fix in **b65 + b66 + b67**.
- RD Gateway cannot be configured from the embedded ServerDialog (G7). Fix in **b60**.

The batches above are all in Now or early Next. They are the first twenty minutes of first use.

### What makes Heimdall credibly **better** (not just equal) after Now + Next

Beyond closing gaps, Heimdall keeps its existing strengths — none of which PuTTY / MobaXterm / mstsc / Royal TS / mRemoteNG expose:

- 26-code typed SSH taxonomy + 41-code RDP decoder with i18n.
- Multi-hop gateway chains with per-hop TOFU.
- Reference-counted tunnel reuse (correctness-level difference).
- SmartPasteGuard with 16 destructive-pattern regexes.
- Temp `.rdp` hardening (CRLF + ACL + memory zeroing).
- FR/EN CredentialAutofill with UI Automation + WM_SETTEXT.
- DPI-aware physical pixel conversion (RDP).
- Sudo-fallback SFTP editing.
- SSH-tunneled RDP with ref-counted tunnel reuse.
- Preflight auth check.

**The end state isn't "caught up". It's "caught up on migration, ahead on reliability and security".** That's the credible pitch against PuTTY and MobaXterm.

---

## 7. Open follow-ups (non-blocking)

Items that do not gate the roadmap but deserve a thought pass before the end of Next.

1. **Session-tree interchange format**. Do we want to import MobaXterm `.mxtsessions` / PuTTY registry / mRemoteNG XML? Medium effort, high migration-pitch value. Candidate for late-Next batch.
2. **Packaging for PuTTY-parity distribution**. PuTTY is small-single-exe-no-install; Heimdall is a WPF+WebView2 app. Not a direct competitor on footprint, but a portable-mode configuration (config in the app folder) would close an objection. Effort S–M.
3. **Welcome-on-first-launch with migration wizards**. Now + Next delivers the mechanisms (`.rdp` import, key conversion, SSH config import). A first-launch wizard that chains them reduces the cognitive cost further. Effort M. Candidate for post-Next.
4. **Preservation-test harness**. The ten strengths to preserve should have at least one automated test each — today some are only covered by manual QA (e.g. DPI-aware pixel conversion, sudo-fallback SFTP pivot). Audit → test gaps → batch. Effort S × 10, spread over time.
5. **VNC audit**. Explicitly deferred from b52/b53. If demand surfaces, a b52-style gap analysis can be spun up without re-doing SSH/Terminal/SFTP/RDP work.

---

## 8. Cross-reference index

- **SSH / Terminal / SFTP gaps** — `docs/audit-gap-ssh-terminal-sftp-2026-04-19.md`.
- **RDP gaps** — `docs/audit-gap-rdp-2026-04-19.md`.
- **Strengths inventory source** — consolidated from b52 §7 + b53 §7.
- **Defaults fixed by Julien 2026-04-19** — §1.3 above.
- **Batch candidates** — §5 above, b55 through b72 scoped, b73+ held as future prompts.

End of b54. On validation, the first concrete prompt to draft is **b55 — Session-state snapshot + restore-on-launch** (§5 above), which is the foundation shared by T31 / M9 / Rc8 restore scenarios.
