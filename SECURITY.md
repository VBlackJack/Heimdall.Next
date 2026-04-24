# Security Notes

This document records known security considerations, limitations, and deliberate
defense-in-depth decisions in Heimdall.Next.

## Reporting a vulnerability

Report suspected vulnerabilities privately to the maintainer. This repository
does not currently publish a dedicated security email address; use the private
channel through which you obtained the source, or see `LICENSE` for maintainer
and licensing context. Do not file public issues for security problems.

## Threat model scope

Heimdall.Next is a single-user desktop application that stores SSH and RDP
credentials locally using DPAPI plus HMAC-SHA256, and manages outbound
connections. It assumes:

- The local Windows user account is trusted. Malware running as the same user
  can observe the app's memory.
- The local disk is trusted. DPAPI-encrypted secrets are bound to the user
  profile.
- The network is untrusted end to end. TOFU host key pinning is the primary
  defense against MITM.

Out of scope: multi-user shared installations, secure boot chain, and supply
chain attacks on SSH.NET or WebView2. Track dependency exposure with
`dotnet list package --vulnerable`.

## Known limitations

### Credential lifetime in managed memory

`System.String` is immutable and lives on the GC heap. Plaintext credentials
passed to:

- `IMsTscNonScriptable.put_ClearTextPassword` for RDP,
- `PasswordAuthenticationMethod` and `KeyboardInteractiveAuthenticationMethod`
  for SSH.NET,
- `CredentialAutofill.InjectPassword` through `WM_SETTEXT`,

are briefly held as `string` instances before being passed to native code. We
zero the owning `char[]` buffers where possible and null out field references
after handoff, but the GC may retain copies until the next Gen2 collection.
`SecureString` does not provide stronger guarantees on modern Windows.

Mitigation: lock the workstation when not in use. Attackers with local memory
read primitives can scrape credentials from desktop SSH and RDP clients,
including this one.

### Pageant shared-memory DACL

`PageantClient.SendMessage` creates a named file mapping using the default DACL
of the process token. This restricts access to the current Windows user, which
is sufficient for the stated threat model. For environments requiring explicit
SACLs or same-user malware isolation, update the mapping creation in
`src/Heimdall.Ssh/Pageant/PageantClient.cs` to supply an explicit
`SECURITY_ATTRIBUTES` structure.

### CredentialAutofill visibility

`CredentialAutofill` logs Win32 window handles, titles, PIDs, and process names
at `Info` level for troubleshooting. Window titles may contain host names, such
as `Enter credentials for server01.corp.local`. Deployments with logging review
obligations, including GDPR or HIPAA environments, should lower logger
verbosity for `CredentialAutofill` or redirect the category to a separate sink.

### TunnelManager port allocation race

`TunnelManager.GetEphemeralPort` and `TunnelManager.AllocatePort` bind an OS
ephemeral port, read its number, release it, then return it. Between release
and the actual tunnel bind, another process can claim the same port. Three
mitigations are in place:

1. A double-check in `OpenTunnelAsync` and `OpenChainedTunnelAsync`
   re-validates `IsPortTracked(localPort)` under `_registryLock` and disposes
   the session on collision.
2. `StartForwardedPortWithRetry` wraps the actual `ForwardedPortLocal.Start()`
   and `ForwardedPortDynamic.Start()` calls with a bounded retry (3 attempts,
   50 ms spacing) on `SocketException(AddressAlreadyInUse)` only. Unrelated
   socket errors and non-socket exceptions propagate immediately with no
   retry. This closes the common case where another local process held the
   port transiently.
3. Chained-tunnel intermediate local ports receive the same retry treatment.
   `ForwardedPortRemote.Start()` does not (server-side bind, different race
   surface).

Callers may still observe `SshFailureCode.PortInUse` when the port is
genuinely occupied; retry is safe at any layer.

### SSH host-key trust model

Host-key trust decisions are resolved **before** the real `Connect()` via a
pre-authentication probe (`SshConnectionFactory.ProbeHostKeyAsync` with
`NoneAuthenticationMethod`). The real connection then uses a strict,
synchronous `PinnedFingerprintVerifier` that only accepts the pre-resolved
fingerprint. SSH.NET's `HostKeyReceived` callback performs no async work, no
UI dispatch, and no `IHostKeyVerifier.VerifyAsync` call from inside it — this
invariant has a dedicated regression test
(`IHostKeyVerifierIntegrationTests.AttachHostKeyVerification_RejectsInteractiveVerifierSynchronously`).

