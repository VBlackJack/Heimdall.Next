# Terminal / ConPTY layer - quality audit

- **Date**: 2026-05-25
- **Mode**: pair-architect (supervisor = Cowork, implementation = Julien)
- **Scope (validated)**: `Heimdall.Terminal` full project, App-side terminal
  handlers (`LocalShellHandler`, `TelnetHandler`, `WinRmHandler`), WinRM launch
  and bootstrap helpers, `TerminalAssetsLoader`, `EmbeddedSshView`
  (shared terminal surface), `terminal.html`, and terminal-specific
  `EmbeddedSessionManager` materialization paths.
- **Out of scope**: `Heimdall.Ssh` core (already covered by
  `audit-ssh-tunnel-2026-05-24.md`), RDP, SFTP, VNC, and the tool subsystem
  except where they only prove terminal session usage.
- **Method**: real Windows workspace reads only, with line-numbered checks of
  each in-scope file. No source changes, no build, no test run by design. Test
  coverage was inventoried with `rg` against `tests/`.

## Verdict

Good base, but the layer is not as mature as the protocol layers already
audited. **0 P1, 8 P2, ~15 P3.** The happy path is sound: ConPTY uses a
`SafeHandle`, WebView2 receives binary-safe base64 frames, WinRM command
construction quotes host and command-line arguments, and the WinRM credential
bootstrap writes a DPAPI blob rather than a plaintext password. No direct
credential logging breach was found.

The risk is concentrated in lifecycle and trust-boundary edges:

- process/session cleanup paths are weaker than the normal close path;
- Telnet negotiation is parsed as if every IAC sequence arrives in one TCP
  read;
- WebView2 accepts `data:` navigations/messages and has a fallback that writes
  unknown messages into the terminal;
- there are no dedicated tests for the three session implementations that own
  processes, sockets, handles, and native PTY resources.

One P1 candidate was deliberately downgraded: `PipeModeSession.Dispose()` does
not kill because it sets `_disposed = true` before calling `Kill()`, but the
main UI close path calls `Kill()` before `Dispose()` (`EmbeddedSshView.xaml.cs:487-500`). That keeps ordinary tab close/user disconnect out of P1. The
implementation is still a real P2 because `Dispose()` is the interface-level
lifecycle contract and failure paths call it directly.

---

## P1 - none

No deterministic crash, credential exposure, or guaranteed orphan on the
primary UI close path was found.

---

## P2 findings

### P2-1 - `PipeModeSession.Dispose()` makes its own `Kill()` call unreachable

`PipeModeSession.cs:130-138` sets `_disposed = true`, then calls `Kill()`.
`Kill()` immediately returns when `_disposed` is true (`PipeModeSession.cs:116-124`), so `Dispose()` never explicitly terminates the child process. It only
disposes the `Process` wrapper at line 138, which releases managed handles but
does not guarantee process termination.

Primary tab close is currently mitigated by `EmbeddedSshView.Dispose()` calling
`_terminalSession.Kill()` before `_terminalSession.Dispose()` (`EmbeddedSshView.xaml.cs:487-500`). The defect remains for any consumer that relies on
`IDisposable` alone, and for exceptional paths that call `session.Dispose()`
after partial startup (`LocalShellHandler.cs:149-156`,
`WinRmHandler.cs:230-231`).

**Trigger**: a `PipeModeSession` is created for fallback ConPTY-less Local Shell
or WinRM, or for pipe-mode terminal use, then a startup path fails after the
process has been assigned, or a non-view owner disposes the session without
calling `Kill()` first. The child can outlive the owning session.

**Direction**: either make `Dispose()` perform termination inline before setting
`_disposed`, or make `Kill()` accept an internal disposal path. Pin with a fake
or real short-lived process test proving `Dispose()` terminates/waits.

### P2-2 - Telnet IAC parsing is stateless across TCP packet boundaries

`TelnetSession.ProcessIncoming` parses each `ReadAsync` chunk independently
(`TelnetSession.cs:181-246`). When a chunk ends with a bare `IAC`, line 195
breaks out of the loop without recording parser state; line 243 then flushes
the remaining bytes as application data. Similar truncation exists for
`IAC DO <option>` / `IAC WILL <option>` split before the option byte, and
`SkipSubnegotiation` returns `data.Length - 1` on a truncated `SB ... IAC SE`
block (`TelnetSession.cs:310-320`).

