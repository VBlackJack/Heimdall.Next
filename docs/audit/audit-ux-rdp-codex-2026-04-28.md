# UX Audit - RDP Layer

- Date: 2026-04-28
- Build version: v2026.042409 (from `CLAUDE.md`; `Directory.Build.props` does not define an application version)
- Baseline commit: `226e597` (`docs(audit): mark SSH UX audit as fully closed (19/19)`)
- Scope: RDP UX only, including embedded MsTscAx ActiveX, external mstsc launch, RDP credential autofill, Citrix StoreBrowse, resolution/DPI, keyboard/mouse behavior, disconnect reasons, i18n, settings, external/embedded mode, splits and tabs.
- Author: Codex CLI

## Executive Summary

The RDP layer has a solid technical foundation for hosting MsTscAx safely: the layout flush before `Connect()`, post-connect resolution stabilization, bounded auto-reconnect, and strict dispose order are all present. The main UX problem is that several of those technical states are not surfaced to the user with the same precision as the code tracks internally. Embedded RDP has specific disconnect diagnostics, reconnect attempts, credential autofill activity, and resolution stabilization windows, but the visible UI often collapses them into generic labels such as `Connected`, `Reconnecting`, or `The Remote Desktop session has ended`. External RDP has an even larger observability gap because Heimdall marks the session connected as soon as `mstsc.exe` starts, before the user has actually authenticated or reached the remote desktop. Citrix follows the same optimistic-launch pattern and has no first-class StoreBrowse discovery UX. Settings coverage is broad, but a few fields imply support that is mode-dependent, especially webcam redirection in embedded mode. i18n key parity for RDP is good across English and French, but some in-session status strings bypass those keys. Overall health is functional but uneven: the core flows likely work, while failure, retry, credential, and mode-transition states still leave users guessing.

Final count: 0 🔴 Critical · 3 🟠 High · 6 🟡 Medium · 2 🟢 Low

## Findings

### F1 - Credential autofill is invisible unless it completely succeeds

- Severity: High
- Category: Credential autofill UX
- Where: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:622-660`, `src/Heimdall.App/Services/Handlers/RdpHandler.cs:238-267`, `src/Heimdall.Rdp/CredentialAutofill.cs:133-189`
- Reproduction:
  1. Save an RDP server with a password.
  2. Connect to a host that shows a CredUI/password prompt, or one where the prompt appears slowly.
  3. Watch the Heimdall tab/status while the autofill watcher searches, times out, or fails to inject the password.
- Expected vs observed:
  - Expected: The user sees a status such as "waiting for Windows credential prompt", "filling saved password", "autofill timed out", or "password was rejected; enter it manually".
  - Observed: Embedded mode logs the watcher and timeout only; external mode starts a background `Task.Run` and logs failures only. The status bar remains on the connection lifecycle state, so users cannot tell whether Heimdall is still trying, already failed, or waiting for manual input.
- Suggested fix: Add a small credential sub-status/toast for RDP autofill start, timeout, injection failure, and manual-fallback states. For external mode, tie the message to the launched `mstsc.exe` session row rather than only the log.
- References: `CredentialAutofill.WaitAndFillAsync` returns `bool`, so callers already have enough signal to surface the result.

### F2 - Specific disconnect reasons are decoded but hidden behind a generic overlay

- Severity: High
- Category: Disconnect reason UX
- Where: `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:432-458`, `src/Heimdall.App/Views/EmbeddedRdp/RdpHostDiagnosticFactory.cs:26-35`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:506-540`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:814-822`, `locales/en.json` (`RdpDisconnect*`, `RdpDisconnectedMessage`)
- Reproduction:
  1. Trigger an embedded RDP failure with a known code, such as bad credentials, DNS failure, timeout, or certificate verification failure.
  2. Wait for the reconnect overlay.
  3. Compare the visible overlay text with the decoded diagnostic key stored on the pane.
- Expected vs observed:
  - Expected: The overlay shows the mapped reason and a next action, for example "Credentials were not accepted. Check username/password or reconnect to retry."
  - Observed: `GetDisconnectReasonKey()` maps 24 codes and both locales contain matching keys, but `ShowReconnectOverlay()` always uses `RdpDisconnectedMessage`: "The Remote Desktop session has ended." The actionable message is stored as a pane diagnostic, not in the primary surface the user is forced to read.
- Suggested fix: Pass the current `SessionDiagnostic` or resolved localized `RdpDisconnect*` message into the overlay, include the raw code for unknown values, and add a concise next-step line for credential, DNS, certificate, timeout, and network cases.
- References: `RdpDisconnectedMessage` is generic while `RdpDisconnectBadCredentials`, `RdpDisconnectDnsLookupFailed`, `RdpDisconnectSocketConnectFailed`, and peers are already localized.

### F3 - Auto-reconnect hides attempt count, reason, and cancel affordance

- Severity: High
- Category: Connection lifecycle UX
- Where: `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:568-573`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:556-566`, `locales/en.json` (`RdpStatusReconnecting`)
- Reproduction:
  1. Start an embedded RDP session with auto-reconnect enabled.
  2. Drop the network or interrupt the server.
  3. Watch the tab/header while MsTscAx retries.
