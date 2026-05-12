# Heimdall.Next — SSH / SFTP Audit (Claude pass)

**Scope:** `src/Heimdall.Ssh/`, `src/Heimdall.Sftp/`, `src/Heimdall.Core/Ssh/`, the SSH/SFTP/FTP handlers in `src/Heimdall.App/Services/Handlers/`, `EmbeddedSftpViewModel`, `RemoteFileEditor`, plus the matching test projects.

**Method:** read-only static audit, file-by-file, cross-referenced with `CLAUDE.md` invariants and the existing test surface (~422 test methods across 22 SSH-side test files).

**Findings are graded:** Critical (security or data-integrity), High (reliability), Medium (correctness / hardening), Low (polish), Notes.

---

## CRITICAL

### C1. Sudo SSH paths can connect with **no host key verification at all** when `_hostKeyStore` is null
**Files:**
- `src/Heimdall.Sftp/RemoteFileEditor.cs` `EditFileSudoAsync` (130-211) and `UploadWithSudoAsync` (395-484)
- `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs` `CreateSudoSshClientAsync` (450-491)

In all three sites the pattern is:
```csharp
PinnedFingerprintVerifier? pinnedVerifier = null;
if (_hostKeyStore is not null) { ... resolve pinned verifier ... }

var connectionInfo = SshConnectionFactory.Create(...);
using var sshClient = new SshClient(connectionInfo);
if (pinnedVerifier is not null) { AttachPinnedHostKeyVerification(...); }
sshClient.Connect();
```
`SshConnectionFactory.Create` does **not** wire a host-key callback by itself. So when callers pass `hostKeyStore: null` (legal per the public ctor signature) the resulting `SshClient.Connect()` accepts whatever key the server presents. This bypasses the entire TOFU model for every privileged file edit / sudo command, including the auto-upload path triggered by `FileSystemWatcher`.

**Recommendation:** make `HostKeyStore` + `IHostKeyVerifier` non-nullable required ctor / `Initialize` parameters on `RemoteFileEditor` and `EmbeddedSftpViewModel`, and throw at construction if either is null. The DI graph in `App.xaml.cs` already provides both as singletons — there is no legitimate reason to allow null.

### C2. `RemoteFileEditor` re-runs `ResolveHostKeyAsync` on **every** auto-upload and uses the result for one connection only
**File:** `src/Heimdall.Sftp/RemoteFileEditor.cs` lines 405-436 (sudo path) and lines 81-108 (non-sudo path delegates back to `IRemoteBrowser`).

Each `OnFileChangedAsync` triggers a full `ProbeHostKeyAsync → IHostKeyVerifier.VerifyAsync` round-trip, then opens fresh `SshClient` + `SftpClient` instances, then disposes them. Between the verify and the connect, the server can rotate its host key and the next save will silently re-prompt the user (or worse, silently re-trust if the verifier defaults to Accept). The user editing a file expects a stable security context for the duration of the edit session, not a fresh TOFU dance per save.

**Recommendation:** capture the pinned verifier once when the edit session opens and reuse it for every upload until `CloseEdit` is called. If the host key changes mid-session, raise a typed `HostKeyRotated` event so the UI can decide whether to abort the session.

### C3. `PageantHostAlgorithm.Sign` documentation contradicts implementation
**File:** `src/Heimdall.Ssh/Pageant/PageantHostAlgorithm.cs` lines 78-91.

The XML doc says *"so we strip the algorithm name prefix before returning"* but the code returns the full agent blob unchanged. CLAUDE.md confirms the behaviour required by SSH.NET (return full SSH blob). The comment is the bug, not the code, but it has high misleading value because anyone reading it would file a "bug" PR that breaks Pageant signing.

**Recommendation:** rewrite the comment to match the actual behaviour, with a literal byte-layout table of what SSH.NET wants vs. what the agent returns.

### C4. `SftpBrowser` and `SshShellSession` collapse a host-key mismatch raised mid-session into a plain `Disconnected` string
**Files:**
- `src/Heimdall.Sftp/SftpBrowser.cs` `OnErrorOccurred` (488-491)
- `src/Heimdall.Ssh/SshShellSession.cs` `ReadLoopAsync` catch block (261-267)

Both handlers raise `Disconnected?.Invoke(ex.Message)` without distinguishing `HostKeyRejectedException` from a benign network drop. A MITM detected after the initial handshake (e.g. on a renegotiation) is therefore indistinguishable from a TCP reset. The connection-level dialogues do nothing escalated.

