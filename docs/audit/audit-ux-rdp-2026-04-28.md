# UX Audit Report — RDP Layer (Heimdall.Next)

**Date:** 2026-04-28
**Audit type:** UX (user experience), parallel to Codex audit (cross-reference pending)
**Audit author:** Claude / Cowork
**Scope:** `src/Heimdall.Rdp/**`, `src/Heimdall.App/Services/Handlers/{RdpHandler,CitrixHandler,RdpSessionDiagnosticFactory}.cs`, `src/Heimdall.App/Views/{EmbeddedRdpView,EmbeddedCitrixView}.xaml(.cs)`, `src/Heimdall.App/Views/EmbeddedRdp/RdpHostDiagnosticFactory.cs`, `src/Heimdall.App/Views/Dialogs/{ServerDialog,RdpImportDialog}.xaml(.cs)`, `src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs`, `src/Heimdall.App/ViewModels/SettingsViewModel.cs`, `src/Heimdall.App/MainWindow.xaml` (RDP/Citrix tabs), `locales/en.json`, `locales/fr.json`.
**Standards:** Nielsen 10 usability heuristics, internal `CLAUDE.md` RDP gotchas, prior SSH UX audit (`audit-ux-ssh-2026-04-25.md`) format reference.

---

## Executive summary

The RDP layer is functionally complete and code-quality solid (covered by `audit-ssh-rdp.md` from 2026-04-24). UX-wise it is uneven: the COM/ActiveX scaffolding is meticulous (layout flushes, dispose order, COM pre-warm, anti-idle, autofill UI Automation + Win32 fallback) but the **end-user feedback layer is thin**. Several settings are silently ignored, one user-visible toggle (`RdpUseGlobalDefaults`) appears to have no runtime effect, status messages bypass the i18n pipeline that already exists for them, and the disconnect overlay shows a generic message even though a precise reason key has just been computed. Multiple disconnect reasons are clear *technically* but lack actionable next steps. The Citrix path is launch-and-pray — it polls 30s for a window with no progress feedback, then can kill the user's session without confirmation. There is no Ctrl+Alt+Del / Ctrl+Alt+End helper for the RDP surface. The first-pass tally is **0 🔴 Critical · 5 🟠 High · 14 🟡 Medium · 11 🟢 Low** — 30 findings total.

Final count: **0 🔴 Critical · 5 🟠 High · 14 🟡 Medium · 11 🟢 Low**

---

## Findings list

| # | Severity | Category | Title |
|---|---|---|---|
| F1 | 🟠 High | i18n / Connection lifecycle | Status text bypasses the i18n pipeline (`UpdateSessionStatus("Connecting")` writes raw English) |
| F2 | 🟠 High | Connection lifecycle | Auto-reconnect attempt counter is captured but never surfaced (`RdpStatusReconnecting` key exists with `{0}` placeholder, never used) |
| F3 | 🟠 High | Connection lifecycle | Reconnect overlay is shown after **user-initiated** disconnect (no distinction from unexpected drops) |
| F4 | 🟠 High | Settings UX | `RdpUseGlobalDefaults` per-server checkbox is exposed and persisted but never honored at connect time |
| F5 | 🟠 High | Disconnect reasons | Disconnect overlay loses context — `pane.FailureDetails` carries the reason key but the overlay shows a generic message |
| F6 | 🟡 Medium | Credential autofill | `RdpCredentialAutofillTimeoutMs` setting ignored in embedded mode (hardcoded 90 s) |
| F7 | 🟡 Medium | Settings UX | Webcam redirection checkbox is silently ineffective in embedded mode (works only in external `.rdp` mode) |
| F8 | 🟡 Medium | Connection lifecycle | ProgressBar (`RdpLoadingBar`) hidden during `Reconnecting` state — only shown during initial `Connecting` |
| F9 | 🟡 Medium | Settings UX | AspectRatio ComboBox in ServerDialog only exposes 2 options; code supports 5 (Auto/Dynamic/16:9/4:3/21:9) and i18n keys exist |
| F10 | 🟡 Medium | Settings UX | 5 RDP redirections (COM ports / Smart cards / Webcam / USB / Audio capture) missing from global Settings tab even though they exist per-server |
| F11 | 🟡 Medium | Resolution / DPI | Manual resolution change is silently ignored during the 10 s post-connect stabilization window |
| F12 | 🟡 Medium | Documentation | `CLAUDE.md` claims a 5 s post-`OnConnected` block; actual default is 10 s (`_initialResizeEnableDelay`) |
| F13 | 🟡 Medium | Embedded vs external | External mode `.rdp` file ignores user-configured resolution and forces `1920x1080`; AdminMode/FullScreen unreachable from UI |
| F14 | 🟡 Medium | Disconnect reasons | Several disconnect messages lack actionability (NetworkError, EncryptionError, SecurityError, ClientDecompressionFailed, CredSspPolicyError, ConnectionTimeout, InternalError) |
| F15 | 🟡 Medium | Citrix StoreBrowse | Three Citrix launch modes (LaunchCommandLine / IcaFilePath / StoreFrontUrl) coexist with implicit precedence; no warning when more than one is configured |
| F16 | 🟡 Medium | Citrix StoreBrowse | Misleading error message: shell-character rejection in `CitrixLaunchCommandLine` reports `CitrixNoConnectionConfigured` instead of "forbidden characters" |
| F17 | 🟡 Medium | Citrix StoreBrowse | 30 s window-capture polling with zero feedback to the user — looks frozen |
| F18 | 🟡 Medium | Citrix StoreBrowse | "Terminate" kills the Citrix process without confirmation (data-loss risk) |
| F19 | 🟡 Medium | Keyboard / mouse | No "Send Ctrl+Alt+Del" / "Send Ctrl+Alt+End" helper button for the RDP surface |
| F20 | 🟢 Low | Code quality | Performance flags checkboxes use imperative code-behind sync (load/save) instead of ViewModel binding — drifts from the rest of the dialog's MVVM pattern |
| F21 | 🟢 Low | Resolution / DPI | Resolution menu presets are hardcoded in XAML; no way to add custom presets and no localization for the labels (digits only, OK in any locale, but extension hook missing) |
| F22 | 🟢 Low | Connection lifecycle | "Connecting" status doesn't differentiate the sub-phases (validating tunnel / launching mstsc / waiting for COM event sink) — the StateMachine has rich states but the view uses raw strings |
| F23 | 🟢 Low | Settings UX | `CitrixUseSso` checkbox is always visible regardless of context — confusing for non-Kerberos environments |
| F24 | 🟢 Low | Credential autofill | External-mode autofill timeout produces only a `FileLogger.Warn` — user sees no UI hint that autofill ran and gave up |
| F25 | 🟢 Low | Credential autofill | Embedded-mode autofill timeout produces only a log warn — user sees the CredUI dialog stuck on screen with no pre-filled password |
| F26 | 🟢 Low | Settings UX | `RdpResizeEnableDelayMs`, `RdpArtifactCleanupDelayMs`, `RdpCredentialAutofillTimeoutMs` settings exist in `AppSettings` but are not exposed in the Settings tab |
| F27 | 🟢 Low | Connection lifecycle | `EnsureHostHandle` retry budget is 10 × 120 ms = 1.2 s; if the surface still isn't ready, `BeginConnect` proceeds anyway with a warning logged — confusing failure mode |
| F28 | 🟢 Low | Disconnect reasons | `RdpFatalError` is mapped to a single i18n key — no per-error-code mapping (only the 24 disconnect codes are decoded) |
| F29 | 🟢 Low | Anti-idle | Anti-idle Shift key injection has no UI indicator — user can't tell whether anti-idle is active for the current session |
| F30 | 🟢 Low | i18n | `CredentialAutofill.TitlePattern` only matches EN + FR; users on a German/Spanish/Italian locale will see autofill silently fail (project targets EN/FR only, but worth tracking) |

