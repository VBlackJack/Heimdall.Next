# SSH / Tunnel Subsystem — Quality Audit (2026-05-24)

Targeted quality audit of the SSH/tunnel subsystem, requested after the
split-system audit closed. Pair-architect mode: supervisor = Cowork, static
analysis of the real code.

## Perimeter

In scope — the SSH/tunnel **core**:

- Project `Heimdall.Ssh` in full: tunnel (`TunnelManager` + `.Build`,
  `TunnelSession`, `TunnelInfo`, `TunnelResult`, `TunnelForwardedPortFailure`,
  `GatewayChainResolver`), connection (`SshConnectionFactory`,
  `SshConnectionParams`, `SshConnectionProbe`, `SshShellSession`,
  `ServerHealthMonitor`, `AuthPreflightChecker`), host-key trust
  (`HostKeyStore`, `HostKeyTrustService`, `KnownHostsImportExport`,
  `HostKeyRejectedException`), failure handling (`FailureClassifier`,
  `SshFailureCode`, `SshSessionFailureDispatcher`, `SshSessionSecurityEvent`),
  Plink (`PlinkTunnelRunner`), agents (Pageant, OpenSSH, registry).
- App layer: `TunnelService`, `ITunnelService`, `PlinkHostKeyDecider`,
  `PlinkHostKeyProbe`, `IPlinkHostKeyProbe`, `SshHandler`,
  `SshSessionDiagnosticFactory`, `RecentForwardedPortFailureTracker`.

Out of scope: the SSH terminal view (`EmbeddedSshView`), SSH key
generation/audit tools, OpenSSH config import. `EmbeddedSshView` was read once,
narrowly, to settle a security question (see "Investigated — not a defect").

Method: five clusters read in full (production + tests), findings cross-checked
by the supervisor against the real code. Each finding cites the producing
file + line.

## Verdict

Good overall health. The security-critical machinery is sound: the fail-closed
host-key contract holds (no PuTTY-cache fallback, `HostKeyUnavailable` returned
when no fingerprint can be resolved), the tunnel reference-count fix from the
recent leak chantier is correct and complete on all three `SshHandler` paths,
the OpenSSH agent protocol parser has rigorous buffer-bounds handling, and
`GatewayChainResolver` has thorough cycle/depth handling with matching tests.

**No P1.** **3 P2** — one real concurrency defect with crash potential, two
genuine test-coverage gaps on risky paths. The rest is P3: dead/obsolete code,
duplication, hardening opportunities, and documentation drift.

## Investigated — not a defect

A first pass flagged a possible P1 around mid-session host-key mismatch and SSH
auto-reconnect. Traced through the real code, it does **not** hold:

- `SshSessionFailureDispatcher.Dispatch` invokes the security handler before the
  disconnect handler, synchronously, on the read-loop thread.
  `EmbeddedSshView.OnSessionSecurityEvent` sets `_pendingSecurityDisconnectMessage`
  synchronously *before* either handler posts to the UI thread.
- `Dispatcher.BeginInvoke` enqueues through the dispatcher's lock-protected
  queue; the UI-thread dequeue establishes a happens-before barrier, so the
  field write is visible when `OnDisconnected` reads it. There is no
  memory-model hole.
- A mid-session host-key rejection always carries a pinned fingerprint (the key
  was verified at connect time), so `HostKeyRejectedException.IsMismatch` is
  true and the event is classified `HostKeyMismatch` — exactly the code that
  `OnDisconnected` checks to suppress auto-reconnect.

The auto-reconnect suppression after a MITM signal is correct. One soft
observation only (see C-class): the suppression hinges on a single code check;
a defence-in-depth `_securityHalt` flag would be cheap insurance, but its
absence is not a defect.

---

## P2 — Real defects

### T1 — Plink `Exited` handler captures the `_process` field, racing `Stop()`

Producer: `src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs:152-153` (event
subscription) and `:270-271` (`Stop()` — `_process.Dispose()` then
`_process = null`).

