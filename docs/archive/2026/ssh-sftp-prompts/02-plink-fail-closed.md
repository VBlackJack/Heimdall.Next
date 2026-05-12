# Prompt 2 — Plink fail-closed when host key cannot be resolved (A1) + test T1

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already; the SSH and Pageant gotchas are particularly relevant.

The SSH/SFTP audit identified a TOFU bypass: when both the Heimdall `HostKeyStore` and the live `PlinkHostKeyProbe` fail to yield a fingerprint, the Plink fallback paths currently keep going and start `plink.exe` with **no `-hostkey` argument**. Plink in `-batch` mode will then either delegate trust to PuTTY's own per-user host-key cache (silent TOFU bypass) or refuse to connect with a generic error — in both cases Heimdall's `HostKeyStore` was never consulted, so the user has no audit trail and no UI prompt.

The two affected sites:

1. `TunnelService.EstablishPlinkTunnelAsync` — `src/Heimdall.App/Services/TunnelService.cs` (look for the local `fingerprint` variable seeded from `GetEffectiveEntry(...)?.Fingerprint`; if both branches fail to populate it, `runner.StartAsync(..., fingerprint, ct, ...)` is invoked with `fingerprint == null`).
2. `SshHandler.ConnectSshViaPlinkAsync` — `src/Heimdall.App/Services/Handlers/SshHandler.cs` (same shape: `hostKeyArg` flows into `BuildPipeModeArguments(..., hostKeyArg, ...)` and is omitted from the command line when null/empty).

`PlinkTunnelRunner.BuildArguments` only emits `-hostkey` when the value is non-empty (`src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs`, the `if (!string.IsNullOrEmpty(hostKeyFingerprint))` check). `BuildPipeModeArguments` in `SshHandler` does the same.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P0 #2 (A1 + T1 + the new `SshFailureCode.HostKeyUnavailable`) only. Prompt 1 (`prompts/01-tunnel-reuse-identity.md`) has already shipped.

## Goal

1. Add a new `SshFailureCode.HostKeyUnavailable` value with English and French localizations.
2. Introduce a tiny `IPlinkHostKeyProbe` abstraction so tests can inject a fake probe instead of spawning the real `plink.exe`.
3. Extract a **pure** static helper `PlinkHostKeyDecider.DecideAsync(...)` that returns a structured decision (`Proceed(fingerprint)` / `Reject(failureCode, message)`) given the stored fingerprint, the probe result, and the user verifier. This is the single place where the fail-closed rule is encoded.
4. Refactor both `TunnelService.EstablishPlinkTunnelAsync` and `SshHandler.ConnectSshViaPlinkAsync` to call the decider; on `Reject` they must return the corresponding `TunnelResult` / `ConnectionResult` **without** instantiating `PlinkTunnelRunner` or `PipeModeSession`.
5. Add focused tests that prove the decider rejects when the probe yields no fingerprint and no entry is stored, and that prove the runner is not started in that case.

## Background — relevant files (read these before writing any code)

- `src/Heimdall.Ssh/SshFailureCode.cs` — enum to extend.
- `locales/en.json` and `locales/fr.json` — locale parity is enforced by CI; both must gain the new keys.
- `src/Heimdall.App/Services/PlinkHostKeyProbe.cs` — the existing static probe to wrap.
- `src/Heimdall.App/Services/TunnelService.cs` — `EstablishPlinkTunnelAsync`, ~lines 263-414 in the current file.
- `src/Heimdall.App/Services/Handlers/SshHandler.cs` — `ConnectSshViaPlinkAsync`, ~lines 350-621.
- `src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs` — `BuildArguments` and the `-hostkey` emission point.
- `src/Heimdall.Core/Ssh/IHostKeyVerifier.cs` and `IHostKeyTrustService.cs` — interfaces consumed by the decider.
- `tests/Heimdall.App.Tests/TunnelReuseIdentityTests.cs` — example test style for the App layer (in particular the use of `internal` accessibility through `InternalsVisibleTo`).

`src/Heimdall.App/Heimdall.App.csproj` already declares `<InternalsVisibleTo Include="Heimdall.App.Tests" />`, so internals visible to the test project are fine — keep new types `internal`.

## Implementation steps

### Step 1 — Add `SshFailureCode.HostKeyUnavailable`

Edit `src/Heimdall.Ssh/SshFailureCode.cs`. Add the new value alongside `HostKeyMismatch` in the **Protocol failures** section:

```csharp
/// <summary>
/// Heimdall could not resolve a host key for the gateway through any of its
/// trusted sources (no stored entry, probe returned no key, or fail-closed
/// fallback). Used to refuse delegation to an external tool's local cache.
/// </summary>
HostKeyUnavailable,
```

Do **not** reorder existing values — `SshFailureCode` is referenced by name elsewhere; appending the new value is fine.

### Step 2 — Add the locale keys

Edit `locales/en.json` and `locales/fr.json`. CI enforces parity, so both files must gain the same set of keys. Place the new entries near the existing `ErrorSshHostKeyMismatch` block.

`locales/en.json`:

```json
"ErrorSshHostKeyUnavailable": "Heimdall could not verify the gateway host key. Refusing to fall back to plink's local cache."
```

`locales/fr.json`:

```json
"ErrorSshHostKeyUnavailable": "Heimdall n'a pas pu vérifier la clé du serveur passerelle. Le repli sur le cache local de plink est refusé."
```

If a parent localized message would be useful (e.g. one that includes the host:port), use `_localizer.Format(SshLocalizationKeys.ErrorSshHostKeyUnavailable, ...)`. The current i18n convention is that callers concatenate detail strings on the side, mirroring how `ErrorHostKeyMismatch` and `ErrorHostKeyMismatchDetail` are paired. For this prompt one key with no placeholder is enough.

If there is a `SshLocalizationKeys` constants class, add `ErrorSshHostKeyUnavailable` there too. (Search the App project for `ErrorSshHostKeyMismatch` to find the file.)

### Step 3 — Introduce `IPlinkHostKeyProbe`

Create `src/Heimdall.App/Services/IPlinkHostKeyProbe.cs`:

```csharp
internal interface IPlinkHostKeyProbe
{
    Task<PlinkHostKeyPresentation?> ProbeAsync(
        string plinkPath,
        string host,
        int port,
        string? username,
        int timeoutMs,
        CancellationToken ct);
}
```

Add a default implementation `DefaultPlinkHostKeyProbe : IPlinkHostKeyProbe` (in the same file or beside `PlinkHostKeyProbe.cs`) that simply delegates to the existing static `PlinkHostKeyProbe.ProbeAsync`. Keep the existing static method so external code that already calls it continues to work — just route the new abstraction through it.

Wire `IPlinkHostKeyProbe` into the production `TunnelService` and `SshHandler` constructors as a dependency, defaulting to `new DefaultPlinkHostKeyProbe()` if the DI container does not already register it. Update `App.xaml.cs` (or wherever services are registered) to add `services.AddSingleton<IPlinkHostKeyProbe, DefaultPlinkHostKeyProbe>();`.

### Step 4 — Extract a pure decider

Create `src/Heimdall.App/Services/PlinkHostKeyDecider.cs`:

```csharp
internal sealed record PlinkHostKeyDecision(
    bool ShouldProceed,
    string? Fingerprint,
    SshFailureCode? FailureCode,
    string? FailureMessageKey,
    string? StoredFingerprint,
    string? PresentedFingerprint);

internal static class PlinkHostKeyDecider
{
    /// <summary>
    /// Centralised fail-closed rule for plink-based fallbacks. Resolves the
    /// fingerprint Heimdall will pass to plink via -hostkey, prompting the
    /// user verifier when needed. Returns Proceed only when Heimdall has a
    /// fingerprint it trusts; otherwise returns Reject with a structured
    /// failure code so callers can surface a localized error without
    /// instantiating any plink process.
    /// </summary>
    internal static async Task<PlinkHostKeyDecision> DecideAsync(
        string host,
        int port,
        string? username,
        string plinkPath,
        int probeTimeoutMs,
        string? storedFingerprint,
        IPlinkHostKeyProbe probe,
        IHostKeyVerifier verifier,
        IHostKeyTrustService trustService,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(trustService);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        // ... see contract table below
    }
}
```

Required behaviour table (one branch per row; encode each as a `PlinkHostKeyDecision` outcome):

