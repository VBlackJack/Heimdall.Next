# Prompt 3 — Compile-time non-null host-key dependencies (C1)

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first.

The SSH/SFTP audit identified that several public methods accept `HostKeyStore?` and `IHostKeyVerifier?` with default `null` values. The DI graph in `App.xaml.cs` always supplies real instances at runtime, but the API contract leaves an unsafe escape hatch: if a caller forgets to pass them, the SSH client connects without TOFU verification and silently trusts whatever key the server presents. The Plink fail-closed work in Prompt 2 relied on this layer being enforced — without it, a regression elsewhere can re-open the same hole.

This prompt removes those defaults. From now on, every production entry point that opens an SSH or SFTP transport must accept its host-key dependencies as non-nullable parameters, and the now-redundant runtime guards (`if (hostKeyStore is not null) { if (hostKeyVerifier is null) throw ...; }`) get deleted. Tests that legitimately need a permissive verifier use `AutoAcceptHostKeyVerifier.Instance`; tests that need fail-closed semantics use `RejectingHostKeyVerifier.Instance`.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P0 #3 (C1) only. Prompts 1 and 2 have already shipped (`prompts/01-tunnel-reuse-identity.md`, `prompts/02-plink-fail-closed.md`).

## Goal

1. Remove the nullable defaults from these five entry points and one supporting record:
   - `SftpBrowser.ConnectAsync(...)` — `src/Heimdall.Sftp/SftpBrowser.cs`.
   - `SshShellSession.ConnectAsync(...)` — `src/Heimdall.Ssh/SshShellSession.cs`.
   - `TunnelManager.OpenTunnelAsync(...)` and `TunnelManager.OpenChainedTunnelAsync(...)` — `src/Heimdall.Ssh/TunnelManager.cs`.
   - `RemoteFileEditor` constructor — `src/Heimdall.Sftp/RemoteFileEditor.cs`. Also tighten the inner `EditSession` `init`-only properties (`HostKeyStore`, `HostKeyVerifier`).
   - `EmbeddedSftpViewModel.Initialize(...)` — `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs`. Also tighten the private fields `_hostKeyStore`, `_hostKeyVerifier`.
2. Simplify every `if (hostKeyStore is not null) { if (hostKeyVerifier is null) throw ...; pinnedVerifier = ResolveHostKeyAsync(...) }` block to a single unconditional `var pinnedVerifier = await ResolveHostKeyAsync(...)`.
3. Update every call site flagged by the compiler (production + tests). Use `RejectingHostKeyVerifier.Instance` where the test doesn't actually exercise an SSH transport (safest default), and `AutoAcceptHostKeyVerifier.Instance` only where the test needs to accept TOFU.
4. Delete the now-obsolete `TunnelManagerTests.OpenTunnelAsync_HostKeyStoreWithoutVerifier_ReturnsUnknownWithoutEvents` (and any sibling) — that contract is enforced by the type system after this prompt.
5. The build must pass with zero new warnings and the full test suite must remain green.

## Background — relevant files

Production:
- `src/Heimdall.Sftp/SftpBrowser.cs` (`ConnectAsync` ~lines 60-111, the `if (hostKeyStore is not null)` block ~73-87).
- `src/Heimdall.Ssh/SshShellSession.cs` (`ConnectAsync` ~lines 63-135, the guard block ~84-98).
- `src/Heimdall.Ssh/TunnelManager.cs` (`OpenTunnelAsync` ~lines 87-169, `OpenChainedTunnelAsync` ~lines 183-336, both forwarding to `ResolvePinnedVerifierAsync` in `TunnelManager.Build.cs`).
- `src/Heimdall.Ssh/TunnelManager.Build.cs` (`ResolvePinnedVerifierAsync` ~lines 29-55 contains the actual guard to delete).
- `src/Heimdall.Sftp/RemoteFileEditor.cs` (ctor ~lines 58-69; `EditFileSudoAsync` guard ~132-145; `UploadWithSudoAsync` guard ~405-418; `EditSession` properties ~539-542).
- `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs` (`Initialize` ~lines 149-182; `CreateSudoSshClientAsync` guard ~457-471; private fields ~43-44).

Verifier types (already in place, reuse them):
- `src/Heimdall.Core/Ssh/RejectingHostKeyVerifier.cs` — `RejectingHostKeyVerifier.Instance` is the safe fail-closed singleton.
- `src/Heimdall.Core/Ssh/AutoAcceptHostKeyVerifier.cs` — `AutoAcceptHostKeyVerifier.Instance` is the test-only TOFU acceptance singleton. Production code must not depend on it.

