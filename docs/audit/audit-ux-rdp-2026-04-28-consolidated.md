# UX Audit Report — RDP Layer (Heimdall.Next) — Consolidated

**Date:** 2026-04-28
**Audit type:** UX (user experience), consolidated from a parallel double pass
**Sources:**
- `docs/audit/audit-ux-rdp-2026-04-28.md` (Cowork / Claude — 30 findings)
- `docs/audit/audit-ux-rdp-codex-2026-04-28.md` (Codex CLI — 11 findings)
- Cross-referenced and severity-arbitrated by Julien Bombled.
**Scope:** `src/Heimdall.Rdp/**`, `src/Heimdall.App/Services/Handlers/{RdpHandler,CitrixHandler,RdpSessionDiagnosticFactory}.cs`, `src/Heimdall.App/Views/{EmbeddedRdpView,EmbeddedCitrixView}.xaml(.cs)`, `src/Heimdall.App/Views/EmbeddedRdp/RdpHostDiagnosticFactory.cs`, `src/Heimdall.App/Views/Dialogs/{ServerDialog,RdpImportDialog}.xaml(.cs)`, `src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs`, `src/Heimdall.App/ViewModels/SettingsViewModel.cs`, `src/Heimdall.App/Services/SplitService.cs` (split path), `src/Heimdall.App/MainWindow.xaml` (RDP/Citrix/Settings tabs), `locales/en.json`, `locales/fr.json`.
**Standards:** Nielsen 10 usability heuristics, internal `CLAUDE.md` RDP gotchas, prior SSH UX audit (`audit-ux-ssh-2026-04-25.md`) format reference.

> This consolidated document is the **working list** for the implementation phase (Codex pair-architect prompts). The two source audits remain in the repo for traceability but supersede neither this consolidation nor each other.

---

## Executive summary

The RDP layer is functionally complete and code-quality solid (already covered by `audit-ssh-rdp.md` from 2026-04-24). The independent Cowork and Codex passes converge on the same diagnostic: 0 critical UX defects, but a measurable observability gap between what the code tracks and what the user sees. The COM/ActiveX engine is meticulous (layout flushes, dispose order, COM pre-warm, anti-idle, autofill UI Automation + Win32 fallback, bounded auto-reconnect), but the visible feedback layer collapses into generic labels — the rich `ConnectionStateMachine`, the 24 decoded disconnect codes, the auto-reconnect attempt counter, and the credential-autofill state never reach the user. Several controls are visible but silently inert (`RdpUseGlobalDefaults`, webcam in embedded mode, performance flags loaded only at dialog open) or visible but ignored at the wrong time (manual resolution during stabilization, ServerDialog options when split forces embedded). Citrix is launch-and-pray: no discovery, optimistic `Connected` state, 30 s capture polling without progress, kill without confirmation. There is no `Send Ctrl+Alt+Del` helper for the embedded RDP surface, and no pre-connect validation analogous to what the SSH audit added. The fixes are well-scoped and most of them are wiring rather than design — the data and the localization keys are already there, just not surfaced.

**Final count: 0 🔴 Critical · 7 🟠 High · 16 🟡 Medium · 10 🟢 Low = 33 findings.**

### Origin of each finding

| Source | Cowork | Codex (unique) | Total |
|---|---|---|---|
| 🟠 High | 6 | 1 | 7 |
| 🟡 Medium | 14 | 2 | 16 |
| 🟢 Low | 9 | 1 | 10 |

Two pairs of findings were consolidated:
- Cowork F24 (external autofill silent) + F25 (embedded autofill silent) → consolidated **F7** (single High).
- Cowork F2 (attempt counter missing) + Codex F3 (also flags missing cancel affordance) → consolidated **F2** (the cancel affordance becomes part of the suggested fix).

Codex finding F2 (overlay loses disconnect reason), F4 (resolution changes during stabilization), F6 (Citrix capture status), F7 (mode-dependent webcam), F10 (status text bypasses i18n) overlap with Cowork findings on identical scope; the consolidated entries credit both audits in the *References* line.

---

## Findings list

### 🟠 High (7)

| # | Origin | Category | Title |
|---|---|---|---|
| F1 | Cowork F1 + Codex F10 | i18n / Connection lifecycle | Status text bypasses the i18n pipeline (`UpdateSessionStatus("Connecting")` writes raw English) |
| F2 | Cowork F2 + Codex F3 | Connection lifecycle | Auto-reconnect status hides attempt count, decoded reason, and cancel affordance |
| F3 | Cowork F3 | Connection lifecycle | Reconnect overlay shown after **user-initiated** disconnect (no distinction from unexpected drops) |
| F4 | Cowork F4 | Settings UX | `RdpUseGlobalDefaults` per-server checkbox is exposed and persisted but **never honored** at connect time |
| F5 | Cowork F5 + Codex F2 | Disconnect reasons | Reconnect overlay shows the generic message although `pane.FailureDetails` already carries the decoded reason key |
| F6 | Codex F5 | Connection lifecycle / Embedded vs external | External RDP marked `Connected` as soon as `mstsc.exe` starts, before the user has actually authenticated |
| F7 | Cowork F24 + F25 + Codex F1 | Credential autofill | Autofill is invisible to the user — timeouts, broker not found, password rejection produce only log warnings |

### 🟡 Medium (16)

| # | Origin | Category | Title |
|---|---|---|---|
| F8 | Cowork F6 | Credential autofill / Settings UX | `RdpCredentialAutofillTimeoutMs` setting is ignored in embedded mode (hardcoded 90 s) |
| F9 | Cowork F7 + Codex F7 | Settings UX | Webcam redirection checkbox is silently ineffective in embedded mode |
| F10 | Cowork F8 | Connection lifecycle | Progress bar hidden during `Reconnecting` state (only shown for initial `Connecting`) |
| F11 | Cowork F9 | Settings UX | AspectRatio ComboBox in ServerDialog only exposes 2 options; code supports 5; i18n keys exist |
| F12 | Cowork F10 | Settings UX | 5 RDP redirections (COM ports / Smart cards / Webcam / USB / Audio capture) missing from the global Settings tab |
| F13 | Cowork F11 + Codex F4 | Resolution / DPI | Manual resolution change silently ignored during the 10 s post-connect stabilization window |
| F14 | Cowork F12 | Documentation | `CLAUDE.md` says 5 s post-`OnConnected` block; actual default is 10 s |
| F15 | Cowork F13 | Embedded vs external | External `.rdp` file ignores user-configured resolution; AdminMode / FullScreen unreachable from UI |
| F16 | Cowork F14 | Disconnect reasons | Several disconnect messages lack actionability (NetworkError, EncryptionError, SecurityError, ClientDecompressionFailed, CredSspPolicyError, ConnectionTimeout, InternalError, LicensingError, SocketClosed, CertificateWarning) |
| F17 | Cowork F15 | Citrix StoreBrowse | Three Citrix launch modes coexist with implicit precedence; no warning when more than one is configured |
| F18 | Cowork F16 | Citrix StoreBrowse | Misleading error message: shell-character rejection in `CitrixLaunchCommandLine` reports `CitrixNoConnectionConfigured` instead of "forbidden characters" |
| F19 | Cowork F17 + Codex F6 | Citrix StoreBrowse | 30 s window-capture polling with zero feedback to the user — looks frozen |
| F20 | Cowork F18 | Citrix StoreBrowse | "Terminate" kills the Citrix process without confirmation (data-loss risk) |
| F21 | Cowork F19 | Keyboard / mouse | No `Send Ctrl+Alt+Del` / `Send Ctrl+Alt+End` helper button for the embedded RDP surface |
| F22 | Codex F8 | Splits & tabs | Split mode silently overrides external RDP profiles to embedded (`SplitService.ForceEmbeddedMode()`) |
| F23 | Codex F9 | Settings UX | No "test connection" / pre-connect validation path for RDP profiles (analogous to recent SSH UX additions) |

### 🟢 Low (10)

| # | Origin | Category | Title |
|---|---|---|---|
| F24 | Cowork F20 | Code quality | Performance flags checkboxes use imperative code-behind sync (load/save) instead of ViewModel binding |
| F25 | Cowork F21 | Resolution / DPI | Resolution menu presets hardcoded in XAML; no extension hook |
| F26 | Cowork F22 | Connection lifecycle | "Connecting" state doesn't differentiate sub-phases — rich state machine never reaches the view |
| F27 | Cowork F23 | Settings UX | `CitrixUseSso` checkbox is always visible regardless of context |
| F28 | Cowork F26 | Settings UX | `RdpResizeEnableDelayMs`, `RdpArtifactCleanupDelayMs`, `RdpCredentialAutofillTimeoutMs` settings exist in `AppSettings` but not exposed in the UI |
| F29 | Cowork F27 | Connection lifecycle | `EnsureHostHandle` retry budget exhaustion produces only a log warning |
| F30 | Cowork F28 | Disconnect reasons | `RdpFatalError` is mapped to a single i18n key — no per-error-code mapping |
| F31 | Cowork F29 | Anti-idle | Anti-idle Shift key injection has no UI indicator |
| F32 | Cowork F30 | i18n | `CredentialAutofill.TitlePattern` only matches EN + FR; users on other Windows locales see autofill silently fail |
| F33 | Codex F11 | Microcopy | `RdpAntiIdleHint` says "mouse movement" but the implementation sends Shift key events |