---

## Detailed findings

### F1 — Status text bypasses the i18n pipeline · 🟠 High · *Connection lifecycle / i18n*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:686-707` (`UpdateSessionStatus`), called from lines 159, 258, 411, 468, 521, 539, 564, 576, 713.

**Reproduction:**
1. Switch the application locale to French.
2. Connect to any RDP server in embedded mode.
3. Observe the status next to the endpoint in the session header bar.

**Expected:** Localized status (e.g. "Connexion…" / "Connecté" / "Déconnecté").
**Observed:** Raw English strings ("Connecting", "Connected", "Disconnected", "Error", "Reconnecting", "Disconnecting") rendered verbatim in the header `StatusTextBlock` and in the tab `Status` property.

The localization keys exist in `locales/en.json` / `locales/fr.json`:

- `RdpStatusPreparing`, `RdpStatusWaiting`, `RdpStatusConnectedDetail`, `RdpStatusDisconnectedDetail`, `RdpStatusErrorDetail`, `RdpStatusReconnecting`, `RdpStatusDisconnecting`, `RdpStatusFatalErrorDetail` — all 8 already translated EN+FR, but not branched.

**Suggested fix:**
- Replace every `UpdateSessionStatus("Connecting")` / `"Connected"` / etc. with calls that pass an enum or a key, then look up the localized string inside `UpdateSessionStatus`.
- Keep the raw English string only for the internal `_sessionTab.Status` if it's used as a state key for binding/converters; otherwise localize that too.
- Make sure the status comparisons (e.g., `string.Equals(status, "Connecting", …)` at line 695) move from string compare to an enum.

**References:** Nielsen heuristic UX-04 (consistency); SSH audit fixed equivalent issue.

---

### F2 — Auto-reconnect attempt counter is never surfaced · 🟠 High · *Connection lifecycle*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:556-566` (`OnRdpAutoReconnecting`); `locales/en.json:2341` (`RdpStatusReconnecting: "Reconnecting (attempt {0})..."`).

**Reproduction:**
1. Connect RDP. Pull the network cable for ~5 s, then plug back in to trigger MsTscAx auto-reconnect.
2. Observe the header status during the reconnect cycle.

**Expected:** Status displays "Reconnecting (attempt 3)…" (or the localized equivalent) so the user knows how many tries are left before the 20-attempt cap fires.
**Observed:** Header just reads "Reconnecting" (raw, see F1) — the `attemptCount` argument from the COM event sink is captured at the method signature but never read.

**Suggested fix:** When localizing the status label (cf. F1), pass `attemptCount` to a `Format("RdpStatusReconnecting", attemptCount)` call. Also consider showing the configured cap (20) for context: "Reconnecting (3 of 20)…".

**References:** Nielsen UX-01 (system status visibility), UX-08 (help — the cap is a knowable bound).

---

### F3 — Reconnect overlay shown on user-initiated disconnect · 🟠 High · *Connection lifecycle*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:506-524` (`OnRdpDisconnected` calls `ShowReconnectOverlay()` unconditionally).

**Reproduction:**
1. Connect RDP. Click the **Disconnect** toolbar button (header bar).
2. Observe the result.

**Expected:** Quiet close, or simply "Disconnected" status without an overlay (the user explicitly asked to close).
**Observed:** The full reconnect overlay appears with "The Remote Desktop session has ended" + Reconnect / Close buttons. The user has to dismiss it after pressing Disconnect, even though they just told the app to disconnect.

The intended COM disconnect reasons for user-initiated close are `1` (`LocalUser`) and `2` (`UserLogoff`). They both currently route through the same `ShowReconnectOverlay()` path as `260 DnsLookupFailed`, `516 SocketConnectFailed`, etc.

**Suggested fix:** In `OnRdpDisconnected(int reason)`, suppress the overlay when `reason ∈ {0, 1, 2, 3}` (NoInfo / LocalUser / UserLogoff / AdminDisconnect — all clean-exit codes). Show a brief toast/inline status instead and keep the auto-close behavior of the tab if the user dispatched the disconnect themselves. Track an internal flag `_userInitiatedDisconnect` set in `OnDisconnectClick` for an even cleaner gate.

**References:** Nielsen UX-04 (consistency between intent and feedback).

---

### F4 — `RdpUseGlobalDefaults` checkbox has no runtime effect · 🟠 High · *Settings UX*

**Where:** `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1603` (checkbox); `src/Heimdall.Core/Configuration/ServerProfileDto.cs:93` (default `true`); `src/Heimdall.App/Services/Handlers/RdpHandler.cs:127-146` (reads server fields directly, no merge with `AppSettings.RdpDefault*`).

**Reproduction:**
1. Open Settings → RDP tab. Set `RdpDefaultRedirectClipboard = false`, `RdpDefaultRedirectDrives = true`, `RdpDefaultMultiMonitor = true`.
2. Open or create a server, leave **Use global defaults** checked, manually toggle **Clipboard** to `true`, **Drives** to `false`, **Multi-Monitor** to `false`. Save.
3. Connect.

**Expected (one of):**
- (a) The "Use global defaults" toggle disables and visually overrides the per-server checkboxes, so the connect uses the global values.
- (b) The toggle has a clear meaning (e.g. "fallback for fields where the server profile is at its default") and the actual behavior is documented.