- Expected vs observed:
  - Expected: The UI shows retry progress such as "Reconnecting (attempt 3 of 20)", the disconnect reason, and an obvious cancel/disconnect option.
  - Observed: The host sets `MaxReconnectAttempts = 20` and receives `attemptCount`, but the view calls `UpdateSessionStatus("Reconnecting")` with no count, no reason, and no visible way to stop the retry loop. The localized `RdpStatusReconnecting` string with `{0}` exists but is not used here.
- Suggested fix: Format `RdpStatusReconnecting` with the attempt count, add "of 20" or a configured maximum, show the decoded disconnect reason when available, and wire the disconnect button to cancel auto-reconnect explicitly before closing.

### F4 - Resolution changes made during post-connect stabilization are silently dropped

- Severity: Medium
- Category: Resolution / DPI handling UX
- Where: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:43-45`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:314-318`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:485-504`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:768-808`
- Reproduction:
  1. Connect to an embedded RDP session with dynamic resolution enabled.
  2. During the first 10 seconds after `OnConnected`, maximize the app, move a split boundary, or choose a manual resolution.
  3. Wait for the stabilization window to end without resizing again.
- Expected vs observed:
  - Expected: Heimdall either queues the latest requested size and applies it once updates are enabled, or tells the user resolution updates are temporarily paused.
  - Observed: resize events log "deferred until post-connect stabilization" and return. Manual menu changes update internal state and the button tooltip/color, but only call `UpdateResolution()` if `_allowResolutionUpdates` is already true. The user sees a selected resolution that may not be the remote desktop's actual size until another resize/menu action happens.
- Suggested fix: Store the last deferred target dimensions and apply them immediately after `EnableResolutionUpdatesAsync()` flips `_allowResolutionUpdates`. Add a transient status such as "Optimizing display..." during the stabilization window.
- References: The prompt mentions a 5s window; the current default is 10s unless settings override it.

### F5 - External RDP is marked connected when mstsc starts, not when the remote desktop is usable

- Severity: Medium
- Category: Connection lifecycle UX
- Where: `src/Heimdall.App/Services/Handlers/RdpHandler.cs:201-236`, `src/Heimdall.App/Services/Handlers/RdpHandler.cs:238-267`
- Reproduction:
  1. Configure an RDP server in external mode.
  2. Connect to a host with bad credentials, unreachable network, certificate warning, or slow NLA.
  3. Observe Heimdall's connection state after `mstsc.exe` launches.