---

## Detailed findings

### F1 — Status text bypasses the i18n pipeline · 🟠 High

**Origin:** Cowork F1 + Codex F10 (severity arbitrated as High).

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:686-707` (`UpdateSessionStatus`), called from lines 159, 258, 411, 468, 521, 539, 564, 576, 713; tunnel endpoint at lines 1015-1028.

**Reproduction:**
1. Switch the application locale to French.
2. Connect to any RDP server in embedded mode.
3. Observe the status text next to the endpoint in the session header bar.

**Expected:** Localized status (e.g., "Connexion…" / "Connecté" / "Déconnecté").
**Observed:** Raw English strings ("Connecting", "Connected", "Disconnected", "Error", "Reconnecting", "Disconnecting") rendered verbatim in the header `StatusTextBlock` and in the tab `Status` property. The tunneled endpoint format also hard-codes `via localhost`.

The localization keys exist in `locales/en.json` / `locales/fr.json` — `RdpStatusPreparing`, `RdpStatusWaiting`, `RdpStatusConnectedDetail`, `RdpStatusDisconnectedDetail`, `RdpStatusErrorDetail`, `RdpStatusReconnecting`, `RdpStatusDisconnecting`, `RdpStatusFatalErrorDetail` — all 8 already translated EN+FR, none branched.

**Suggested fix:**
- Replace every `UpdateSessionStatus("Connecting")` / `"Connected"` / etc. with calls that pass an enum or a key, then look up the localized string inside `UpdateSessionStatus`.
- Move state comparisons (line 695: `string.Equals(status, "Connecting", …)`) from string compare to an enum.
- Localize the tunneled endpoint format string ("{0}:{1} via localhost:{2}") via a new `RdpEndpointTunneledFormat` key.

**References:** Nielsen UX-04 (consistency); the SSH audit fixed the equivalent issue.

**Status:** ✅ Closed — commit `dcda468` on 2026-04-28.

---

### F2 — Auto-reconnect status hides attempt count, reason, and cancel affordance · 🟠 High

**Origin:** Cowork F2 + Codex F3 (cancel affordance contributed by Codex).

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:556-566` (`OnRdpAutoReconnecting`); `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:568-573` (`MaxReconnectAttempts = 20`); `locales/en.json:2341` (`RdpStatusReconnecting: "Reconnecting (attempt {0})..."`).

**Reproduction:**
1. Connect RDP. Pull the network cable for ~5 s, then plug back in to trigger MsTscAx auto-reconnect.
2. Observe the header status during the reconnect cycle.
3. Try to abort the reconnect attempts.

**Expected:**
- Status displays "Reconnecting (attempt 3 of 20)…" or similar with the disconnect reason.
- An obvious cancel option terminates the retry loop without forcing a tab close.

**Observed:**
- Header just reads "Reconnecting" (raw, see F1).
- The `attemptCount` argument from the COM event sink is captured at the method signature but never read.
- The disconnect reason for the auto-reconnect cause is not surfaced.
- The Disconnect toolbar button is the only way to escape, and it tears down the whole session via `_rdpHost.CancelAutoReconnect = true` only on dispose.

**Suggested fix:**
- When localizing the status (cf. F1), call `Format("RdpStatusReconnecting", attemptCount)` and append " of 20" (or the configured cap).
- Add a localized "Cancel reconnect" button that flips `CancelAutoReconnect` without disposing the view, then transitions to a final `Disconnected` state.
- Surface the decoded `disconnectReason` in a sub-line of the status (e.g., "Reason: network error").

**References:** Nielsen UX-01, UX-02, UX-08.

**Status:** ✅ Closed — commit `9944f50` on 2026-04-29.

---

### F3 — Reconnect overlay shown on user-initiated disconnect · 🟠 High

**Origin:** Cowork F3.

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:506-524` (`OnRdpDisconnected` calls `ShowReconnectOverlay()` unconditionally).

**Reproduction:**
1. Connect RDP. Click the **Disconnect** toolbar button (header bar).
2. Observe the result.

**Expected:** Quiet close, or simply "Disconnected" status without an overlay (the user explicitly asked to close).
**Observed:** The full reconnect overlay appears with "The Remote Desktop session has ended" + Reconnect / Close buttons. The user has to dismiss it after pressing Disconnect, even though they just told the app to disconnect.

The intended COM disconnect reasons for user-initiated close are `1` (`LocalUser`) and `2` (`UserLogoff`), and they currently route through the same `ShowReconnectOverlay()` path as `260 DnsLookupFailed`, `516 SocketConnectFailed`, etc.

**Suggested fix:** In `OnRdpDisconnected(int reason)`, suppress the overlay when:
- `reason ∈ {0, 1, 2, 3}` (NoInfo / LocalUser / UserLogoff / AdminDisconnect — clean-exit codes), OR
- An internal flag `_userInitiatedDisconnect` was set by `OnDisconnectClick`.

Show a brief toast/inline status instead, and consider auto-closing the tab when the user dispatched the disconnect themselves.

**References:** Nielsen UX-04 (consistency between intent and feedback).

**Status:** ✅ Closed — commit `8a91d1f` on 2026-04-29.

---

### F4 — `RdpUseGlobalDefaults` checkbox has no runtime effect · 🟠 High

**Origin:** Cowork F4. **Disagreement with Codex** (Codex chose not to flag, presuming wiring through redirection construction; verification confirms no wiring exists — see Notes).

**Where:**
- Checkbox: `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1603`
- DTO: `src/Heimdall.Core/Configuration/ServerProfileDto.cs:93` (default `true`)
- Embedded build: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:1096-1118` (`BuildRedirections` — reads server fields directly, ignores the flag and `AppSettings.RdpDefault*`)
- External build: `src/Heimdall.App/Services/Handlers/RdpHandler.cs:127-146` (same — direct read).

**Reproduction:**
1. Open Settings → RDP. Set `RdpDefaultRedirectClipboard = false`, `RdpDefaultRedirectDrives = true`, `RdpDefaultMultiMonitor = true`.
2. Open or create a server. Leave **Use global defaults** checked. Manually toggle **Clipboard** to `true`, **Drives** to `false`, **Multi-Monitor** to `false`. Save.
3. Connect.

**Expected (one of):**
- (a) The "Use global defaults" toggle disables and visually overrides the per-server checkboxes; connect uses the global values.
- (b) The toggle has a clear meaning (e.g., "fallback for fields where the server profile is at its default") and the actual behavior is documented and tested.

**Observed:** The connect uses the per-server fields verbatim. The `RdpUseGlobalDefaults` flag is read in `ServerDialogViewModel`, persisted into the DTO, propagated through import / migration code paths, but **no handler** consults it before launching mstsc or feeding `RdpRedirectionOptions`. The user sees a knob that does nothing.

**Suggested fix:**
- Wire the merge logic in both `EmbeddedRdpView.BuildRedirections` (line 1096) and `RdpHandler.ConnectAsync` (line 127). When `server.RdpUseGlobalDefaults == true`, read the corresponding `AppSettings.RdpDefault*` and let server fields override only when explicitly diverged from defaults (or apply a strict override semantics — to be decided).
- Until the wiring exists, hide or disable the checkbox so the user doesn't believe they're configuring something. A grey-out + tooltip "Coming soon" is a stop-gap; a feature kill is also acceptable.
- Add an integration test asserting that flipping the flag changes the values fed to `RdpRedirectionOptions`.
- F12 (5 missing redirection defaults in Settings) is a prerequisite to fully wire this.

**References:** Nielsen UX-04 (consistency between control state and effect), UX-03 (error prevention — currently the user is silently misled).

**Notes — disagreement resolution:** Codex declined to flag this finding under the assumption that "behavior is wired through redirection construction". A search of the source confirmed `RdpUseGlobalDefaults` existed only in `ServerProfileDto`, `ServerDialogViewModel`, `ServerListViewModel.Bulk`, `MigrationService`, `RdpImportService` — never in any handler or view that built the connect-time options. The arbitration retained this as a High finding.

**Status:** ✅ Closed — commit `cd83d35` on 2026-04-29.

**Disagreement resolution outcome:** the wiring was indeed missing as Cowork verified during consolidation. F4 is now wired via `RdpProfileResolver` with strict-override semantics: `RdpUseGlobalDefaults = true` always reads from `AppSettings.RdpDefault*` for the 16 governable fields. The disagreement is settled in favor of the High classification.

---

### F5 — Reconnect overlay loses the decoded disconnect reason · 🟠 High

**Origin:** Cowork F5 + Codex F2.

**Where:**
- Diagnostic factory: `src/Heimdall.App/Views/EmbeddedRdp/RdpHostDiagnosticFactory.cs:26-35`
- Pane diagnostic set: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:520`
- Overlay: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:814-822`
- Reason key map: `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:432-458` (24 codes mapped)
- Localization: 24 `RdpDisconnect*` keys in `locales/en.json` / `fr.json` (perfect parity).

**Reproduction:**
1. Connect RDP with wrong credentials (or to a server with NLA disabled).
2. The session disconnects with reason `2055` (`BadCredentials`).
3. Wait for the reconnect overlay.

