# SSH / SFTP Audit — Consolidated Action Plan (CLOSED)

> **Status: complete.** All P0, P1, and P2 items shipped. The two source audits remain at `audit-ssh-sftp-claude.md` (Claude pass) and `audit-ssh-sftp-codex.md` (Codex cross-check). The 12 implementation prompts are archived under `prompts/`.
>
> **Final test baseline:** 5,453 passing / 6 skipped / 0 failing (versus 5,030 / 6 / 0 pre-audit). Net delta: +423 tests across the audit phase, build green, zero new warnings.

## Closed items, by phase

### P0 — Security must-fix

| # | Item | Closed by | Source files touched | Tests added |
|---|------|-----------|----------------------|-------------|
| 1 | **A2** — Tunnel reuse identity key (gateway-aware + forwarding-mode-aware) | [Prompt 1](prompts/01-tunnel-reuse-identity.md) | `TunnelInfo`, `TunnelManager`, `TunnelManager.Build`, `GatewayChainResolver`, `TunnelService` | `TunnelReuseIdentityTests` (13) |
| 2 | **A1** — Plink path fail-closed when no Heimdall fingerprint can be resolved + new `SshFailureCode.HostKeyUnavailable` + `IPlinkHostKeyProbe` injectable | [Prompt 2](prompts/02-plink-fail-closed.md) | `SshFailureCode`, `IPlinkHostKeyProbe`, `DefaultPlinkHostKeyProbe`, `PlinkHostKeyDecider`, `TunnelService`, `SshHandler`, locales | `PlinkFailClosedTests` (12) |
| 3 | **C1** — Compile-time non-null host-key dependencies on five entry points + `RejectingHostKeyVerifier` repositioned as fail-closed safe singleton | [Prompt 3](prompts/03-host-key-deps-non-null.md) | `SftpBrowser`, `SshShellSession`, `TunnelManager`, `RemoteFileEditor`, `EmbeddedSftpViewModel`, all production call sites | Migrated existing tests; 2 obsolete tests deleted |
| 4 | **H1** — Typed permission-denied classification (`SftpPermissionDeniedException` / `UnauthorizedAccessException`); substring heuristic eliminated | [Prompt 5](prompts/05-typed-permission-denied.md) | `EmbeddedSftpViewModel.IsPermissionDenied` | `IsPermissionDeniedTests` (9) |
| 5 | **C2** — Cached `PinnedFingerprintVerifier` per `RemoteFileEditor` sudo edit session + `HostKeyRotatedDuringUpload` typed event + UI banner | [Prompt 4](prompts/04-edit-session-verifier-cache.md) | `RemoteFileEditor`, `EditSession`, `HostKeyRotationEvent`, `EmbeddedSftpView`, locales | `RemoteFileEditorRotationTests` (5) |

### P1 — Robustness / UX

| # | Item | Closed by | Source files touched | Tests added |
|---|------|-----------|----------------------|-------------|
| 6 | **C4** — Typed mid-session security events (`SshSessionSecurityEvent` + `SshSessionFailureDispatcher`); MITM blocks SSH auto-reconnect | [Prompt 6](prompts/06-typed-security-events.md) | `SshSessionSecurityEvent`, `SshSessionFailureDispatcher`, `SftpBrowser`, `SshShellSession`, `EmbeddedSftpView`, `EmbeddedSshView`, locales | `SshSessionFailureDispatcherTests` (6) |
| 7 | **M9** — `UploadViaSudoAsync` cleanup unconditional (split `&&` chain into two `RunCommand` calls; `rm` runs in `finally`) | [Prompt 7](prompts/07-sudo-cleanup-and-editor-path.md) | `EmbeddedSftpViewModel`, `SudoUploadCommands` | `SudoUploadCommandsTests` (4) |
| 8 | **H2** — `RemoteFileEditor.LaunchEditor` resolves `notepad.exe` to `%WINDIR%\System32\notepad.exe` + `UseShellExecute=false` (no more file-association hijack) | [Prompt 7](prompts/07-sudo-cleanup-and-editor-path.md) | `RemoteFileEditor.ResolveEditorPath` | `ResolveEditorPathTests` (6) |
| 9 | **A3** — App-side `KnownHostsImporter.ParseFileAsync` streams via `StreamReader`, enforces `MaxFileSizeBytes`, surfaces typed `FileTooLarge` / `FileReadError` diagnostics | [Prompt 8](prompts/08-streaming-known-hosts-importer.md) | App `KnownHostsImporter`, `KnownHostsDiagnosticCode` | `KnownHostsImporterStreamingTests` (7) |
| 10 | **M3 / A5** — `RemoteFileEditor` upload tasks tracked per `EditSession`, cancellation propagated, exceptions observed, bare `throw;` removed | [Prompt 9](prompts/09-edit-upload-task-tracking.md) | `RemoteFileEditor`, `EditSession`, `DrainSession` | `RemoteFileEditorTaskTrackingTests` (7) |