Production runtime paths **fail closed** when a `HostKeyStore` is provided
without an `IHostKeyVerifier`. `AutoAcceptHostKeyVerifier.Instance` is only
reachable via explicit test injection; it has zero occurrences in `src/`
outside its defining file. `ToolGatewayConnector` refuses to route tool
traffic through a gateway that has no pinned fingerprint yet; the user must
complete a normal interactive SSH session first so the host key is captured
into `HostKeyStore` via the confirmed-trust path.

Trust entries carry metadata (`FirstSeen`, `LastSeen`, `Algorithm`, `Source`,
`PublicKeyBase64`) via `HostKeyEntry`. Persistence is additive:
`trustedHostKeysV2` in `settings.json` holds the enriched entries; the
legacy `trustedHostKeys` string dictionary remains readable for downgrade
safety and is never rewritten from the V2 path.

`~/.ssh/known_hosts` import and export are explicit user actions surfaced in
`Settings > SSH & SFTP > Trusted host keys`. Import preserves conflicting
existing entries unless the user explicitly opts into replacement in a
dedicated modal. Export preserves every line Heimdall did not originate
(including `@cert-authority`, `@revoked`, and hashed entries that Heimdall
cannot fully consume) verbatim.

### SSH agent identity enumeration

`ISshAgent` implementations (`PageantAgent`, `OpenSshPipeAgent`) never hold
IPC handles across requests. Every `GetIdentities` and `Sign` call opens a
new shared-memory mapping (Pageant) or named-pipe connection (OpenSSH Agent)
and disposes it before returning. Availability probes have a 250 ms timeout;
real requests have a 5 s timeout. Pipe-not-found and timeout both return
"unavailable" without raising. User preference between agents is a runtime
setting (`AppSettings.SshAgentPreference`); changes take effect on the next
connection attempt without app restart.

## Security testing

- Unit tests for TOFU verification:
  `tests/Heimdall.Ssh.Tests/HostKeyStoreTests.cs` and
  `tests/Heimdall.Ssh.Tests/IHostKeyVerifierIntegrationTests.cs`, including
  an anti-deadlock regression test that runs the host-key callback under a
  single-threaded `SynchronizationContext` with a slow verifier and asserts
  the callback returns under 50 ms.
- Trust service orchestration and known_hosts round-trip:
  `tests/Heimdall.Ssh.Tests/KnownHostsImportExportTests.cs`.
- SSH agent protocol and IPC: `tests/Heimdall.Ssh.Tests/OpenSshAgentProtocolTests.cs`
  (pure protocol encoding/decoding) and
  `tests/Heimdall.Ssh.Tests/OpenSshPipeAgentTests.cs` (named-pipe transport
  using a GUID-suffixed test pipe, independent of the real Windows OpenSSH
  Agent service).
- Local bind retry helper:
  `tests/Heimdall.Ssh.Tests/TunnelManagerStartRetryTests.cs`, including a
  test that holds a real TCP port via `Socket.Bind` and confirms the retry
  helper still fails closed with `AddressAlreadyInUse`.
- `TunnelManager` characterization tests (pre-refactor behaviour capture,
  still passing on the refactored code):
  `tests/Heimdall.Ssh.Tests/TunnelManagerTests.cs`.
- Shell-injection regression tests: `InputValidator` coverage in
  `tests/Heimdall.Core.Tests`.
- RDP file generation sanitization:
  `tests/Heimdall.Ssh.Tests/RdpFileGeneratorTests.cs`.
- CI enforces: build with zero warnings under `TreatWarningsAsErrors`,
  `dotnet format --verify-no-changes`, full test suite, JSON locale parity
  (EN and FR key sets must be identical, currently 5,185 keys each), and an
  informational `dotnet list package --vulnerable` scan.
- Dependency scan for manual review: `dotnet list Heimdall.slnx package
  --vulnerable --include-transitive`. CI emits warnings but does not gate on
  vulnerability results, since advisories occasionally include false
  positives or entries without an upgrade path.
