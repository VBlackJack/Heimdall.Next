# RDP / ActiveX subsystem — quality audit

- **Date**: 2026-05-24
- **Mode**: pair-architect (supervisor = Cowork, implementation = Julien)
- **Scope (validated)**: ActiveX/COM core, embedded view + lifecycle + teardown,
  display & resolution, handler + connection path + resolvers, credential
  autofill + keyboard hook, `.rdp` file generation.
- **Out of scope**: `.rdp` import (`RdpImportService`, dialog, Core parser),
  `CitrixHandler`, XAML theming.
- **Method**: six parallel cluster reads of the real source, every finding
  re-verified against `src/` by the supervisor (file + line + trigger). Reported
  P1 candidates were down-graded after verification — see the verdict.

## Verdict

Good overall health. **0 P1, 4 P2, ~20 P3.** The COM-interop mechanics are
sound — the documented QueryInterface fallbacks, the deliberate avoidance of
`dynamic` and `Marshal.ReleaseComObject`, the fixed teardown order, and the
ref-count discipline all hold up. **No credential-logging breach** was found in
any cluster: connect logs reference host + protocol only, password buffers are
zeroed, the COM-side password is cleared after handoff, and the `.rdp` file
never contains the password (it goes to Credential Manager instead). The pure
helpers (`Display/*`, `Views/EmbeddedRdp/*` policies) are well-tested.

The four P2s are concentrated in exception-safety at the COM/Win32 boundaries
and one validator inconsistency. The P3s are the usual mix: dead code, doc
drift, hardening, and duplicated scale-factor logic.

Two big structural facts are noted but deliberately **not** treated as audit
findings (architecture-first): `EmbeddedRdpView.xaml.cs` is a 3815-line
code-behind, and `var` is pervasive against the project's explicit-types
preference. Both are deliberate refactors on their own merits, not test-driven
fixes — same call as the SSH/tunnel audit's S11.

---

## P1 — none

The two cluster-level P1 candidates were verified and down-graded:

- *"`RdpActiveXHost.Connect()` is unguarded → hard crash"* — the only caller,
  `EmbeddedRdpView.BeginConnect` (`EmbeddedRdpView.xaml.cs:1368-1455`), wraps the
  whole body, including `_rdpHost.Connect()` at line 1440, in `try/catch` →
  `HandleFailure`. Not a crash path. Residual consistency nit kept as **D1**.
- *"`.rdp` generator uses `Environment.NewLine` → Linux CI produces LF files"* —
  the `.rdp` file is only ever generated at runtime on the user's Windows
  machine to feed `mstsc.exe`; CI never produces a `.rdp` for `mstsc`. In
  production `Environment.NewLine` is always `\r\n`. Residual explicitness nit
  kept as **D3**.

---

## P2 findings

### P2-1 — COM event sink dispatches .NET events with no exception guard

`ComInterfaces.cs:122-135` (`MsTscAxEventSink`) forwards every `IMsTscAxEvents`
callback straight to `RdpActiveXHost.Raise*` (`RdpActiveXHost.cs:629-676`), and
the `Raise*` methods invoke the public `Connected` / `Disconnected` / `FatalError`
/ `LoginComplete` / `AutoReconnecting` / `AutoReconnected` events with no
`try/catch`. An exception thrown by any subscriber (the `EmbeddedRdpView`
handlers do substantial work) propagates back across the COM connection-point
boundary. The CLR converts it to a failure HRESULT at the CCW boundary, so it is
**not** a process crash — but it is still a real defect:

- Partial event delivery: subscribers after the throwing one never run.
- `RaiseConnected` (629-635) runs `StripScrollbarStylesRecursiveOnUiThread` and
  `BeginPostConnectStripTimerOnUiThread` **after** `Connected?.Invoke()` — a
  throwing subscriber skips both post-connect steps (scrollbar leak, no strip
  timer).
- `OnAutoReconnecting` (`ComInterfaces.cs:128-133`) is worst: if
  `RaiseAutoReconnecting` throws, `continueReconnect` (an `out` param marshalled
  back to MsTscAx) is never assigned.

**Direction**: wrap each `Raise*` body in a `try/catch` that logs and swallows
(there is no safe way to surface a fault to the COM caller anyway), and in the
sink assign `continueReconnect` before the `Raise*` call or in a `finally`.

### P2-2 — `RdpKeyboardEscapeHook.OnKeyboardHook` is an unguarded hook callback

