# Gap Analysis — RDP vs mstsc modern + Royal TS + mRemoteNG

**Date**: 2026-04-19
**Batch**: b53 (RDP scope — SSH/Terminal/SFTP covered in b52)
**Next**: b54 = synthesis and prioritized roadmap (consolidates b52 + b53)
**Primary references**: mstsc modern (Windows 11 Remote Desktop Connection + MSRDC), Royal TS, mRemoteNG
**Secondary reference**: MobaXterm — on the *integration* angle only (how RDP coexists with the rest of the suite).
**Not referenced**: legacy `RDPManager`. It may be consulted as an archive if a specific historical detail is needed, but it is not a baseline.

---

## 0. Executive snapshot

### Top 3 blockers for migration
1. **No `.rdp` import from UI, no drag-drop of `.rdp` files onto the app.** The parser (`RdpFileImporter`) exists and is tested, but is not wired to any button or drop target. A user migrating from mstsc with dozens or hundreds of saved shortcuts cannot bulk-onboard.
2. **RD Gateway is not surfaced in the embedded `ServerDialog`.** The data model (`RdpGateway`), the `.rdp` file generator, and the external-mstsc flow all handle gateway correctly, but the dialog does not expose a field. Enterprise users behind an RD Gateway cannot configure it via the UI for embedded mode.
3. **Per-monitor selection is absent.** `UseMultimon` is a boolean, equivalent to mstsc's "All monitors" toggle. Modern multi-workstation users routinely want a specific subset of monitors (e.g., the two landscape displays, not the portrait one). `SelectedMonitors` is mentioned internally but not implemented.

### Top 5 quick wins (S effort, user-visible impact)
1. **`.rdp` import button** in `ServerDialog` routing to the existing `RdpFileImporter`. Single dialog change, ~1 day.
2. **`.rdp` drag-drop onto the app window**, reusing the same importer.
3. **RD Gateway UI field** in the embedded `ServerDialog` (data model already there).
4. **Per-server keep-alive interval override** (today hard-coded to 60 s in `RdpActiveXHost.cs`).
5. **Keyboard shortcut for Fullscreen toggle** (the feature exists via the context menu, but no accelerator is bound).

### Top 5 strengths to preserve
1. **41-code disconnect reason decoder** with i18n keys (`GetDisconnectReasonKey()` in `RdpActiveXHost.cs`). Royal TS and mRemoteNG show numeric codes; mstsc shows generic strings. Heimdall has the best error surface.
2. **Dual-path `UpdateResolution`** (`UpdateSessionDisplaySettings` on `IMsRdpClient9+`, fallback to `Reconnect(width, height)` on `IMsRdpClient7+` via reflection). Robust across RDP client versions.
3. **DPI-aware physical pixel conversion** via `PresentationSource.CompositionTarget.TransformToDevice` before feeding dimensions to the ActiveX control. Correct rendering on 125/150/200 % DPI out of the box.
4. **`CredentialAutofill`** via `EnumWindows` + UI Automation + `WM_SETTEXT` with multi-dialog contention handling and FR/EN i18n pattern matching — a non-trivial piece of engineering that noticeably smooths the external-mstsc handoff.
5. **Temp `.rdp` hardening**: CRLF-injection sanitization (CWE-93), per-file ACL (Current User + Admins + SYSTEM, inherited ACEs removed), plaintext password memory zeroing post-handoff. Neither mstsc nor the other three references do this.

---

## 1. Scope and methodology

### 1.1 In scope
- The RDP protocol stack: `src/Heimdall.Rdp/`, `src/Heimdall.App/Services/Handlers/RdpHandler.cs`, `CitrixHandler.cs`, RDP-specific branches of `ConnectionService.cs`, `EmbeddedSessionManager.cs`, `SplitService.cs`, `SessionTabContextMenuFactory.cs`.
- The RDP user experience: `EmbeddedRdpView.xaml(.cs)`, `ServerDialog.xaml(.cs)` RDP sections, RDP-related viewmodels, context menus, keyboard shortcuts, settings.
- Citrix, treated as a protocol variant users may expect next to RDP.