### P2 — Tech debt

| # | Item | Closed by | Source files touched | Tests added |
|---|------|-----------|----------------------|-------------|
| 11 | **C3** — `PageantHostAlgorithm.Sign` XML doc fixed (was lying about stripping the algo prefix) + regression test pinning full-blob output | [Prompt 10](prompts/10-p2-grouped-fixes.md) | `PageantHostAlgorithm`, `PageantClient` (`SignData` made `virtual`) | `PageantHostAlgorithmTests` (1) |
| 12 | **M5** — `PlinkTunnelRunner.SanitizeForLog` redaction tightened (token / bearer values redacted to end-of-line, not just the next whitespace token) | [Prompt 10](prompts/10-p2-grouped-fixes.md) | `PlinkTunnelRunner` | `PlinkTunnelRunnerTests` (5 new) |
| 13 | **M1** — `HostKeyStore.Verify(byte[])` overload marked `[Obsolete]` with migration message; tests opt-in via targeted `#pragma warning disable CS0618` | [Prompt 10](prompts/10-p2-grouped-fixes.md) | `HostKeyStore`, `HostKeyStoreTests` | `LegacyByteOverload_FirstUse_ReturnsTrustedTrueByDesign` (1, contract-document) |
| 14 | **H4** — `SshShellSession.StopReadLoop` no longer disposes the CTS prematurely; reference held for final 2s wait inside `Dispose`; `_disposed` guards inside `ReadLoopAsync` post-`ReadAsync` | [Prompt 11](prompts/11-shell-session-teardown-hygiene.md) | `SshShellSession` | `SshShellSessionTeardownTests` (7) |
| 15 | **M2 / A4** — `FtpBrowser` parsing helpers promoted to `internal static` + tests; `FtpHandler` host/port validation; `ConnectionResult.Warning` field + cleartext FTP banner; FluentFTP migration roadmap doc | [Prompt 12](prompts/12-ftp-tests-and-cleartext-warning.md) | `FtpBrowser`, `FtpHandler`, `ConnectionResult`, `ConnectionService`, locales, `docs/audit/ftp-fluentftp-migration.md` | `FtpBrowserParsingTests` + `FtpHandlerValidationTests` (~25 with theories) |

## Notable design decisions taken in flight