- Expected vs observed:
  - Expected: Heimdall distinguishes "external client launched" from "remote session connected", and failure-prone credential/certificate/NLA states remain visible.
  - Observed: `RdpHandler` transitions to `ConnectionState.Connected` immediately after `Process.Start` returns. Later authentication failure, certificate prompts, or rejected credentials happen in the external client and are not reflected in Heimdall except through background autofill logs.
- Suggested fix: Rename the visible state for external mode to "Launched in mstsc" or "Waiting in external client", keep a pending state until a reliable signal exists, and surface a small "Open mstsc window / troubleshooting" affordance for launch-only sessions.

### F6 - Citrix StoreBrowse launch has no discovery flow and reports success before capture

- Severity: Medium
- Category: Citrix StoreBrowse UX
- Where: `src/Heimdall.App/Services/Handlers/CitrixHandler.cs:113-153`, `src/Heimdall.App/Services/Handlers/CitrixHandler.cs:238-269`, `src/Heimdall.App/Views/EmbeddedCitrixView.xaml.cs:126-136`, `src/Heimdall.App/Views/EmbeddedCitrixView.xaml.cs:179-284`, `src/Heimdall.App/Views/EmbeddedCitrixView.xaml.cs:405-418`
- Reproduction:
  1. Configure a Citrix server with StoreFront URL and app name.
  2. Mistype the app name, use a StoreFront with delayed auth, or launch an app whose window appears slowly.
  3. Watch the embedded Citrix tab.
- Expected vs observed:
  - Expected: StoreBrowse offers a discover/select/test path for published apps, and launch status distinguishes "starting", "waiting for Citrix window", "embedded", and "external fallback".
  - Observed: The handler accepts only a manually typed `CitrixAppName` and starts `storebrowse.exe`/`SelfService.exe`. It transitions to `Connected` immediately after process launch. The view calls `UpdateStatus(true)` before the 30s capture loop starts, so users can see `Connected` while Heimdall is still searching for a window or before the app has actually opened.
- Suggested fix: Add a StoreBrowse discovery/test action in the dialog, validate app names before save or connect when possible, and surface capture progress/fallback text in the Citrix view.

### F7 - Mode-dependent redirection support is not explained in settings

- Severity: Medium
- Category: Embedded vs external mode UX
- Where: `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1505-1513`, `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1580-1586`, `src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs:630-644`, `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:581-583`, `src/Heimdall.Rdp/RdpFileGenerator.cs:104-106`
- Reproduction:
  1. Create or edit an RDP server.
  2. Select embedded mode.
  3. Enable webcam redirection.
  4. Connect and expect the remote session to receive a redirected camera.
- Expected vs observed:
  - Expected: Unsupported or external-only features are disabled, annotated, or automatically switch the user to external mode.
  - Observed: The same webcam checkbox is available for embedded and external sessions. The ActiveX path documents that webcam redirection works only in external mode via `.rdp` file generation, but the settings UI does not warn the user.
- Suggested fix: Add mode-aware help text or disable webcam redirection when RDP mode is embedded. If the user enables an external-only feature, offer to switch the profile to external mode.

### F8 - Split mode silently overrides external RDP profiles to embedded

- Severity: Medium
- Category: Splits & tabs UX
- Where: `src/Heimdall.App/Services/SplitService.cs:150-175`, `src/Heimdall.App/Services/SplitService.cs:768-776`, `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1505-1513`
- Reproduction:
  1. Configure an RDP server with session mode set to external.
  2. Start or command-palette-launch it into a split pane.
  3. Observe that the split pane uses embedded mode.
- Expected vs observed:
  - Expected: The user is told that split panes require embedded mode before or during launch.
  - Observed: `ForceEmbeddedMode()` mutates the DTO copy to embedded because external processes cannot be docked. That technical constraint is correct, but the user-facing split flow has no explanation, so a profile setting appears to be ignored.
- Suggested fix: Add a split-launch notice such as "Splits use embedded RDP; external mode opens only in normal tabs", and consider disabling split actions for profiles whose required features are external-only.