### 1.2 Out of scope
- SSH / Terminal / SFTP (covered in b52).
- VNC (to be triaged in b54 if warranted).
- Web-based RDP (HTML5 clients, not part of Heimdall.Next's current positioning).
- Legacy `RDPManager` features.

### 1.3 References
- **mstsc modern / MSRDC** (Windows 11 built-in RDC plus Microsoft Remote Desktop Client for AVD): the lowest-common-denominator baseline that any RDP user knows. Defines file format (`.rdp`), gateway UX, device-redirection toggles, monitor-picker ergonomics.
- **Royal TS v7/v8**: defines the enterprise session-manager expectations — tabbed UI, credential vault, connection sequences, tasks, dashboards, detachable panes, inline tab naming, session comments.
- **mRemoteNG**: defines the community-OSS session-manager expectations — folder tree, inheritance, per-protocol external tools, `.rdp` bulk import, panel docking, session persistence.
- **MobaXterm** (secondary): defines the *integration* expectation — RDP coexists seamlessly with SSH, SFTP, X11 in the same window.

### 1.4 Method
Two parallel reconnaissance passes on the codebase (backend/protocol and frontend/UX), consolidated into a capability map keyed by the six user journeys (§2). Cross-referenced against well-known feature sets of the reference products. Gaps classified on three axes:
- **Parity**: ✓ Parity / ~ Partial / ✗ Missing.
- **Impact**: **Bl** Blocker for migration, **H** High daily pain, **M** Medium occasional friction, **L** Low nice-to-have.
- **Effort**: **S** ≤ 1 day / **M** ≤ 1 week / **L** 1–3 weeks / **XL** > 3 weeks.

### 1.5 Gap code convention
Each gap has a code `<Journey>-<N>`:
- **L** = P1 Launch flow
- **G** = P2 Security / credentials / gateway
- **D** = P3 Display / multi-screen / resize
- **Rd** = P4 Clipboard / devices / drive redirection
- **Rc** = P5 Reconnect / resilience
- **M** = P6 Session management

---

## 2. Heimdall.Next baseline — by user journey

### 2.1 P1 — Launch flow

The launch path starts from either the session tree, the two-step `ServerDialog` (protocol-card picker followed by a protocol-specific form), or the Ctrl+K command palette. On RDP, the flow routes through `RdpHandler` which dispatches to `Embedded` (the default) or `External` based on `ServerProfileDto.RdpMode`. Embedded uses the `MsTscAx` ActiveX control hosted in a `WindowsFormsHost` inside `EmbeddedRdpView`; external spawns `mstsc.exe` with a temp `.rdp` file.

The embedded mode has a **layout flush pattern** (`UpdateLayout` → `DoEvents` → `Dispatcher.Invoke(Render)` twice, pre-connect and post-handle) that reliably works around the WPF ↔ ActiveX airspace quirks documented in `CLAUDE.md`. A **COM pre-warm** runs a throwaway `RdpActiveXHost` instance on a background STA thread at startup, saving ~400 ms on the first connect. Split panes force `Embedded` mode because external `mstsc.exe` cannot be docked into the WPF pane tree.

Progress is surfaced via `RdpLoadingBar` (indeterminate `ProgressBar`, accent-brushed) with i18n status text (`RdpStatusPreparing`, `RdpStatusWaiting`, etc.). Errors are surfaced via a `ReconnectOverlay` animated panel with a localized decoded reason (via `GetDisconnectReasonKey()`) and Reconnect / Close buttons.

The command palette supports `server:port` quick-connect parsing for RDP but lacks a confirmation UI.

### 2.2 P2 — Security / credentials / gateway

Authentication is password-only on the UI surface. Credentials are stored DPAPI-encrypted per server (`RdpPasswordEncrypted`), decrypted in-memory at connection time, injected into the ActiveX control via `IMsTscNonScriptable.put_ClearTextPassword()` (late-bound through COM), and then zeroed via `ClearPassword()` post-connect. The `ServerDialog` supports `DOMAIN\user` and UPN formats.

For the **external mode**, credentials go through `CredentialAutofill.WaitAndFillAsync`: a `CredentialManagerHelper` writes a `DOMAIN_PASSWORD` (CRED_TYPE=2) entry in Windows Credential Manager with the mstsc-compatible `TERMSRV/host` target, then an async watcher polls for CredUI dialogs (500 ms interval, 90 s default timeout — configurable via `AppSettings.RdpCredentialAutofillTimeoutMs` between 5 and 300 s), matches by class name + title regex (FR + EN patterns, `Credential Dialog Xaml Host` / `Windows Security`, owned by `mstsc` / `CredentialUIBroker` / `LogonUI` / `consent`), sets the foreground, and `WM_SETTEXT`-injects the password before sending Tab+Enter. Post-handoff, Credential Manager entries and the temp `.rdp` are cleaned after `RdpArtifactCleanupDelayMs`.

**NLA + CredSSP**: `authentication level:i:2` + `enablecredsspsupport:i:1` in the `.rdp` file, mirrored by the ActiveX `EnableCredSspSupport` property. Per-server configurable via `RdpNla` (default `true`).

**RD Gateway (TSGW)**: `gatewayusagemethod:i:1`, `gatewayprofileusagemethod:i:1`, `gatewayhostname:s:(validated host)`, `gatewaycredentialssource:i:0` in the `.rdp` file, with `InputValidator` sanitization on the hostname. Configurable per server via `ServerProfileDto.RdpGateway` — **but not exposed in `ServerDialog` for embedded mode**. Gateway auth is fully delegated to `mstsc.exe` in external mode; embedded mode inherits the RDP-over-gateway capability via the COM control, but the user cannot configure it from the UI.

**Smart card redirection**: ActiveX `RedirectSmartCards` flag, binary only. No PnP enumeration, no certificate selection UI.

**SSH-tunneled RDP**: if a server profile has `SshGatewayId`, `EmbeddedSessionManager` allocates a local ephemeral tunnel via `TunnelManager`, the `EmbeddedRdpView` resolves the connect target to `127.0.0.1:<tunnelPort>`, and the session header text reads `server:port via localhost:<port>` so the user sees the route.

**No GSSAPI / Kerberos UI**, **no Azure AD / AVD step-up**, **no Windows Hello integration**, **no MFA prompt forwarding** for interactive auth challenges.

### 2.3 P3 — Display / multi-screen / resize

**Resolution picker**: `ResolutionMenu` dropdown with "Fit to Window" default plus 1024×768 → 3840×2160 presets and a checkmark on the active choice. Selecting a preset on a connected session calls `RdpActiveXHost.UpdateResolution()`, which first attempts `IMsRdpClient9+.UpdateSessionDisplaySettings(desktopWidth, desktopHeight, physicalWidth, physicalHeight, orientation, desktopScaleFactor, deviceScaleFactor)` via reflection, then falls back to `IMsRdpClient7+.Reconnect(width, height)` if the first path raises.

**Aspect ratio**: `AspectRatioManager` supports Preserve / 16:9 / 4:3 / 21:9 / Stretch / Auto, computing letterboxed/pillarboxed dimensions with even-pixel enforcement. Configurable per server (default Stretch).

**Dynamic resize**: `SizeChanged` on the surface container feeds a 1000 ms debounce timer; changes < 50 px are ignored (filters scrollbar/hover toggles); a 10 s post-connect stabilization window (`RdpResizeEnableDelayMs`, configurable) blocks resize entirely to avoid MSTSC error 4360 (`ResolutionChangeTimeout`). Toggleable per server via `RdpDynamicResolution`.

**DPI awareness**: `PresentationSource.CompositionTarget.TransformToDevice` converts WPF DIPs to physical pixels before feeding the ActiveX control, so 150 % scale correctly requests 3357 px instead of 2238 px. Automatic, no user toggle.

**Colour depth**: `RdpColorDepth` per server (default 32), applied via COM `ColorDepth` property and `.rdp` field `session bpp:i:*`.

**Multi-monitor**: ActiveX `UseMultimon` boolean via `IMsRdpClientNonScriptable5`. Exception-guarded (warn-logged on older clients). **No `SelectedMonitors` UI or backend implementation** — cannot pick a subset of displays.

**Fullscreen**: `SetFullscreen()` collapses `SessionHeaderBar`. Accessible via the tab context menu; **no keyboard shortcut**.

### 2.4 P4 — Clipboard / devices / drive redirection

All standard redirections are wired to COM properties and `.rdp` fields with matching per-server booleans and per-mode enums in `ServerProfileDto`: clipboard (`RedirectClipboard`), drives (`RedirectDrives`), printers (`RedirectPrinters`), COM ports (`RedirectPorts`), USB devices (`RedirectDevices` — unvalidated, may fail on older clients), smart cards (`RedirectSmartCards`). Audio has a dedicated enum: mode (`AudioMode` Disabled / Local / Remote → COM `AudioRedirectionMode` 0/1/2) plus capture toggle (`AudioCaptureRedirectionMode`).

**Webcam redirection** (`cameraStoreRedirect`) is written to `.rdp` files only and is therefore functional in external mode. In embedded mode, it requires `IMsRdpClientNonScriptable7.CameraRedirConfigCollection`, which is **not implemented**.

**Per-session global-defaults switch**: `RdpUseGlobalDefaults` lets a profile inherit `AppSettings.RdpDefault*` values instead of carrying per-server overrides. UI is present; behaviour is wired through `BuildRedirections`.

**All redirections are set before `Connect()`**. There is **no hot-toggle at runtime** — changing a checkbox mid-session has no effect until reconnection.

**No filters, no allowlists, no per-drive-letter selection**, **no default-printer hinting**.

### 2.5 P5 — Reconnect / resilience

**Automatic reconnection**: COM `EnableAutoReconnect` is enabled when `RdpAutoReconnect = true` (default). `MaxReconnectAttempts = 20` is **hard-coded** in `RdpActiveXHost.cs`. The `AutoReconnecting` event bridges COM → view with an `attemptCount` parameter; the status bar updates to a localized `RdpStatusReconnecting` with the count. `CancelAutoReconnect` is a public bool on the host; setting it to `true` in the `OnAutoReconnecting` callback halts the retry loop — propagated through the COM `continueReconnect` out-param.

**Reconnect overlay**: a modal animated panel on unexpected disconnect shows the localized decoded reason plus Reconnect / Close buttons. Clicking Reconnect fires `ReconnectRequested` up to `MainViewModel`, which closes the current session and reopens it.

**Disconnect reason decoding**: `GetDisconnectReasonKey()` maps 41 mstsc disconnect codes (0 = `NoInfo`, 1 = `LocalUser`, 260 = `DnsLookupFailed`, 2055 = `BadCredentials`, 4360 = `ResolutionChangeTimeout`, etc.) to i18n key suffixes (`RdpDisconnectXxx`). Unknown codes display the numeric value.

**TCP keep-alive**: COM `KeepAliveInterval = 60000 ms`, hard-coded. Detects network breaks early.

**State machine**: `ConnectionStateMachine` enforces `ValidatingConfig → LaunchingRdp → Connected → Disconnecting → Disconnected` with thread-safe transitions and a `StateChanged` event. Error state is reachable from any other state; no automatic recovery — a user or callback must explicitly transition back.

**Split fallback**: on split attempts for an External-mode profile, `SplitService` silently forces `RdpMode = Embedded` and reconnects (external `mstsc.exe` cannot be docked).

### 2.6 P6 — Session management

**Tabs**: every connection becomes a `SessionTab` with localized Title, Status, optional `TunnelRoute` indicator, and optional `EnvironmentColor` tag. **Inline rename** is not surfaced clearly in the code; a context-menu "Rename" action exists but the edit path is unclear — gap candidate.

**Splits**: up to 8 panes per tab via binary-tree `SplitContainerModel`, drag-free UI through the split context menu. RDP forces embedded mode on split. Layout persists via `SplitLayoutMemory`.

**Duplicate**: right-click tab → "Duplicate" clones the server config and opens a new session with the same profile. Localized and wired.

**Detach**: right-click tab → "Detach" opens a `FloatingSessionWindow` with a single session; the window has its own lifecycle and is not persisted across restarts.

**Context menu** on the session tab: Disconnect, Aspect Ratio submenu (Preserve / 16:9 / 4:3 / 21:9 / Stretch), Fullscreen toggle, Duplicate, Detach.

**Session tree persistence**: `ServerProfileDto` entries are saved to `servers.json` via `ConfigManager`. Tabs open at the last session are restored — profile list, not session state. Current-session snapshot (e.g., open tabs at the moment of exit) **is not restored** across restarts.

**`.rdp` import**: `RdpFileImporter` parses the format into a `ServerProfileDto` (full address → host:port, username + domain, color depth, multi-monitor, gateway, audio capture, compression, smart sizing). Tested in `RdpFileImporterTests`. **Not surfaced in the UI**: no Import button, no drag-drop handler, no file association.

**`.rdp` export**: `RdpFileGenerator` writes a complete `.rdp` with CRLF sanitization and ACL hardening. Used by the external-mstsc flow; no UI "Export as .rdp" button for user-initiated export.

**Citrix**: a separate protocol handler (`CitrixHandler`) with its own launch flow (SelfService.exe + pre-auth command line, or direct `.ica` execution). `CitrixCacheScanner` parses `StoreBrowse` XML cache to extract published resources. No RDP-Citrix bridge; treated as an independent protocol from the UI POV.

---

## 3. Reference products — feature surface relevant to the audit

### 3.1 mstsc modern + MSRDC (Windows 11)

The lowest-common-denominator baseline any RDP user knows. No tabs, no session manager, one connection at a time. Strong on the knobs themselves:
- Full **RD Gateway UI** (dedicated `Gateway` tab with hostname, auth method, same-credentials-as-remote toggle, bypass for local addresses).
- **Device redirection** with per-category granularity (specific drives, specific printers, smart cards, clipboard, audio mode + quality, cameras — `camerastoredirect:s:*`).
- **Monitor picker**: "All monitors" + "Choose monitors" sub-UI that lists each physical display with `id` / resolution / position and lets the user tick which ones to include.
- **Azure AD / AVD auth** via MSRDC (the Remote Desktop Client available from the Store / MS360 portal).
- **Windows Hello for Business** unlock when the target is joined to Azure AD.
- **Saved credentials** stored in Credential Manager, keyed by `TERMSRV/host` (the target Heimdall's `CredentialManagerHelper` writes to).
- **`.rdp` file format** is the de facto interchange format for session configs.
- **Auto-reconnect** enabled by default, with a progress indicator.
- No tabs, no inline naming, no session tree, no vault — all of that is where Royal TS and mRemoteNG pick up.

### 3.2 Royal TS

Enterprise multi-protocol session manager. Expectations shaped by paying customers:
- **Credential vault** (Royal Server-backed or local), with per-credential scope (user / team), rotation, expiration, password generation.
- **Connection sequences**: chain actions pre/during/post connect (e.g., open a PDF with runbook, then RDP, then run a PowerShell on connect).
- **Session templates**, **session groups** with inherited settings.
- **Dashboards** — multiple sessions shown simultaneously on a grid.
- **Inline tab rename**, custom tab colors, detachable tabs with independent layout.
- **`.rdp` bulk import** from a folder.
- **Keyboard macros** per session.
- **Session comments / notes / attachments** per session entry.
- **Tasks** per session: pre-connect / post-connect scripts (batch, PowerShell, custom).
- **Licence hooks** (smart-card insertion triggers, logoff handlers).

### 3.3 mRemoteNG

Community OSS multi-protocol session manager:
- **Folder tree** with arbitrary nesting; per-folder default settings with inheritance (child sessions pull from parent unless overridden).
- **Per-protocol external tool integration**: configure `putty.exe`, `filezilla.exe`, `winscp.exe`, etc. and invoke them from any session's context menu.
- **`.rdp` bulk import** from mstsc.
- **Panel docking**: multiple docked panels (session tree, session tabs, Notes, External Tools, QuickConnect, Config) that can be floated, tabbed, pinned.
- **Session persistence** on exit (reopen where you left off).
- **XML session tree export/import**.
- **No credential vault** — per-session credentials, like Heimdall.Next today.
- **SSH config import** (mRemoteNG parses `~/.ssh/config`).

### 3.4 MobaXterm (secondary — integration only)

- Integrated RDP alongside SSH / SFTP / X11, sharing the same tabbed UI.
- Session templates.
- Relies on the built-in Windows RDP stack — no dedicated advanced knobs; the MobaXterm differentiator is the coexistence, not the RDP depth.
- Seamless handoff between an RDP session and an SSH session to the same host.

---

## 4. Gap matrix — by user journey

Parity: ✓ at parity / ~ partial / ✗ missing. Impact: **Bl** / **H** / **M** / **L**. Effort: **S** / **M** / **L** / **XL**.

### 4.1 P1 — Launch flow

| # | Capability | Heimdall | mstsc | Royal TS | mRemoteNG | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|---|
| L1 | `.rdp` import via UI button | ✗ | n/a | ✓ (folder) | ✓ (bulk) | ✗ | **Bl** | S |
| L2 | `.rdp` drag-drop onto window | ✗ | ✓ | ✓ | ✓ | ✗ | **Bl** | S |
| L3 | `.rdp` file association | ✗ | ✓ | ✓ | ✓ | ✗ | H | S |
| L4 | `rdp://` URI handler | ✗ | ~ | ~ | ~ | ✗ | L | M |
| L5 | Two-step protocol-card selection | ✓ | n/a | ~ | ~ | ✓ ahead | — | — |
| L6 | Quick-connect (Ctrl+K) | ~ stubbed | n/a | ✓ | ✓ (QuickConnect panel) | ~ | M | S |
| L7 | Embedded vs external mode toggle | ✓ | n/a | ~ | ✓ | ✓ | — | — |
| L8 | Progress indicator | ✓ i18n'd | ✓ | ✓ | ~ | ✓ | — | — |
| L9 | Error-reason overlay | ✓ with 41-code decoder | ~ (generic) | ~ | ~ | ✓ ahead | — | — |
| L10 | COM pre-warm (perf) | ✓ | n/a | ~ | ~ | ✓ ahead | — | — |

### 4.2 P2 — Security / credentials / gateway

| # | Capability | Heimdall | mstsc | Royal TS | mRemoteNG | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|---|
| G1 | Password auth | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| G2 | Smart card redirection (bool) | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| G3 | Smart card cert selection UI | ✗ | ✓ | ✓ | ~ | ✗ | M | L |
| G4 | Windows Hello unlock | ✗ | ✓ | ~ | ✗ | ✗ | L | L |
| G5 | Azure AD / AVD auth | ✗ | ✓ (MSRDC) | ✓ | ✗ | ✗ | M | XL (requires freerdp/MSRDC backend) |
| G6 | RD Gateway in `.rdp` (generator) | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| G7 | RD Gateway UI field in ServerDialog | ✗ | ✓ | ✓ | ✓ | ✗ | **Bl** enterprise | S |
| G8 | Gateway same-creds-as-remote toggle | ✗ | ✓ | ✓ | ~ | ✗ | M | S |
| G9 | NLA + CredSSP toggle | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| G10 | GSSAPI / Kerberos auth | ✗ | ✓ | ✓ | ~ | ✗ | M | L |
| G11 | Credential autofill (external mstsc) | ✓ EnumWindows + UIA + WM_SETTEXT | n/a | ~ (via Cred Man) | ~ | ✓ ahead | — | — |
| G12 | CredMan `TERMSRV/host` integration | ✓ | ✓ | ✓ | ~ | ✓ | — | — |
| G13 | MFA prompt UI forwarding | ✗ | ~ | ~ | ✗ | ✗ | M | M |
| G14 | Credential vault (central) | ✗ (per-server DPAPI) | ~ (CredMan) | ✓ (vault) | ~ (per-session) | ✗ | M (Royal TS users) | XL |
| G15 | Per-session credential prompt (never-save) | ~ | ✓ | ✓ | ✓ | ~ | L | S |
| G16 | SSH-tunneled RDP | ✓ | ✗ | ✗ | ~ (via PuTTY) | ✓ ahead | — | — |
| G17 | `.rdp` temp file ACL + CRLF sanitization | ✓ (CWE-93 hardening) | ~ (unprotected) | ✓ | ✗ | ✓ ahead | — | — |
| G18 | Plaintext password memory zeroing | ✓ | ✗ | ~ | ✗ | ✓ ahead | — | — |

### 4.3 P3 — Display / multi-screen / resize

| # | Capability | Heimdall | mstsc | Royal TS | mRemoteNG | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|---|
| D1 | Resolution preset picker | ✓ (fit + 8 presets) | ✓ | ✓ | ✓ | ✓ | — | — |
| D2 | Dynamic resize (live) | ✓ debounced + stabilized | ✓ | ✓ | ~ | ✓ | — | — |
| D3 | Aspect-ratio manager | ✓ (5 modes) | ✗ | ~ | ✗ | ✓ ahead | — | — |
| D4 | DPI-aware physical pixels | ✓ (WPF → device) | ✓ | ✓ | ~ | ✓ | — | — |
| D5 | Colour depth | ✓ (default 32) | ✓ | ✓ | ✓ | ✓ | — | — |
| D6 | Multi-monitor toggle (all) | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| D7 | Per-monitor selection | ✗ | ✓ | ✓ | ~ | ✗ | **Bl** power users | M |
| D8 | Fullscreen toggle | ✓ (via menu) | ✓ | ✓ | ✓ | ~ | — | — |
| D9 | Fullscreen keyboard shortcut | ✗ | ✓ (Ctrl+Alt+Break) | ✓ | ✓ | ✗ | M | S |
| D10 | Window-position save/restore | ✗ (per-tab) | ~ | ✓ | ✓ | ✗ | L | M |
| D11 | DPI scaling at connect time override | ~ auto-detect only | ✓ | ✓ | ~ | ~ | L | S |
| D12 | Zoom in-session | ✗ | ~ (via SmartSizing) | ~ | ✗ | ✗ | L | M |

### 4.4 P4 — Clipboard / devices / drive redirection

| # | Capability | Heimdall | mstsc | Royal TS | mRemoteNG | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|---|
| Rd1 | Clipboard redirect toggle | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| Rd2 | Drive redirect toggle | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| Rd3 | Specific-drive selection (per-letter) | ✗ | ✓ | ✓ | ~ | ✗ | M | M |
| Rd4 | Printer redirect toggle | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| Rd5 | Specific-printer / default-printer hint | ✗ | ✓ | ✓ | ~ | ✗ | L | M |
| Rd6 | COM-port redirect | ✓ | ✓ | ✓ | ~ | ✓ | — | — |
| Rd7 | USB device redirect | ✓ (unvalidated on old clients) | ✓ | ✓ | ~ | ~ | L | S (validation) |
| Rd8 | Smart-card redirect | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| Rd9 | Audio mode (Local / Remote / Off) | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| Rd10 | Audio capture | ✓ | ✓ | ✓ | ~ | ✓ | — | — |
| Rd11 | Webcam redirection (embedded mode) | ✗ stubbed | ✓ | ✓ | ~ | ✗ | M | L |
| Rd12 | Webcam redirection (external mode) | ✓ | ✓ | ✓ | ~ | ✓ | — | — |
| Rd13 | Device-redirection *hot toggle* at runtime | ✗ | ✗ | ~ | ✗ | ✗ | H | L |
| Rd14 | Use-global-defaults switch | ✓ | n/a | ✓ | ~ | ✓ | — | — |
| Rd15 | Redirection notification on block / failure | ✗ | ~ | ~ | ✗ | ✗ | M | S |

### 4.5 P5 — Reconnect / resilience

| # | Capability | Heimdall | mstsc | Royal TS | mRemoteNG | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|---|
| Rc1 | Auto-reconnect enable | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| Rc2 | Max-retries configurable | ✗ hard-coded 20 | ~ | ✓ | ~ | ✗ | M | S |
| Rc3 | Cancel auto-reconnect (user) | ✓ | ~ | ✓ | ~ | ✓ | — | — |
| Rc4 | Disconnect-reason decoder | ✓ 41 codes i18n | ~ | ~ | ✗ | ✓ ahead | — | — |
| Rc5 | Reconnect overlay with Reconnect/Close | ✓ | ~ | ✓ | ~ | ✓ | — | — |
| Rc6 | Reconnect attempt counter | ✓ | ~ | ✓ | ~ | ✓ | — | — |
| Rc7 | Configurable keep-alive interval | ✗ hard-coded 60 s | ~ | ✓ | ~ | ✗ | M | S |
| Rc8 | Session-state restore after crash | ✗ | ✗ | ✓ | ✓ | ✗ | H | M |
| Rc9 | Telemetry on reconnect patterns | ✗ | ✗ | ~ | ✗ | ✗ | L | M |
| Rc10 | Back-off curve (exponential) | ✗ (COM default) | ~ | ~ | ~ | ✗ | L | S |

### 4.6 P6 — Session management

| # | Capability | Heimdall | mstsc | Royal TS | mRemoteNG | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|---|
| M1 | Tabbed sessions | ✓ | ✗ | ✓ | ✓ | ✓ | — | — |
| M2 | Session tree / folders | ✓ (sidebar) | ✗ | ✓ | ✓ | ✓ | — | — |
| M3 | N-pane split | ✓ (up to 8) | ✗ | ~ (dashboard) | ~ (docked panels) | ✓ ahead | — | — |
| M4 | Inline tab rename | ~ unclear | n/a | ✓ | ✓ | ~ | H | S |
| M5 | Tab custom colour / tag | ✓ (EnvironmentColor) | n/a | ✓ | ✓ | ✓ | — | — |
| M6 | Duplicate tab | ✓ | ✗ | ✓ | ✓ | ✓ | — | — |
| M7 | Detach / float window | ✓ | ✗ | ✓ | ✓ | ✓ | — | — |
| M8 | Session-saved prompt on close | ✗ | ✗ | ~ | ~ | ✗ | L | S |
| M9 | Restore open tabs after restart | ✗ profile-only | ✗ | ✓ | ✓ | ✗ | **Bl** MobaXterm/mRemoteNG users | M |
| M10 | Connection sequences (pre/post actions) | ✗ | ✗ | ✓ | ~ | ✗ | M | L |
| M11 | Session notes / comments | ✗ | ✗ | ✓ | ~ | ✗ | M | S |
| M12 | Session attachments (runbooks, files) | ✗ | ✗ | ✓ | ✗ | ✗ | L | M |
| M13 | Per-session keyboard macros | ~ (SSH only via MacroService) | ✗ | ✓ | ✗ | ~ | M | M |
| M14 | Per-protocol external-tool integration | ~ (ToolRegistry) | ✗ | ✓ | ✓ | ~ | M | M |
| M15 | Drag-drop session between tabs | ✗ | ✗ | ✓ | ~ | ✗ | L | M |
| M16 | Dashboards (multiple sessions on grid) | ~ (via split) | ✗ | ✓ | ~ | ~ | L | M |
| M17 | Session tree XML / JSON export-import | ~ (JSON existing) | ✗ | ✓ | ✓ | ~ | M | S |
| M18 | Session search across tree | ~ (sidebar filter) | n/a | ✓ | ✓ | ~ | L | S |
| M19 | `.rdp` export from UI | ✗ (generator used only internally) | ✓ | ✓ | ✓ | ✗ | M | S |

---

## 5. Top critical gaps (consolidated ranking)

### 5.1 Absolute blockers (cannot migrate without)

1. **L1 / L2 / L3 — `.rdp` bulk migration** (button, drag-drop, file association). Single most concrete blocker for any user with an existing mstsc estate. Effort S × 3 = ~2 batches.
2. **G7 — RD Gateway UI in `ServerDialog`**. Enterprise mstsc users cannot configure gateway in embedded mode today. Effort S.
3. **D7 — Per-monitor selection**. Power users with asymmetric multi-display setups. Effort M.
4. **M9 — Restore open tabs after restart**. MobaXterm / mRemoteNG parity. Today only the profile list is restored, not the session snapshot. Effort M, shares plumbing with b52's T31.

### 5.2 High-impact improvements

5. **Rd13 — Hot-toggle redirections at runtime**. Changing "share drives" mid-session requires a full reconnect today — mid-workflow friction. Effort L.
6. **Rc7 / Rc2 — Configurable keep-alive and max-retries**. Per-server overrides of today's hard-coded 60 s and 20 attempts. Effort S × 2.
7. **M4 — Inline tab rename**. Explicit rename action exposed and functional — the context menu entry suggests it exists but the UX is unclear. Effort S.
8. **M19 — `.rdp` export from UI**. Companion to L1 — users want round-trip. Effort S.
9. **D9 — Fullscreen keyboard shortcut**. Feature exists, no accelerator. Effort S.
10. **Rc8 — Session-state restore after crash**. A narrower version of M9 triggered by abnormal exit. Effort M.

### 5.3 Medium-impact quality improvements

11. **G8 — Gateway same-creds-as-remote toggle**. Common mstsc pattern. Effort S.
12. **G13 — MFA prompt forwarding**. Today no interactive auth challenge UI. Effort M.
13. **Rd3 — Specific-drive selection** (per-letter). Effort M.
14. **Rd15 — Redirection failure notification**. Today silent. Effort S.
15. **M10 — Connection sequences (pre/post actions)**. Royal TS parity. Effort L.
16. **M11 — Session notes / comments**. Royal TS parity. Effort S.
17. **L6 — Quick-connect confirmation UI** in the palette (today stubbed). Effort S.
18. **Rd7 — USB redirection validation on older clients**. Currently unguarded. Effort S.
19. **M13 — Per-session keyboard macros for RDP**. Today SSH only. Effort M.

---

## 6. Proposed roadmap clusters

Clusters group related gaps. Sizing is indicative; batch sizing should match the 1–2-files-plus-tests cadence. The `>>` marker indicates a dependency.

### Cluster RA — `.rdp` bulk migration
**Gaps**: L1, L2, L3, M19. **Effort**: S + S + S + S. **Value**: single biggest migration unblocker for mstsc estates.
**Batches**: ~2.

### Cluster RB — Gateway + security surfacing
**Gaps**: G7, G8, G13. **Effort**: S + S + M. **Value**: unblocks enterprise + MFA-driven orgs.
**Batches**: ~1–2.

### Cluster RC — Multi-monitor refinement
**Gaps**: D7, D9, D11. **Effort**: M + S + S. **Value**: power-user ergonomics.
**Batches**: ~1–2.

### Cluster RD — Reconnect / resilience knobs
**Gaps**: Rc2, Rc7, Rc8, Rc10. **Effort**: S + S + M + S. **Value**: removes hard-coded guesses; pairs naturally with b52 Cluster B.
**Batches**: ~1.

### Cluster RE — Session management UX
**Gaps**: M4, M9, M11, M17, M18. **Effort**: S + M + S + S + S. **Value**: Royal TS parity on session-manager fundamentals. `>>` M9 pairs with b52's T31.
**Batches**: ~2.

### Cluster RF — Hot-toggle redirections
**Gaps**: Rd13, Rd15. **Effort**: L + S. **Value**: mid-session workflow friction.
**Batches**: ~1 (L effort).

### Cluster RG — Device redirection granularity
**Gaps**: Rd3, Rd5, Rd7 (validation), Rd11 (embedded webcam). **Effort**: M + M + S + L. **Value**: closes mstsc-depth gaps.
**Batches**: ~2.

### Cluster RH — Connection sequences / tasks (Royal TS parity)
**Gaps**: M10, M12, M13, M14 (external-tool per protocol). **Effort**: L + M + M + M. **Value**: enterprise differentiation; consumer-niche.
**Batches**: ~3.

### Cluster RI — Credential vault (Royal TS / enterprise parity)
**Gaps**: G14. **Effort**: XL architectural. **Value**: team-sharing enablement; out of single-user product positioning today.
**Batches**: TBD — needs a product decision first, not a batch.

---

## 7. Existing strengths to preserve

Heimdall.Next has six RDP behaviours that meet or exceed *all* three reference products. Any refactor must keep these intact.

- **41-code disconnect reason decoder with i18n** (`GetDisconnectReasonKey()` in `RdpActiveXHost.cs`). Numeric MSTSC codes (0 through 4360) map to localized keys. mstsc's modal shows generic phrases; Royal TS and mRemoteNG show numeric errors with no decoding.
- **Dual-path `UpdateResolution`** (`UpdateSessionDisplaySettings` on `IMsRdpClient9+` with fallback to `Reconnect(w, h)` on `IMsRdpClient7+`). Works across RDP client versions without breaking on older Windows 10 builds.
- **DPI-aware physical pixel conversion** before feeding dimensions to the ActiveX control. Correct rendering at 125/150/200 % scale out of the box, which mRemoteNG notoriously struggles with.
- **`CredentialAutofill`** via `EnumWindows` + UI Automation + `WM_SETTEXT`, including multi-dialog contention handling and FR/EN title regex matching. Replaces the rigid CredMan-only handoff of Royal TS and mstsc.
- **Temp `.rdp` hardening**: CRLF-injection sanitization, per-file ACL reducing the attack surface on shared user profiles, plaintext password memory zeroing after COM handoff. Neither mstsc nor the other three references apply these mitigations.
- **SSH-tunneled RDP via `SshGatewayId` + `TunnelManager`** with reference-counted tunnel reuse. mRemoteNG requires the user to manually configure a PuTTY session for the tunnel; mstsc has no equivalent. Royal TS supports tunnels per session but without cross-session reuse.

The following three behaviours are also superior but less visible, hence flagged for preservation:

- **COM pre-warm** saving ~400 ms on first connect.
- **`AspectRatioManager`** with 5 modes and even-pixel enforcement (neither mstsc nor Royal TS exposes this — mstsc has only `smart sizing`).
- **`ConnectionStateMachine`** typed transitions, enabling future reconnect/back-off logic to reason about recoverability per state.

---

## 8. Open questions for b54 synthesis

Consolidated with b52's open questions; to be decided together once b54 begins.

1. **Target audience resolution**. mstsc-heavy users (Windows sysadmins), Royal TS users (enterprise ops), mRemoteNG users (mid-market IT), or MobaXterm users (Unix-centric engineers)? Several priorities above depend on the answer. Heimdall.Next can serve all four partially, but a clear primary signals ordering.
2. **Credential vault scope** (G14). Without one, Heimdall.Next stays positioned as a single-user tool. With one, it becomes a Royal-TS competitor with multi-year build. The decision is a product bet, not an engineering question.
3. **Azure AD / AVD auth** (G5). Requires moving off MsTscAx to MSRDC or freerdp. The cost is architectural; the payoff is real for cloud-migrated estates. Worth validating demand before committing.
4. **Webcam in embedded mode** (Rd11). `IMsRdpClientNonScriptable7.CameraRedirConfigCollection` is complex COM surface. Is webcam in embedded mode a blocker for anyone, or are external-mode users covered?
5. **Hot-toggle redirections** (Rd13). Technically feasible but invasive (COM property changes may require a session-layer reconnect even if we control the trigger). Worth a proof-of-concept first.
6. **Session-state restore** (M9, Rc8). Scope: reopen tabs + auto-connect vs. reopen tabs and wait for user confirmation per tab? MobaXterm auto-connects; Royal TS prompts.
7. **Connection sequences** (M10). Is this the right place to invest to reach Royal TS parity, or is the vault (G14) the more strategic target?

---

## Appendix A — Discarded items (low-priority, not proposed)

Documented for trace purposes — conscious choices *not* to prioritize, so we don't re-litigate them on every audit pass.

- **freerdp / MSRDC backend swap.** Requires replacing MsTscAx with a non-ActiveX stack. Unlocks Azure AD (G5), cross-platform reach, and modern RDP extensions. Cost XL+. Not proposed until the Windows-only positioning is re-examined.
- **Web-based RDP (HTML5 canvas).** Out of scope for a desktop connection manager.
- **Royal Server / team-sharing backend** (extension of G14). Enterprise multi-year investment.
- **Session recording (VT-free RDP video)** — analogue of b52 T10. Architecturally possible via COM event hooks on the frame surface, but effort is XL and demand is niche.
- **Plugin / extension API for third-party protocol handlers.** Mentioned in some Royal TS competitive literature. Not an identified user request.
- **Mobile companion app** (Royal TS has one). Out of scope.
- **RDP performance-profile presets** ("Modem / Satellite / LAN / Auto") beyond the raw `RdpPerformanceFlags` field. Nice-to-have for classic mstsc users but rarely touched on modern networks.
- **Per-drive-letter redirection** (Rd3) *with UI for a specific named drive* — the matrix flags the base gap, but fine-tuning individual drives is a low-frequency power-user request.
- **Keyboard-hook mode selector** (`KeyboardHookMode` Local / Remote / Fullscreen). mstsc exposes it; Royal TS exposes it; Heimdall uses the COM default. If Windows+R behavior becomes a complaint, revisit.
- **Fullscreen across all monitors via single `F11`** (distinct from D9 single-monitor fullscreen) — implicit in `UseMultimon=true` today.

---

## Appendix B — Cross-reference index

- **Backend reconnaissance source**: `src/Heimdall.Rdp/` (entire project tree), `src/Heimdall.App/Services/Handlers/RdpHandler.cs`, `CitrixHandler.cs`, `CredentialManagerHelper.cs`, `RdpFileGenerator.cs`, `RdpFileImporter.cs`, `RdpActiveXHost.cs`, `AspectRatioManager.cs`, `ConnectionStateMachine.cs`, `ServerProfileDto.cs`, relevant `tests/`.
- **Frontend reconnaissance source**: `src/Heimdall.App/Views/EmbeddedRdpView.xaml(.cs)`, `Views/Dialogs/ServerDialog.xaml(.cs)`, RDP-related viewmodels, `ConnectionService.cs`, `EmbeddedSessionManager.cs`, `SplitService.cs`, `SessionTabContextMenuFactory.cs`, `AppSettings.cs` RDP block, locale keys matching `Rdp*`.
- **Reference product information**: public documentation of mstsc / MSRDC (`docs.microsoft.com`), Royal TS v7/v8 official site, mRemoteNG project site, MobaXterm product pages. Used only to cross-check feature surfaces; no product-internals were referenced.
- **Prior audit**: `docs/audit-gap-ssh-terminal-sftp-2026-04-19.md` (b52). b54 will consume both.

End of b53 audit. b54 (priority synthesis + roadmap) begins on Julien's signal and consumes b52 + b53 outputs.