**Expected:** Overlay says "The credentials were not accepted." (the localized `RdpDisconnectBadCredentials` string already exists), with the Reconnect button and ideally an "Edit credentials & retry" shortcut.
**Observed:** Overlay shows the generic `RdpDisconnectedMessage`: "The Remote Desktop session has ended." The pane diagnostic is correctly populated with the `RdpDisconnectBadCredentials` key (line 520) but is never read by the overlay.

This is the single highest-leverage UX fix in the RDP path: the data is there, the keys are there, the rendering is missing.

**Suggested fix:**
- In `ShowReconnectOverlay()`: read `_ownerPane?.FailureDetails ?? _sessionTab?.PrimaryPane?.FailureDetails`. If a diagnostic is present, build the message as `Localizer[diagnostic.MessageKey]` (or `Format` if the key has placeholders) and display it as the title; keep `RdpDisconnectedMessage` as the subtitle/secondary line for context.
- Surface the numeric code in a smaller font for forensic / support purposes.
- For credential-class reasons (`BadCredentials`, `UserNotFound`, `AccountLockedOut`, `AccountExpired`, `PasswordExpired`), replace the generic Reconnect button with "Edit credentials & retry" that opens the ServerDialog focused on the credentials section.
- For `CertificateWarning`, expose an "Open server certificate dialog" affordance (separate work — F16 lists the missing accept/decline path).

**References:** Nielsen UX-03 (error recovery), UX-08 (help/context for recovery).

**Status:** ✅ Closed — commit `51c4de3` on 2026-04-28.

---

### F6 — External RDP marked `Connected` as soon as `mstsc.exe` starts · 🟠 High

**Origin:** Codex F5.

**Where:** `src/Heimdall.App/Services/Handlers/RdpHandler.cs:201-236` (`Process.Start(mstsc.exe, …)` immediately followed by `_connectionSm.TryTransition(server.Id, ConnectionState.Connected)` at line 236).

**Reproduction:**
1. Configure an RDP server in external mode.
2. Connect to a host with bad credentials, unreachable network, certificate warning, or slow NLA.
3. Observe Heimdall's connection state in the server list and tab header right after the launch.

**Expected:** Heimdall distinguishes "external client launched" from "remote session connected"; failure-prone credential / certificate / NLA states remain visible until either the user authenticates or the external client dies.
**Observed:** `RdpHandler` transitions to `ConnectionState.Connected` immediately after `Process.Start` returns. Authentication failure, certificate prompts, or rejected credentials happen inside the external `mstsc.exe` and are not reflected in Heimdall except through background autofill log lines (cf. F7).

**Suggested fix:**
- Introduce an intermediate `LaunchedExternalClient` state in `ConnectionStateMachine` (or rename the visible label of the existing state) and add a localized status key `RdpStatusLaunchedExternalClient` ("Launched in mstsc — sign in to the remote computer to complete the connection.").
- Track the `mstscProcess.Exited` event: when the external client exits, transition to `Disconnected` (and surface the exit code if non-zero).
- Add a "Bring mstsc to front" affordance in the Heimdall tab while the external session is alive (similar to the Citrix `BringToFrontButton`).
- Optional: poll `mstsc.exe` window title every ~2 s for a window class change (`TscShellContainerClass` once authenticated) to detect "remote desktop visible" transition.

**References:** Nielsen UX-01 (status visibility), UX-04 (consistency: embedded mode does not have this problem).

**Status:** ✅ Closed — commit `8fbec1e` on 2026-04-29.

**Note:** The "Bring mstsc to front" button suggested in the original audit is intentionally deferred — it requires a new `EmbeddedExternalRdpView` that does not exist today and is a separate finding scope.

---

### F7 — Credential autofill is invisible to the user · 🟠 High

**Origin:** Cowork F24 + F25 + Codex F1, consolidated into one finding (severity promoted to High per Codex).

**Where:**
- External: `src/Heimdall.App/Services/Handlers/RdpHandler.cs:238-267` (background `Task.Run`, `FileLogger.Warn` only on timeout)
- Embedded: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:622-660` (same pattern, hardcoded 90 s timeout — cf. F8)
- Engine: `src/Heimdall.Rdp/CredentialAutofill.cs:133-189` (returns `bool`, callers have enough signal already)

**Reproduction:**
1. Save an RDP server with a stored password.
2. Connect to a host that shows a CredUI prompt slowly, or to one where autologon precedes CredUI.
3. Watch the Heimdall tab/status while the autofill watcher searches, times out, or fails to inject the password.

**Expected:** The user sees a status such as "waiting for Windows credential prompt", "filling saved password", "autofill timed out", or "password was rejected — enter it manually".
**Observed:** Embedded mode logs the watcher and timeout only; external mode runs in a background `Task.Run` and logs failures only. The status bar stays on the lifecycle state, so users cannot tell whether Heimdall is still trying, already failed, or waiting for manual input. If the autofill does succeed, this is invisible too.

**Suggested fix:**
- Emit autofill state transitions on a small in-tab credential sub-status (banner or row under the status bar):
  - `RdpAutofillSearching` ("Looking for the Windows credentials prompt…")
  - `RdpAutofillFilled` ("Saved password sent to the credentials prompt.") — short-lived (~3 s).
  - `RdpAutofillTimeout` ("Couldn't find the credentials prompt — type your password manually.")
  - `RdpAutofillFailed` ("Couldn't fill the credentials prompt automatically — type your password manually.")
- For external mode, tie the message to the launched session row in the server list (the same row that hosts the Heimdall tab — not just the log).
- Make the timeout cancellable from the UI (a small "Stop autofill" link) so the user can dismiss it without waiting 90 s.

**References:** Nielsen UX-01 (status visibility), UX-08 (help on silent failure). `CredentialAutofill.WaitAndFillAsync` already returns `bool`.

**Status:** ✅ Closed — commit `1750409` on 2026-04-29.

---

### F8 — Embedded autofill timeout is hardcoded · 🟡 Medium

**Origin:** Cowork F6.

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:642` (`TimeSpan.FromSeconds(90)`); `src/Heimdall.Core/Configuration/AppSettings.cs:52` (`RdpCredentialAutofillTimeoutMs = 90000`).

**Reproduction:** Edit `Settings.RdpCredentialAutofillTimeoutMs` (e.g., set to 30000). Connect a server in embedded mode. Observe that autofill keeps polling for 90 s.

**Expected:** The setting is honored in both embedded and external modes.
**Observed:** Only `RdpHandler.ConnectAsync` line 245 uses the setting; `EmbeddedRdpView.TryAutofillCredentialsAsync` line 642 inlines a constant.

**Suggested fix:** Pass the timeout from `AppSettings` into `EmbeddedRdpView.InitializeSession` (or accept it as a parameter at autofill start time) and use it in `TryAutofillCredentialsAsync`.

**References:** Nielsen UX-04 (consistency).

**Status:** ✅ Closed — commit `73e8f9f` on 2026-04-29.

---

### F9 — Webcam redirection silently ineffective in embedded mode · 🟡 Medium

**Origin:** Cowork F7 + Codex F7.

**Where:**
- Comment: `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:581-583` ("Webcam redirection works in external mode (.rdp file) only")
- Checkbox: `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1585` (`DlgSrv_RedirWebcamCb`)
- External wire: `src/Heimdall.Rdp/RdpFileGenerator.cs:104-106` (`camerastoredirect:s:*` written when `r.Webcam`).

**Reproduction:** In ServerDialog → RDP → Device redirection, check **Webcam**. Save. Connect in embedded mode. Open Camera app on the remote.

**Expected:** Either the camera works on the remote, or the UI clearly says webcam redirection requires external mode.
**Observed:** The checkbox state is stored, the COM-level `ApplyRedirectionSettings` skips it (the comment acknowledges the limitation), and the user has no clue. The remote sees no camera and may waste time troubleshooting.

**Suggested fix (cheapest):**
- Add a hint label/tooltip next to the Webcam checkbox: "Webcam redirection requires external mode."
- Disable the checkbox (or grey it out with a tooltip) when `RdpMode = Embedded`. Better: add a small inline notice "Switch to external mode to enable webcam redirection — [Switch]" with a one-click switch.

