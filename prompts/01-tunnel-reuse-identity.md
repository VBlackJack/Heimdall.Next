# Prompt 1 — Tunnel reuse identity key (A2) + test T2

## Context

You are working on **Heimdall.Next**, a .NET 10 + WPF Windows connection manager. The full project context lives in the repo's `CLAUDE.md`. Read it first if you have not already; the SSH and tunnel gotchas are particularly relevant to this task.

A recent SSH/SFTP audit identified a cross-tenant data exposure risk in `TunnelService`: when two server profiles target the same private IP/port combination through **different SSH gateways**, the existing tunnel-reuse logic happily reuses whichever tunnel was opened first because the matching predicate inspects only `(RemoteHost, RemotePort, IsAlive)`. Picture a Heimdall user managing two customer estates that both expose `10.0.0.5:3389` through their own bastion; today, traffic intended for customer B can be routed through customer A's tunnel.

Your job is to fix this by composing a **stable, gateway-aware reuse key** and to add a regression test that pins the new behaviour.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P0 #1 (A2 + T2) only.

## Goal

1. Augment `Heimdall.Ssh.TunnelInfo` with a new immutable `GatewayChainKey` field carrying a stable identifier of the gateway path used to open the tunnel.
2. Thread the gateway chain key through `TunnelManager.OpenTunnelAsync`, `TunnelManager.OpenChainedTunnelAsync`, and the Plink fallback path in `TunnelService.EstablishPlinkTunnelAsync`.
3. Replace the matching predicate in `TunnelService.EstablishTunnelAsync` so reuse requires:
   - identical `GatewayChainKey`,
   - identical `RemoteHost` and `RemotePort`,
   - identical forwarding mode (`SocksProxyPort` and `RemoteBindPort`).
4. Extract the matching logic into a static helper that can be unit-tested without wiring up the full DI graph.
5. Add a focused test class covering the new behaviour.

## Background — relevant files (read these before writing any code)

- `src/Heimdall.Ssh/TunnelInfo.cs` — the record being augmented.
- `src/Heimdall.Ssh/TunnelManager.cs` — public `OpenTunnelAsync` / `OpenChainedTunnelAsync` / `GetActiveTunnels` surface.
- `src/Heimdall.Ssh/TunnelManager.Build.cs` — `BuildTunnelInfo` helper that constructs `TunnelInfo` instances.
- `src/Heimdall.App/Services/TunnelService.cs` — the production caller that performs reuse matching at lines 122-127 and constructs a `TunnelInfo` for the Plink fallback at lines 426-432.
- `src/Heimdall.Core/Configuration/SshGatewayDto.cs` — the `Id` field is the stable per-gateway identifier we will hash.
- `src/Heimdall.Ssh/SshConnectionParams.cs` — what `GatewayChainResolver.ResolveChain` returns. Note that `SshConnectionParams` does **not** carry the gateway DTO Id, so the chain identity must be computed by the caller that still has the DTO chain in scope (i.e. `TunnelService`).
- `tests/Heimdall.Ssh.Tests/TunnelManagerTests.cs` — for the test style currently used in the SSH test project.

## Implementation steps

### Step 1 — Add `GatewayChainKey` to `TunnelInfo`

Edit `src/Heimdall.Ssh/TunnelInfo.cs`. Add an `init`-only `string` property called `GatewayChainKey` defaulting to an empty string for backward compatibility with code that constructs `TunnelInfo` directly (tests, external-tunnel call sites). Document it precisely. **Do not** reference `TunnelService` in the XML doc — `Heimdall.Ssh` lives below `Heimdall.App` in the dependency graph and must not document upward into the App layer.

```csharp
/// <summary>
/// Stable identifier of the gateway chain that opened this tunnel. Used by
/// callers to decide whether an alive tunnel can be reused for a new request,
/// instead of matching only on the remote endpoint. Empty for tunnels opened
/// without an associated gateway chain (legacy / test fixtures).
/// </summary>
public string GatewayChainKey { get; init; } = string.Empty;
```

### Step 2 — Compute the chain key in `TunnelService` (collision-safe)

Add an `internal static` helper to `src/Heimdall.App/Services/TunnelService.cs`. **Do not** use a naive `string.Join` separator: `SshGatewayDto.Id` has no format constraint beyond "non-empty string", so a value containing the separator could collide two distinct chains. Use a **length-prefixed encoding hashed with SHA-256**, then expose the digest as a versioned string so the format is upgrade-safe.