Production call sites that already pass concrete instances (they only need to keep working — no logic change here, just confirm the compiler is happy after the signature changes):
- `src/Heimdall.App/Services/Handlers/SftpHandler.cs` line 99 — passes `_hostKeyStore`, `_hostKeyVerifier` to `browser.ConnectAsync`.
- `src/Heimdall.App/Services/Handlers/SshHandler.cs` — passes both to `SshShellSession.ConnectAsync`.
- `src/Heimdall.App/Services/TunnelService.cs` — passes both to `OpenTunnelAsync` / `OpenChainedTunnelAsync`.
- `src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs` lines 133, 1168, 1185 — constructs `SftpBrowser` and `RemoteFileEditor`.
- `src/Heimdall.App/ViewModels/Tunnels/TunnelsViewModel.cs` lines 451, 464 — invokes `OpenTunnelAsync` / `OpenChainedTunnelAsync`.

Test files that **will need updating** (compile errors will show you exactly where):
- `tests/Heimdall.Ssh.Tests/IHostKeyVerifierIntegrationTests.cs` lines 255, 269 — already exercises host-key flow; verify it still compiles.
- `tests/Heimdall.Ssh.Tests/SshShellSessionResizeTests.cs` line 44 — likely passes nulls today.
- `tests/Heimdall.Ssh.Tests/TunnelManagerTests.cs` — multiple `OpenTunnelAsync` / `OpenChainedTunnelAsync` invocations and the `OpenTunnelAsync_HostKeyStoreWithoutVerifier_ReturnsUnknownWithoutEvents` test that becomes obsolete (line ~584).
- `tests/Heimdall.App.Tests/PlinkFailClosedTests.cs` line 230 — recent file from Prompt 2; should already pass real instances.
- `tests/Heimdall.App.Tests/SessionCoordinatorPreMountTests.cs` line 221.
- `tests/Heimdall.App.Tests/TunnelBadgeStateResolverTests.cs` (multiple `new TunnelManager()` lines).
- `tests/Heimdall.App.Tests/TunnelsViewModelTests.cs` (multiple `new TunnelManager()` lines).

Most of those tests just construct a `TunnelManager` for state-tracking purposes and never call `OpenTunnelAsync` directly, so they will not break.

## Implementation steps

### Step 1 — `SftpBrowser.ConnectAsync`

In `src/Heimdall.Sftp/SftpBrowser.cs`:

```csharp
// before
public async Task ConnectAsync(
    SshConnectionParams connectionParams,
    HostKeyStore? hostKeyStore = null,
    IHostKeyVerifier? hostKeyVerifier = null,
    CancellationToken ct = default) { ... }

// after
public async Task ConnectAsync(
    SshConnectionParams connectionParams,
    HostKeyStore hostKeyStore,
    IHostKeyVerifier hostKeyVerifier,
    CancellationToken ct = default) { ... }
```

Inside the method, replace the guarded block:

```csharp
PinnedFingerprintVerifier? pinnedVerifier = null;
if (hostKeyStore is not null) {
    if (hostKeyVerifier is null) {
        throw new InvalidOperationException("IHostKeyVerifier is required when HostKeyStore is provided.");
    }
    pinnedVerifier = await SshConnectionFactory.ResolveHostKeyAsync(
        connectionParams, hostKeyStore, hostKeyVerifier, ct).ConfigureAwait(false);
}
```

with:

```csharp
ArgumentNullException.ThrowIfNull(hostKeyStore);
ArgumentNullException.ThrowIfNull(hostKeyVerifier);

var pinnedVerifier = await SshConnectionFactory.ResolveHostKeyAsync(
    connectionParams, hostKeyStore, hostKeyVerifier, ct).ConfigureAwait(false);
```

Drop the `if (pinnedVerifier is not null) { AttachPinnedHostKeyVerification(...); }` guard around the attach call — `ResolveHostKeyAsync` always returns a non-null `PinnedFingerprintVerifier`, so the attach is unconditional now.

### Step 2 — `SshShellSession.ConnectAsync`

Same surgery in `src/Heimdall.Ssh/SshShellSession.cs`. Drop `?` and `= null`, add `ArgumentNullException.ThrowIfNull` at the top, replace the guarded block, drop the `if (pinnedVerifier is not null)` guard.

### Step 3 — `TunnelManager.OpenTunnelAsync` and `TunnelManager.OpenChainedTunnelAsync`

In `src/Heimdall.Ssh/TunnelManager.cs`:

```csharp
// before
public async Task<TunnelResult> OpenTunnelAsync(
    SshConnectionParams gatewayParams,
    string remoteHost,
    int remotePort,
    int localPort,
    CancellationToken cancellationToken = default,
    HostKeyStore? hostKeyStore = null,
    IHostKeyVerifier? verifier = null,
    int keepAliveIntervalSeconds = 30,
    int socksProxyPort = 0,
    int remoteBindPort = 0,
    int remoteLocalPort = 0,
    string? label = null,
    string? gatewayChainKey = null) { ... }
```