- **Tunnel reuse key shape (Prompt 1).** `BuildGatewayChainKey` uses a length-prefixed encoding hashed with SHA-256 and a versioned `v1:sha256:<base64>` prefix. Naive `string.Join` would have been collision-vulnerable for gateway IDs that happen to contain the separator. Pinned by a regression test using `["foo:1", "bar"]` vs `["foo", "1|bar"]`.
- **`SshFailureCode.HostKeyUnavailable` vs `HostKeyMismatch` (Prompt 2).** Added a dedicated code for "could not resolve a fingerprint at all" rather than reusing `HostKeyMismatch`, which implies a comparison happened. Cleaner UX and cleaner SIEM output.
- **`RejectingHostKeyVerifier` is the fail-closed safe singleton (Prompt 3).** The Codex cross-check correctly pointed out it is **not** an unsafe escape hatch. Tests that need TOFU acceptance use `AutoAcceptHostKeyVerifier.Instance` instead, and that singleton is now intentionally test-only.
- **Sudo session caches the verifier; rotation between saves is a security event (Prompt 4).** A new `HostKeyRotatedDuringUpload(HostKeyRotationEvent)` event lets the UI show a banner and force-close the edit session rather than silently re-prompting per save.
- **Prompt 5 deletes the substring heuristic entirely**, no fallback. Generic SSH failures whose message happens to contain "Failure" or "denied" no longer trigger sudo escalation. Trade-off: occasional false-negatives where a permission failure does not produce a typed exception just show the user a clearer error message and let them retry manually.
- **Mid-session MITM blocks SSH auto-reconnect (Prompt 6, bonus).** Codex added this beyond the prompt: when `SecurityEventOccurred(HostKeyMismatch)` fires on the SSH terminal, the session enters `Error` state and the auto-reconnect loop is suppressed. Without that, an automatic reconnect on a MITM signal would silently re-trust the new key.
- **Sudo upload cleanup runs without the caller's CancellationToken (Prompt 7).** Otherwise an already-cancelled token would prevent the cleanup itself, defeating the whole point of "guaranteed temp removal".
- **`UploadWithSudoAsync` promoted from `private static` to `internal static` (Prompt 4).** Surface widening to make the verifier-missing invariant testable. No production caller can reach it because `EmbeddedSftpViewModel.UploadViaSudoAsync` is the only call site.
- **`ConnectionResult.Warning` is non-blocking, status-line only (Prompt 12).** Cleartext FTP shows a status-bar banner instead of a modal — the connection is still usable, the user has been told once, and the choice was made deliberately by configuring an FTP profile without TLS.
- **`PageantClient.SignData` promoted from final to `virtual`, not behind a new interface (Prompt 10).** Smallest seam that lets the Pageant test fake the agent without changing the inheritance shape.
- **`StopReadLoop` returns `bool` (Prompt 11).** True = task drained inside the 500ms grace window; false = still running, `Dispose` will retry with a 2s wait. The CTS dispose moves to `Dispose` so the loop cannot observe a freshly-disposed CTS mid-await.
- **App-side `KnownHostsImporter` on `Task.Run` wrap (Prompt 8).** The Core `Parse(TextReader)` is synchronous; wrapping in `Task.Run` keeps the public method awaitable and respects `CancellationToken` without bloating the parser API.

## Test surface contributions, prompt by prompt

| Prompt | Tests added | Key contracts pinned |
|--------|-------------|----------------------|
| 1 | 13 | Tunnel reuse identity, gateway chain key collision resistance |
| 2 | 12 | Plink fail-closed decider — every branch of the 9-row decision table |
| 3 | (migrations + 2 deletions) | Compile-time enforcement, no runtime nulls |
| 4 | 5 | Pinned verifier cached, rotation rejected, event raised |
| 5 | 9 | Typed exceptions only; `SshException("Channel failure")` no longer escalates |
| 6 | 6 | Dispatcher branches: mismatch, first-use refused, generic, null guards |
| 7 | 10 | `&&` cleanup leak fixed; notepad path resolution |
| 8 | 7 | Streaming, file-too-large rejection, IO error mapping, cancellation |
| 9 | 7 | Task tracking, cancellation, observed exceptions, drain timing |
| 10 | 7 | Pageant doc-vs-code; multi-token redaction; legacy contract documentation |
| 11 | 7 | Disposal idempotence, ObjectDisposedException on Resize/Write post-Dispose |
| 12 | ~25 | Unix LIST, DOS LIST, edge cases, ParseUnixDate rollover, validation, cleartext warning helper |

## Out-of-scope items deferred to follow-up work

These were noted during the audit but explicitly scoped out of the immediate fixes:

- **FluentFTP migration** (A4 long-term). Roadmap committed at `docs/audit/ftp-fluentftp-migration.md`; pickup gated on prioritisation.
- **`IShellStreamReader` injection seam** (H4 deeper test coverage). The `Dispose` ordering and `_disposed` guards inside `ReadLoopAsync` are pinned by contract tests; observability of "no `DataReceived` after Dispose" requires a stream injection that SSH.NET 2025.1.0 does not expose.
- **`L3` ParseUnixDate heuristic correctness for "Dec 31" parsed in early January.** Documented in the Prompt 12 test (`ParseUnixDate_FutureDateInTimeFormat_RollsBackOneYear`); a proper fix replacing the year-rollover heuristic with an explicit "treat as previous year if month > current month" rule is a separate piece of work.
- **Cross-handler validation refactor.** Each protocol handler still hand-rolls its own host validation (`IsValidSshHost`, `IsValidFtpHost`, etc.). Extracting a shared validator was deliberately out of scope to keep the audit prompts surgical.

## Reference files

- `audit-ssh-sftp-claude.md` — Claude initial pass.
- `audit-ssh-sftp-codex.md` — Codex cross-check pass.
- `prompts/01-*` through `prompts/12-*` — implementation prompts handed off to Claude Code.
- `docs/audit/ftp-fluentftp-migration.md` — follow-up roadmap.

End of consolidated plan.