**Recommendation:** introduce a typed event (or include the `SshFailureCode`) so the UI can show an MITM banner instead of a vanilla "disconnected" status.

---

## HIGH

### H1. `EmbeddedSftpViewModel.IsPermissionDenied` is heuristic-based and can silently escalate to sudo
**File:** `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs` lines 851-878.

The fallback branches `Delete`, `Rename`, `Chmod`, `CreateFolder`, `LoadDirectory` to a `sudo`-prefixed shell command whenever the helper returns true. The helper currently matches:
- exception type name containing "PermissionDenied"
- message strings "permission denied", "access denied", "not permitted", "SSH_FX_PERMISSION_DENIED"
- and the catch-all: `message.Contains("Failure") && (typeName.Contains("Sftp") || typeName.Contains("Ssh"))`

The catch-all is too broad. SSH.NET surfaces dozens of `SshException` subclasses whose message contains the literal word "Failure" (e.g. server channel failures, agent failures, key-exchange failures). Any of those can mis-trigger `sudo rm -rf` on a path the user did not intend to escalate against.

**Recommendation:** replace the heuristic with a typed check:
```csharp
ex is Renci.SshNet.Common.SftpPermissionDeniedException
   or Renci.SshNet.Common.SshPermissionDeniedException
```
Drop the substring fallback entirely. Worst case the user gets a clearer "permission denied" UX prompt to retry with sudo manually.

### H2. `RemoteFileEditor.LaunchEditor` opens privileged content via Windows file association when the editor path is the default `notepad.exe`
**File:** `src/Heimdall.Sftp/RemoteFileEditor.cs` lines 486-518.

The default branch is:
```csharp
proc = Process.Start(new ProcessStartInfo
{
    FileName = localPath,
    UseShellExecute = true
});
```
This delegates to the user's file-type association. A user who has set `.conf → VS Code` or `.yaml → custom-script.exe` will get that program, not Notepad, even though the configured editor is "notepad.exe". For root-owned files this is a privacy + integrity bug: the file content goes to whatever app is associated.

**Recommendation:** explicitly resolve `notepad.exe` from `%WINDIR%\System32\notepad.exe` and pass `localPath` via `ArgumentList` with `UseShellExecute=false`. Same code shape as the non-default branch.

### H3. `TunnelManager.OpenChainedTunnelAsync` does a TOFU probe per hop, doubling the SSH handshake count
**File:** `src/Heimdall.Ssh/TunnelManager.cs` lines 256-307 + `TunnelManager.Build.cs` `ResolvePinnedVerifierAsync`.

For each hop in the chain we:
1. Open a `noneAuth` SshClient to probe and capture the host key (one full SSH handshake).
2. Throw it away.
3. Open the real `SshClient` with the pinned verifier (second SSH handshake).

For a 3-gateway chain that's 6 inbound handshakes on a bastion that may have `MaxStartups` configured aggressively (5:30:60). Several customers have reported `MaxStartups` lockouts in the legacy product. The behaviour is correct but expensive.

**Recommendation:** keep the probe SshClient alive when the resolution result is "trusted, no prompt needed" and reuse it as the real session. The `noneAuth` method lets the handshake complete enough to receive the host key without authenticating; SSH.NET can then attach a real auth method post-banner if reusable.

If reusing the probe client is too invasive, at least document the doubled connection count so operators can size `MaxStartups` accordingly.

### H4. `SshShellSession.StopReadLoop` continues teardown after a 500 ms join timeout
**File:** `src/Heimdall.Ssh/SshShellSession.cs` lines 271-295.

After 500 ms `_readLoopTask` is set to null and `_readCts` is disposed. If the loop is still running (rare but possible during a stalled native pipe read on SSH.NET internals), the orphaned task will try to invoke `DataReceived` on a disposed session or hit `ObjectDisposedException` against the now-null `_stream`. The current `catch (Exception ex) { ... }` block then re-raises `Disconnected` — at this point the SshShellSession is half-torn-down, and the `Disconnected` consumer may try to do work on a disposed session.

**Recommendation:** keep the `_readLoopTask` reference, do `await _readLoopTask.WaitAsync(token)` as the last step of `Dispose`, and fence any post-cancel `DataReceived` invocations with a `_disposed` check at the top of the loop.

### H5. `SshConnectionFactory.AddPasswordMethods` retains the password in two delegate closures
**File:** `src/Heimdall.Ssh/SshConnectionFactory.cs` lines 548-565.