becomes:

```csharp
public async Task<TunnelResult> OpenTunnelAsync(
    SshConnectionParams gatewayParams,
    string remoteHost,
    int remotePort,
    int localPort,
    HostKeyStore hostKeyStore,
    IHostKeyVerifier verifier,
    CancellationToken cancellationToken = default,
    int keepAliveIntervalSeconds = 30,
    int socksProxyPort = 0,
    int remoteBindPort = 0,
    int remoteLocalPort = 0,
    string? label = null,
    string? gatewayChainKey = null) { ... }
```

The two host-key params are now required and move ahead of the optional ones (the convention C# enforces is "required parameters come before optional ones"). The same reorder applies to `OpenChainedTunnelAsync`.

This **will** force every call site — production and tests — to switch to **named arguments** if it was passing positional today. That is part of the win: the compiler stops accepting silent nulls.

In `src/Heimdall.Ssh/TunnelManager.Build.cs`, simplify `ResolvePinnedVerifierAsync`:

```csharp
// before
private static async Task<PinnedFingerprintVerifier?> ResolvePinnedVerifierAsync(
    SshConnectionParams connectionParams,
    string verificationHost,
    int verificationPort,
    HostKeyStore? hostKeyStore,
    IHostKeyVerifier? verifier,
    CancellationToken cancellationToken)
{
    if (hostKeyStore is null) return null;
    if (verifier is null) throw new InvalidOperationException(...);
    return await SshConnectionFactory.ResolveHostKeyAsync(...).ConfigureAwait(false);
}
```

becomes:

```csharp
private static Task<PinnedFingerprintVerifier> ResolvePinnedVerifierAsync(
    SshConnectionParams connectionParams,
    string verificationHost,
    int verificationPort,
    HostKeyStore hostKeyStore,
    IHostKeyVerifier verifier,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(hostKeyStore);
    ArgumentNullException.ThrowIfNull(verifier);

    return SshConnectionFactory.ResolveHostKeyAsync(
        connectionParams, verificationHost, verificationPort, hostKeyStore, verifier, cancellationToken);
}
```

Update callers (`OpenTunnelAsync`, `OpenChainedTunnelAsync` for each hop) to drop the `if (pinnedVerifier is not null)` branches around `AttachPinnedHostKeyVerification`.

### Step 4 — `RemoteFileEditor` ctor + `EditSession`

In `src/Heimdall.Sftp/RemoteFileEditor.cs`:

```csharp
// before
public RemoteFileEditor(
    IRemoteBrowser browser,
    string editorPath = "notepad.exe",
    HostKeyStore? hostKeyStore = null,
    IHostKeyVerifier? hostKeyVerifier = null) { ... }

// after
public RemoteFileEditor(
    IRemoteBrowser browser,
    HostKeyStore hostKeyStore,
    IHostKeyVerifier hostKeyVerifier,
    string editorPath = "notepad.exe") { ... }
```

Required parameters move ahead of optional. Add `ArgumentNullException.ThrowIfNull` for both new required params.

Promote the private fields to non-nullable:

```csharp
private readonly HostKeyStore _hostKeyStore;
private readonly IHostKeyVerifier _hostKeyVerifier;
```

Tighten `EditSession`:

```csharp
// before
public HostKeyStore? HostKeyStore { get; init; }
public IHostKeyVerifier? HostKeyVerifier { get; init; }

// after
public required HostKeyStore HostKeyStore { get; init; }
public required IHostKeyVerifier HostKeyVerifier { get; init; }
```

Simplify the two pinned-verifier guards in `EditFileSudoAsync` and `UploadWithSudoAsync` (drop the `if (_hostKeyStore is not null) { if (_hostKeyVerifier is null) throw ...; ... }` blocks; call `ResolveHostKeyAsync` unconditionally).

### Step 5 — `EmbeddedSftpViewModel.Initialize`

In `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs`:

```csharp
// before
public void Initialize(
    IRemoteBrowser browser,
    SessionTabViewModel sessionTab,
    string displayName,
    string endpoint,
    LocalizationManager localizer,
    IDialogService dialogService,
    SshConnectionParams? sshParams = null,
    HostKeyStore? hostKeyStore = null,
    IHostKeyVerifier? hostKeyVerifier = null) { ... }
```

becomes:

```csharp
public void Initialize(
    IRemoteBrowser browser,
    SessionTabViewModel sessionTab,
    string displayName,
    string endpoint,
    LocalizationManager localizer,
    IDialogService dialogService,
    HostKeyStore hostKeyStore,
    IHostKeyVerifier hostKeyVerifier,
    SshConnectionParams? sshParams = null) { ... }
```

Promote the private fields (lines 43-44) to non-nullable. Simplify the `CreateSudoSshClientAsync` guarded block (lines 457-471) the same way as the others.

`SshConnectionParams?` stays nullable on this method — direct-connect SFTP without sudo doesn't need it.

### Step 6 — Fix the production call sites

The compiler will list them. Most should already pass real instances; the ones to double-check are:
- `src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs` lines 133, 1168, 1185 — make sure both deps are forwarded into the new `RemoteFileEditor` ctor and the `Initialize(...)` call.
- `src/Heimdall.App/ViewModels/Tunnels/TunnelsViewModel.cs` lines 451, 464 — both `OpenTunnelAsync` / `OpenChainedTunnelAsync` calls must pass real `HostKeyStore` + `IHostKeyVerifier` instances. They get them from DI; the VM's ctor likely already receives them — if not, add them.

### Step 7 — Fix the test call sites

Compile errors are your map. For each error:

- If the test is exercising a real SSH transport, it already passes both deps — just adjust to the new parameter order if it used positional args.
- If the test uses `new TunnelManager()` purely for state tracking and never calls `OpenTunnelAsync`, no change needed.
- If the test calls `OpenTunnelAsync` / `OpenChainedTunnelAsync` / `SftpBrowser.ConnectAsync` / `SshShellSession.ConnectAsync` / `RemoteFileEditor` ctor / `EmbeddedSftpViewModel.Initialize` and previously passed `null` (explicitly or implicitly), pick:
  - **Default**: `RejectingHostKeyVerifier.Instance` + `new HostKeyStore()`. This is the safest replacement for "I don't care about host keys in this test"; if the code path actually tries to verify a host key, `RejectingHostKeyVerifier` will reject it, which is the correct behaviour for a test that wasn't meant to set one up.
  - **TOFU acceptance**: `AutoAcceptHostKeyVerifier.Instance` + `new HostKeyStore()`. Use this only when the test deliberately exercises a connection-success path against a real or stubbed server.
- The test `tests/Heimdall.Ssh.Tests/TunnelManagerTests.cs::OpenTunnelAsync_HostKeyStoreWithoutVerifier_ReturnsUnknownWithoutEvents` (around line 584) becomes invalid — its premise (passing a non-null store with a null verifier) is now impossible at the type level. **Delete the test method**. Add a one-line code comment in the test class explaining why it was removed: "Removed: contract enforced at compile time after C1 hardening."

### Step 8 — Verify the obsolete branches are gone

After Step 1-5, the following patterns should no longer appear anywhere in `src/`:

```csharp
if (hostKeyStore is not null)
{
    if (hostKeyVerifier is null)
    {
        throw new InvalidOperationException("IHostKeyVerifier is required when HostKeyStore is provided.");
    }
    ...
}
```

Run `grep -n "IHostKeyVerifier is required when HostKeyStore"` over `src/` — there should be **zero** matches when you are done. If a match remains, you missed a guard.

Also check that no production code path calls `SshConnectionFactory.ResolveHostKeyAsync` from inside an `if (hostKeyStore is not null)` branch — those branches should be unconditional after this prompt.

## Coding standards

Same as previous prompts:

- Apache 2.0 header on any new file (this prompt likely doesn't introduce new files).
- English only.
- Nullable reference types stay enabled; do not silence warnings.
- `TreatWarningsAsErrors` is on.
- `ConfigureAwait(false)` on every `await` you introduce in non-UI projects (`Heimdall.Ssh`, `Heimdall.Sftp`). The App project keeps its existing convention.
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build is green with zero new warnings; the test suite passes (5,363 minus the deleted obsolete test = 5,362 expected passing). The `TracerouteViewModelTests` suite has a known flaky test unrelated to this work — re-run the suite once if it fails on first run, do not modify it.

## Reporting back

When you finish, report:

1. The list of source files modified, with a one-line summary per file.
2. The list of test files modified, with the deleted test method and any test fixture changes.
3. The final test counts (passed / failed / skipped).
4. Confirm the `grep -n "IHostKeyVerifier is required when HostKeyStore"` over `src/` returns no matches.
5. Confirm `grep -rn "HostKeyStore? " src/` (note the trailing `?`-space combo on parameter declarations) returns no matches in production source (only `EditSession` is allowed to keep `HostKeyStore?` if it's an unrelated field — but it shouldn't be after Step 4 either).
6. Any decision that diverged from this prompt, with a one-line rationale.