**Observed:** The connect uses the per-server fields verbatim (lines 129-146 of `RdpHandler.cs`). The `RdpUseGlobalDefaults` flag is read in `ServerDialogViewModel`, persisted into the DTO, propagated through import/migration code paths, but **no handler** consults it before launching mstsc or feeding `RdpRedirectionOptions`. The user sees a knob that does nothing.

**Suggested fix:**
- If the intent is "use global defaults": the connect code path (both embedded `EmbeddedRdpView.BuildRedirections` line 1096-1118 and external `RdpHandler.ConnectAsync` line 127-146) must check the flag and read from `AppSettings.RdpDefault*` when it is `true`.
- Until that wiring exists, hide or disable the checkbox so the user doesn't believe they're configuring something. A grey-out + tooltip "Coming soon" is a stop-gap; a feature kill is also acceptable.
- Add an integration test asserting that flipping the flag changes the values fed to `RdpRedirectionOptions`.

**References:** Nielsen UX-04 (consistency between control state and effect), UX-03 (error prevention — currently the user is silently misled).

---

### F5 — Reconnect overlay loses the disconnect reason · 🟠 High · *Disconnect reasons*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:520, 814-822` (`SetPaneDiagnostic` + `ShowReconnectOverlay`).

**Reproduction:**
1. Connect RDP with wrong credentials (or to a server with NLA disabled).
2. The session disconnects with reason `2055` (`BadCredentials`).

**Expected:** The overlay says "The credentials were not accepted." (the localized `RdpDisconnectBadCredentials` string already exists) with the Reconnect button and possibly an Edit Credentials shortcut.
**Observed:** The overlay shows the generic `RdpDisconnectedMessage`: "The Remote Desktop session has ended." The pane diagnostic is correctly populated with the `RdpDisconnectBadCredentials` key (line 520) but it's never read by the overlay.

This is the single most actionable improvement in the whole RDP path: the data is there, the keys are there, the rendering is missing.

**Suggested fix:** In `ShowReconnectOverlay()`:
- Read `_ownerPane?.FailureDetails ?? _sessionTab?.PrimaryPane?.FailureDetails`.
- If a diagnostic is present, build the message as `Localizer[diagnostic.MessageKey]` (or `Format` if the key has placeholders) and display it as the title; keep `RdpDisconnectedMessage` as the subtitle/secondary line for context.
- Surface the numeric code in a smaller font for forensic / support purposes.
- Optional: when the reason is `BadCredentials`, `UserNotFound`, `AccountLockedOut`, `AccountExpired`, `PasswordExpired`, replace the generic Reconnect button with "Edit credentials & retry" that opens the ServerDialog focused on the credentials section.

**References:** Nielsen UX-03 (error recovery), UX-08 (help/context for recovery).

---

### F6 — Embedded autofill timeout is hardcoded · 🟡 Medium · *Credential autofill / Settings UX*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:642` (`TimeSpan.FromSeconds(90)`); `src/Heimdall.Core/Configuration/AppSettings.cs:52` (`RdpCredentialAutofillTimeoutMs = 90000`).

**Reproduction:** Edit `Settings.RdpCredentialAutofillTimeoutMs` (e.g. set to 30000). Connect a server in embedded mode. Observe that autofill keeps polling for 90 s.

**Expected:** The setting is honored in both embedded and external modes.
**Observed:** Only `RdpHandler.ConnectAsync` line 245 uses the setting; `EmbeddedRdpView.TryAutofillCredentialsAsync` line 642 inlines a constant.

**Suggested fix:** Pass the timeout from `Settings` into `EmbeddedRdpView.InitializeSession` (or read from a static `AppSettings` accessor at autofill start time) and use it in `TryAutofillCredentialsAsync`.

**References:** Nielsen UX-04 (consistency).

---

### F7 — Webcam redirection silently ineffective in embedded mode · 🟡 Medium · *Settings UX*

**Where:** `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:581-583` (comment: "Webcam redirection works in external mode (.rdp file) only"); `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1585` (`DlgSrv_RedirWebcamCb`).

**Reproduction:** In ServerDialog → RDP → Device redirection, check **Webcam**. Save. Connect in embedded mode. Open Camera app on the remote.

**Expected:** Either the camera works on the remote, or the UI clearly says webcam redirection requires external mode.
**Observed:** The checkbox state is stored, the COM-level `ApplyRedirectionSettings` skips it (the comment acknowledges the limitation), and the user has no clue. The remote sees no camera and may waste time troubleshooting.

**Suggested fix (cheapest):**
- Add a hint label/tooltip next to the Webcam checkbox: "Webcam redirection requires external mode."
- Disable the checkbox when `RdpMode = Embedded`, with a tooltip explaining why.

**Suggested fix (cleaner):**
- Implement webcam via `IMsRdpClientNonScriptable7.CameraRedirConfigCollection`; the comment in `RdpActiveXHost.cs:581` notes it's not exposed via simple IDispatch but does exist on newer interfaces.

**References:** Nielsen UX-04 (consistency between control state and effect).

---

### F8 — ProgressBar hidden during reconnecting · 🟡 Medium · *Connection lifecycle*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:695-696` (`isConnecting = string.Equals(status, "Connecting", …)`).

**Reproduction:** Connect, then drop the network. Observe the header bar during the auto-reconnect cycle.

**Expected:** A progress indicator (the `RdpLoadingBar` indeterminate bar) is visible while reconnect attempts are in-flight, since the user can't interact with the surface during this time.
**Observed:** The bar only shows during the initial `Connecting` state and is hidden during `Reconnecting`. The screen surface is frozen with no progress affordance.

**Suggested fix:** Change the predicate to:

```csharp
var isConnecting = status is "Connecting" or "Reconnecting";
```

(After F1 is resolved, use the enum equivalent.)

**References:** Nielsen UX-01 (system status visibility).

---

### F9 — AspectRatio ComboBox underexposed · 🟡 Medium · *Settings UX*