```csharp
methods.Add(new PasswordAuthenticationMethod(username, password));
var capturedPassword = password;
var kbdInteractive = new KeyboardInteractiveAuthenticationMethod(username);
kbdInteractive.AuthenticationPrompt += (_, e) => { foreach (var prompt in e.Prompts) prompt.Response = capturedPassword; };
methods.Add(kbdInteractive);
```
The plaintext sits in two strong-rooted references for the lifetime of the `ConnectionInfo`. SSH.NET requires this so it's not avoidable, but the code does not document the lifetime contract or null out `capturedPassword` after auth completes. The `SshClient.Disconnect` path does not run these methods' finalizers eagerly.

**Recommendation:** document the retention; consider clearing the captured password by replacing the delegate after first auth (SSH.NET 2024.x exposes `Authenticated` events on `BaseClient`).

### H6. `KnownHostsImporter` enforces a global file-size cap but no per-line cap
**File:** `src/Heimdall.Core/Ssh/KnownHostsParser.cs` (referenced via `KnownHostsImporter.cs` line 56).

A malformed `known_hosts` with one 50 MB line passes the file-level cap (assume the cap is 64 MB) but the line buffer in `StreamReader.ReadLine` then grows to 50 MB. Recommend a per-line limit (16 KB is the OpenSSH default).

---

## MEDIUM

### M1. `HostKeyStore.Verify(byte[])` returns `Trusted=true, FirstUse=true` for unknown hosts
**File:** `src/Heimdall.Ssh/HostKeyStore.cs` lines 50-82.

The legacy byte-array overload signals "trust on first use" with `Trusted=true`. Production code routes through `ResolvePresentedHostKeyAsync` which correctly inspects `FirstUse` and reroutes to the verifier — but a developer reading the API can easily misuse the legacy method by checking only `Trusted`.

**Recommendation:** mark the byte-array overload `[Obsolete("Use HostKeyTrustService.Verify or ResolvePresentedHostKeyAsync.")]`, or change the contract to return `Trusted=false, FirstUse=true` and force the caller to call `IHostKeyVerifier`.

### M2. `FtpBrowser` is built on the deprecated `FtpWebRequest`
**File:** `src/Heimdall.Sftp/FtpBrowser.cs`.

Known limitations:
- Synchronous I/O wrapped in `Task.Run` (no real async).
- `EnableSsl=true` issues `AUTH TLS` but does not enforce `PROT P` for the data channel — depending on server config, file payloads can transit clear-text on a "TLS-enabled" session.
- `LIST` parsing relies on regex for Unix and DOS formats; servers in non-English locales (German months, Cyrillic dates) silently return empty entries.
- `FtpBrowser` is the only `IRemoteBrowser` implementation **without unit tests** in `tests/Heimdall.Sftp.Tests` (the test directory does not exist; there is only `EmbeddedSftpViewModelTests.cs` in `Heimdall.App.Tests`).

**Recommendation:** track migration to FluentFTP (MIT) or document the limitations + add explicit warnings to the UI when FTPS is selected. At minimum, add unit tests.

### M3. `RemoteFileEditor.OnFileChangedAsync` is fire-and-forget and accepts no `CancellationToken`
**File:** `src/Heimdall.Sftp/RemoteFileEditor.cs` lines 333-393.

`_ = OnFileChangedAsync(session)` discards the task. If the user closes the edit session mid-upload, the upload continues to completion. Worse, the `HostKeyRejectedException` re-thrown at line 379 is thrown **into a fire-and-forget task** — there's no observer, so the unhandled exception lands on the `TaskScheduler.UnobservedTaskException` event and is silently logged.

**Recommendation:** track active upload tasks per session (`Task` field on `EditSession`); cancel them in `CloseEdit`/`Dispose`. Surface uncaught host-key exceptions via `FileUploaded(remotePath, false)` + a typed `HostKeyEvent`.

### M4. `OpenSshPipeAgent.SendRequest` sync-over-async
**File:** `src/Heimdall.Ssh/OpenSsh/OpenSshPipeAgent.cs` line 89: `SendRequestAsync(...).GetAwaiter().GetResult()`.

Invoked from SSH.NET's auth callback which can run on a thread-pool thread without a sync context — so deadlock risk is low — but the pattern is a code smell and compounds when nested under another sync-over-async caller. Consider making the agent contract async-only.