**Suggested fix (cleaner):**
- Implement webcam via `IMsRdpClientNonScriptable7.CameraRedirConfigCollection` (the comment in `RdpActiveXHost.cs:581` notes it's not exposed via simple IDispatch but does exist on newer interfaces).

**References:** Nielsen UX-04 (consistency between control state and effect).

---

### F10 — Progress bar hidden during reconnecting · 🟡 Medium

**Origin:** Cowork F8.

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:695-696` (`isConnecting = string.Equals(status, "Connecting", …)`).

**Reproduction:** Connect, then drop the network. Observe the header bar during the auto-reconnect cycle.

**Expected:** A progress indicator (the `RdpLoadingBar` indeterminate bar) is visible while reconnect attempts are in-flight, since the user can't interact with the surface during this time.
**Observed:** The bar only shows during the initial `Connecting` state; during `Reconnecting` it's hidden. The screen surface is frozen with no progress affordance.

**Suggested fix:** Change the predicate to `status is "Connecting" or "Reconnecting"` (and switch to enum after F1).

**References:** Nielsen UX-01 (system status visibility).

**Status:** ✅ Closed — commit `73e8f9f` on 2026-04-29 (alongside F8).

---

### F11 — AspectRatio ComboBox underexposed · 🟡 Medium

**Origin:** Cowork F9.

**Where:**
- ComboBox: `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1518-1523` (only `Stretch` and `Preserve` items)
- Code support: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:1078-1093` (`ParseAspectRatio` accepts `Stretch`, `Auto`, `Preserve`, `Dynamic`, `16:9`, `4:3`, `21:9`)
- I18n: `RdpAspectAuto` and `RdpAspectDynamic` keys exist in `locales/en.json` / `fr.json` but no `ComboBoxItem` references them.

**Reproduction:** Open ServerDialog → Options → Aspect Ratio. Observe the dropdown.

**Expected:** All ratios documented in `RdpAspectRatioHint` are reachable from the dropdown.
**Observed:** Only `Stretch` and `Preserve` are exposed.

**Suggested fix:** Add the missing `ComboBoxItem`s with their tags matching the values `ParseAspectRatio` accepts. Add `RdpAspect16x9`, `RdpAspect4x3`, `RdpAspect21x9` localization keys (or use literal labels for technical content).

**References:** Nielsen UX-05 (recognition over recall — show all options).

---

### F12 — Settings RDP defaults incomplete · 🟡 Medium

**Origin:** Cowork F10.

**Where:**
- UI: `src/Heimdall.App/MainWindow.xaml:2154-2222` (Settings → RDP tab)
- VM: `src/Heimdall.App/ViewModels/SettingsViewModel.cs:447-462`, `:582-595` (`Load`/`Save`)
- Settings DTO: `src/Heimdall.Core/Configuration/AppSettings.cs:99-115` (16 `RdpDefault*` fields).

**Reproduction:** Open Settings → RDP. Compare to the available redirection options in ServerDialog → Options → Device redirection.

**Expected:** All redirection types configurable per-server are also configurable as global defaults.
**Observed:** Settings exposes 3 of 7 redirections (Clipboard, Drives, Printers). Missing: COM Ports, Smart Cards, Webcam, USB, Audio Capture. The `AppSettings` class declares the underlying booleans but neither `MainWindow.xaml` nor `SettingsViewModel` reads/writes them. They are dormant.

**Suggested fix:**
- Add the missing 5 checkboxes in the Settings RDP tab and the matching `Load`/`Save` blocks in `SettingsViewModel`.
- Required prerequisite for fully wiring F4 (`RdpUseGlobalDefaults`).

**References:** Nielsen UX-04 (consistency).

**Status:** ✅ Closed — commit `8ca6f9e` on 2026-04-29.

---

### F13 — Manual resolution change ignored during stabilization · 🟡 Medium

**Origin:** Cowork F11 + Codex F4.

**Where:**
- Stabilization delay: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:43-45`
- Resize tick guard: `:314-318`
- Enable trigger: `:485-504` (`EnableResolutionUpdatesAsync`)
- Manual menu: `:768-808` (`OnResolutionMenuClick`).

**Reproduction:** Connect RDP. Click the resolution toolbar button within ~10 s of seeing "Connected"; pick `1920x1080`. Observe.

**Expected:** Either the change applies immediately, or the UI explains the temporary unavailability and queues the choice.
**Observed:** The click is silently skipped because `_allowResolutionUpdates == false` until `_initialResizeEnableDelay` elapses (10 s by default). The user gets no feedback. After 10 s the menu works again — and the user has no idea why their click "didn't take" earlier.

**Suggested fix:**
- Disable the resolution button (with a tooltip "Stabilizing connection — available in {x} s") while `_allowResolutionUpdates == false`.
- Better: queue the user's choice and apply it as soon as `EnableResolutionUpdatesAsync` finishes. Show a transient `RdpStatusOptimizingDisplay` ("Optimizing display…") status during the window.

**References:** Nielsen UX-01 (status visibility), UX-04 (predictability).

---

### F14 — Documentation drift on stabilization delay · 🟡 Medium

**Origin:** Cowork F12.

**Where:** `CLAUDE.md` "RDP ActiveX (Critical)" section says "Resolution updates blocked 5 s after `OnConnected`"; actual `_initialResizeEnableDelay = TimeSpan.FromSeconds(10)` in `EmbeddedRdpView.xaml.cs:43` (also `AppSettings.RdpResizeEnableDelayMs = 10000`).

**Expected:** Doc and code agree.
**Observed:** Doc says 5 s; code default is 10 s.

**Suggested fix:** Update the gotcha in `CLAUDE.md` to "10 s" (and ideally cross-reference `AppSettings.RdpResizeEnableDelayMs`).

**References:** Internal documentation accuracy.

---

### F15 — External `.rdp` file ignores user-configured resolution + AdminMode/FullScreen unreachable · 🟡 Medium

**Origin:** Cowork F13.

**Where:** `src/Heimdall.App/Services/Handlers/RdpHandler.cs:118-147` builds `RdpFileOptions` without setting `Width`/`Height` (defaults from `RdpFileOptions` are `1920x1080`); same block hardcodes `FullScreen = false` (line 124) and `AdminMode = false` (line 125).

**Reproduction:**
1. Configure a server with `RdpAspectRatio = "Preserve"` and a window-sized resolution from `Settings.DefaultResolutionWidth/Height`.
2. Switch the server to `RdpMode = External`.
3. Launch.

**Expected:** mstsc.exe opens at the user-configured resolution, and the dialog has a way to enable Admin mode and FullScreen for sysadmin scenarios (commonly needed for RDS console connections).
**Observed:** mstsc.exe opens at exactly `1920x1080` regardless of any user setting. Admin mode and FullScreen options are unreachable from the UI.

**Suggested fix:**
- Pass `Width = settings.DefaultResolutionWidth, Height = settings.DefaultResolutionHeight` (with sane fallbacks).
- Add a per-server "Run as administrator session" checkbox and wire `AdminMode`.
- Add a "Open in fullscreen" toggle and wire `FullScreen`.

**References:** Nielsen UX-04 (consistency between embedded and external modes).

---

### F16 — Disconnect message wording lacks actionability · 🟡 Medium

**Origin:** Cowork F14.

**Where:** `locales/en.json:2342-2367` (the 25 `RdpDisconnect*` strings).

**Coverage:** All 24 codes in `RdpActiveXHost.GetDisconnectReasonKey` have a corresponding key, plus the `RdpDisconnectUnknownCode` fallback. EN/FR parity is perfect (86 / 86).

**Quality of message:**

| Code | Key | Action quality |
|---|---|---|
| `516 SocketConnectFailed` | "...It may be turned off, not on the network, or Remote Desktop may be disabled." | ✅ Excellent — three actionable hypotheses |
| `2055 BadCredentials` | "The credentials were not accepted." | 🟡 Mid — could suggest "verify username/password/domain" |
| `3335 AccountLockedOut` | "The account is currently locked out." | ✅ Implies action (contact admin) |
| `3591 AccountExpired` | "The account has expired." | ✅ Implies action |
| `3847 PasswordExpired` | "The password has expired and must be changed before connecting." | ✅ Action implicit |
| `260 DnsLookupFailed` | "Could not resolve the remote computer name." | 🟡 Mid — could mention DNS / hostname check |
| `264 ConnectionTimeout` | "The connection timed out." | ❌ No action — retry? firewall? VPN? |
| `772 NetworkError` | "A network error interrupted the session." | ❌ No action |
| `1030 SecurityError` | "A security error prevented the connection." | ❌ Vague — TLS? CredSSP? |
| `1796 InternalError` | "An internal error occurred in the Remote Desktop client." | ❌ No action |
| `2056 LicensingError` | "A licensing error occurred on the remote computer." | ❌ No action — call admin? |
| `2308 SocketClosed` | "The network connection was lost." | ❌ No action — check network? |
| `2311 CertificateWarning` | "The remote computer's certificate could not be verified." | ❌ No accept/decline path — and there is no UI to accept the cert |
| `2822 EncryptionError` | "A data encryption error ended the session." | ❌ No action |
| `2825 DecompressionError` | "A data decompression error ended the session." | ❌ No action |
| `3080 ClientDecompressionFailed` | "The client could not decompress data from the server." | ❌ Unintelligible to non-technical user |
| `3848 CredSspPolicyError` | "CredSSP policy prevents sending credentials to the remote computer." | ❌ No fix path — could mention KB4093492 |
| `4360 ResolutionChangeTimeout` | "The session was disconnected after a display resolution change." | 🟡 Mid — could suggest "retry, possibly with dynamic resolution disabled" |

**Suggested fix:** For each ❌ row, add a follow-up sentence with the most likely user-facing action. Examples:
- `ConnectionTimeout`: "...Verify that the remote host is reachable and that no firewall is blocking the connection."
- `NetworkError`: "...Check your network connection and try again."
- `SecurityError`: "...The TLS handshake or NLA negotiation failed. Verify NLA is supported on the remote host."
- `CertificateWarning`: "...The certificate is self-signed, expired, or untrusted. To connect, ensure the certificate's CA is in your trusted store, or import it manually."
- `CredSspPolicyError`: "...CredSSP versions don't match. Update either client or server (Microsoft KB4093492)."

**References:** Nielsen UX-03 (error recovery messages must be actionable).

---

### F17 — Citrix mode precedence not surfaced · 🟡 Medium

**Origin:** Cowork F15.

**Where:** `src/Heimdall.App/Services/Handlers/CitrixHandler.cs:64-147` chains `if/else if/else` for three modes.

**Reproduction:**
1. Configure a Citrix server with both `CitrixIcaFilePath` and `CitrixStoreFrontUrl + CitrixAppName`.
2. Connect.

**Expected:** The user is told which mode is being used (or warned about the conflict).
**Observed:** `CitrixLaunchCommandLine` wins over `IcaFilePath` wins over `StoreFrontUrl` silently. The user has no way to know which mode launched.

**Suggested fix:**
- Add a small mode indicator in `EmbeddedCitrixView.SessionInfoText` or `StoreFrontText` showing the active mode (Cache / ICA file / StoreFront).
- In ServerDialog Citrix section, show a warning when more than one launch field is populated explaining the precedence.

**References:** Nielsen UX-04 (predictability).

---

### F18 — Misleading shell-rejection error · 🟡 Medium

**Origin:** Cowork F16.

**Where:** `src/Heimdall.App/Services/Handlers/CitrixHandler.cs:64-72`.

**Reproduction:** Configure `CitrixLaunchCommandLine` containing `&` or `|`. Connect.

**Expected:** Error message says "Citrix launch command contains forbidden shell metacharacters."
**Observed:** Error message is `CitrixNoConnectionConfigured` ("No Citrix StoreFront URL or ICA file configured.") — wrong: the user *did* configure something, validation just rejected it.

**Suggested fix:** Add a dedicated `CitrixLaunchCommandRejected` localization key with text like "The Citrix launch command was rejected because it contains forbidden characters (`|`, `&`, `;`, `` ` ``, `$`, newlines)." Use it on the validation failure path.