| Stored fingerprint | Probe result | Verifier decision | Outcome |
|--------------------|--------------|-------------------|---------|
| present | match | n/a (not invoked) | Proceed(stored) |
| present | mismatch | Accept | Trust(verifier-presented), Proceed(presented) |
| present | mismatch | TrustOnce | TrustForSession(verifier-presented), Proceed(presented) |
| present | mismatch | Reject | Reject(`HostKeyMismatch`, localized mismatch message) |
| present | probe returns null | n/a | Proceed(stored) — we already have a pinned fingerprint, the probe just could not run; do not block legitimate connections on a one-off probe failure. Log a `Warn`. |
| absent | probe returns key | Accept | Trust(presented), Proceed(presented) |
| absent | probe returns key | TrustOnce | TrustForSession(presented), Proceed(presented) |
| absent | probe returns key | Reject | Reject(`Cancelled`, localized "cancelled" message) |
| absent | probe returns null | n/a | **Reject(`HostKeyUnavailable`, localized "could not verify gateway key" message)** — this is the new fail-closed rule |

`FailureMessageKey` carries the i18n key, not the formatted string. The caller is responsible for `_localizer[key]` lookup so the decider stays free of `LocalizationManager` dependencies and easy to unit-test.

`StoredFingerprint` and `PresentedFingerprint` on the result are populated when relevant so the caller can build a detailed mismatch message via `_localizer.Format(...)`.

### Step 5 — Refactor `TunnelService.EstablishPlinkTunnelAsync`

In `src/Heimdall.App/Services/TunnelService.cs`, replace the open-coded host-key block (the `if (!string.IsNullOrWhiteSpace(storedFingerprint)) { ... } else { ... }` section that currently mutates the local `fingerprint` variable) with a single call to `PlinkHostKeyDecider.DecideAsync`. On `decision.ShouldProceed == false`, return a `TunnelResult` immediately with the structured failure code and a localized message — do **not** instantiate `PlinkTunnelRunner`.

Also: the `gatewayChainKey` parameter introduced by Prompt 1 must continue to flow into this method. Do not remove it.

### Step 6 — Refactor `SshHandler.ConnectSshViaPlinkAsync`

Same surgery in `src/Heimdall.App/Services/Handlers/SshHandler.cs`. Replace the block that resolves `hostKeyArg` with a call to `PlinkHostKeyDecider.DecideAsync`. On `decision.ShouldProceed == false`, build a `ConnectionResult(false, ...)` with `SshSessionDiagnosticFactory.CreateHostKeyMismatchFailure` (for `HostKeyMismatch`) or a new `SshSessionDiagnosticFactory.CreateHostKeyUnavailableFailure` helper for the new `HostKeyUnavailable` code. Do **not** instantiate `PipeModeSession`. Existing SSH `ConnectionResult` shape and the `SshLocalizationKeys` integration must stay intact.

If `SshSessionDiagnosticFactory` does not yet have a constructor for `HostKeyUnavailable`, add one analogous to the existing `CreateHostKeyMismatchFailure` (look at the file in `src/Heimdall.App/Services/Handlers/`). Reuse the same diagnostic shape.

### Step 7 — Defense in depth in `PlinkTunnelRunner.BuildArguments` (recommended, not blocking)

In `src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs`, the `if (!string.IsNullOrEmpty(hostKeyFingerprint))` check silently drops the `-hostkey` argument when the fingerprint is missing. After Prompt 2 ships, that branch should be unreachable from the production paths, but a future regression could re-introduce the bug. Add an explicit guard:

```csharp
if (string.IsNullOrEmpty(hostKeyFingerprint))
{
    Heimdall.Core.Logging.FileLogger.Warn(
        $"PlinkTunnelRunner: launching without -hostkey for {gatewayHost}:{gatewayPort}. "
        + "This should never happen in production paths after the fail-closed refactor.");
}
```

A hard `throw` is too risky here because legacy tests / call sites may still construct args with no fingerprint; a `Warn` is enough for the audit trail.

### Step 8 — Tests

Create `tests/Heimdall.App.Tests/PlinkFailClosedTests.cs`. Cover the decider exhaustively:

1. **No stored, probe returns null → Reject(HostKeyUnavailable).** Probe spy receives one call. Verifier is never invoked. Returned `FailureMessageKey` equals `"ErrorSshHostKeyUnavailable"`.
2. **No stored, probe returns key, verifier Accept → Proceed(presented).** `trustService.Trust(...)` is invoked exactly once with `presentedFingerprint`.
3. **No stored, probe returns key, verifier TrustOnce → Proceed(presented).** `trustService.TrustForSession(...)` called once; `Trust(...)` not called.
4. **No stored, probe returns key, verifier Reject → Reject(Cancelled).**
5. **Stored, probe returns same key → Proceed(stored).** Verifier never invoked. No mutation of trust service.
6. **Stored, probe returns null → Proceed(stored), Warn logged.** Verifier never invoked. Allowed because we already have a pinned key — refusing here would block legitimate reconnections after a one-off probe glitch.
7. **Stored, probe returns mismatching key, verifier Accept → Proceed(presented), trustService.Trust called.**
8. **Stored, probe returns mismatching key, verifier TrustOnce → Proceed(presented), TrustForSession called.**
9. **Stored, probe returns mismatching key, verifier Reject → Reject(HostKeyMismatch).** `StoredFingerprint` and `PresentedFingerprint` populated on the decision.
10. **Argument null guards** on `probe`, `verifier`, `trustService`, and an empty `host`.
11. **Cancellation** — when `ct` is already cancelled, the decider throws `OperationCanceledException` before invoking the probe.