### M5. `PlinkTunnelRunner.SanitizeForLog` redaction misses multi-token secrets
**File:** `src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs` lines 456-503.

Regex `\b(password|passphrase|secret|token|bearer)\b\s*[:=]?\s*\S+` only consumes a single non-whitespace token. A line such as `Authorization: Bearer foo bar` redacts only `foo`. Plink stderr is not currently a vector for this format, but if the redaction is reused elsewhere it is unsafe.

**Recommendation:** anchor the redaction to end-of-line / end-of-pattern (`.+`) for the token / bearer cases. Add a unit test for the multi-token case.

### M6. `SftpBrowser.DownloadFileAsync` opens the local file with `FileMode.Create` after lock acquisition
**File:** `src/Heimdall.Sftp/SftpBrowser.cs` lines 217-249.

A local race between a malicious sibling process and the `FileStream` open could redirect the download to a symlink target. The window is short (microseconds) and Windows has no symlinks by default, but on POSIX builds (.NET on macOS/Linux supports the SFTP module) this is exploitable.

**Recommendation:** open the FileStream **before** kicking off the SFTP download, or use `FileMode.CreateNew` + retry with a randomised filename if it exists. Validate `localPath` is rooted under the user's chosen download directory upstream.

### M7. `TunnelManager` has no upper bound on simultaneous tunnels
A scripted caller (e.g. an automated network audit) could open 1000 tunnels and exhaust ephemeral ports, file descriptors, and SshClient threadpool work. Add a configurable `AppSettings.MaxConcurrentTunnels` (default 64) and reject `OpenTunnelAsync` past the cap.

### M8. `SecurityAttributesScope.BuildSelfOnlySddl` provides cross-user protection only
**File:** `src/Heimdall.Ssh/Pageant/SecurityAttributesFactory.cs` lines 112-116.

SDDL `D:P(A;;FA;;;<userSid>)` blocks other users but allows any process running as the same user to open the mapping by guessing the random name (16 hex chars = 64 bits — practically infeasible). The protection is correct as documented in the comment; just verify the doc-comment is accurate and not promising more. Suggest adding a one-line comment that says "same-user processes can already access Pageant directly via WM_COPYDATA, so a same-user DACL would not raise the bar".

### M9. `EmbeddedSftpViewModel.UploadViaSudoAsync` chains `&&` for cleanup but does not run cleanup on failure
**File:** `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs` lines 546-575.

The shell command is `cat <temp> | sudo tee -- <target> > /dev/null && sudo rm -f <temp>`. If `tee` fails (disk full, permission denied at the precise sudo-tee step), the temp file under `/tmp/.heimdall_upload_*` is **not removed**. Over time `/tmp` accumulates orphans.

**Recommendation:** either always run the `sudo rm` regardless (use `;` instead of `&&`), or add a separate cleanup pass.

---

## LOW

### L1. `RemoteFileEditor.UploadDebounceInterval` is a static settable property without thread-safety on assignment.
### L2. `SshShellSession.Resize` swallows `InvalidOperationException` and only warns — hides genuine bugs in callers that resize after disconnect.
### L3. `FtpBrowser.ParseUnixDate` "subtract a year if in the future" heuristic is off by up to 11 months for files dated `Dec 31` parsed in early January.
### L4. `PlinkTunnelRunner.WaitForPortBindAsync` hard-codes 15 attempts × 2 s = 30 s. Surface as a setting.
### L5. `ServerHealthMonitor.PollLoopAsync` relies on `top -b -n 1 | head -5`. Locales using comma decimals other than the explicit `Replace(',', '.')` parsed (covered for fr_FR, untested for ar/zh).
### L6. `TunnelInfo.Label` is trimmed on construction but the null vs. empty distinction is undocumented in xmldoc.
### L7. `HostKeyStore.GetAllTrusted()` returns a fresh `Dictionary` snapshot per call; tight-loop callers (settings UI, search) allocate. Consider an immutable cache invalidated on `HostKeyEvent`.
### L8. `SftpBrowser.DeleteDirectoryRecursive` cap of `MaxDeleteDepth = 256` is a good defence, but the call site `DeleteAsync` does not surface the `InvalidOperationException` to the user with a localized message.
### L9. License headers (Apache 2.0, "Julien Bombled", English-only) are present and consistent across every reviewed file. Compliant.

---

## NOTES