`RdpKeyboardEscapeHook.cs:202-231` — the `WH_KEYBOARD` (thread-local, value 2)
hook callback has no `try/catch`. It calls `FindFocusedRdpView()` and
`view.Dispatcher.BeginInvoke(...)`. `BeginInvoke` on a dispatcher that is
shutting down throws; `FindFocusedRdpView` touches disposing views. The hook is
installed on the UI thread, so an exception escaping the callback is an
unhandled exception **on the UI thread → app crash**, and `CallNextHookEx`
(line 230) is skipped so other apps' hooks are starved. Narrow trigger
(shutdown race + a matching keypress) but a crash-class defect.

**Direction**: wrap the whole callback body in `try { } catch (Exception ex)
{ FileLogger.Error(...) }` and always fall through to `CallNextHookEx`.

### P2-3 — external credential autofill is bound to the connect-scoped token

`RdpHandler.cs:293-318` — the external-launch autofill `Task.Run(async …, ct)`
and the inner `CredentialAutofill.WaitAndFillAsync(…, ct)` both observe the
connect-scoped `CancellationToken`. For an external launch `ConnectAsync`
returns immediately after spawning `mstsc` (line 338), and the connection
coordinator then cancels/disposes that token — long before `mstsc`'s CredUI
dialog appears (autofill polls up to `RdpCredentialAutofillTimeoutMs`, default
90 s). The autofill is silently cancelled (`OperationCanceledException` swallowed
at 311-313). The artifact-cleanup task on line 326-336 **deliberately uses
`CancellationToken.None`** for exactly this reason — proof the connect token does
not outlive the call. External credential autofill is therefore effectively
non-functional in production.