Use a hand-rolled `FakeProbe : IPlinkHostKeyProbe` and a hand-rolled `FakeHostKeyVerifier : IHostKeyVerifier` rather than introducing Moq if the existing tests do not use it (check `tests/Heimdall.App.Tests/*.cs` first; if Moq is already in use, you may use it). Same for `IHostKeyTrustService`.

Add one **integration-style** test that exercises the decider through `TunnelService` (without spawning plink). The simplest approach:

12. **TunnelService_PlinkFallback_FailsClosed** — construct `TunnelService` with a fake `IPlinkHostKeyProbe` that returns null and a `RejectingHostKeyVerifier.Instance`. Invoke `EstablishPlinkTunnelAsync` (you may need to expose it `internal` if it isn't already). Assert:
    - return value is `TunnelResult { Success: false, FailureCode: SshFailureCode.HostKeyUnavailable }`,
    - the fake probe was invoked exactly once,
    - no `PlinkTunnelRunner` was registered (`tunnelManager.GetActiveTunnels()` is empty),
    - the localized error message contains the key text from `locales/en.json` (or matches `_localizer["ErrorSshHostKeyUnavailable"]`).

If exposing `EstablishPlinkTunnelAsync` `internal` requires too much surgery, refactor minimally so the decider call + early-return is testable through a public/internal method on `TunnelService`. Document the choice in the report.

If `RejectingHostKeyVerifier.Instance` is not yet a public singleton, expose it the same way `AutoAcceptHostKeyVerifier.Instance` is exposed (look at `src/Heimdall.Core/Ssh/AutoAcceptHostKeyVerifier.cs`).

### Step 9 — Update existing tests if needed

If you change the `TunnelService` ctor signature to inject `IPlinkHostKeyProbe`, audit all call sites. The DI registration in `App.xaml.cs` plus any test that constructs `TunnelService` directly must be updated. Use the `DefaultPlinkHostKeyProbe` for the production registration.

If `SshHandler` ctor changes, do the same. Both should keep their existing dependencies; you are *adding* one parameter, not replacing.

## Coding standards (non-negotiable)

These come from `CLAUDE.md` plus the `dev-standards` skill:

- All new files start with the Apache 2.0 header listing **Julien Bombled** as author.
- Code, comments, XML docs, identifier names, log messages: **English only**.
- Nullable reference types are enabled; do not silence warnings.
- `TreatWarningsAsErrors` is on — no new warnings.
- Use `ConfigureAwait(false)` on every `await` you introduce in non-UI code paths (this prompt only touches App services + `Heimdall.Ssh`).
- Do not introduce hardcoded user-facing strings; route them through `LocalizationManager` with the new locale keys.
- Do not add `[Co-Authored-By]` or any AI attribution anywhere.
- i18n key parity is CI-enforced: every key added to `en.json` must also exist in `fr.json` with a translated value.
- Locale convention from the existing keys: `Error` prefix + `Ssh` segment + CamelCase suffix.

## Build & verify

After your changes are in place, run:

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build is green with zero new warnings; the suite stays at 5,351+ passing tests (your additions push the count up). The `TracerouteViewModelTests` suite has a known flaky test unrelated to this work — if a single Traceroute test fails on first run, re-run the suite once to confirm; do not modify it.

## Reporting back

When you finish, report exactly:

1. The list of files modified or created.
2. The list of tests added (class name + each method name).
3. The final test counts (passed / failed / skipped) for both the targeted runs (`PlinkFailClosedTests`, `TunnelReuseIdentityTests`) and the full suite.
4. Any decision that diverged from this prompt, with a one-line rationale per decision.
5. The exact diff of `PlinkHostKeyDecider.cs` and `IPlinkHostKeyProbe.cs`, inline in the report.
6. The diff of the locale entries added to `en.json` and `fr.json`.