**References:** Nielsen UX-03 (specific error messages).

---

### F19 — Citrix capture polling without progress feedback · 🟡 Medium

**Origin:** Cowork F17 + Codex F6 (Codex broadens to include lack of StoreBrowse discovery flow).

**Where:** `src/Heimdall.App/Views/EmbeddedCitrixView.xaml.cs:179-284` (`TryCaptureWindowAsync`, 60 attempts × 500 ms).

**Reproduction:** Connect a Citrix StoreFront server. Watch the Heimdall window during the 30 s capture window.

**Expected:** Visible progress / status: "Looking for Citrix session window…", with a cancel option.
**Observed:**
- The session header shows "Connected" or "Embedded" immediately (line 126 in `InitializeSession`), but the actual rendering surface is empty. No spinner. The `HealthDot` is green. After 30 s, if no window was captured, BringToFrontButton becomes visible — silently.
- The handler also transitions to `ConnectionState.Connected` immediately after process launch (Codex finding F6 broadening — see also F6 for external RDP).
- Users with a slow Citrix login experience this as "the app is broken".

**Suggested fix:**
- Show a loading state in `EmbeddedCitrixView` while `_captureInProgress = true`: a centered spinner + localized text "Locating Citrix session window…".
- After 10 s of polling without success, change the message to "Still searching… (this can take up to 30 s)".
- Add a Cancel button that aborts the polling and falls back to external mode immediately.
- Codex broadening: add a StoreBrowse discovery / test action in the dialog (let users pick the published app from the cached StoreFront list rather than typing it manually); validate app names before save when possible.

**References:** Nielsen UX-01 (system status visibility), UX-02 (user control).

---

### F20 — Citrix Terminate kills the process without confirmation · 🟡 Medium

**Origin:** Cowork F18.

**Where:** `src/Heimdall.App/Views/EmbeddedCitrixView.xaml.cs:363-374` (`OnTerminateClick` calls `Process.Kill()` straight away).

**Reproduction:** Open a Citrix-published Office application. Click **Terminate** in the session header.

**Expected:** Confirmation prompt warning that unsaved work in the remote app will be lost.
**Observed:** The Citrix process is killed immediately. Any unsaved work in the published application is gone.

**Suggested fix:** Before `Process.Kill()`, show a confirmation dialog with localized text "Terminate the Citrix session? Unsaved work in the remote application will be lost." Default button: Cancel.

Optional: add a "Disconnect" path that sends a graceful `WM_CLOSE` first and falls back to `Kill` after a timeout.

**References:** Nielsen UX-02 (user control — confirmation for destructive action).

---

### F21 — No `Send Ctrl+Alt+Del` / `Send Ctrl+Alt+End` helper · 🟡 Medium

**Origin:** Cowork F19.

**Where:** Searched `src/Heimdall.Rdp/`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml(.cs)` — no `Ctrl+Alt+Del`, `VK_DELETE`, `SendKeys` references.

**Reproduction:** Connect to a Windows Server in embedded mode. Try to lock the remote Windows session, or to send the secure attention sequence on a domain-joined server requiring CAS.

**Expected:** A toolbar button or context menu item "Send Ctrl+Alt+Del" that posts the secure attention sequence to the remote session.
**Observed:** No such control. The user can use `Ctrl+Alt+End` in external mstsc mode (which works because mstsc handles it natively); embedded mode has no equivalent.

**Suggested fix:** Add a header-bar button (next to Resolution) that calls `IMsRdpClient.SendOnVirtualChannel` or, simpler: dispatch `WM_KEYDOWN`/`WM_KEYUP` with `VK_CTRL + VK_MENU + VK_DELETE` to the deepest child window of the host (similar to the anti-idle Shift key path).

**References:** Nielsen UX-06 (efficiency for power users), platform conventions for RDP clients.

---

### F22 — Split mode silently overrides external RDP profiles to embedded · 🟡 Medium

**Origin:** Codex F8.

**Where:** `src/Heimdall.App/Services/SplitService.cs:150-175, 768-776` (`ForceEmbeddedMode()` mutates the DTO copy).

**Reproduction:**
1. Configure an RDP server with `RdpMode = External`.
2. Start it normally — confirm it opens in `mstsc.exe`.
3. Now launch it via a split pane (split-launch / Command Palette into a split).
4. Observe the resulting tab.

**Expected:** The user is told that split panes require embedded mode, ideally before the launch.
**Observed:** `SplitService.ForceEmbeddedMode()` silently converts the DTO copy to embedded because external processes cannot be docked. That technical constraint is correct, but the user-facing flow has no explanation, so a profile setting appears to be ignored.

**Suggested fix:**
- Add a split-launch notice such as "Splits use embedded RDP; external mode opens only in normal tabs." Surface it as a one-time confirmation toast or a permanent inline note in the split picker.
- Optionally disable split actions for profiles whose required features are external-only (after F15: webcam, AdminMode, FullScreen).

**References:** Nielsen UX-04 (predictability).

---

### F23 — No "test connection" / pre-connect validation for RDP profiles · 🟡 Medium

**Origin:** Codex F9.

**Where:** `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:712-738, 1547-1556`; `src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs:967-1021`.

**Reproduction:**
1. Create an RDP server with questionable settings: invalid gateway text, unsupported external-only redirection in embedded mode, mistyped username/domain.
2. Save it.
3. Discover the problem only at connect time.

**Expected:** The RDP dialog highlights common RDP-specific mistakes and provides a "test connection" or "validate RDP settings" path similar in spirit to the SSH UX improvements.
**Observed:** RDP-specific inline validation covers `RdpAudioMode` and `RdpColorDepth` only. The basic credential section has username/password fields but no hint about optional password prompting, autofill behavior, domain format, or NLA credential implications.

**Suggested fix:**
- Add RDP-aware hints and validation for: domain/username format (`DOMAIN\user` vs UPN), mode/feature incompatibilities (cf. F9, F15, F22), StoreFront / app name when relevant, RD Gateway hostname (already validated in `RdpFileGenerator`, but failure is silent there).
- Add a "Test connection" action that exercises DNS / tunnel / TCP socket / NLA reachability without opening a full session — analogous to the equivalent SSH affordance.

**References:** Nielsen UX-03 (error prevention), prior SSH UX audit (`audit-ux-ssh-2026-04-25.md` F10 "Test Connection").

---

### F24 — Performance flags use imperative code-behind sync · 🟢 Low

**Origin:** Cowork F20.

**Where:** `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml.cs:160-169` (save), `:577-583` (load).

**Reproduction (technical):** Mutate `vm.RdpPerformanceFlags` in the ViewModel after the dialog is loaded (e.g., via a future "Reset to defaults" command). Observe checkbox state.

**Expected:** Checkboxes update to reflect the new bitmask.
**Observed:** The seven `DlgSrv_PerfDisable*` / `DlgSrv_PerfEnable*` checkboxes are sync'd only at `Loaded` and at `OnSaveClick`. They have no XAML `IsChecked` binding. A programmatic VM mutation between those two points is invisible to the UI.

**Suggested fix:** Refactor to per-flag boolean properties in the ViewModel and bind each checkbox. The conversion to/from the `RdpPerformanceFlags` int can live in `partial void OnXxxChanged` notifications. Removes ~60 lines of imperative code-behind.

**References:** Nielsen UX-04 (consistency with the rest of the dialog's MVVM pattern).

---

### F25 — Resolution menu presets hardcoded · 🟢 Low

**Origin:** Cowork F21.

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml:48-58` (10 hardcoded `MenuItem`s).