The `Exited` handler lambda references the `_process` **field**, not a captured
local: `_process.Exited += (_, _) => FileLogger.Warn($"...pid={_process?.Id}...")`.
`EnableRaisingEvents` is true, so `Exited` is raised on a thread-pool thread.
If the plink process exits at the moment `Stop()` runs, the handler can read
`_process.Id` after `_process.Dispose()` but before `_process = null` →
`ObjectDisposedException` thrown inside an event handler on a thread-pool
thread, with no `try/catch` → unhandled exception → **process crash**. The
window is narrow but real, and `Process.Dispose()` does not detach the handler.

Fix: capture the `Process` in a local before subscribing and reference the
local inside the lambda; wrap the body in `try/catch`. Optionally detach the
handler before `Dispose()`.

### T2 — Chained-tunnel intermediate-cleanup path has zero test coverage

Producer: cleanup logic in `src/Heimdall.Ssh/TunnelManager.Build.cs`
(`TunnelBuildContext.Cleanup`, reverse-order disposal of N intermediate
clients/ports) and the chain loop in `src/Heimdall.Ssh/TunnelManager.cs`
(`OpenChainedTunnelAsync`); tests in
`tests/Heimdall.Ssh.Tests/TunnelManagerTests.cs`.

The multi-hop teardown — dispose every intermediate `SshClient` and
`ForwardedPortLocal` in reverse order when an intermediate hop fails mid-chain —
is the riskiest code in the tunnel cluster, and it is entirely unexercised. The
existing `OpenChainedTunnelAsync` tests cover only empty-chain, port-in-use,
cancel-before-root-connect, and single-gateway delegation; none reaches the
loop body past the root connect. A regression that swapped disposal order or
dropped an intermediate hop would pass CI silently.

Fix: add a test injecting a 2+ hop chain whose second-hop connect fails
(unroutable loopback port), asserting no port/client leak and the expected
failure code. If a test seam is needed, decide it as an explicit architecture
change (architecture-first rule), not as a side effect.

### T3 — Plink-delegated tunnel-release path in `SshHandler` has no regression test

Producer: `src/Heimdall.App/Services/Handlers/SshHandler.cs` —
`ConnectSshViaPlinkAsync` (≈ lines 410-578), `finally` releasing the tunnel;
tests in `tests/Heimdall.App.Tests` (`SshHandlerConnectTests`).

`SshHandlerConnectTests` covers tunnel ref-count release for the embedded
SSH.NET failure path and the external-PuTTY path, but **not** the
Plink-delegated path. That method is the branch-densest release path in the
file — every early return (plink not configured, invalid host/port/key,
host-key rejection, password-prompt cancellation, `StartAsync` throwing) relies
on the `finally`/`releaseTunnel` flag — and it is exactly the path the recent
tunnel-leak chantier modified. A future edit returning before the `finally`, or
flipping `releaseTunnel` early, would silently reintroduce a tunnel leak with
no test to catch it.

Fix: add `SshHandlerConnectTests` cases that force the Plink branch and assert
`ReleaseCount == 1` on a representative early return (e.g. plink-not-configured)
and on host-key rejection.

---

## P3 — Dead / obsolete code

### D1 — Obsolete `AttachHostKeyVerification` 4-arg overload is an unreachable tombstone
`src/Heimdall.Ssh/SshConnectionFactory.cs:91-103` — `[Obsolete(..., error: true)]`
and the body unconditionally throws. It cannot be called (compile error) and
cannot run. Pure surface noise; the live 5-arg overload already carries the
migration message. Remove it.

### D2 — Dead single-arg `BuildAuthMethods` overload
`src/Heimdall.Ssh/SshConnectionFactory.cs:431-438` — no callers in `src/`; both
`Create` paths route through the two-arg builder, and the reflection-based tests
explicitly select the two-arg overload. Its XML doc also states a stale auth
ordering. Delete the overload (and the stale doc with it).

### D3 — `HostKeyStore.Verify` obsolete `byte[]` overload
`src/Heimdall.Ssh/HostKeyStore.cs:63-95` — `[Obsolete]`, returns
`Trusted: true, FirstUse: true` on first use (normal trust-on-first-use
semantics, but a caller reading only `Trusted` would trust any first-seen key).
No production caller (only tests, under `#pragma warning disable CS0618`). Make
it `internal` or delete it so the dangerous shape is not reachable.