**Where:** `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1518-1523`. Code support: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:1078-1093` (`ParseAspectRatio` accepts `Stretch`, `Auto`, `Preserve`, `Dynamic`, `16:9`, `4:3`, `21:9`).

**Reproduction:** Open ServerDialog → Options → Aspect Ratio. Observe the dropdown.

**Expected:** All ratios documented in `RdpAspectRatioHint` are reachable from the dropdown.
**Observed:** Only `Stretch` and `Preserve` are exposed; the i18n keys `RdpAspectAuto` and `RdpAspectDynamic` exist (with EN+FR translations) but no `ComboBoxItem` references them. `16:9` / `4:3` / `21:9` are also unreachable.

**Suggested fix:** Add the missing `ComboBoxItem`s to `DlgSrv_RdpAspectRatio` ComboBox with their tags matching the values `ParseAspectRatio` accepts. Add `RdpAspect16x9`, `RdpAspect4x3`, `RdpAspect21x9` localization keys (or use literal labels for technical content).

**References:** Nielsen UX-05 (recognition over recall — show all options).

---

### F10 — Settings RDP defaults incomplete · 🟡 Medium · *Settings UX*

**Where:** `src/Heimdall.App/MainWindow.xaml:2154-2222` (RDP settings tab); `src/Heimdall.App/ViewModels/SettingsViewModel.cs:447-462` (LoadFromSettings RDP block).

**Reproduction:** Open Settings → RDP. Compare to the available redirection options in ServerDialog → Options → Device redirection.

**Expected:** All redirection types that are configurable per-server are also configurable as global defaults.
**Observed:** Settings exposes 3 of 7 redirections (Clipboard, Drives, Printers). Missing: COM Ports, Smart Cards, Webcam, USB, Audio Capture. The `AppSettings` class declares the underlying booleans (`RdpDefaultRedirectComPorts`, `RdpDefaultRedirectSmartCards`, `RdpDefaultRedirectWebcam`, `RdpDefaultRedirectUsb`, `RdpDefaultAudioCapture`) but neither `MainWindow.xaml` nor `SettingsViewModel` reads/writes them. They are dormant.

**Suggested fix:**
- Add the missing checkboxes in the Settings RDP tab and the matching `Load`/`Save` blocks in `SettingsViewModel`.
- This is a prerequisite for F4 (`RdpUseGlobalDefaults` should mean something).

**References:** Nielsen UX-04 (consistency).

---

### F11 — Manual resolution change ignored during stabilization · 🟡 Medium · *Resolution / DPI*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:798-808` (`OnResolutionMenuClick`).

**Reproduction:** Connect RDP. Click the resolution toolbar button within ~10 s of seeing "Connected", pick `1920x1080`. Observe.

**Expected:** Either the change applies immediately, or the UI explains the temporary unavailability.
**Observed:** The click is silently skipped because `_allowResolutionUpdates == false` until `_initialResizeEnableDelay` elapses (10 s by default). The user gets no feedback. After 10 s the menu works again — and the user has no idea why their click "didn't take" earlier.

**Suggested fix:**
- Disable the resolution button (with a tooltip "Stabilizing connection — available in {x} s") while `_allowResolutionUpdates == false`.
- Or: queue the user's choice and apply it as soon as `EnableResolutionUpdatesAsync` finishes.

**References:** Nielsen UX-01 (status visibility), UX-04 (predictability).

---

### F12 — Documentation drift on stabilization delay · 🟡 Medium · *Documentation*

**Where:** `CLAUDE.md` "RDP ActiveX (Critical)" section says "Resolution updates blocked 5s after `OnConnected`"; actual `_initialResizeEnableDelay = TimeSpan.FromSeconds(10)` in `EmbeddedRdpView.xaml.cs:43` (also `AppSettings.RdpResizeEnableDelayMs = 10000`).

**Reproduction:** Read `CLAUDE.md`. Read the source.

**Expected:** Doc and code agree.
**Observed:** Doc says 5 s, code default is 10 s.

**Suggested fix:** Update the gotcha in `CLAUDE.md` to "10s" (and ideally cross-reference `AppSettings.RdpResizeEnableDelayMs`).

**References:** Internal documentation accuracy.

---

### F13 — External `.rdp` file ignores user-configured resolution + AdminMode/FullScreen unreachable · 🟡 Medium · *Embedded vs external mode*

**Where:** `src/Heimdall.App/Services/Handlers/RdpHandler.cs:118-147` builds `RdpFileOptions` without setting `Width`/`Height` (defaults from `RdpFileOptions` are `1920x1080`); same block hardcodes `FullScreen = false` (line 124) and `AdminMode = false` (line 125).

**Reproduction:**
1. Configure a server with `RdpAspectRatio = "Preserve"` and a window-sized resolution from `Settings.DefaultResolutionWidth/Height`.
2. Switch the server to `RdpMode = External`.
3. Launch.

**Expected:** The mstsc.exe window opens at the user-configured resolution, and the dialog has a way to enable Admin mode and FullScreen for sysadmin scenarios (those are commonly needed for RDS console connections).
**Observed:** The mstsc.exe window opens at exactly `1920x1080` regardless of any user setting (the only path in `RdpFileGenerator` to set width/height is via explicit `RdpFileOptions.Width/Height`, never populated by `RdpHandler`). The Admin mode and FullScreen options are unreachable from the UI.

**Suggested fix:**
- Pass `Width = settings.DefaultResolutionWidth, Height = settings.DefaultResolutionHeight` (with sane fallbacks).
- Add a per-server "Run as administrator session" checkbox and wire `AdminMode`.
- Add a "Open in fullscreen" toggle and wire `FullScreen`.

**References:** Nielsen UX-04 (consistency between embedded and external modes).

---

### F14 — Disconnect message wording lacks actionability · 🟡 Medium · *Disconnect reasons*

**Where:** `locales/en.json:2342-2367` (the 25 `RdpDisconnect*` strings).

**Coverage:** All 24 codes in `RdpActiveXHost.GetDisconnectReasonKey` have a corresponding key, plus the `RdpDisconnectUnknownCode` fallback. Localization parity with FR is perfect (86 / 86).

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
| `3848 CredSspPolicyError` | "CredSSP policy prevents sending credentials to the remote computer." | ❌ No fix path — could mention KB4093492 / Encryption Oracle Remediation |
| `4360 ResolutionChangeTimeout` | "The session was disconnected after a display resolution change." | 🟡 Mid — could suggest "try again, possibly with dynamic resolution disabled" |

**Suggested fix:** For each ❌ row, add a follow-up sentence with the most likely user-facing action. Examples:
- `ConnectionTimeout`: "...Verify that the remote host is reachable and that no firewall is blocking the connection."
- `NetworkError`: "...Check your network connection and try again."
- `SecurityError`: "...The TLS handshake or NLA negotiation failed. Verify NLA is supported on the remote host."
- `CertificateWarning`: "...The certificate is self-signed, expired, or untrusted. To connect, ensure the certificate's CA is in your trusted store, or import it manually."
- `CredSspPolicyError`: "...CredSSP versions don't match. Update either client or server (Microsoft KB4093492)."