**Reproduction:** Try to add a 2560x1080 ultrawide preset to the menu without recompiling.

**Expected:** Either the list is configurable in Settings, or the user can type a custom resolution.
**Observed:** Presets are baked in. No "Custom…" entry, no Settings extension hook.

**Suggested fix:** Move the list to `AppSettings.RdpResolutionPresets` (string array `1920x1080,1680x1050,…`) and bind the menu to it. Add a "Custom…" item that opens a small dialog for width/height entry.

**References:** Nielsen UX-06 (efficiency / power-user flexibility).

---

### F26 — "Connecting" doesn't differentiate sub-phases · 🟢 Low

**Origin:** Cowork F22.

**Where:** `src/Heimdall.Core/StateMachine/ConnectionStateMachine.cs:34-44` (rich states: `ValidatingConfig`, `EstablishingTunnel`, `TunnelEstablished`, `LaunchingRdp`, `Connected`); `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:159, 411` writes raw "Connecting".

**Reproduction:** Connect a server with an SSH tunnel. Observe the header status.

**Expected:** Status shows "Establishing tunnel…" → "Launching Remote Desktop…" → "Connected".
**Observed:** Status shows just "Connecting" until the COM event sink fires `OnConnected`.

**Suggested fix:** Wire `EmbeddedRdpView` to subscribe to `ConnectionStateMachine.StateChanged` for its server ID and surface the localized `Status*` keys (`StatusEstablishingTunnel`, `StatusConnecting`, etc.) the metadata table already provides.

**References:** Nielsen UX-01 (granular status feedback).

---

### F27 — `CitrixUseSso` always visible · 🟢 Low

**Origin:** Cowork F23.

**Where:** `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:2003`.

**Reproduction:** Open ServerDialog → Citrix advanced. Observe "Use SSO (Kerberos)".

**Expected:** Visible only in domain-joined / Kerberos-applicable contexts, or with a tooltip explaining the prerequisite.
**Observed:** Always visible. Confusing for users on personal machines / non-domain-joined environments.

**Suggested fix:** Add a tooltip "Use Single Sign-On with the current Windows Kerberos identity. Requires a domain-joined client."

**References:** Nielsen UX-08 (contextual help).

---

### F28 — Some Settings unreachable from UI · 🟢 Low

**Origin:** Cowork F26.

**Where:** `AppSettings.RdpResizeEnableDelayMs` (line 54), `RdpArtifactCleanupDelayMs` (line 53), `RdpCredentialAutofillTimeoutMs` (line 52), `EmbeddedRdpTimeoutMs` (line 120 — partially exposed).

**Reproduction:** Try to change `RdpResizeEnableDelayMs` from 10 s to 5 s without editing JSON manually.

**Expected:** A "Tuning" sub-section in Settings → RDP exposing the timeout knobs.
**Observed:** Only `EmbeddedRdpTimeoutMs` is exposed (line 2316-2318 of MainWindow). The other three live only in JSON.

**Suggested fix:** Add a collapsed "Advanced timeouts" section with three `TextBox`-bound fields (numeric input). Annotate each with units (ms) and acceptable ranges.

**References:** Nielsen UX-06 (power-user efficiency).

---

### F29 — `EnsureHostHandle` retry budget exhaustion has no user signal · 🟢 Low

**Origin:** Cowork F27.

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:358-368, 424-442`.

**Observation:** If `IsVisualSurfaceReady()` is false 10 times in a row (each retry after 120 ms = 1.2 s), the code **continues anyway** with a `Warn` log. The user sees the surface render incorrectly or not at all, with no on-screen explanation.

**Suggested fix:** When the retry budget is exhausted, surface a diagnostic on `_ownerPane.FailureDetails` with a `RdpSurfaceNotReady` key suggesting "Please retry — the rendering surface failed to initialize." Plus actionable hint: try moving the window from one screen to another, or relaunching.

**References:** Nielsen UX-03 (visible failure mode).

---

### F30 — `RdpFatalError` collapses all error codes into one message · 🟢 Low

**Origin:** Cowork F28.

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:526-542`; `src/Heimdall.App/Views/EmbeddedRdp/RdpHostDiagnosticFactory.cs:41-48`; key `RdpFatalError: "Remote Desktop reported a fatal error."`.

**Observation:** The 24 disconnect codes are decoded with helpful localized strings. Fatal errors arrive separately and are mapped to a single key with the integer code as `Reason`. The `RdpStatusFatalErrorDetail: "Remote Desktop reported a fatal error ({0})."` key exists but is not used (cf. F1 plumbing).

**Suggested fix:** Either add per-code mapping for the small set of MsTscAx fatal-error codes, or at minimum use `RdpStatusFatalErrorDetail` to show the numeric code so support can act on it.

**References:** Nielsen UX-08 (help / diagnosability).

---

### F31 — Anti-idle has no UI indicator · 🟢 Low

**Origin:** Cowork F29.

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:910-934`.

**Observation:** When `RdpAntiIdle = true`, a Shift-key PostMessage is sent every `_antiIdleIntervalSeconds` (default 60 s) to the inner ActiveX surface. There's no visible indication anywhere that anti-idle is active for this session — the user can't tell whether the feature is running or not.

**Suggested fix:** Add a small badge/icon next to the status text when `_antiIdleTimer` is running, with a tooltip "Anti-idle is keeping this session alive. Click to disable."

**References:** Nielsen UX-01 (visibility), UX-05 (recognition over recall — show what's running).

---

### F32 — Autofill regex matches EN/FR titles only · 🟢 Low

**Origin:** Cowork F30.

**Where:** `src/Heimdall.Rdp/CredentialAutofill.cs:106-108` (`TitlePattern` matches `Windows Security|Securité Windows|Credential|mstsc`).

**Observation:** A user running Heimdall on a German/Spanish/Italian/etc. Windows installation will see autofill silently fail because the credential dialog title doesn't match the regex. The application targets EN/FR only per `CLAUDE.md`, so the impact is limited, but the failure mode is invisible (cf. F7).

**Suggested fix:** Either expand the regex to cover the top 5–10 European Windows locales (DE, ES, IT, PT, NL, PL — title strings are documented by Microsoft), or fall back to class-name matching only (the class names `Credential Dialog Xaml Host` and `Windows Security` don't depend on the OS locale).

**References:** Nielsen UX-04 (consistency across locales).

---

### F33 — Anti-idle hint says "mouse movement" but the code sends Shift · 🟢 Low

**Origin:** Codex F11.

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:936-977` (sends Shift key down/up); `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:588-591` (allowBackgroundInput); `locales/en.json` `RdpAntiIdleHint`.

**Reproduction:**
1. Enable RDP anti-idle in a server profile.
2. Read the `RdpAntiIdle` checkbox hint in the ServerDialog.
3. Compare to the embedded RDP anti-idle implementation.

**Expected:** The UI accurately describes what Heimdall sends to the remote session and any caveats for keyboard-sensitive remote apps.
**Observed:** The hint says Heimdall "Periodically simulates a small mouse movement to prevent the remote server from disconnecting…", but the code posts Shift key down/up messages to the ActiveX child window. Shift is intended to have no visible effect, but the mismatch makes troubleshooting modifier-sensitive remote apps harder.

**Suggested fix:** Update the hint to say "Periodically sends a harmless Shift tap to keep the remote session active." Or switch the implementation to actual `WM_MOUSEMOVE` events to match the existing wording. Consider exposing the anti-idle method in advanced settings if both approaches are supported later.

**References:** Nielsen UX-04 (consistency between described and actual behavior).

---

## Section per audit angle

### 1. Connection lifecycle UX

Findings: **F1, F2, F3, F6, F10, F26, F29.**

The COM/ActiveX scaffolding is meticulous: 2 layout flushes pre-connect (`FlushLayoutPipeline`), explicit `EnsureHostHandle`, post-connect block on resolution updates, bounded auto-reconnect (20 attempts via `MaxReconnectAttempts`), `CancelAutoReconnect` flag respected during dispose. Pre-warmed COM at startup via `App.xaml.cs:PreWarmRdpRuntime` is a clear UX win (~400 ms saved on first connect).

The user-facing layer hasn't kept up with the engine. Status messages bypass the i18n pipeline that already has matching keys (F1), the auto-reconnect attempt counter is captured but never displayed and there is no cancel affordance (F2), the disconnect overlay treats user-initiated disconnect the same as a network drop (F3), the external-mode `Connected` state fires before authentication (F6), the progress bar disappears during reconnect cycles when it would be most helpful (F10). The rich `ConnectionStateMachine` states never reach the view (F26). The retry budget on visual-surface readiness has a silent exhaustion path (F29).

### 2. Credential autofill UX

Findings: **F7, F8, F32.**