```csharp
/// <summary>
/// Computes a collision-resistant stable identity key for a resolved gateway
/// chain. Each hop's gateway DTO Id is encoded as
/// "<utf8-byte-count>:<id>" and concatenated in order; the resulting buffer
/// is SHA-256 hashed and emitted as "v1:sha256:&lt;base64&gt;". Two profiles
/// routed through the same chain produce the same key; two chains that
/// differ in length, order, or any Id produce distinct keys, even when the
/// raw Id values contain the encoding delimiter.
/// </summary>
internal static string BuildGatewayChainKey(IReadOnlyList<SshGatewayDto> chainDtos)
{
    ArgumentNullException.ThrowIfNull(chainDtos);
    if (chainDtos.Count == 0)
    {
        return string.Empty;
    }

    using var ms = new MemoryStream();
    foreach (var hop in chainDtos)
    {
        var idBytes = Encoding.UTF8.GetBytes(hop.Id ?? string.Empty);
        var lenPrefix = Encoding.ASCII.GetBytes($"{idBytes.Length}:");
        ms.Write(lenPrefix, 0, lenPrefix.Length);
        ms.Write(idBytes, 0, idBytes.Length);
    }

    var digest = SHA256.HashData(ms.ToArray());
    return "v1:sha256:" + Convert.ToBase64String(digest);
}
```

Add the matching `using` directives (`System.IO`, `System.Security.Cryptography`, `System.Text`) at the top of `TunnelService.cs` if they are not already present.

Note: `GatewayChainResolver.ResolveChain` currently returns `List<SshConnectionParams>` and discards the DTO list. Change it to return both — the simplest change is to expose a new method `GatewayChainResolver.ResolveChainDtos(...)` that returns `List<SshGatewayDto>` ordered root-to-target, and let `ResolveChain` keep delegating. **Do not break the existing `ResolveChain` signature** — there are tests that depend on it. Have `TunnelService` call the new DTO method and pass both the DTOs (for the key) and the connection params (for the open call).

If extending `GatewayChainResolver` proves invasive, an acceptable alternative is to extract a helper method in `TunnelService` that walks `settings.SshGateways` itself with the same depth and cycle detection, but the resolver-side fix is preferred because it keeps a single source of truth.

### Step 3 — Thread the key through `TunnelManager`

Edit `src/Heimdall.Ssh/TunnelManager.cs`:

- Add a new optional parameter `string? gatewayChainKey = null` to both `OpenTunnelAsync` and `OpenChainedTunnelAsync`. Passing `null` means "no chain context" and is preserved as `string.Empty` on the resulting `TunnelInfo` (legacy / test behaviour).
- Forward the value to `BuildTunnelInfo`.

Edit `src/Heimdall.Ssh/TunnelManager.Build.cs`:

- Add `string? gatewayChainKey = null` to `BuildTunnelInfo` and set it on the constructed `TunnelInfo`:

```csharp
return new TunnelInfo(...)
{
    SocksProxyPort = socksProxyPort,
    RemoteBindPort = remoteBindPort,
    Label = ...,
    GatewayChainKey = gatewayChainKey ?? string.Empty
};
```

### Step 4 — Update `TunnelService`

Edit `src/Heimdall.App/Services/TunnelService.cs`:

1. Resolve the DTO chain alongside the connection-params chain at line 148.
2. Compute `gatewayChainKey` via the new helper.
3. Replace the existing match (lines 122-127) with a call to a new internal helper:

```csharp
internal static TunnelInfo? FindReusableTunnel(
    IReadOnlyList<TunnelInfo> activeTunnels,
    string gatewayChainKey,
    string remoteHost,
    int remotePort,
    int socksProxyPort,
    int remoteBindPort)
{
    ArgumentNullException.ThrowIfNull(activeTunnels);
    ArgumentNullException.ThrowIfNull(gatewayChainKey);
    ArgumentNullException.ThrowIfNull(remoteHost);

    foreach (var tunnel in activeTunnels)
    {
        if (!tunnel.IsAlive) { continue; }
        if (!string.Equals(tunnel.GatewayChainKey, gatewayChainKey, StringComparison.Ordinal)) { continue; }
        if (!string.Equals(tunnel.RemoteHost, remoteHost, StringComparison.Ordinal)) { continue; }
        if (tunnel.RemotePort != remotePort) { continue; }
        if (tunnel.SocksProxyPort != socksProxyPort) { continue; }
        if (tunnel.RemoteBindPort != remoteBindPort) { continue; }
        return tunnel;
    }

    return null;
}
```

4. Have `EstablishTunnelAsync` call `FindReusableTunnel` after computing `gatewayChainKey`.
5. Pass `gatewayChainKey` to the `OpenTunnelAsync` and `OpenChainedTunnelAsync` calls.
6. In `EstablishPlinkTunnelAsync`, set the new property on the manually constructed `TunnelInfo` at lines 426-432:

```csharp
var tunnelInfo = new TunnelInfo(
    gatewayParams.Host,
    localPort,
    remoteHost,
    remotePort,
    DateTime.UtcNow,
    IsAlive: true)
{
    GatewayChainKey = gatewayChainKey
};
```
(`gatewayChainKey` will need to flow into `EstablishPlinkTunnelAsync` as a parameter.)

### Step 5 — Add tests