**Direction**: give the autofill task an independent, timeout-bounded CTS (or
`CancellationToken.None` capped by `WaitAndFillAsync`'s own `TimeSpan`),
mirroring the cleanup task.

### P2-4 — `RdpDisplayResolver.ValidateMultimon` accepts negative monitor indices

`RdpDisplayResolver.cs:110` — `requested.SelectedMonitorIndices.Any(index =>
index >= host.MonitorCount)` checks only the upper bound. A negative index
(`-1`) passes validation and is handed to the RDP COM layer. The sibling
`RdpSelectedMonitorValidator.Validate` correctly rejects `index < 0`, and
`RdpFileGenerator.cs:45` filters `index >= 0` — so the two other validators
disagree with this one. Trigger requires a corrupt/hand-edited profile, hence
P2 not P1, but it is a real fail-open inconsistency.

**Direction**: change the predicate to `index < 0 || index >= host.MonitorCount`
and pin it with a `[-1]` test in `RdpMultimonValidationTests`.

### P2 — test coverage gaps on risky, testable logic

Pure and decoupled, genuinely risky, currently untested:

- `RdpActiveXHost.GetDisconnectReasonKey` / `FormatDisconnectCode` /
  `GetDisconnectSeverity` — `RdpActiveXHostTests.cs` covers only the scrollbar
  strip + the post-connect timer. A decoder regression silently mislabels every
  disconnect.
- `RdpSelectedMonitorValidator` — **zero** direct tests (dedup, order
  preservation, negative index, `availableMonitorCount <= 0`).
- `RdpShortcutParser.ParseOrDefault` / `ParseFullscreenOrDefault`
  (`RdpKeyboardEscapeHook.cs`) — pure, branchy, decides which keystrokes get
  swallowed; untested.
- The credentialed external-launch path of `RdpHandler` (CredMan write,
  decrypt-failure diagnostic, `.rdp` write-failure fallback) is untested.

---

## P3 findings

### Dead code & unreachable API

- **D2** — `IMsRdpClientNonScriptable5` (`ComInterfaces.cs:71-73`) is an empty
  marker interface. `TrySetUseMultimon` reaches the vtable by slot index
  (correct), but `TrySetNonScriptable5SelectedMonitors` (`RdpActiveXHost.cs`
  ~1045) uses `GetObjectForIUnknown` + late-bound `InvokeMember("SelectedMonitors")`
  — that dispatches through `IDispatch`, which `IMsRdpClientNonScriptable5`
  generally does not expose. The `selectedmonitors` fallback is very likely
  non-functional. CLAUDE.md already documents it as "best-effort"; either
  implement the slotted native call (as for `UseMultimon`) or drop the fallback
  and state that only the `SetRdpProperty` shell path is real.
- **D5** — `RdpFileOptions.Domain` (`RdpFileGenerator.cs:229-230`) is written
  (56-59) and unit-tested, but no production caller sets it and
  `ServerProfileDto` has no domain field — half-wired dead API. Decide: wire a
  real domain field through the DTO, or remove the property.
- **D14** — dead constants: `RdpActiveXHost.NotSafeForScriptingClsid` (no
  reference), `CredentialManagerHelper.CredPersistEnterprise` (only
  `CredPersistSession` is used).

### Doc drift

- **D6** — the `.rdp` generator tests (`RdpFileGeneratorTests.cs`,
  `RdpRedirectionOptionsTests.cs`) live in `tests/Heimdall.Ssh.Tests/` under
  `namespace Heimdall.Ssh.Tests`, while a real `tests/Heimdall.Rdp.Tests/`
  project exists (ActiveX + Display tests). The RDP file-format tests are
  misfiled. Move them to `Heimdall.Rdp.Tests` and drop the `Heimdall.Rdp`
  reference from the SSH test csproj.
- **D7** — `CLAUDE.md` repo-layout tree lists `EmbeddedRdp/` indented under the
  `Heimdall.App/Services/` block; the folder is actually
  `src/Heimdall.App/Views/EmbeddedRdp/` (15 files). `ARCHITECTURE.md` is
  correct. Fix the CLAUDE.md tree.
- **D8** — `CLAUDE.md`'s "Fullscreen keyboard escape" gotcha states reliable
  shortcuts require `WH_KEYBOARD_LL` + a foreground-process filter. The code
  uses `WH_KEYBOARD` (thread-local, `RdpKeyboardEscapeHook.cs:31`,
  `WhKeyboard = 2`) installed on the UI thread, with no foreground filter — and
  that is arguably *correct* (a thread-local hook sees keys for the MsTscAx
  child HWND owned by the same thread, no global hook needed). Update the
  gotcha to match the code, or document why the thread-local choice was made.
- **D15** — doc-comment drift: `RdpDisplayHelper.SnapToMultipleOf` XML-doc says
  "snaps a positive dimension" but the method is total over `int` (returns `0`
  for non-positive); `RdpFileGenerator`'s class XML-doc claims "all parameter
  values are sanitized against CRLF injection" while only four call sites route
  through `AppendSanitized`; `RdpActiveXHost.MaxAutoReconnectAttempts` XML-doc
  reads as a hard ceiling but it is only a default (clamped `[1,60]`).
- **D19** — `GetDisconnectReasonKey`: any code not in the switch falls to
  `_ => null` and surfaces only a raw number to the user. Confirm the 24-code
  list against the MsTscAx typelib and document that extended-reason bit-packing
  is intentionally not decoded.
- **D20** — `RdpResolutionModeIndicator.cs:41` comment says the glyphs are
  "expressed as escape sequences" but the constants hold raw PUA codepoints
  (verified: U+E713/E740/E73F/E799/…). Trivial — fix the comment.

### Hardening

- **D1** — `RdpActiveXHost.Connect()` (`RdpActiveXHost.cs:401`) does not set
  `LastError` on a COM failure, unlike `Disconnect()` (410-418). The caller
  catches the exception, so it is not a crash; align it for diagnostic
  consistency.
- **D3** — `RdpFileGenerator` builds the file with `StringBuilder.AppendLine`
  (`Environment.NewLine`). Correct in production (Windows-only path) but the
  CRLF requirement is implicit and untested. Pin it explicitly and add a
  byte-level test asserting `\r\n` / UTF-8 no-BOM.
- **D4** — `RdpFileGenerator` interpolates `Port`, `Width`, `Height`,
  `ColorDepth` with no range validation (`full address:s:{Host}:{Port}` at
  line 50, display ints at 62-65). A zero/negative value yields a malformed
  directive; an unbracketed IPv6 `Host` makes `full address` ambiguous.
  Validate/clamp before writing; bracket IPv6 literals.
- **D11** — `RdpConnectivityTester` returns hardcoded English user-facing
  strings (`RdpConnectivityTester.cs:36-42`, `133-152`) consumed by the
  ServerDialog "Test Connection" button. Move to locale JSON or carry an
  outcome enum and localize at the call site.
- **D12** — `CredentialAutofill` logs Edit/Button `Name` + `AutomationId` at
  `Info` (`CredentialAutofill.cs:628-629`, `672-673`). `AutomationElement.Name`
  is the accessible label, not edit-field content, so this is **not** a
  credential-logging breach — but on a credential dialog it is worth
  down-grading to `Debug` and logging only `controlType` / `isPassword` /
  `isEnabled` / `index` as hygiene.
- **D13** — P/Invoke style is inconsistent across the security-critical cluster:
  `CredentialAutofill` uses source-generated `LibraryImport`, while
  `RdpKeyboardEscapeHook` and `CredentialManagerHelper` still use legacy
  `DllImport`. Normalize on `LibraryImport`.
- **D16** — `EmbeddedRdpView.EnableResolutionUpdatesAsync` eagerly
  `Cancel()` + `Dispose()`s `_stabilizationCts` before overwriting it; two
  concurrent calls (`OnRdpConnected` + `OnRdpAutoReconnected`) can dispose a CTS
  another invocation still awaits. The `ReferenceEquals` guards keep the
  *outcome* safe today, but capture-cancel-dispose-in-`finally` is the robust
  shape.
- **D17** — `async void` handlers `OnWindowDpiChanged` and `OnResolutionMenuClick`
  have no top-level `try/catch`; their safety relies entirely on the callees
  swallowing everything. Add a defensive top-level catch.
- **D18** — `_resizeTimer` and `_autofillFilledTimer` `Tick` handlers are never
  detached in `Dispose` (other timers in the file are). Self-contained, not a
  true leak, but inconsistent — add the symmetric `Tick -=`.

### Duplication & magic numbers

- **D9** — `RdpDisplayResolver` keeps its own `DesktopScaleFactors` table
  (`[100,125,150,175,200]`, line 25) and a private nearest-neighbour scan
  (`ResolveDesktopScaleFactor`, 284-304), duplicating
  `RdpDisplayHelper.DesktopScaleFactors`/`MapDpiToDesktopScaleFactor`. The two
  tables diverge (the helper goes to 500%, has `DeviceScaleFactors`
  `[100,140,180]`), so the resolver silently caps DPI at 200% and can never
  emit device scale 180. Consolidate onto `RdpDisplayHelper`.
- **D10** — unnamed magic numbers: `RdpDisplayResolver` `640` (min desktop
  width) and `4` (snap floor); the `<= 140` device-scale boundary (line 39),
  which is never an actual table value; `RdpActiveXHost` keep-alive default
  `60_000` and the `[1,60]` / `[5_000,300_000]` clamp bounds. Promote to named
  constants or `AppSettings`.

---

## Out of scope / motivated dismissals

- **`EmbeddedRdpView.xaml.cs` is a 3815-line code-behind** owning the whole RDP
  session lifecycle, resolution math, autofill state and overlay logic — far
  past "minimal event wiring". The policy extraction into `Views/EmbeddedRdp/*`
  is the right direction; a `ConnectionViewModel`-style extraction is a
  deliberate architecture change, not an audit fix. Standing structural debt.
- **`var` pervasive** vs the project's explicit-types preference — a
  project-wide `.editorconfig` divergence, not an RDP defect (same call as SSH
  audit S11).

## Dismissed false positives (verified)

- *"`RdpResolutionModeIndicator` glyph constants are empty strings → blank
  toolbar icons"* — **false**. `hexdump` confirms valid PUA codepoints
  (U+E713 / E740 / E73F / E799 / …). The Read tooling just does not render PUA.
- *"`RdpHandler` `EnableRaisingEvents` ordering race orphans the tunnel"* —
  **false**. The `Exited` handler is subscribed (line 240) before
  `EnableRaisingEvents = true` (287), and setting `EnableRaisingEvents = true`
  after a process has already exited still raises `Exited` (.NET registers a
  wait on the already-signaled handle). No leak.
- *"`SetFullscreen` is called off-thread from the keyboard hook"* — **false**.
  The hook marshals via `Dispatcher.BeginInvoke(view.ToggleFullscreen)`
  (`RdpKeyboardEscapeHook.cs:222-224`); `SetFullscreen` always runs on the UI
  thread.

---

## Remediation backlog — pair-architect chunks

| Chunk | Content | Notes |
|---|---|---|
| **A** | The 4 P2s: P2-1 event-sink guard, P2-2 hook-callback guard, P2-3 autofill token, P2-4 `ValidateMultimon` negative index — each with its regression test | Real risks; ordered first |
| **B** | Dead code & doc drift: D2, D5, D6 (misfiled tests), D7, D8, D14, D15, D19, D20 | Low-risk; some need a 1-line decision (D5 wire-or-remove) |
| **C** | Hardening: D1, D3, D4, D11 (i18n), D12, D13, D16, D17, D18 | D11 touches locale JSON |
| **D** | Duplication & magic numbers: D9 (scale-factor consolidation), D10 | D9 is the riskiest P3 — changes DPI behaviour, needs care |
| **E** | Test coverage: disconnect decoder, `RdpSelectedMonitorValidator`, `RdpShortcutParser`, credentialed external-launch path | Pure-logic tests, no production change |

Suggested order: **A → B → C → E → D** (D last — it changes runtime DPI
behaviour and benefits from the regression tests landed in E).