**Trigger**: a Telnet server sends `IAC DO NAWS`, `IAC WILL ECHO`, or
subnegotiation bytes split across two TCP reads. The client can leak Telnet
control bytes into xterm output, duplicate preceding application bytes, or miss
NAWS negotiation, so later resize messages never reach the server.

**Direction**: replace the per-span parser with a tiny state machine carrying
pending `IAC`, command, option, and subnegotiation state across reads. Add tests
that feed negotiation one byte at a time.

### P2-3 - Terminal event callbacks can tear down read loops or escape process callbacks

Session implementations invoke public events directly:

- `ConPtySession.cs:409` invokes `DataReceived`; the read loop only catches
  cancellation/dispose/IO exceptions (`ConPtySession.cs:412-414`), so a
  subscriber exception faults `_readLoop`. `ProcessExited` is also unguarded at
  `ConPtySession.cs:427`.
- `PipeModeSession.cs:156` invokes `DataReceived` inside the stream loop and
  catches general exceptions by terminating that stream loop; `ProcessExited`
  is unguarded in the `Process.Exited` callback (`PipeModeSession.cs:163-168`).
- `TelnetSession.cs:326` invokes `DataReceived`; a subscriber exception is
  caught as a generic read-loop failure, then `ProcessExited` is invoked
  unguarded at `TelnetSession.cs:174`.

**Trigger**: a view or test subscriber throws while handling output or exit.
For ConPTY, output stops and the task faults. For pipe mode and Telnet, one bad
subscriber can terminate the read loop or escape the process/event callback.

**Direction**: centralize event dispatch behind `SafeInvokeDataReceived` /
`SafeInvokeProcessExited`, log with `FileLogger`, and never let subscriber
exceptions cross native/process/IO loop boundaries.

### P2-4 - WebView2 terminal trust boundary is fail-open for `data:` and unknown messages

`EmbeddedSshView.OnWebViewNavigationStarting` allows any `about:` or `data:`
navigation (`EmbeddedSshView.xaml.cs:898-908`). `OnWebMessageReceived` then
accepts messages whose `args.Source` starts with `about:` or `data:`
(`EmbeddedSshView.xaml.cs:911-922`). Finally, any non-empty message that does
not match a known prefix falls through to `WriteToSession(message)` at
`EmbeddedSshView.xaml.cs:1071`.

`terminal.html` only posts known prefixed messages today (`terminal.html:453-461`), but the host guard is broader than the actual page contract.

**Trigger**: any future bug, injected client-side script, or accidental
navigation to a `data:` document with `window.chrome.webview` access posts
`input:<base64>` or an unknown raw string. The host accepts it and can write to
the active shell.

**Direction**: after the initial `NavigateToString`, reject all top-level
navigation except the known inline origin, reject `data:` message sources, and
drop/log unknown prefixes instead of writing them to the terminal.

### P2-5 - `ConPtySession.StartAsync` leaks parent pipe handles on early setup failure

`ConPtySession.StartAsync` creates four pipe handles at `ConPtySession.cs:109-110`, then immediately stores only the child-side handles (`_pipeInputRead`
and `_pipeOutputWrite`) at lines 112-114. The parent-side handles (`inputWrite`
and `outputRead`) are not owned by fields until `FileStream` construction at
lines 121-124. If `CreatePseudoConsole` or `SetupProcessAttributeList` throws
first (`ConPtySession.cs:118-119`), the catch calls `Dispose()` (`ConPtySession.cs:129-132`), but `Dispose()` has no reference to those two parent handles.

**Trigger**: ConPTY allocation fails, attribute-list allocation fails, or
`UpdateProcThreadAttribute` fails after pipe creation. Two `SafeFileHandle`
instances are left to finalization instead of deterministic disposal.

**Direction**: assign all four handles to owned fields before any throwing
operation, or wrap the local parent handles in `using` / `try/finally` until
`FileStream` takes ownership.