The mechanics are good: dual UI Automation + Win32 fallback, host-hint regex disambiguation, broker-process allow-list, password buffer cleared in `finally`. The settings layer is inconsistent: the timeout is honored in external mode but not in embedded mode (F8). The user-facing layer is invisible: timeouts and failures only produce log warnings and the user has no signal about what autofill is doing (F7 — the highest-impact autofill issue). A non-EN/non-FR Windows host will see autofill silently fail (F32 — limited impact given EN/FR-only target locale).

### 3. Citrix StoreBrowse UX

Findings: **F17, F18, F19, F20, F27.**

Citrix is the weakest sub-area. Three launch modes coexist with implicit precedence and no UI signal (F17). One validation error is wrapped in a misleading message (F18). The 30-second window-capture polling has no progress feedback and the optimistic `Connected` state misleads users (F19). Terminate kills the process without confirmation (F20). The `CitrixUseSso` checkbox lacks context (F27). Codex broadens F19 with a missing StoreBrowse discovery flow (manual app name typing only).

The capture path itself is solid: scans 5 known Citrix process names, uses `EnumWindows` + `GetWindowRect` + class-name allow-list (`Transparent Windows Client`, `CtxSeamless`, `CDViewer`, `TUIWindowClass`, `IHWindow`), picks the largest viable window, calls `SetParent` with style adjustments, monitors via `IsWindow` poll. Disposal correctly restores the original window style and reparents back to the desktop.

### 4. Resolution / DPI handling UX

Findings: **F13, F14, F15, F25.**

DPI-awareness is correct: WPF logical pixels are converted to physical via `PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice`, with a sane fallback to 1.0. SmartSizing is enabled by default to absorb pixel rounding during the debounce delay. Dynamic resolution uses `IMsRdpClient9+.UpdateSessionDisplaySettings` with a fallback to `Reconnect(width, height)` for older interfaces.

Issues are at the surface: manual resolution change is silently ignored during the 10 s stabilization window (F13), the documentation contradicts the actual delay (F14), external mode `.rdp` files don't propagate user-configured resolution (F15), and the preset list isn't extensible (F25). Multi-monitor support uses `TrySetDynamic("UseMultimon", …)` so it fails silently if the COM interface version is too old — no UX impact in modern environments, doc-only.

### 5. Keyboard / mouse / clipboard UX

Findings: **F21, F31, F33.**

Anti-idle (`OnAntiIdleTick`) drills to the deepest child window and sends `WM_KEYDOWN`/`WM_KEYUP` for `VK_SHIFT`. Combined with `allowBackgroundInput=1` (correctly set in `ApplyRedirectionSettings`), this works on background tabs. `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED)` keeps the local display on during a session.

Clipboard redirection toggle is per-server and per-default. No issue identified on the toggle itself, only on the absence of a "Send Ctrl+Alt+Del" helper (F21) — a standard expected by power users on RDS / domain-joined hosts. Anti-idle has no UI indicator (F31), and its hint copy contradicts the implementation (F33). Modifier-stuck issues (a known WPF/WindowsFormsHost gotcha) were not reproducible from static analysis; would need a manual capture.

### 6. Disconnect reason UX

Findings: **F5, F16, F30.**

Coverage: 24 of 24 MsTscAx codes mapped via `RdpActiveXHost.GetDisconnectReasonKey`, plus an `RdpDisconnectUnknownCode` fallback. EN/FR localization parity is 86 / 86 keys (perfect). The infrastructure is sound.

Quality is mixed. The reason key is correctly populated on the pane diagnostic but the overlay shows a generic message (F5 — biggest miss in this category, since the data is right there). Many messages are technically correct but not actionable (F16), particularly for the network/security/encryption family. Fatal errors collapse to a single key without per-code mapping (F30).

### 7. i18n

Findings: **F1, F32.**

86 `Rdp*` keys in EN, 86 in FR — exact parity, no dead keys, no orphaned. CI `i18n-parity` job covers this. The issue is *usage*: status text bypasses the pipeline (F1) and `RdpStatusReconnecting` / `RdpStatusFatalErrorDetail` keys are dormant. The autofill regex is locale-bound (F32) but only EN/FR are targeted so the impact is bounded.

### 8. Settings UX

Findings: **F4, F9, F11, F12, F15, F23, F27, F28.**

The single most user-facing issue is F4: `RdpUseGlobalDefaults` is a checkbox that does nothing at runtime. F12 (5 missing redirections in defaults) is a prerequisite for fixing F4. F11 (incomplete AspectRatio choices) and F27 (Citrix SSO context) are local glitches. F9 (webcam silently disabled in embedded) and F15 (external mode resolution / Admin / FullScreen unreachable) are mode-specific gaps. F28 catalogs three timeout settings that exist only in JSON. F23 introduces the missing pre-connect validation / test-connection capability analogous to the SSH UX.

### 9. Embedded vs external mode UX

Findings: **F6, F9, F15, F22.**

The mode toggle in ServerDialog is clear (Embedded / External, with descriptive labels via `RdpModeEmbedded`/`RdpModeEmbeddedDesc` / `RdpModeExternal`/`RdpModeExternalDesc`). Mode-specific feature gaps are not communicated: webcam (F9) silently no-ops in embedded; resolution / AdminMode / FullScreen (F15) are only reachable on the external path but not exposed there either; external mode lies about state (F6); split mode silently rewrites external→embedded (F22).

The embedded-vs-external split itself is correct: external mode writes a sanitized `.rdp` file with restricted ACL, launches `mstsc.exe`, and schedules cleanup of the file + CredMan entry after `RdpArtifactCleanupDelayMs` (default 10 s). One robustness note: if the user reconnects within 10 s, the cleanup still runs against the previous artifact — fine because each artifact has a unique path with `Guid.NewGuid():N`.

### 10. Splits & tabs UX

Findings: **F22.**

The split system handles RDP correctly per `CLAUDE.md` gotchas: `Visibility=Collapsed` → `Child=null` → `Disconnect()` → `DetachEventSink()` → `Dispose()` order is observed in `EmbeddedRdpView.Dispose` (lines 188-227). The Command Palette uses `Popup` for HWND airspace above the ActiveX surface (documented in `CLAUDE.md`). `SetFullscreen` collapses the header bar without disrupting the COM control. Anti-idle continues to function on background tabs because `allowBackgroundInput=1` is set in `ApplyRedirectionSettings`. The single split-related UX issue is F22 — `ForceEmbeddedMode()` is silent.

A note for the next pass: a pure visual capture session will be required to confirm there is no repaint glitch on tab switch with active RDP session, which static analysis cannot confirm.

---

## What was NOT flagged (and why)