Create the test class at **`tests/Heimdall.App.Tests/TunnelReuseIdentityTests.cs`**. The helpers under test (`TunnelService.FindReusableTunnel`, `TunnelService.BuildGatewayChainKey`) live in the `Heimdall.App` assembly, and `src/Heimdall.App/Heimdall.App.csproj` already declares `<InternalsVisibleTo Include="Heimdall.App.Tests" />`, so `internal` accessibility is enough — do not weaken the modifiers to `public`.

The test class must include at minimum the following cases (one `[Fact]` per scenario):

1. **Same chain, same target → reuse.** Given an alive `TunnelInfo` with `GatewayChainKey = "gw-A"` targeting `10.0.0.5:3389`, a second request with the same chain key and target returns the existing tunnel.
2. **Different chains, same target → no reuse.** Given an alive `TunnelInfo` with `GatewayChainKey = "gw-A"`, a request with `gatewayChainKey = "gw-B"` returns null.
3. **Same chain, different target → no reuse.** Same chain, different `remotePort` returns null.
4. **Same chain, target, but different SOCKS port → no reuse.**
5. **Same chain, target, but different remote-bind port → no reuse.**
6. **Dead tunnel is not reused.** `IsAlive = false` returns null even with everything else matching.
7. **Empty chain key matches empty chain key.** Used for legacy / direct-connection tunnels — two such tunnels with the same target should match.
8. **Null arguments.** `ArgumentNullException` when `activeTunnels`, `gatewayChainKey`, or `remoteHost` is null.

Also add tests for `BuildGatewayChainKey`:

9. **Empty chain → empty string.** Asserts the helper returns `string.Empty` (not the SHA-256 of zero bytes).
10. **Single hop → versioned digest.** Asserts the result starts with `"v1:sha256:"` and is deterministic across two calls with equivalent input.
11. **Order matters.** Two chains with the same Ids in different orders produce different keys.
12. **Length matters.** A chain `["a"]` and a chain `["a", ""]` produce different keys (the empty trailing hop is a real hop with `Id = ""`).
13. **Collision resistance against the encoding delimiter.** Two chains whose raw Ids contain `:` and `|` characters produce different keys when they would collide under a naive `string.Join` scheme. Use these two chains, which would produce the identical concatenation `"foo:1|bar"` under naive joining but differ under length-prefixed encoding:
    - Chain A: Ids `["foo:1", "bar"]`
    - Chain B: Ids `["foo", "1|bar"]`
   Assert their keys differ.

Use FluentAssertions where the existing test files use it; otherwise plain xUnit `Assert.*`. Do not introduce new test dependencies.

### Step 6 — Update existing tests if needed

`tests/Heimdall.Ssh.Tests/TunnelManagerTests.cs` constructs `TunnelInfo` instances inline via `MakeInfo(...)`. The new `GatewayChainKey` defaults to an empty string and must not break these. Do not modify them unless a build error forces it; if you do, set `GatewayChainKey = ""` explicitly so the intent is clear.

If you change `GatewayChainResolver.ResolveChain` (or add a new method), check the existing `tests/Heimdall.Ssh.Tests/GatewayChainResolverTests.cs` and update only what is strictly required.

## Coding standards (non-negotiable)

These come from `CLAUDE.md` plus the `dev-standards` skill:

- All new files start with the Apache 2.0 header listing **Julien Bombled** as author.
- Code, comments, XML docs, identifier names, log messages: **English only**.
- Nullable reference types are enabled project-wide; do not silence warnings with `#nullable disable`.
- `TreatWarningsAsErrors` is on — your build will fail on any new warning.
- Non-UI projects (this prompt only touches `Heimdall.Ssh` and `Heimdall.App` services / tests): use `ConfigureAwait(false)` on every `await` you introduce in non-UI code paths. UI projects intentionally omit it.
- Do not introduce hardcoded user-facing strings; if you need a new error message, add a key to `locales/en.json` and `locales/fr.json` and route it through `LocalizationManager`. (This prompt should not need new locale keys; if you find yourself reaching for one, double-check.)
- Do not add `[Co-Authored-By]` or any AI attribution anywhere.

## Build & verify

After your changes are in place, run:

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected outcome: build succeeds with no new warnings, full test suite stays green. Baseline is 5,030 passing tests + 6 known-skipped (WPF Application context). Your new tests bring the passing count up.

If a test fails, **fix the test or the implementation**; do not weaken the assertion.

## Reporting back

When you finish, report exactly:

1. The list of files modified.
2. The list of tests added (class name + method names).
3. The final test counts (passed / failed / skipped).
4. Any decision you took that diverged from this prompt, with a one-line rationale per decision (for example: "extended `ResolveChain` instead of adding `ResolveChainDtos` because the call sites were already returning the DTOs implicitly via …").
5. The exact diff of `TunnelInfo.cs` and the new helper method, inline in the report.