### F9 - RDP settings validate only two advanced fields and lack a test path

- Severity: Medium
- Category: Settings UX
- Where: `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:712-738`, `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml:1547-1556`, `src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs:967-1021`
- Reproduction:
  1. Create an RDP server with questionable settings: invalid gateway text, unsupported external-only redirection in embedded mode, or a mistyped username/domain.
  2. Save it.
  3. Discover the problem only during connect.
- Expected vs observed:
  - Expected: The RDP dialog highlights common RDP-specific mistakes and provides a "test connection" or "validate RDP settings" path similar in spirit to the SSH UX improvements.
  - Observed: RDP-specific inline validation covers `RdpAudioMode` and `RdpColorDepth` only. The basic credential section has username/password fields but no hint about optional password prompting, autofill behavior, domain format, or NLA credential implications.
- Suggested fix: Add RDP-aware hints and validation for domain/username format, mode/feature incompatibilities, StoreFront/app name when relevant, and a test action that exercises DNS/tunnel/socket/NLA reachability without opening a full session.

### F10 - Some visible RDP status text bypasses i18n and accessibility-friendly detail

- Severity: Low
- Category: i18n
- Where: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:158-160`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:686-707`, `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:1021-1028`, `locales/en.json`, `locales/fr.json`
- Reproduction:
  1. Switch the app locale to French.
  2. Open an embedded RDP session and observe the status header through connect, reconnect, disconnect, and tunneled endpoint display.
- Expected vs observed:
  - Expected: Visible status and screen-reader text use localized, descriptive strings such as `RdpStatusPreparing`, `RdpStatusWaiting`, `RdpStatusDisconnecting`, and `RdpStatusReconnecting`.
  - Observed: `UpdateSessionStatus()` writes raw English tokens (`Connecting`, `Connected`, `Disconnecting`, `Reconnecting`, `Error`) directly into `StatusTextBlock` and the session model. The tunneled endpoint text also hard-codes `via localhost`.
- Suggested fix: Introduce a typed RDP status enum or key-based helper so in-session labels and automation live regions use the existing localized keys. Localize the tunnel endpoint format string as well.
- References: English/French RDP key parity is currently good: both locales have 246 `*Rdp*`/`Rdp*` keys in the inspected JSON.

### F11 - Anti-idle microcopy says mouse movement, but the implementation sends Shift key events

- Severity: Low
- Category: Keyboard / mouse UX
- Where: `src/Heimdall.App/Views/EmbeddedRdpView.xaml.cs:936-977`, `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:588-591`, `locales/en.json` (`RdpAntiIdleHint`)
- Reproduction:
  1. Enable RDP anti-idle in a server profile.
  2. Read the settings hint.
  3. Compare it to the embedded RDP anti-idle implementation.
- Expected vs observed:
  - Expected: The UI accurately describes what Heimdall sends to the remote session and any caveats for keyboard-sensitive remote apps.
  - Observed: The hint says Heimdall "simulates a small mouse movement", but the code posts Shift key down/up messages to the ActiveX child window. Shift is intended to have no visible effect, but the mismatch makes troubleshooting modifier-sensitive remote apps harder.
- Suggested fix: Update the hint to say "sends a harmless Shift tap" or switch the implementation to match the existing mouse-movement wording. Consider exposing the anti-idle method in advanced settings if both approaches are supported later.

## Audit Angle Notes

### 1. Connection lifecycle UX

The lifecycle state machine distinguishes validation, tunnel setup, RDP launch, and connected states, but the visible embedded status is reduced to raw tokens. The highest-impact gaps are auto-reconnect status detail and external RDP being marked connected when `mstsc.exe` merely starts. The ActiveX dispose/connect sequencing itself follows the documented gotchas and was not flagged.

### 2. Credential autofill UX

Autofill is implemented for embedded and external flows and has logging for start, timeout, and failure. The user-facing surface does not expose those states, so a slow or failed CredUI search looks identical to normal connection progress. This is the clearest core-flow UX gap.