### D4 — `PageantKeyWrapper` is a dead class
`src/Heimdall.Ssh/Pageant/PageantKeyWrapper.cs` — no production caller; only
tests and stale audit docs reference it. Its `Dispose()` only flips an unread
bool. The class doc's "kept for code that still constructs Pageant-specific key
sources directly" describes code that no longer exists. Delete the class and
its test.

### D5 — `PlinkTunnelRunnerOptions.Default` is unused public API
`src/Heimdall.Ssh/Plink/PlinkTunnelRunnerOptions.cs:45` — no production caller;
the parameterless `PlinkTunnelRunner` ctor builds a fresh options object instead.
Use `Default` in that ctor path, or drop the property.

### D6 — `StderrReadTimeoutMs` is dead config, plumbed end-to-end
`PlinkTunnelRunnerOptions.cs:34-42` (its own doc admits "Reserved for future
use"). The value is read from `AppSettings.PlinkStderrReadTimeoutMs`
(`TunnelService.cs:~362`), range-validated in `SchemaValidator`, and accepted by
the ctor — but never applied; the drain loop runs purely until cancellation. A
user-facing, schema-validated setting with zero runtime effect is misleading.
Either apply it (a per-read `CancellationTokenSource` on `ReadLineAsync`) or
remove the option, the `AppSettings` field, and its validator.

### D7 — `SshFailureCode` values never produced by `FailureClassifier`
`src/Heimdall.Ssh/SshFailureCode.cs` vs `FailureClassifier.cs` — several codes
(`SessionDisconnected`, `PortInUse`, `TunnelBroken`, `ChainDepthExceeded`,
`CircularChainDependency`, `UsernamePrompt`, `PasswordPrompt`, `KeyFileNotFound`,
`HostKeyUnavailable`) are never emitted by `FailureClassifier`. Some are produced
elsewhere (`HostKeyUnavailable` by the Plink fail-closed path, the chain codes
by `GatewayChainResolver`); confirm which are genuinely dead and prune those.

### D8 — `PageantClient` vestigial `IDisposable`
`src/Heimdall.Ssh/Pageant/PageantClient.cs:426-433` — the class owns no
long-lived handles (`SendMessage` creates and tears down the file mapping per
call); `Dispose()` only flips `_disposed`. It forces every call site
(`PageantAgent`, `PageantAgentKey`) into `using` blocks that protect nothing.
Either remove `IDisposable` (and the now-pointless `using`s/tests), or document
it as intentional speculative API.

## P3 — Code duplication

### X1 — Localized-message fallback helpers duplicated across `TunnelService` and `SshHandler`
`TunnelService.cs` and `SshHandler.cs` carry byte-identical
`BuildHostKeyMismatchMessage`, `BuildCancelledMessage`, and
`BuildHostKeyUnavailableMessage`. A future fix to the fallback text or the
key-equality guard must be made twice; drift would yield inconsistent host-key
messaging between the embedded and gateway paths. Extract a shared internal
helper.

### X2 — `IsValidSshHost` duplicated
Identical private static method in `SshHandler.cs` and `PlinkHostKeyProbe.cs`
(`ValidateDomain || IPAddress.TryParse`). Promote to one shared internal helper.

## P3 — Robustness / hardening

### H1 — `FailureClassifier` classifies by substring-matching exception text
`src/Heimdall.Ssh/FailureClassifier.cs` — `ClassifyAuthException`,
`ClassifySshException`, `ClassifyConnectionException` branch on
`msg.Contains("key" | "password" | "denied" | "refused" | "reset" | ...)`.
This is the exact anti-pattern CLAUDE.md bans for the SFTP layer ("do not
reintroduce substring matching on generic `Failure` messages"). SSH.NET messages
are English literals today, so it works, but a wording change in a future
SSH.NET release silently reclassifies failures to `Unknown`. Host-key failures
are safe (they flow through the typed `HostKeyRejectedException`, not substrings).
Prefer typed exception subclasses / properties; treat substring matching as a
documented last resort.

### H2 — `KnownHostsExporter.ExportFile` writes `known_hosts` without ACL hardening
`src/Heimdall.Ssh/KnownHostsImportExport.cs:~170-230` — the OpenSSH-format
`known_hosts` is written via plain `File.WriteAllText` + `File.Replace`, with no
ACL restriction. `known_hosts` is trust-pin-critical; the project standard for
trust/credential-adjacent files is `SecureFileWriter.WriteAndProtect()`
(current-user ACL, atomic, no TOCTOU). There is also a read→write window
(`File.ReadAllLines` then a later `WriteAtomic`) during which a concurrent
external writer is lost. Route the write through `SecureFileWriter`; accept or
close the TOCTOU window explicitly.

### H3 — `KnownHostsParser.TryParseHostToken` accepts any multi-colon token as IPv6
A host token with two or more colons is accepted verbatim as an IPv6 host with
port 22, with no `IPAddress.TryParse` validation. A malformed or crafted token
becomes a trusted-host entry (trust dilution — the bogus entry can never make a
real host *more* trusted, but it pollutes the store). Validate the multi-colon
branch and reject non-parseable tokens.

### H4 — `ITunnelService.ReleaseTunnelReference` ships an empty default body
`src/Heimdall.App/Services/ITunnelService.cs:53-55` — the interface defines
`void ReleaseTunnelReference(int localPort) { }`. Any future implementer or test
fake that forgets to implement it compiles cleanly and silently leaks every
tunnel reference — the exact bug class the recent chantier just fixed. The
sibling member `GetRecentForwardedPortFailure` is correctly abstract. Make
`ReleaseTunnelReference` a regular abstract member so omission is a compile error.

### H5 — Plink stdin is redirected but never used or closed
`src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs:~446` — `RedirectStandardInput =
true`, but nothing ever writes to or closes the stdin pipe. With `-N -batch`
plink is normally quiet, but a build that blocks on an unexpected stdin read
would hang on a live, idle pipe until the kill grace period. Set
`RedirectStandardInput = false`, or close `StandardInput` right after `Start()`
so plink sees EOF.

### H6 — `ProbeHostKeyAsync` cancellation can call `Disconnect()` concurrently with `Connect()`
`src/Heimdall.Ssh/SshConnectionFactory.cs:251-264` — the cancellation
registration runs `client.Disconnect()` while `client.Connect()` runs on the
`Task.Run` thread. SSH.NET `BaseClient` is not documented as safe for concurrent
`Connect`/`Disconnect`. Consequence is bounded (the exception is swallowed, the
`finally` and `using` dispose the client), but it is a genuine race in a
security-path method, and the same shape recurs in
`SshShellSession.ConnectAsync`. Gate the registered `Disconnect()` behind an
"after Connect returned" flag, or rely on the `finally` alone.

### H7 — Inconsistent cleanup-exception handling in `TunnelBuildContext`
`src/Heimdall.Ssh/TunnelManager.Build.cs` — `SafeDispose` catches all
exceptions, while `CleanupPort`/`CleanupClient` catch only
`ObjectDisposedException`. A non-`ObjectDisposedException` thrown by
`port.Stop()` / `client.Disconnect()` during cleanup would escape and abort the
rest of `Cleanup()`, potentially leaking later intermediate hops. Unify on one
catch-all helper.

## P3 — Documentation drift

### C1 — `PageantClient.SignData` `<returns>` contradicts the signing contract
`src/Heimdall.Ssh/Pageant/PageantClient.cs:80` — the `<returns>` says "Raw
signature bytes", but the method returns the full SSH signature blob
(`[algo_len][algo][sig_len][sig]`), as `PageantHostAlgorithm` correctly and
emphatically documents. Given the project rule "Sign() must return the full SSH
blob", a return-value comment asserting the opposite is a real hazard for a
future maintainer. Correct the `<returns>`.

### C2 — `ServerHealthMonitor` claims a "configurable interval" that does not exist
`src/Heimdall.Ssh/ServerHealthMonitor.cs:75-82` — the class doc says the monitor
polls "at a configurable interval", but the interval is a hardcoded 15-second
value, declared `static readonly int` where it should be `const`. Either drop
the "configurable" wording, or expose a real override.

### C3 — Stale `TunnelInfo.ServerName` param doc
`src/Heimdall.Ssh/TunnelInfo.cs:22` — documented as "Display name of the gateway
server", but `BuildTunnelInfo` populates it with the gateway *host* (the last
gateway's host for a chained tunnel). Reword to "Host of the (final) gateway
server."

### C4 — `HostKeyStore.ConstantTimeEquals` ASCII assumption
`src/Heimdall.Ssh/HostKeyStore.cs:~99-123` — the doc claims fingerprints are
"ASCII-only by construction", but `Encoding.ASCII.GetBytes` lossily maps any
non-ASCII char to `?`, so two different strings could encode equal — a
theoretical false-positive match. Use `Encoding.UTF8`, or stop asserting a
guarantee the code does not enforce.

## P3 — Minor

### M1 — `TunnelManager.ReleaseReference` on an untracked port does redundant work
`src/Heimdall.Ssh/TunnelManager.cs:~67-79` — for a never-seen port it inserts a
`0` entry via `AddOrUpdate`, immediately `TryRemove`s it, and calls a no-op
`CloseTunnel`. Early-return `true` when the port is not tracked.

### M2 — Import "matched" path reuses `Verify` as a side-effecting write
`src/Heimdall.Ssh/KnownHostsImportExport.cs:~116-121` — on a matched imported
entry the importer calls `HostKeyTrustService.Verify`, whose match branch
rewrites the stored `Algorithm`. This conflates "verify a live connection" with
"refresh import metadata", and the match comparison uses an ordinary
`string.Equals` while the rest of the subsystem uses `ConstantTimeEquals`
(timing is moot at import time, but the inconsistency invites copy-paste into a
live path). Add a dedicated metadata-refresh method instead of reusing `Verify`.

### M3 — `ProbeHostKeyAsync` failure message cites the transport endpoint
`src/Heimdall.Ssh/SshConnectionFactory.cs:295-296` — the "completed without
receiving a host key" message cites `connectionParams.Host:Port`, which for a
tunneled probe is the loopback transport endpoint (`127.0.0.1:localport`) rather
than the logical host. Cite `verificationHost:verificationPort` for a useful
diagnostic. (The probe itself is correct — it dials the transport endpoint and
stores trust against the logical host, as designed.)

### M4 — Intermediate forwarded ports in a chained tunnel get no `Exception` handler
`src/Heimdall.Ssh/TunnelManager.Build.cs:78-129` — `WireFinalForwardedPorts`
wires `Exception` only on `context.FinalPort`. A runtime failure of an
*intermediate* hop's forwarded port is not attributed to that hop; the failure
still surfaces (the chained `SshClient` disconnects, or the final port faults),
but the gateway-failure diagnostic is generic rather than hop-specific. Either
wire the same handler on the intermediate ports, or document that mid-chain
attribution is out of scope.

---

## Remediation backlog — pair-architect chunks

Suggested order. One chunk at a time; each chunk is one or two self-contained
Claude Code prompts ending with build + test verification.

- **Chunk A — P2 (priority).**
  - A1: T1 — fix the Plink `Exited`-handler race (capture local + `try/catch`),
    add a regression test. Real code fix, small.
  - A2: T2 + T3 — add the missing tunnel test coverage (chained-tunnel
    intermediate cleanup; Plink-delegated ref-count release). Test-only.
- **Chunk B — dead / obsolete code (D1-D8).** Low-risk, mostly mechanical
  deletions; one or two prompts.
- **Chunk C — robustness / hardening (H1-H7).** Mixed effort: H4/H5/H7 are
  small; H2 (route through `SecureFileWriter`) and H6 need care; **H1
  (`FailureClassifier`) needs its own scoping discussion** before any prompt —
  moving off substring matching is a design change.
- **Chunk D — duplication (X1, X2).** One prompt; needs a small call on where
  the shared helpers live.
- **Chunk E — doc drift + minor (C1-C4, M1-M4).** One mostly docs-only prompt.

## Notes

- The `var`-vs-explicit-type observation was deliberately excluded, consistent
  with the split-system audit decision (S11): `var` usage is a project-wide
  `.editorconfig` stance, not a subsystem defect.
- Producer-first and architecture-first rules apply: a test that needs a new
  seam (notably T2) must have that seam decided as an explicit architecture
  change, not introduced as a side effect of test coverage.