### N1. Test coverage observations
- 422 SSH-side test methods. `TunnelManagerTests` (734 LOC), `HostKeyStoreTests` (558 LOC), `SshConnectionFactoryTests` (517 LOC), `IHostKeyVerifierIntegrationTests` (361 LOC), `PageantClientTests` (439 LOC), `PlinkTunnelRunnerTests` (379 LOC) — solid coverage of the security-critical paths.
- Gaps:
  - No test for `RemoteFileEditor.UploadWithSudoAsync` with a host-key rotation between download and upload.
  - No test verifying `SftpBrowser.OnErrorOccurred` distinguishes host-key errors from network errors (currently it does not — see C4).
  - No tests for `FtpBrowser` at all.
  - No test for `EmbeddedSftpViewModel.IsPermissionDenied` covering the false-positive case (`SshException` containing "Failure").
  - No integration test for chained tunnel + host-key change on an intermediate hop.

### N2. Async hygiene
`ConfigureAwait(false)` is consistently used in `Heimdall.Ssh` and `Heimdall.Sftp` (non-UI projects). Good. UI projects (`Heimdall.App`) intentionally omit it where the await must resume on the UI thread.

### N3. Resource lifecycle
- `TunnelSession.Dispose` uses `Interlocked.Exchange` for double-dispose protection — correct.
- `SftpBrowser.Disconnect` is idempotent and detaches `ErrorOccurred` before disposing — correct.
- `RemoteFileEditor.EditSession.Dispose` disposes both the watcher and the upload semaphore — correct.
- `PlinkTunnelRunner.Stop` joins the stderr drain task with a 500 ms cap before killing the process — correct.

### N4. i18n compliance
All user-facing failure messages route through `LocalizationManager` (`SshLocalizationKeys.*` keys) with English-fallback strings inline for resilience. Compliant with `CLAUDE.md` standards.

### N5. Cancellation
All public async APIs accept `CancellationToken`. `Task.Run(..., ct)` + `cancellationToken.Register(client.Disconnect)` pattern is consistently applied. **One exception:** `RemoteFileEditor.OnFileChangedAsync` (M3 above).

### N6. Thread safety
- `TunnelManager` uses `ConcurrentDictionary` + an explicit lock for the register / check-and-add critical section — correct.
- `HostKeyStore` likewise.
- `SftpBrowser` uses `SemaphoreSlim` to serialize all SSH.NET operations (SshClient is not thread-safe) — correct.
- `FtpBrowser` does the same for `FtpWebRequest` calls — correct.

---

## PROPOSED ACTION PLAN — sorted by impact

1. **C1, C2** — Hard-require `HostKeyStore` + `IHostKeyVerifier` in `RemoteFileEditor` and `EmbeddedSftpViewModel`; cache the pinned verifier per edit session.
2. **C4, M3** — Surface typed host-key events from `SftpBrowser` / `SshShellSession` mid-session error handlers; track upload tasks in `RemoteFileEditor` so they can be cancelled and exceptions observed.
3. **H1** — Replace `IsPermissionDenied` heuristic with a typed-exception check.
4. **H2** — Resolve `notepad.exe` to its absolute path and use `ArgumentList` + `UseShellExecute=false`.
5. **C3** — Reconcile the `PageantHostAlgorithm.Sign` doc-comment with the implementation.
6. **H3, H4, H5** — Tunnel teardown / chained-tunnel handshake count / password closure lifetime.
7. **M1** — `[Obsolete]` the byte-array `HostKeyStore.Verify` overload.
8. **M2** — Add `FtpBrowser` test coverage; track FluentFTP migration.
9. **M5, M9** — Sanitization regex + sudo-tee cleanup robustness.
10. **L1-L8** — Polish, address opportunistically.

---

## QUESTIONS FOR CODEX (for cross-review)

1. Does Codex see additional MITM windows in the `Plink` fallback path inside `SshHandler.ConnectSshViaPlinkAsync` (host-key argument injection via `-hostkey`)?
2. Does Codex agree that the `noneAuth` probe in `SshConnectionFactory.ProbeHostKeyAsync` is safe when the gateway logs `none` auth attempts (e.g. some SIEMs flag this as suspicious)?
3. Does Codex think the per-session `_sessionTrustedKeys` in `HostKeyStore` should expire on its own clock, or is process-lifetime acceptable?
4. Does Codex have stronger opinions on the `FtpWebRequest` deprecation timeline — block on it, or document and ship?
5. Did Codex identify any issues in `TunnelManager.Build.cs` `WireFinalForwardedPorts` race conditions that we missed?

---

End of Claude pass.
