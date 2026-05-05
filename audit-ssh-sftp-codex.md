# Heimdall.Next — SSH / SFTP Audit (Codex pass)

> Codex's cross-check pass. Reproduced verbatim for the consolidation step.
> Note from Codex: this was not a blind pass — Codex had read Claude's audit before writing this file.

## Critical / High

### C1 - Host-key verification is optional in several runtime APIs
Confirmed.

The main app DI path usually passes `HostKeyStore` and `IHostKeyVerifier`, so the primary SFTP handler is not currently the weakest point. The issue is the API contract: multiple SSH/SFTP/sudo/tunnel code paths accept `null` host-key dependencies and then create `SshClient`/`SftpClient` without attaching pinned verification.

Examples:
- `RemoteFileEditor.EditFileSudoAsync`
- `EmbeddedSftpViewModel.CreateSudoSshClientAsync`
- `SftpBrowser.ConnectAsync`
- `SshShellSession.ConnectAsync`
- `TunnelManager.OpenTunnelAsync`

Recommendation: make host-key verification dependencies mandatory for production constructors/entry points. If tests or legacy callers need bypass behavior, require an explicit `UnsafeNoHostKeyVerification` opt-in type/name.

### C2 - RemoteFileEditor re-resolves host key on every sudo auto-upload
Confirmed.

`RemoteFileEditor` resolves trust when opening the sudo edit session, but does not store the resulting `PinnedFingerprintVerifier` in the edit session. Each upload re-runs host-key resolution before opening fresh SFTP/SSH clients.

Recommendation: resolve once per edit session, store the pinned verifier in `EditSession`, and reuse it for all subsequent upload clients.

### C3 - PageantHostAlgorithm.Sign XML comment contradicts implementation
Confirmed. The code returns the full SSH signature blob, which matches the project contract. The XML doc says the opposite.

Recommendation: fix the XML comment and add/keep a test asserting full-blob behavior.

### C4 - Host-key/security failures are collapsed into plain disconnect strings
Confirmed.

`SftpBrowser.OnErrorOccurred` and `SshShellSession.ReadLoopAsync` reduce typed failures to `Disconnected?.Invoke(ex.Message)`. That prevents the UI from distinguishing MITM/host-key rejection from network loss.

Recommendation: add a typed security/error event or diagnostic model. Host-key rejection should surface as a distinct UI state.

### H1 - EmbeddedSftpViewModel permission-denied heuristic is too broad
Confirmed.

`IsPermissionDenied` can classify generic SSH/SFTP "Failure" text as permission denied. Since this is used to trigger sudo fallbacks, false positives are risky.

Recommendation: use typed SSH.NET exceptions/status codes where possible. Avoid broad substring fallback for operations that can escalate to sudo.

### H2 - RemoteFileEditor default editor launch uses shell association
Confirmed.

The default path launches the local temp file with `UseShellExecute=true`, so Windows file associations may open privileged remote content in an unexpected editor.

Recommendation: launch explicit `notepad.exe` with `UseShellExecute=false` and argument list handling.

### H3 - Chained tunnel host-key probing doubles SSH handshakes
Confirmed, with nuance.

The none-auth probe is protocol-reasonable, but it doubles handshakes per hop and can create SIEM noise or hit aggressive `MaxStartups` limits.

Recommendation: keep the behavior only if needed by SSH.NET constraints, document it, and consider a stricter/smarter single-connect path if available.

## Additional Codex Findings

### A1 - Plink fallback can proceed without `-hostkey`
If `PlinkHostKeyProbe.ProbeAsync` returns `null` and Heimdall has no stored fingerprint, both SSH terminal fallback and Plink tunnel fallback can continue without passing `-hostkey` to Plink. This delegates trust to PuTTY/Plink cache or interactive behavior instead of Heimdall's TOFU store.

Recommendation: fail closed when no Heimdall fingerprint can be obtained, or use SSH.NET host-key probing to obtain the fingerprint before starting Plink.

### A2 - Tunnel reuse key ignores gateway / chain identity
`TunnelService` reuses an alive tunnel by matching only `RemoteHost` and `RemotePort`. That is unsafe with overlapping private networks: two different gateways can expose the same internal `10.x` / `172.16.x` / `192.168.x` target, and Heimdall may reuse the wrong tunnel.

Recommendation: include gateway host, port, user/credential identity, chain identity, and forwarding mode in the reuse key.

### A3 - KnownHostsImporter issue is app-side full-file read, not parser-side
The core parser already has line-size and file-size protections, and the core import path streams. The app importer still uses full-file read before parsing.

Recommendation: app importer should stream via `StreamReader` and enforce the same file-size cap.

### A4 - FTP has broader risk than just deprecated FtpWebRequest
`FtpBrowser` uses obsolete `FtpWebRequest`, has weak/no test coverage, and the app handler does limited host/port validation. Cleartext FTP credentials are logged only as a warning, not surfaced clearly to users.

Recommendation: short term: add validation, tests, and UI warning. Medium term: migrate to a maintained FTP/FTPS library such as FluentFTP.

### A5 - RemoteFileEditor fire-and-forget upload needs ownership
`OnFileChanged` fires `_ = OnFileChangedAsync(session)`. Exceptions can become unobserved, cancellation on close is weak, and security failures are not surfaced cleanly.

Recommendation: track the active upload task and cancellation token per edit session; serialize saves; surface typed failures.

## Disagreements / Nuances

- C1 is not a confirmed exploit in the main DI SFTP path. The current app path normally supplies the store/verifier. Still fix it as high priority because the API default is unsafe.
- Split H6: parser/core import are already bounded; app importer is the remaining issue.
- No concrete `WireFinalForwardedPorts` race beyond normal local-port check/use behavior. The tunnel reuse key is the more concrete routing/security issue.
- Password retention inside SSH.NET auth method objects is partly unavoidable. Reduce closure lifetime and document, but rank below host-key and sudo issues.

## Codex Proposed Prioritized Plan

### P0 / P1 Security
1. Make host-key verification mandatory or require explicit unsafe opt-in.
2. Make Plink fallback fail closed if no Heimdall-pinned fingerprint is available.
3. Fix tunnel reuse identity to include gateway/chain context.
4. Cache pinned verifier per sudo edit session.
5. Replace sudo permission-denied heuristic with typed classification.

### P1 Robustness / UX
6. Add typed host-key/security error propagation to SSH/SFTP UI paths.
7. Fix sudo temp cleanup in `EmbeddedSftpViewModel.UploadViaSudoAsync`.
8. Fix `RemoteFileEditor` editor launch.
9. Stream app known_hosts import with caps.
10. Own/cancel/serialize `RemoteFileEditor` upload tasks.

### P2 Debt
11. Fix Pageant XML docs and add explicit regression test.
12. Add FTP tests, validation, and cleartext warning; plan library migration.
13. Improve Plink log redaction.
14. Review `SshShellSession.StopReadLoop` lifecycle.
15. Mark legacy `HostKeyStore.Verify(byte[])` obsolete or change its contract so first-use cannot be misread as trusted.