**References:** Nielsen UX-03 (error recovery messages must be actionable).

---

### F15 — Citrix mode precedence not surfaced · 🟡 Medium · *Citrix StoreBrowse*

**Where:** `src/Heimdall.App/Services/Handlers/CitrixHandler.cs:64-147` chains `if/else if/else` for three modes.

**Reproduction:**
1. Configure a Citrix server with both `CitrixIcaFilePath` and `CitrixStoreFrontUrl + CitrixAppName`.
2. Connect.

**Expected:** The user is told which mode is being used (or warned about the conflict).
**Observed:** `CitrixLaunchCommandLine` wins over `IcaFilePath` wins over `StoreFrontUrl` silently. The user has no way to know which mode launched.

**Suggested fix:**
- Add a small mode indicator in `EmbeddedCitrixView.SessionInfoText` or `StoreFrontText` showing the active mode (Cache / ICA file / StoreFront).
- In ServerDialog Citrix section, add a hint when more than one launch field is populated explaining the precedence.

**References:** Nielsen UX-04 (predictability).

---

### F16 — Misleading shell-rejection error · 🟡 Medium · *Citrix StoreBrowse*

**Where:** `src/Heimdall.App/Services/Handlers/CitrixHandler.cs:64-72`.

**Reproduction:** Configure `CitrixLaunchCommandLine` containing `&` or `|`. Connect.

**Expected:** Error message says "Citrix launch command contains forbidden shell metacharacters."
**Observed:** Error message is `CitrixNoConnectionConfigured` ("No Citrix StoreFront URL or ICA file configured.") — which is wrong: the user *did* configure something, it's just that the validation rejected it.

**Suggested fix:** Add a dedicated `CitrixLaunchCommandRejected` localization key with text like "The Citrix launch command was rejected because it contains forbidden characters (`|`, `&`, `;`, `` ` ``, `$`, newlines)." Use it on the validation failure path.

**References:** Nielsen UX-03 (specific error messages).

---

### F17 — Citrix capture polling without progress feedback · 🟡 Medium · *Citrix StoreBrowse*

**Where:** `src/Heimdall.App/Views/EmbeddedCitrixView.xaml.cs:179-284` (`TryCaptureWindowAsync`, 60 attempts × 500 ms).

**Reproduction:** Connect a Citrix StoreFront server. Watch the Heimdall window during the 30 s capture window.

**Expected:** Visible progress / status: "Looking for Citrix session window…", with a cancel option.
**Observed:** The session header shows "Connected" or "Embedded" immediately (line 126 in `InitializeSession`), but the actual rendering surface is empty. No spinner. The `HealthDot` is green. After 30 s, if no window was captured, BringToFrontButton becomes visible — silently. Users with a slow Citrix login experience this as "the app is broken".

**Suggested fix:**
- Show a loading state in `EmbeddedCitrixView` while `_captureInProgress = true`: a centered spinner + localized text "Locating Citrix session window…".
- After 10 s of polling without success, change the message to "Still searching… (this can take up to 30 s)".
- Add a Cancel button that aborts the polling and falls back to external mode immediately.

**References:** Nielsen UX-01 (system status visibility), UX-02 (user control).

---

### F18 — Citrix Terminate kills the process without confirmation · 🟡 Medium · *Citrix StoreBrowse*

**Where:** `src/Heimdall.App/Views/EmbeddedCitrixView.xaml.cs:363-374` (`OnTerminateClick` calls `Process.Kill()` straight away).

**Reproduction:** Open a Citrix-published Office application. Click **Terminate** in the session header.

**Expected:** Confirmation prompt warning that unsaved work in the remote app will be lost.
**Observed:** The Citrix process is killed immediately. Any unsaved work in the published application is gone.

**Suggested fix:** Before `Process.Kill()`, show a confirmation dialog with localized text "Terminate the Citrix session? Unsaved work in the remote application will be lost." Default button: Cancel.

Optional: add a "Disconnect" path that sends a graceful `WM_CLOSE` first and falls back to `Kill` after a timeout.

**References:** Nielsen UX-02 (user control — confirmation for destructive action).

---

### F19 — No "Send Ctrl+Alt+Del" / "Send Ctrl+Alt+End" helper · 🟡 Medium · *Keyboard / mouse*

**Where:** Searched `src/Heimdall.Rdp/`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml(.cs)` — no `Ctrl+Alt+Del`, `VK_DELETE`, `SendKeys` references.

**Reproduction:** Connect to a Windows Server in embedded mode. Try to lock the remote Windows session, or to use the secure attention sequence on a domain-joined server requiring CAS.

**Expected:** A toolbar button or context menu item "Send Ctrl+Alt+Del" that posts the secure attention sequence to the remote session via `IMsRdpClient.SendKeyboardEvent` or `MsTscAx`'s built-in API. mstsc.exe has this in its system menu (`Ctrl+Alt+End` shortcut by default).
**Observed:** No such control. The user must use `Ctrl+Alt+End` in external mstsc mode (which works because mstsc handles it natively); embedded mode has no equivalent.

**Suggested fix:** Add a header-bar button (next to Resolution) that calls `IMsRdpClient.SendOnVirtualChannel` or the simpler approach: dispatch `WM_KEYDOWN`/`WM_KEYUP` with `VK_CTRL + VK_MENU + VK_DELETE` to the deepest child window of the host (similar to the anti-idle Shift key path).

**References:** Nielsen UX-06 (efficiency for power users), platform conventions for RDP clients.

---

### F20 — Performance flags use imperative code-behind sync · 🟢 Low · *Code quality (UX-adjacent)*

**Where:** `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml.cs:160-169` (save), `:577-583` (load).

**Reproduction (technical):** Mutate `vm.RdpPerformanceFlags` in the ViewModel after the dialog is loaded (e.g. via a "Reset to defaults" command). Observe checkbox state.

**Expected:** The checkboxes update to reflect the new bitmask.
**Observed:** The seven `DlgSrv_PerfDisable*` / `DlgSrv_PerfEnable*` checkboxes are sync'd only at `Loaded` (via the load block) and at `OnSaveClick` (via the save block). They have no XAML `IsChecked` binding. A programmatic VM mutation between those two points is invisible to the UI.

**Suggested fix:** Refactor to a per-flag boolean property in the ViewModel and bind each checkbox to it. The conversion to/from the `RdpPerformanceFlags` int can be done via `partial void OnXxxChanged` notifications. Removes ~60 lines of imperative code-behind.

**References:** Nielsen UX-04 (consistency with the rest of the dialog's MVVM pattern).

---

### F21 — Resolution menu presets hardcoded · 🟢 Low · *Resolution / DPI*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml:48-58` (10 hardcoded `MenuItem`s).

