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
and the actual tunnel bind, another process can claim the same port. The race
is mitigated by a double-check in `OpenTunnelAsync` and
`OpenChainedTunnelAsync`, which re-validates `IsPortTracked(localPort)` under
the `_registryLock` and disposes the session on collision. Callers may observe
`SshFailureCode.PortInUse` on contention; retry is safe.

## Security testing

- Unit tests for TOFU verification:
  `tests/Heimdall.Ssh.Tests/HostKeyStoreTests.cs` and
  `tests/Heimdall.Ssh.Tests/IHostKeyVerifierIntegrationTests.cs`.
- Shell-injection regression tests: `InputValidator` coverage in
  `tests/Heimdall.Core.Tests`.
- RDP file generation sanitization:
  `tests/Heimdall.Ssh.Tests/RdpFileGeneratorTests.cs`.
- Dependency scan: run
  `dotnet list Heimdall.slnx package --vulnerable --include-transitive` before
  each release. CI currently does not gate on this; add a CI job if not already
  present.