- **CRLF sanitization in `.rdp` files** — already correctly implemented (`RdpFileGenerator.SanitizeValue` strips CR/LF/`\0`); not a UX issue.
- **ACL on temporary `.rdp` files** — correct via `SecureFileWriter.WriteAndProtect` + `AclEnforcer.SetFileAcl` fallback. Out of UX scope.
- **COM dispose order** — meticulous, follows the `CLAUDE.md` gotcha. Out of scope.
- **COM pre-warm at startup** — already in place; UX win, not an issue.
- **`MaxReconnectAttempts = 20`** — bounded retry is good UX (the user is not left stranded waiting forever); not flagged. Could be configurable as a power-user knob, but this is a feature-request, not a UX issue.
- **HostHint regex strictness in autofill** — already gated against fallback "single broker match → accept" (mentioned in the prior code-quality audit as SEC-P06); not duplicated here.
- **`DefaultMsTscAxClsid`** vs. `NotSafeForScriptingClsid` — code allows passing a CLSID; transparent to users.
- **`UpdateResolution` fallback to `Reconnect`** — correct multi-version COM dance; transparent to users.
- **Resolution menu items hardcoded `1920x1080` etc.** — these labels are language-neutral digits, no i18n issue. Extensibility flagged separately (F25) as Low.
- **Performance flags bitmask values (`0x01..0x100`)** — internal IMsRdpClient constants; not a UX issue.
- **`SmartSizing = true` default** — correct given the embedded model; visual artifact during resize is absorbed, not a UX issue.
- **`administrative session:i:1` write path** — correct .rdp file format; flagged separately as F15 because the *trigger* (UI checkbox) is missing, not the .rdp behavior.
- **Pageant / SSH-related gotchas** — out of RDP scope.
- **Aspect ratio `AspectRatio.Stretch` default for unconfigured profiles** — sensible default, no UX issue.
- **Tab error badges in ServerDialog (`HasOptionsTabErrors`, `OptionsTabErrorCount`)** — already correctly implemented, helpful UX, not flagged.
- **`AutomationProperties.Name` coverage** — checked across `EmbeddedRdpView` toolbar buttons, status text, overlay buttons; all correctly localized at runtime via `SetName`. Not a UX issue.
- **`StatusTextBlock.LiveSetting="Polite"`** — already set; good a11y practice. Not flagged.
- **External mstsc.exe stdout/stderr** — process is launched detached; no readable output piped, but `mstsc.exe` doesn't produce useful console output. Not flagged.
- **`InvokeMember(BindingFlags.InvokeMethod, …)` reflection on `UpdateSessionDisplaySettings`** — required for COM late binding compatibility. Not a UX issue.
- **Citrix shell-arg validation rejecting `|`, `&`, `;`, `` ` ``, `$`, `\n`, `\r`** — necessary security gate. Only the *error message* is flagged (F18), not the gate itself.
- **Citrix `EmbeddedContainer.Visibility = Collapsed` initial state** — correct; the InfoPanel is shown until the captured window is reparented. Not a UX issue.
- **`ResolveSelfServicePath` fallback chain (3 paths + PATH search)** — robust resolution. Not flagged.
- **External RDP mode mstsc launch via `Process.Start("mstsc.exe", quoted-rdp-path)`** — correct, sanitized via `SecureFileWriter`. Not a UX issue.
- **`HealthDot` color binding via code-behind in CitrixView** — slightly imperative but works; matches the rest of the file's style. Low impact, not flagged.
- **Splits: `_emptyPane` is per-instance (not static)** — correct pattern from `CLAUDE.md`. Not a UX issue.
- **CredentialAutofill `TryInjectPasswordViaWin32` heuristic of "second Edit control = password"** — heuristic but well-documented and cross-validated by class-name + UIA fallback. Not flagged.
- **`RdpDisconnectUnknownCode` fallback** — correct safety net. Not flagged.
- **ActiveX dispose order** *(Codex echo)* — view hides `FormsHost`, clears `Child`, detaches events, cancels auto-reconnect, disconnects, detaches, and disposes in deliberate order, matching the documented crash-avoidance pattern.
- **Pre-connect layout flush** *(Codex echo)* — `BeginConnect` forces host handle creation and layout flushing before `Connect()`, so the known "phantom client" ActiveX issue is not re-flagged.
- **RDP disconnect code coverage** *(Codex echo)* — current mapper covers 24 codes and inspected locale files contain matching keys, so coverage itself is not the finding (quality of wording is — see F16).
- **Command Palette above RDP** *(Codex echo)* — palette is a `Popup`, not a normal WPF overlay inside the ActiveX airspace, so not flagged as an RDP occlusion bug.
- **Clipboard redirection** *(Codex echo)* — setting is wired through redirection options and ActiveX advanced settings. No code-level UX issue without running remote clipboard scenarios manually.
- **File redirection semantics** *(Codex echo)* — drive redirection is a coarse RDP setting rather than an app-managed file transfer UX. No finding raised because this is expected for MsTscAx/mstsc.
- **Post-connect resolution delay as a concept** *(Codex echo)* — delay is justified by the ActiveX gotcha; the finding is only that user-triggered changes during the delay are not queued or explained (F13).

---

## Action plan (priority order)

### Priority 1 — Core flow visibility (recommended before next release)

1. **F5** — overlay reads `pane.FailureDetails` and shows the decoded reason (single highest-leverage fix).
2. **F1** — route status text through the i18n pipeline (also unblocks F2/F26/F30 which depend on the same plumbing).
3. **F2** — auto-reconnect status with attempt counter + decoded reason + cancel affordance.
4. **F7** — autofill state surfaced in-tab (banner / row, with cancel).
5. **F3** — suppress reconnect overlay on user-initiated disconnect.
6. **F6** — external mode `Connected` state corrected (intermediate launched-external state + Exited tracking).

### Priority 2 — Settings consistency (next sprint)

7. **F4** — wire `RdpUseGlobalDefaults` (depends on F12).
8. **F12** — add 5 missing redirection defaults to Settings UI.
9. **F15** — external `.rdp` file uses configured resolution + AdminMode + FullScreen toggles.
10. **F11** — expose all AspectRatio options.
11. **F9** — disable webcam in embedded mode + tooltip / mode-switch suggestion.
12. **F22** — split-launch notice on external→embedded conversion.
13. **F23** — RDP test-connection / pre-connect validation.

### Priority 3 — Citrix and hot fixes

14. **F19** — Citrix capture progress + cancel + StoreBrowse discovery.
15. **F20** — Citrix terminate confirmation.
16. **F17** — Citrix mode precedence indicator.
17. **F18** — dedicated `CitrixLaunchCommandRejected` key.
18. **F21** — `Send Ctrl+Alt+Del` button.
19. **F16** — disconnect message actionability rewrite (10 keys).

### Priority 4 — Polish (backlog, when convenient)

20. **F8** — embedded autofill timeout reads setting.
21. **F10** — progress bar visible during reconnect.
22. **F13** — queue manual resolution changes during stabilization.
23. **F14** — fix `CLAUDE.md` 5 s → 10 s.
24. **F33** — anti-idle hint copy.
25. **F31** — anti-idle UI indicator.
26. **F30** — fatal-error code surfaced.
27. **F27** — Citrix SSO tooltip.
28. **F28** — expose 3 hidden timeout settings.
29. **F29** — surface `EnsureHostHandle` exhaustion.
30. **F26** — connect sub-phase status from state machine.
31. **F32** — autofill regex locale extension (or class-name-only fallback).
32. **F25** — resolution presets configurable.
33. **F24** — performance flags MVVM refactor.

---

## Annex — files read during this audit

**`Heimdall.Rdp` (full):**
- `IRdpSession.cs`
- `ActiveX/RdpActiveXHost.cs` (658 L)
- `RdpRedirectionOptions.cs`
- `RdpFileGenerator.cs`
- `CredentialAutofill.cs` (768 L)
- `CredentialManagerHelper.cs` (referenced)
- `AspectRatioManager.cs` (referenced)

**`Heimdall.App/Services/Handlers`:**
- `RdpHandler.cs` (335 L)
- `CitrixHandler.cs` (272 L)
- `RdpSessionDiagnosticFactory.cs` (90 L)

**`Heimdall.App/Views`:**
- `EmbeddedRdpView.xaml` + `.xaml.cs` (1146 L)
- `EmbeddedCitrixView.xaml` + `.xaml.cs` (483 L)
- `EmbeddedRdp/RdpHostDiagnosticFactory.cs` (50 L)
- `Dialogs/ServerDialog.xaml` (RDP section ~ lines 1450-1650)
- `Dialogs/ServerDialog.xaml.cs` (RDP-relevant excerpts)
- `Dialogs/RdpImportDialog.xaml`

**`Heimdall.Core/Configuration`:**
- `ServerProfileDto.cs` (RDP fields, lines 40-112)
- `AppSettings.cs` (RDP defaults, lines 52-120)
- `GroupDefaultsDto.cs` (referenced)
- `ConnectionStateMachine.cs`

**`Heimdall.App/ViewModels`:**
- `Dialogs/ServerDialogViewModel.cs` (RDP-relevant excerpts)
- `SettingsViewModel.cs` (RDP defaults block, ApplyRdpModeToAllAsync, lines 350-470)

**`Heimdall.App/Services` (split path):**
- `EmbeddedSessionManager.cs` (referenced)
- `SplitService.cs` (`ForceEmbeddedMode` referenced)

**`Heimdall.App/MainWindow.xaml`:**
- Settings RDP tab (lines 2154-2225)

**`locales/`:**
- `en.json` and `fr.json` — all 86 `Rdp*` keys + 6 `Citrix*` keys read and compared.

**Other:**
- `CLAUDE.md` (RDP gotchas section)
- `audit-ssh-rdp.md` (prior audit, format reference only — no inheritance of findings)
- `audit-ux-ssh-2026-04-25.md` (prior SSH UX audit, format reference — F23 echoes the test-connection pattern)

---

## Status — in progress (10/33 closed)

| # | Title | Closed in |
|---|---|---|
| F5 | Reconnect overlay loses the decoded disconnect reason | `51c4de3` |
| F1 | Status text bypasses the i18n pipeline | `dcda468` |
| F2 | Auto-reconnect status hides attempt count, reason, and cancel affordance | `9944f50` |
| F3 | Reconnect overlay shown on user-initiated disconnect | `8a91d1f` |
| F7 | Credential autofill is invisible to the user | `1750409` |
| F6 | External RDP marked Connected as soon as mstsc.exe starts | `8fbec1e` |
| F12 | Settings RDP defaults incomplete — 5 missing redirections | `8ca6f9e` |
| F4 | RdpUseGlobalDefaults checkbox has no runtime effect | `cd83d35` |
| F8 | Embedded autofill timeout is hardcoded | `73e8f9f` |
| F10 | Progress bar hidden during reconnecting | `73e8f9f` |

Once a finding is closed by a Codex commit, append a status line under its detailed entry:

```
**Status:** ✅ Closed — commit `<short-sha>` on YYYY-MM-DD.
```

After all 33 findings are closed, replace this section with:

```
## Status — All findings closed (33/33)

| # | Title | Closed in |
|---|---|---|
…ledger…
```

(Mirrors the `audit-ux-ssh-2026-04-25.md` final structure.)

---

*Consolidated audit: 0 🔴 / 7 🟠 / 16 🟡 / 10 🟢 = 33 findings. Cowork F1–F30 + Codex F1–F11 mapped, 4 Codex-only findings adopted (F6, F22, F23, F33), Cowork F24+F25 merged into F7 with severity promoted to High, 1 disagreement resolved (F4 retained as High over Codex's choice not to flag, then closed via `RdpProfileResolver`; evidence and outcome in Notes). Reproducibility: a third independent pass on the same unmodified codebase should confirm this list within ±2 findings on the Low end.*