**Reproduction:** Try to add a 2560x1080 ultrawide preset to the menu without recompiling.

**Expected:** Either the list is configurable in Settings, or the user can type a custom resolution.
**Observed:** Presets are baked in. No "Custom…" entry, no Settings extension hook.

**Suggested fix:** Move the list to `AppSettings.RdpResolutionPresets` (string array `1920x1080,1680x1050,…`) and bind the menu to it. Add a "Custom…" item that opens a small dialog for width/height entry.

**References:** Nielsen UX-06 (efficiency / power user flexibility).

---

### F22 — "Connecting" state doesn't differentiate sub-phases · 🟢 Low · *Connection lifecycle*

**Where:** `src/Heimdall.Core/StateMachine/ConnectionStateMachine.cs:34-44` (rich states: `ValidatingConfig`, `EstablishingTunnel`, `TunnelEstablished`, `LaunchingRdp`, `Connected`); `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:159, 411` writes raw "Connecting".

**Reproduction:** Connect a server with an SSH tunnel. Observe the header status.

**Expected:** Status shows something like "Establishing tunnel…" → "Launching Remote Desktop…" → "Connected".
**Observed:** Status shows just "Connecting" until the COM event sink fires `OnConnected`. The transitions captured by `_connectionSm.TryTransition` (in `RdpHandler.ConnectAsync` line 57, 72) drive log messages but never reach the embedded view.

**Suggested fix:** Wire `EmbeddedRdpView` to subscribe to `ConnectionStateMachine.StateChanged` for its server ID and surface the localized `Status*` keys (`StatusEstablishingTunnel`, `StatusConnecting`, etc.) that the metadata table already provides.

**References:** Nielsen UX-01 (granular status feedback).

---

### F23 — `CitrixUseSso` always visible · 🟢 Low · *Settings UX*

**Where:** `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:2003`.

**Reproduction:** Open ServerDialog → Citrix advanced. Observe "Use SSO (Kerberos)".

**Expected:** Visible only in domain-joined / Kerberos-applicable contexts, or with a tooltip explaining the prerequisite.
**Observed:** Always visible. Confusing for users on personal machines / non-domain-joined environments.

**Suggested fix:** Add a tooltip from `RdpAntiIdleHint` style that explains "Use Single Sign-On with the current Windows Kerberos identity. Requires a domain-joined client."

**References:** Nielsen UX-08 (contextual help).

---

### F24 / F25 — Autofill timeouts produce no UI hint · 🟢 Low · *Credential autofill*

**Where (F24):** `src/Heimdall.App/Services/Handlers/RdpHandler.cs:255-256` (external mode). **(F25):** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:646-649` (embedded mode).

**Reproduction:** Connect a server with a stored RDP password to a host that doesn't show CredUI (e.g., autologon kicks in first). Wait 90 s.

**Expected:** Either a toast/banner says "Autofill could not find a credentials prompt"; or autofill stays invisible if the connection succeeds anyway.
**Observed:** Only `FileLogger.Warn`. If the connection eventually succeeds, the user is none the wiser. If it doesn't, the user sees a CredUI dialog without their password pre-filled and wonders why they bothered storing it.

**Suggested fix:** When `WaitAndFillAsync` returns `false` AND the session is still in `Connecting` state, surface a one-line non-blocking toast: "Couldn't find the credentials prompt — type your password manually."

**References:** Nielsen UX-01 (status visibility), UX-08 (help when something fails silently).

---

### F26 — Some Settings unreachable from UI · 🟢 Low · *Settings UX*

**Where:** `AppSettings.RdpResizeEnableDelayMs` (line 54), `RdpArtifactCleanupDelayMs` (line 53), `RdpCredentialAutofillTimeoutMs` (line 52), `EmbeddedRdpTimeoutMs` (line 120 — partially exposed).

**Reproduction:** Try to change `RdpResizeEnableDelayMs` from 10 s to 5 s without editing JSON manually.

**Expected:** A "Tuning" sub-section in Settings → RDP exposing the timeout knobs.
**Observed:** Only `EmbeddedRdpTimeoutMs` is exposed (line 2316-2318 of MainWindow). The other three live only in JSON.

**Suggested fix:** Add a collapsed "Advanced timeouts" section with three `TextBox`-bound fields (numeric input). Annotate each with units (ms) and acceptable ranges.

**References:** Nielsen UX-06 (power-user efficiency).

---

### F27 — `EnsureHostHandle` retry budget exhaustion has no user signal · 🟢 Low · *Connection lifecycle*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:358-368, 424-442`.

**Observation:** If `IsVisualSurfaceReady()` is false 10 times in a row (each retry after 120 ms = 1.2 s), the code **continues anyway** with a `Warn` log. The user sees the surface render incorrectly or not at all, with no on-screen explanation.

**Suggested fix:** When the retry budget is exhausted, surface a diagnostic on `_ownerPane.FailureDetails` with a `RdpSurfaceNotReady` key suggesting "Please retry — the rendering surface failed to initialize." Plus actionable hint: try moving the window from one screen to another, or relaunching.

**References:** Nielsen UX-03 (visible failure mode).

---

### F28 — `RdpFatalError` collapses all error codes into one message · 🟢 Low · *Disconnect reasons*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:526-542`; `src/Heimdall.App/Views/EmbeddedRdp/RdpHostDiagnosticFactory.cs:41-48`; key `RdpFatalError: "Remote Desktop reported a fatal error."`.

**Observation:** The 24 disconnect codes are decoded with helpful localized strings. Fatal errors arrive separately and are mapped to a single key with the integer code as `Reason`. The `RdpStatusFatalErrorDetail: "Remote Desktop reported a fatal error ({0})."` key exists but is not used (cf. F1 plumbing).

**Suggested fix:** Either add per-code mapping for the small set of MsTscAx fatal-error codes (rare in practice — `IMsTscAxEvents::OnFatalError` mostly fires `0` "internal failure" or a few infrastructure codes), or at minimum use `RdpStatusFatalErrorDetail` to show the numeric code so support can act on it.

**References:** Nielsen UX-08 (help / diagnosability).

---

### F29 — Anti-idle has no UI indicator · 🟢 Low · *Anti-idle*

**Where:** `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:910-934`.