### P2-6 - Failed terminal start/connect paths leave sessions poisoned

`PipeModeSession.StartAsync` allocates `_cts` (`PipeModeSession.cs:48`), assigns
`_process` (`PipeModeSession.cs:73`), subscribes `Exited` (`PipeModeSession.cs:74`), then calls `Start()` at line 76 with no cleanup on exception. A failed
`Start()` or a later exception before return leaves `_process` non-null, so a
retry hits "Session already started" (`PipeModeSession.cs:45-46`).

`TelnetSession.StartAsync` assigns `_cts` and `_client` before awaiting
`ConnectAsync` (`TelnetSession.cs:81-87`). A DNS/connect timeout leaves `_client`
non-null and the session cannot be retried (`TelnetSession.cs:76-77`) unless the
caller disposes it.

Handlers do dispose on their own failure paths (`TelnetHandler.cs:77-80`,
`LocalShellHandler.cs:147-156`, `WinRmHandler.cs:230-231`), but the session
implementations themselves do not uphold a clean post-failure state.

**Direction**: wrap `StartAsync` bodies in `try/catch`, detach events, dispose
allocated resources, reset fields, and rethrow.

### P2-7 - SmartPasteGuard is Unix-centric while the terminal now runs Windows shells

`SmartPasteGuard.DangerousPatterns` covers Unix/destructive patterns and a few
generic commands (`SmartPasteGuard.cs:48-66`). It does not catch common
Windows/PowerShell destructive single-line pastes such as:

- `Remove-Item -Recurse -Force C:\Important`
- `Stop-Computer` / `Restart-Computer`
- `Format-Volume`, `Clear-Disk`, `Remove-Partition`
- `reg delete ... /f`, `bcdedit`, `diskpart /s ...`

The shared terminal view uses the guard for clipboard paste
(`EmbeddedSshView.xaml.cs:1027-1060`) across SSH, Local Shell, WinRM, and
Telnet. Current tests are also Unix-focused (`SmartPasteGuardTests.cs:39-148`).

**Trigger**: a user pastes a one-line destructive PowerShell command into a
Local Shell or WinRM session. It is classified `Safe` and is sent without the
warning dialog.

**Direction**: add Windows/PowerShell pattern coverage and tests. Keep the guard
conservative: a warning dialog is acceptable for high-risk single-line commands.

### P2-8 - Risky terminal implementations have no dedicated tests

Inventory confirms no dedicated tests for:

- `ConPtySession`
- `PipeModeSession`
- `TelnetSession`

The only terminal-adjacent tests are `TerminalAssetsLoaderTests` and
`SmartPasteGuardTests`, plus WinRM launch/preflight/bootstrap tests. The `rg`
hit for "TelnetSession" in tests is `MobaXtermImporterTests.Parse_TelnetSession`
and does not exercise `src/Heimdall.Terminal/TelnetSession.cs`.

**Risk**: the exact defects above - disposal kill ordering, split Telnet IAC
frames, partial start cleanup, event callback guards - are testable but
currently unpinned.

**Direction**: add process/socket-free seams where needed, but do not invent a
large refactor solely for testing. Small injectable factories for process,
stream, or TCP client ownership would be enough.

---

## P3 findings

### Lifecycle, cancellation, and resize hardening

- **D1** - `ITerminalSession.StartAsync` has no `CancellationToken`
  (`ITerminalSession.cs:56`). `LocalShellHandler.ConnectAsync` receives `ct`
  but does not observe it after validation (`LocalShellHandler.cs:42-46`),
  and `TelnetHandler.ConnectAsync` also does not pass `ct` into the session
  (`TelnetHandler.cs:45-75`). This is acceptable for fast local launches, but
  it is inconsistent with WinRM preflight/tunnel cancellation.
- **D2** - ConPTY dimensions are only checked for `> 0` in `Resize`
  (`ConPtySession.cs:169-180`) and not checked before startup
  (`ConPtySession.cs:268-272`). Values above `short.MaxValue` wrap when cast to
  `short`. The host parser accepts any positive `int` (`EmbeddedSshView.xaml.cs:1829-1843`).