### 3. Citrix StoreBrowse UX

Citrix can launch via cached SelfService, direct ICA, or StoreFront/app name. StoreBrowse discovery is not represented as a user workflow; users type app names manually and learn about mistakes at launch time. Capture/fallback status also starts optimistic and becomes specific only after timeout.

### 4. Resolution / DPI handling UX

The code intentionally delays resolution updates after connect to avoid MsTscAx instability, uses SmartSizing, and debounces resize events. The UX gap is that deferred requests are not queued or explained, so early window maximizes, split changes, or manual resolution choices can appear ignored. SmartSizing is useful but not explained as scaling rather than a remote resolution guarantee.

### 5. Keyboard / mouse UX

No direct blocker was found for focus capture, command palette airspace, or split overlay placement from the inspected code: the command palette is hosted in a `Popup`, matching the documented RDP airspace rule. The anti-idle wording/implementation mismatch is a low-severity keyboard UX issue because it can confuse users debugging modifier-sensitive remote apps.

### 6. Disconnect reason UX

All 24 codes in `GetDisconnectReasonKey()` have matching English and French keys. The English wording is mostly clear, and several messages are actionable enough for first-pass triage. The issue is delivery: the main reconnect overlay uses a generic message instead of the decoded reason/action.

### 7. i18n

RDP key parity between `locales/en.json` and `locales/fr.json` is good for the inspected key set. The remaining i18n issue is not missing keys but bypassed keys: embedded RDP status labels and the tunneled endpoint string use hard-coded English text.

### 8. Settings UX

The ServerDialog covers many RDP options: mode, aspect ratio, audio, color depth, NLA, multimonitor, dynamic resolution, device redirection, global defaults, anti-idle, compression, and auto-reconnect. Validation is much thinner than the option surface, and mode-dependent features such as webcam redirection need inline explanation. I did not verify actual WPF rendering because this was a read-only code audit.

### 9. Embedded vs external mode UX

The explicit embedded/external toggle exists, and external mode generates a `.rdp` file with features that ActiveX cannot expose simply. The confusing cases are feature support differences and split mode forcing embedded without telling the user. External launch state also needs wording that reflects "client launched" rather than "remote connected".

### 10. Splits & tabs UX

The split path creates a loading pane immediately and forces dockable embedded mode, which is technically correct. The missing piece is user-facing explanation when this overrides the profile's external-mode preference. I did not flag command palette airspace because the implementation uses a top-level `Popup`, matching the documented gotcha.

## What Was NOT Flagged (And Why)

- ActiveX dispose order: The view hides `FormsHost`, clears `Child`, detaches events, cancels auto-reconnect, disconnects, detaches, and disposes in a deliberate order, matching the documented crash-avoidance pattern.
- Pre-connect layout flush: `BeginConnect` forces host handle creation and layout flushing before `Connect()`, so the known "phantom client" ActiveX issue was not re-flagged.
- RDP disconnect code coverage: The current mapper covers 24 codes and the inspected locale files contain matching keys, so coverage itself is not the finding.
- Command Palette above RDP: The palette is a `Popup`, not a normal WPF overlay inside the ActiveX airspace, so I did not flag it as an RDP occlusion bug.
- Clipboard redirection: The setting is wired through the redirection options and ActiveX advanced settings. I did not see a code-level UX issue without running remote clipboard scenarios manually.
- File redirection semantics: Drive redirection is a coarse RDP setting rather than an app-managed file transfer UX. No finding was raised because this is expected for MsTscAx/mstsc.
- Post-connect resolution delay as a concept: The delay is justified by the ActiveX gotcha; the finding is only that user-triggered changes during the delay are not queued or explained.
- Global RDP defaults switch: The setting is present in the dialog and DTO. I did not flag it because existing documentation says behavior is wired through redirection construction, and the UX issue was weaker than the mode-specific support gaps above.