**Observation:** When `RdpAntiIdle = true`, a Shift-key PostMessage is sent every `_antiIdleIntervalSeconds` (default 60 s) to the inner ActiveX surface. There's no visible indication anywhere that anti-idle is active for this session — the user can't tell whether the feature is running or not.

**Suggested fix:** Add a small badge/icon next to the status text when `_antiIdleTimer` is running, with a tooltip "Anti-idle is keeping this session alive. Click to disable."

**References:** Nielsen UX-01 (visibility), UX-05 (recognition over recall — show what's running).

---

### F30 — Autofill regex matches EN/FR titles only · 🟢 Low · *i18n*

**Where:** `src/Heimdall.Rdp/CredentialAutofill.cs:106-108` (`TitlePattern` matches `Windows Security|Securité Windows|Credential|mstsc`).

**Observation:** A user running Heimdall on a German/Spanish/Italian/etc. Windows installation will see autofill silently fail because the credential dialog title doesn't match the regex. The application targets EN/FR only per `CLAUDE.md`, so the impact is limited, but the failure mode is invisible (cf. F24/F25).

**Suggested fix:** Either expand the regex to cover the top 5–10 European Windows locales (DE, ES, IT, PT, NL, PL — the title strings are documented by Microsoft), or fall back to class-name matching only (the class names `Credential Dialog Xaml Host` and `Windows Security` don't depend on the OS locale).

**References:** Nielsen UX-04 (consistency across locales).

---

## Section per audit angle

### 1. Connection lifecycle UX

Findings: **F1, F2, F3, F8, F22, F27.**

The COM/ActiveX scaffolding is meticulous: 2 layout flushes pre-connect (`FlushLayoutPipeline`), explicit `EnsureHostHandle`, 5-second post-connect block on resolution updates (actually 10 s, see F12), bounded auto-reconnect (20 attempts via `MaxReconnectAttempts`), `CancelAutoReconnect` flag respected during dispose. Pre-warmed COM at startup via `App.xaml.cs:PreWarmRdpRuntime` is a clear UX win (~400 ms saved on first connect).

The user-facing layer hasn't kept up with the engine. Status messages bypass the i18n pipeline that already has matching keys (F1), the auto-reconnect attempt counter is captured but never displayed (F2), the disconnect overlay treats user-initiated disconnect the same as a network drop (F3), and the progress bar disappears during reconnect cycles when it would be most helpful (F8). The rich `ConnectionStateMachine` states never reach the view (F22). The retry budget on visual-surface readiness has a silent exhaustion path (F27).

### 2. Credential autofill UX

Findings: **F6, F24, F25, F30.**

The mechanics are good: dual UI Automation + Win32 fallback, host-hint regex disambiguation, broker-process allow-list, password buffer cleared in `finally`. The settings layer is inconsistent: the timeout setting is honored in external mode but not in embedded mode (F6). The user-facing layer is invisible: timeouts only produce log warnings (F24, F25) and a non-EN/non-FR Windows host will see autofill silently fail (F30 — limited impact given EN/FR-only target locale).

### 3. Citrix StoreBrowse UX

Findings: **F15, F16, F17, F18, F23.**

Citrix is the weakest sub-area. Three launch modes coexist with implicit precedence and no UI signal (F15). One validation error is wrapped in a misleading message (F16). The 30-second window-capture polling has no progress feedback (F17). Terminate kills the process without confirmation (F18). The `CitrixUseSso` checkbox lacks context (F23).

The capture path itself is solid: scans 5 known Citrix process names, uses `EnumWindows` + `GetWindowRect` + class-name allow-list (`Transparent Windows Client`, `CtxSeamless`, `CDViewer`, `TUIWindowClass`, `IHWindow`), picks the largest viable window, calls `SetParent` with style adjustments, monitors via `IsWindow` poll. Disposal correctly restores the original window style and reparents back to the desktop.

### 4. Resolution / DPI handling UX

Findings: **F11, F12, F13, F21.**

DPI-awareness is correct: WPF logical pixels are converted to physical via `PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice`, with a sane fallback to 1.0. SmartSizing is enabled by default to absorb pixel rounding during the debounce delay. Dynamic resolution uses `IMsRdpClient9+.UpdateSessionDisplaySettings` with a fallback to `Reconnect(width, height)` for older interfaces.

Issues are at the surface: manual resolution change is silently ignored during the 10 s stabilization window (F11), the documentation contradicts the actual delay (F12), external mode `.rdp` files don't propagate user-configured resolution (F13), and the preset list isn't extensible (F21). Multi-monitor support uses `TrySetDynamic("UseMultimon", …)` so it fails silently if the COM interface version is too old (no UX impact in modern environments, doc-only).

### 5. Keyboard / mouse / clipboard UX

Findings: **F19.**

Anti-idle (`OnAntiIdleTick`) drills to the deepest child window and sends `WM_KEYDOWN`/`WM_KEYUP` for `VK_SHIFT`. Combined with `allowBackgroundInput=1` (correctly set in `ApplyRedirectionSettings`), this works on background tabs. `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED)` keeps the local display on during a session.

Clipboard redirection toggle is per-server and per-default. No issue identified on the toggle itself, only on the absence of a "Send Ctrl+Alt+Del" helper (F19) — a standard expected by power users on RDS / domain-joined hosts. Modifier-stuck issues (a known WPF/WindowsFormsHost gotcha) were not reproducible from static analysis; would need a manual capture.

### 6. Disconnect reason UX

Findings: **F5, F14, F28.**

Coverage: 24 of 24 MsTscAx codes mapped via `RdpActiveXHost.GetDisconnectReasonKey`, plus an `RdpDisconnectUnknownCode` fallback. EN/FR localization parity is 86 / 86 keys (perfect). The infrastructure is sound.

Quality is mixed. The reason key is correctly populated on the pane diagnostic but the overlay shows a generic message (F5 — biggest miss in this category, since the data is right there). Many messages are technically correct but not actionable (F14), particularly for the network/security/encryption family. Fatal errors collapse to a single key without per-code mapping (F28).

### 7. i18n parity

Findings: **F1, F30.**

86 `Rdp*` keys in EN, 86 in FR — exact parity, no dead keys, no orphaned. CI `i18n-parity` job covers this. The issue is *usage*: status text bypasses the pipeline (F1) and `RdpStatusReconnecting` / `RdpStatusFatalErrorDetail` keys are dormant. The autofill regex is locale-bound (F30) but only EN/FR are targeted so the impact is bounded.

### 8. Settings UX

Findings: **F4, F7, F9, F10, F13, F23, F26.**

The single most user-facing issue is F4: `RdpUseGlobalDefaults` is a checkbox that does nothing at runtime. F10 (5 missing redirections in defaults) is a prerequisite for fixing F4. F9 (incomplete AspectRatio choices) and F23 (Citrix SSO context) are local glitches. F7 (webcam silently disabled in embedded) and F13 (external mode resolution/Admin/FullScreen unreachable) are mode-specific gaps. F26 catalogs three timeout settings that exist only in JSON.

### 9. Embedded vs external mode UX

Findings: **F7, F13.**

The mode toggle in ServerDialog is clear (Embedded / External, with descriptive labels via `RdpModeEmbedded`/`RdpModeEmbeddedDesc` / `RdpModeExternal`/`RdpModeExternalDesc`). Mode-specific feature gaps are not communicated: webcam (F7) silently no-ops in embedded; resolution / AdminMode / FullScreen (F13) are only reachable on the external path but not exposed there either.

The embedded-vs-external split itself is correct: external mode writes a sanitized `.rdp` file with restricted ACL, launches `mstsc.exe`, and schedules cleanup of the file + CredMan entry after `RdpArtifactCleanupDelayMs` (default 10 s). One robustness note: if the user reconnects within 10 s, the cleanup still runs against the previous artifact — fine because each artifact has a unique path with `Guid.NewGuid():N`.

### 10. Splits & tabs UX

No findings (no issues identified).

The split system handles RDP correctly per `CLAUDE.md` gotchas: `Visibility=Collapsed` → `Child=null` → `Disconnect()` → `DetachEventSink()` → `Dispose()` order is observed in `EmbeddedRdpView.Dispose` (lines 188-227). The Command Palette uses `Popup` for HWND airspace above the ActiveX surface (documented in `CLAUDE.md`). `SetFullscreen` collapses the header bar without disrupting the COM control. Anti-idle continues to function on background tabs because `allowBackgroundInput=1` is set in `ApplyRedirectionSettings`. Nothing visibly broken.

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
- **Resolution menu items hardcoded `1920x1080` etc.** — these labels are language-neutral digits, no i18n issue. Extensibility flagged separately (F21) as Low.
- **Performance flags bitmask values (`0x01..0x100`)** — internal IMsRdpClient constants; not a UX issue.
- **`SmartSizing = true` default** — correct given the embedded model; visual artifact during resize is absorbed, not a UX issue.
- **`administrative session:i:1` write path** — correct .rdp file format; flagged separately as F13 because the *trigger* (UI checkbox) is missing, not the .rdp behavior.
- **Pageant / SSH-related gotchas** — out of RDP scope.
- **Aspect ratio `AspectRatio.Stretch` default for unconfigured profiles** — sensible default, no UX issue.
- **Tab error badges in ServerDialog (`HasOptionsTabErrors`, `OptionsTabErrorCount`)** — already correctly implemented, helpful UX, not flagged.
- **`AutomationProperties.Name` coverage** — checked across `EmbeddedRdpView` toolbar buttons, status text, overlay buttons; all correctly localized at runtime via `SetName`. Not a UX issue.
- **`StatusTextBlock.LiveSetting="Polite"`** — already set; good a11y practice. Not flagged.
- **External mstsc.exe stdout/stderr** — process is launched detached; no readable output piped, but `mstsc.exe` doesn't produce useful console output. Not flagged.
- **`InvokeMember(BindingFlags.InvokeMethod, …)` reflection on `UpdateSessionDisplaySettings`** — required for COM late binding compatibility. Not a UX issue.
- **Citrix shell-arg validation rejecting `|`, `&`, `;`, `` ` ``, `$`, `\n`, `\r`** — necessary security gate. Only the *error message* is flagged (F16), not the gate itself.
- **Citrix `EmbeddedContainer.Visibility = Collapsed` initial state** — correct; the InfoPanel is shown until the captured window is reparented. Not a UX issue.
- **`ResolveSelfServicePath` fallback chain (3 paths + PATH search)** — robust resolution. Not flagged.
- **External RDP mode mstsc launch via `Process.Start("mstsc.exe", quoted-rdp-path)`** — correct, sanitized via `SecureFileWriter`. Not a UX issue.
- **`HealthDot` color binding via code-behind in CitrixView** — slightly imperative but works; matches the rest of the file's style. Low impact, not flagged.
- **Splits: `_emptyPane` is per-instance (not static)** — correct pattern from `CLAUDE.md`. Not a UX issue.
- **CredentialAutofill `TryInjectPasswordViaWin32` heuristic of "second Edit control = password"** — heuristic but well-documented and cross-validated by class-name + UIA fallback. Not flagged.
- **`RdpDisconnectUnknownCode` fallback** — correct safety net. Not flagged.

---

## Annex — files read during this audit

**`Heimdall.Rdp` (full):**
- `IRdpSession.cs`
- `ActiveX/RdpActiveXHost.cs`
- `RdpRedirectionOptions.cs`
- `RdpFileGenerator.cs`
- `CredentialAutofill.cs`
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
- `Dialogs/ServerDialog.xaml.cs` (RDP-relevant excerpts: load/save flags, localization)
- `Dialogs/RdpImportDialog.xaml`

**`Heimdall.Core/Configuration`:**
- `ServerProfileDto.cs` (RDP fields, lines 40-112)
- `AppSettings.cs` (RDP defaults, lines 52-120)
- `GroupDefaultsDto.cs` (referenced)

**`Heimdall.App/ViewModels`:**
- `Dialogs/ServerDialogViewModel.cs` (RDP-relevant excerpts)
- `SettingsViewModel.cs` (RDP defaults block, ApplyRdpModeToAllAsync, lines 350-470)

**`Heimdall.App/MainWindow.xaml`:**
- Settings RDP tab (lines 2154-2225)
- RDP settings expansions and settings labels

**`locales/`:**
- `en.json` and `fr.json` — all 86 `Rdp*` keys + 6 `Citrix*` keys read and compared.

**Other:**
- `CLAUDE.md` (RDP gotchas section)
- `audit-ssh-rdp.md` (prior audit, format reference only — no inheritance of findings)

---

*Audit conducted under the `project-audit` skill UX module + Nielsen heuristics. Total findings: 0 🔴 / 5 🟠 / 14 🟡 / 11 🟢 = 30. Reproducible: a second pass on the same unmodified codebase must produce the same report. Cross-referencing with the parallel Codex audit (`audit-ux-rdp-codex-2026-04-28.md`) is the next step.*