- **D3** - `_autoReconnectTimer` posts with raw `Dispatcher.BeginInvoke` from a
  thread-pool timer (`EmbeddedSshView.xaml.cs:766-768`). Other paths use
  `BeginInvokeIfAvailable` (`EmbeddedSshView.xaml.cs:1646-1661`). A shutdown
  race can still throw after the timer observes a disposing view.
- **D4** - `ResizeSession` logs repeated resize failures one-by-one
  (`EmbeddedSshView.xaml.cs:1204-1226`). If a hostile or broken renderer posts a
  burst of invalid large sizes, logs can be noisy. Clamp and dedupe.

### Encoding and transcript fidelity

- **D5** - The interactive path is binary-safe (`terminal.html:167-182`,
  `EmbeddedSshView.xaml.cs:1112-1122`), but transcript and macro paths decode
  arbitrary chunks independently with `Encoding.UTF8.GetString`
  (`EmbeddedSshView.xaml.cs:1236-1245`, `1368-1375`, `1444-1445`). A multi-byte
  sequence split across chunks records replacement characters.
- **D6** - `PipeModeSession` and `TelnetSession` write string input using
  `Encoding.UTF8.GetBytes` (`PipeModeSession.cs:104-108`,
  `TelnetSession.cs:106-110`). That is correct for xterm input, but the
  contract should stay byte-first; avoid future string-only call sites for
  escape sequences or binary payloads.

### Credential and command-line hygiene

- **D7** - WinRM credential bootstrap deletion swallows `IOException` and
  `UnauthorizedAccessException` silently (`WinRmCredentialBootstrap.cs:87-104`).
  The script self-deletes before decrypting (`WinRmCredentialBootstrap.cs:136-145`), and the host deletes on failure (`WinRmHandler.cs:230-231`), so
  this is not a plaintext leak. It should still log stale bootstrap files.
- **D8** - The WinRM bootstrap still materializes the stored password as a
  managed `string` (`WinRmCredentialBootstrap.cs:66-83`) before re-protecting it
  as a DPAPI bootstrap blob. Existing tests prove it is not written to disk
  (`WinRmCredentialBootstrapTests.cs:27-61`), but the memory exposure is longer
  than the `SecureString` path available in `DpapiProvider`.
- **D9** - `LocalShellHandler` logs full executable + arguments for local and
  elevated shells (`LocalShellHandler.cs:107-108`, `126`, `178-179`). Local
  shell arguments are user-controlled and may include ad-hoc secrets. Log the
  executable and mode, but consider redacting arguments.
- **D10** - LocalShell PowerShell detection uses substring matching over the
  executable path (`LocalShellHandler.cs:56-57`). A path containing
  "powershell" in a directory name can receive PowerShell-specific arguments.
  Use `Path.GetFileNameWithoutExtension`.

### WebView2 / asset hardening

- **D11** - `TerminalAssetsLoader.CreateLazyAsset` is `internal`, test-visible,
  and accepts arbitrary relative paths (`TerminalAssetsLoader.cs:59-78`). Current
  production callers use constants only, but the helper should still reject
  rooted paths and `..` traversal before `Path.Combine`.
- **D12** - `TerminalAssetsLoader` XML docs still describe the assets as
  "embedded SSH terminal" assets (`TerminalAssetsLoader.cs:22-25`), but the
  same loader is used for Local Shell, Telnet, and WinRM through
  `EmbeddedSshView` / `EmbeddedSessionManager` (`EmbeddedSessionManager.cs:391-412`, `EmbeddedSessionManager.cs:700-724`). Rename the docs to "embedded terminal".
- **D13** - `terminal.html` has hardcoded English UI strings for loading and
  search (`terminal.html:136-141`). The outer WPF view is localized; the
  renderer shell is not.
- **D14** - CSP requires `script-src 'unsafe-inline'` because assets are inlined
  for `NavigateToString` (`terminal.html:5-6`, `EmbeddedSshView.xaml.cs:1846-1894`). That is acceptable for an offline page, but keep all injected
  placeholders constrained to known constants or sanitized values.

### Async and event handling

- **D15** - `BroadcastInput?.Invoke(bytes)` is inside the input path but only
  `FormatException` is caught around the message decode (`EmbeddedSshView.xaml.cs:989-1005`). A broadcast subscriber exception escapes the WebView2 message
  handler on the UI thread.
- **D16** - Several dispatcher posts are still raw and unguarded:
  `OnDisconnected` (`EmbeddedSshView.xaml.cs:1125-1174`),
  `OnSessionSecurityEvent` (`EmbeddedSshView.xaml.cs:1176-1197`), and
  `OnHealthDataReceived` (`EmbeddedSshView.xaml.cs:644-665`). Use the local
  `BeginInvokeIfAvailable` helper consistently.

### Telnet and test polish

- **D17** - `TelnetSession.IsRunning` uses `TcpClient.Connected`
  (`TelnetSession.cs:54`), which is a stale last-operation value. The read loop
  eventually detects disconnect, but status can be optimistic until then.
- **D18** - `SmartPasteGuardTests.GetDangerousPatterns_Returns16Items`
  (`SmartPasteGuardTests.cs:94-100`) will fail for any legitimate new dangerous
  pattern. Assert required labels, not the exact count.
- **D19** - `tests/Heimdall.Ssh.Tests/SmartPasteGuardTests.cs` tests
  `Heimdall.Terminal.SmartPasteGuard`; the file is terminal-level, not SSH-only.
  Move tests to a terminal/core-appropriate project when session tests are
  added.

---

## Verified strengths / dismissed false positives

- **WinRM bootstrap is not plaintext-on-disk**: tests and code agree. The stored
  password is unprotected in memory, immediately re-protected as a DPAPI blob,
  and the script contains `$blob = '<dpapi>'`, not the password
  (`WinRmCredentialBootstrap.cs:66-79`, `WinRmCredentialBootstrapTests.cs:27-61`).
- **WinRM command injection is well-contained**: host and endpoint validation
  reject invalid names (`WinRmPowerShellLaunchBuilder.cs:199-236`), PowerShell
  literals double single quotes (`WinRmPowerShellLaunchBuilder.cs:137-141`),
  and command-line arguments escape quotes/trailing slashes
  (`WinRmPowerShellLaunchBuilder.cs:143-178`).
- **The primary xterm data plane is binary-safe**: renderer input/output travels
  as UTF-8 bytes wrapped in base64 (`terminal.html:167-185`, `330-335`,
  `416-421`; `EmbeddedSshView.xaml.cs:989-1000`, `1112-1122`).
- **Successful ConPTY teardown uses the right primitives**: `HPCON` is wrapped
  in `SafePseudoConsoleHandle` (`SafePseudoConsoleHandle.cs:26-43`), and the
  success-path dispose closes pseudo console, streams, child-side handles,
  process/thread handles, and the attribute list (`ConPtySession.cs:199-240`,
  `432-463`).

---

## Remediation backlog - pair-architect chunks

| Chunk | Content | Notes |
|---|---|---|
| **A** | Lifecycle fixes: P2-1, P2-5, P2-6, plus minimal tests for dispose/start-failure cleanup | Highest risk; no UI required |
| **B** | WebView2 trust boundary and event guard fixes: P2-3, P2-4, D3, D15, D16 | Keep changes scoped to `EmbeddedSshView` |
| **C** | Telnet parser state machine: P2-2, D17, byte-by-byte negotiation tests | Pure terminal behavior |
| **D** | SmartPasteGuard Windows coverage: P2-7, D18, D19 | Add PowerShell/Windows destructive patterns and move/adjust tests |
| **E** | P3 hardening/docs: D1, D2, D5-D14 | Low-risk polish after P2s |
| **F** | Dedicated terminal test suite: P2-8 across ConPTY/Pipe/Telnet seams | May require small injectable process/TCP seams |

Suggested order: **A -> B -> C -> D -> F -> E**. Chunk A removes lifecycle
uncertainty first; Chunk B closes the only clear host/page trust-boundary gap;
Chunk C is independent and testable; Chunk D aligns SmartPasteGuard with WinRM
and Local Shell; Chunk F locks the layer before lower-priority polish.
