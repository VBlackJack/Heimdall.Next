# Prompt 10 — Three grouped P2 fixes (C3 + M5 + M1)

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

This prompt closes three small P2 items from the SSH/SFTP audit. They are independent of each other and small enough to ship together. The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. Prompts 1-9 have already shipped; the P0 and P1 phases are complete.

**C3 — Pageant XML doc lies about its implementation**

`src/Heimdall.Ssh/Pageant/PageantHostAlgorithm.cs` ~lines 78-91:

```csharp
/// <remarks>
/// Pageant returns the full SSH signature blob: [algo_len:4][algo][sig_len:4][sig].
/// SSH.NET expects only the raw signature bytes (it wraps them internally),
/// so we strip the algorithm name prefix before returning.
/// </remarks>
public override byte[] Sign(byte[] data)
{
    return _pageantClient.SignData(_publicKeyBlob, data, _signFlags);
}
```

The `<remarks>` says "we strip the algorithm name prefix before returning". The implementation does not strip anything — it returns the full blob unchanged, which **is** the correct SSH.NET contract per `CLAUDE.md`. The XML doc is the bug, not the code. A regression test pins the full-blob behaviour so a future "doc-driven" PR cannot break Pageant signing.

**M5 — Plink log redaction misses multi-token secrets**

`src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs` ~lines 456-503. The current credential-assignment regex consumes a single non-whitespace token after the keyword:

```csharp
@"(?i)\b(password|passphrase|secret|token|bearer)\b\s*[:=]?\s*\S+"
```

A line like `Authorization: Bearer foo bar baz` redacts only `foo`; `bar baz` leaks. Today's plink stderr does not produce that shape, but the helper is reusable and the asymmetry is a footgun. Fix: split the keywords into two patterns — `password` / `passphrase` / `secret` keep the single-token match (those values never legitimately span tokens), while `token` / `bearer` consume to end-of-line.

**M1 — Legacy `HostKeyStore.Verify(byte[])` overload signals trusted-on-first-use**

`src/Heimdall.Ssh/HostKeyStore.cs` ~line 50-82. The byte-array overload returns `Trusted=true, FirstUse=true` for unknown hosts. A naive caller checking only `Trusted` would silently TOFU-trust without prompting the user. Production code routes through `SshConnectionFactory.ResolvePresentedHostKeyAsync` and is safe; the overload is only used by 15 sites in `tests/Heimdall.Ssh.Tests/HostKeyStoreTests.cs` that legitimately exercise the legacy contract. Mark the overload `[Obsolete]` and let the tests opt in with `#pragma warning disable CS0618`. No call sites in `src/` need changes.

## Goals

1. **C3** — Replace the misleading XML `<remarks>` on `PageantHostAlgorithm.Sign` with text that matches the actual contract. Add a unit test that constructs the algorithm with a fake `PageantClient` and asserts `Sign(data)` returns the exact bytes the client returned (no stripping).
2. **M5** — Split `PlinkTunnelRunner.SanitizeForLog`'s credential regex so `token` / `bearer` redact to end-of-line. Add unit tests covering the multi-token leak case + the existing single-token cases (must still pass).
3. **M1** — Mark `HostKeyStore.Verify(byte[] hostKey)` `[Obsolete("...")]` with a clear migration message. Add `#pragma warning disable CS0618` around the legacy test class so it still compiles. Production source touches: zero.

## Background — relevant files

- `src/Heimdall.Ssh/Pageant/PageantHostAlgorithm.cs` — file under change for C3.
- `src/Heimdall.Ssh/Pageant/PageantClient.cs` — `SignData(byte[] keyBlob, byte[] data, uint flags)` returns `byte[]`. The fake for the test must produce a known byte array.
- `src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs` — file under change for M5. The two existing regexes (`PlinkCredentialFlagPattern`, `CredentialAssignmentPattern`) are at the top of the file ~lines 456-468. `SanitizeForLog` is `internal static` so it is reachable from tests.
- `src/Heimdall.Ssh/HostKeyStore.cs` — file under change for M1. The byte-array overload is at line 50; the string-overload (`Verify(host, port, fingerprint, algorithm)` from `HostKeyTrustService`) is the safe replacement.
- `tests/Heimdall.Ssh.Tests/HostKeyStoreTests.cs` — 15 call sites that exercise the byte-array overload; will need a single `#pragma warning disable CS0618` at the top of the file (or per-method, your choice).

`Heimdall.Ssh.csproj` already declares `<InternalsVisibleTo Include="Heimdall.Ssh.Tests" />`, so all three test files have full reach.

## Implementation steps

### C3 — Step 1: Rewrite the XML remarks

In `src/Heimdall.Ssh/Pageant/PageantHostAlgorithm.cs`, replace the misleading `<remarks>` on `Sign`:

```csharp
/// <summary>
/// Signs the given data by delegating to Pageant via shared memory IPC.
/// Pageant performs the cryptographic operation using the private key
/// it holds in memory; the private key never leaves the agent.
/// </summary>
/// <remarks>
/// Pageant returns the full SSH signature blob:
/// <c>[algo_name_len:4][algo_name][raw_sig_len:4][raw_sig]</c>.
/// SSH.NET's <see cref="Renci.SshNet.PrivateKeyAuthenticationMethod"/>
/// expects <see cref="HostAlgorithm.Sign"/> to return this exact format
/// (matching <c>KeyHostAlgorithm.Sign</c> which wraps via
/// <c>SignatureData</c>). We pass the blob through unchanged; do
/// <b>not</b> strip the algorithm name prefix.
/// </remarks>
/// <param name="data">Data to sign (session hash + auth request from SSH.NET).</param>
/// <returns>Full SSH signature blob, including the algorithm name prefix.</returns>
public override byte[] Sign(byte[] data)
{
    return _pageantClient.SignData(_publicKeyBlob, data, _signFlags);
}
```

The inline comment block above the call is fine to keep (it already says the right thing).

### C3 — Step 2: Pin the contract with a test

Create `tests/Heimdall.Ssh.Tests/PageantHostAlgorithmTests.cs`:

```csharp
public sealed class PageantHostAlgorithmTests
{
    [Fact]
    public void Sign_ReturnsBlobFromAgentUnchanged()
    {
        // Arrange: a dummy public-key blob and a known agent response.
        var publicKeyBlob = new byte[] { 0x00, 0x00, 0x00, 0x07, (byte)'s', (byte)'s', (byte)'h', (byte)'-', (byte)'r', (byte)'s', (byte)'a' };
        var dataToSign = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var expectedAgentResponse = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };

        var fakeClient = new FakePageantClient(expectedAgentResponse);
        var algorithm = new Heimdall.Ssh.Pageant.PageantHostAlgorithm(
            "ssh-rsa",
            publicKeyBlob,
            fakeClient,
            signFlags: 0);

        // Act
        var actual = algorithm.Sign(dataToSign);

        // Assert: same reference content, byte-for-byte (no stripping).
        Assert.Equal(expectedAgentResponse, actual);
        Assert.Equal(publicKeyBlob, fakeClient.LastKeyBlob);
        Assert.Equal(dataToSign, fakeClient.LastData);
        Assert.Equal(0u, fakeClient.LastFlags);
    }

    private sealed class FakePageantClient : Heimdall.Ssh.Pageant.PageantClient
    {
        private readonly byte[] _response;
        public byte[]? LastKeyBlob;
        public byte[]? LastData;
        public uint LastFlags;

        public FakePageantClient(byte[] response)
        {
            _response = response;
        }

        public new byte[] SignData(byte[] keyBlob, byte[] data, uint flags = 0)
        {
            LastKeyBlob = keyBlob;
            LastData = data;
            LastFlags = flags;
            return _response;
        }
    }
}
```

If `PageantClient.SignData` is not `virtual` (and therefore cannot be `override`-d via `new`), pivot to one of these alternatives:

- Promote `SignData` to `virtual`. This is the cleanest; it does not change behaviour for production callers and it lets the test fake the client.
- Or extract a small `IPageantSigner` interface with a single method `byte[] Sign(byte[] keyBlob, byte[] data, uint flags)`, implement it in `PageantClient`, and inject it into `PageantHostAlgorithm`. This is more invasive — only do it if `virtual` is rejected by some constraint you discover.

Document the choice you made in the report.

### M5 — Step 1: Split the regex

In `src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs`, replace `CredentialAssignmentPattern` with two:

```csharp
/// <summary>
/// Match credential-like assignments where the secret is a single token
/// (<c>password=...</c>, <c>passphrase: ...</c>, <c>secret=...</c>).
/// </summary>
private static readonly Regex SingleTokenCredentialPattern = new(
    @"(?i)\b(password|passphrase|secret)\b\s*[:=]?\s*\S+",
    RegexOptions.Compiled);

/// <summary>
/// Match credential-like assignments where the secret can span multiple
/// tokens (<c>token ...</c>, <c>Authorization: Bearer ...</c>). Greedy
/// to end-of-line so trailing words are not leaked.
/// </summary>
private static readonly Regex EndOfLineCredentialPattern = new(
    @"(?i)\b(token|bearer)\b\s*[:=]?\s*.+",
    RegexOptions.Compiled);
```

Update `SanitizeForLog` to apply both:

```csharp
var redacted = PlinkCredentialFlagPattern.Replace(builder.ToString(), RedactedMarker);
redacted = EndOfLineCredentialPattern.Replace(redacted, RedactedMarker);
redacted = SingleTokenCredentialPattern.Replace(redacted, RedactedMarker);
```

The order matters: apply the end-of-line pattern **first** so it consumes a `Bearer foo bar` line before the single-token pattern can fire on `foo` and stop. The single-token pattern then handles isolated `password=` / `passphrase:` / `secret=` cases that did not match the end-of-line set.

`SanitizeForLog` already operates per-line (the caller reads via `StandardError.ReadLineAsync`), so `.+` does not eat across lines.

### M5 — Step 2: Tests

Augment `tests/Heimdall.Ssh.Tests/PlinkTunnelRunnerTests.cs` (or create a new `PlinkSanitizeForLogTests.cs` if the existing file is already crowded — your call) with:

1. **`SanitizeForLog_RedactsBearerToEndOfLine`**

   Input: `"Authorization: Bearer abc def ghi"` → output contains `"[REDACTED]"` and **does not contain** `"def"` or `"ghi"`.

2. **`SanitizeForLog_RedactsTokenToEndOfLine`**

   Input: `"token = xyz some-uuid extra"` → output contains `"[REDACTED]"` and **does not contain** `"some-uuid"`.

3. **`SanitizeForLog_RedactsSingleTokenPassword`** (regression — must keep working)

   Input: `"password=secret123"` → output contains `"[REDACTED]"` and **does not contain** `"secret123"`. Output **does** still contain `"password"` (only the value is redacted; the keyword itself is preserved by the regex semantics).

4. **`SanitizeForLog_RedactsSingleTokenPassphrase`** (regression)

   Input: `"passphrase: foobar123"` → output contains `"[REDACTED]"` and **does not contain** `"foobar123"`.

5. **`SanitizeForLog_DoesNotOverRedactNonCredentialLines`** (negative)

   Input: `"connecting to gateway 192.0.2.1"` → output is unchanged (no `[REDACTED]` substring, no `?` substitution).

6. **`SanitizeForLog_TruncatesAtMaxLength`** (regression — already exists, do not duplicate; just confirm it still passes after the refactor).

If `PlinkTunnelRunnerTests.cs` already contains some of the regression cases, add only the missing ones and avoid duplication.

### M1 — Step 1: Mark the overload `[Obsolete]`

In `src/Heimdall.Ssh/HostKeyStore.cs`, find the byte-array overload (line 50) and decorate it:

```csharp
/// <summary>
/// Verify a host key received from the SSH.NET HostKeyReceived event.
/// Returns trusted on first use (TOFU) or fingerprint match.
/// Returns untrusted on fingerprint mismatch.
/// </summary>
/// <remarks>
/// <b>Obsolete:</b> the on-first-use return value is <c>Trusted = true,
/// FirstUse = true</c>, which a naive caller may misread as "trust without
/// prompting". Production code must route through
/// <see cref="HostKeyTrustService.Verify"/> or
/// <see cref="Heimdall.Ssh.SshConnectionFactory.ResolvePresentedHostKeyAsync"/>,
/// which prompt the user via <see cref="Heimdall.Core.Ssh.IHostKeyVerifier"/>
/// before granting trust.
/// </remarks>
[Obsolete(
    "Use HostKeyTrustService.Verify or SshConnectionFactory.ResolvePresentedHostKeyAsync. "
    + "The byte[] overload returns Trusted=true on first use which is unsafe for callers "
    + "that do not also prompt via IHostKeyVerifier.")]
public HostKeyVerifyResult Verify(string host, int port, byte[] hostKey)
{
    // body unchanged
}
```

Do **not** change the body. The behaviour is preserved exactly; we only signal that the overload is dangerous to use directly.

### M1 — Step 2: Suppress the warning in `HostKeyStoreTests`

`TreatWarningsAsErrors=true` means the `[Obsolete]` warning will fail the build at every test call site. Open `tests/Heimdall.Ssh.Tests/HostKeyStoreTests.cs` and wrap the class with a single file-level pragma:

```csharp
// At the top of the file, after the using directives:
#pragma warning disable CS0618 // Type or member is obsolete — this test exercises the legacy byte[] contract intentionally.

namespace Heimdall.Ssh.Tests;

public class HostKeyStoreTests
{
    // ... existing tests unchanged ...
}

#pragma warning restore CS0618
```

The pragma must explicitly target `CS0618` only (do not use a bare `#pragma warning disable`) so any other warning that creeps into the file still fails the build.

### M1 — Step 3: Add a contract-document test

Add **one** test to `HostKeyStoreTests.cs` that pins the legacy contract and references the obsolete attribute, so a future developer reading the test understands why the pragma exists:

```csharp
[Fact]
public void LegacyByteOverload_FirstUse_ReturnsTrustedTrueByDesign()
{
    // This test documents the obsolete legacy contract: the byte[] overload
    // returns Trusted=true on first use, which is why production code must
    // route through HostKeyTrustService instead. See the [Obsolete] message
    // on HostKeyStore.Verify(string, int, byte[]) for the migration path.
    var result = _store.Verify("first.example.com", 22, _sampleKey);

    Assert.True(result.FirstUse);
    Assert.True(result.Trusted);  // ← unsafe contract, see Obsolete message
}
```

If `HostKeyStoreTests` already has a near-equivalent test, attach the explanatory comment block to that test rather than duplicating.

## Sanity checks

After all three fixes:

```bash
grep -n "we strip the algorithm name prefix" src/Heimdall.Ssh/Pageant/PageantHostAlgorithm.cs
```

Must return zero matches — the lying remarks are gone.

```bash
grep -n "CredentialAssignmentPattern" src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs
```

Must return zero matches — the old single combined pattern was deleted.

```bash
grep -n "EndOfLineCredentialPattern\|SingleTokenCredentialPattern" src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs
```

Must return at least four matches (two declarations + two `Replace` invocations).

```bash
grep -n "ObsoleteAttribute\|\[Obsolete" src/Heimdall.Ssh/HostKeyStore.cs
```

Must return at least one match — the new attribute is in place.

## Coding standards

Same as previous prompts:

- Apache 2.0 header on any new test file.
- English only.
- Nullable reference types stay enabled.
- `TreatWarningsAsErrors` is on (the `#pragma warning disable CS0618` in the test file is the only legitimate exception — and it is a targeted, file-level disable, not a bare suppression).
- `ConfigureAwait(false)` is irrelevant (no async added).
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build green with zero new warnings; suite passing count rises by 7 (1 Pageant test + 5 sanitize tests + 1 contract-document test, minus any duplicates folded into existing tests). The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite once if it fails on first attempt.

## Reporting back

When you finish, report:

1. The list of source files modified or created (split per fix: C3, M5, M1).
2. The exact diff of `PageantHostAlgorithm.Sign`'s `<remarks>` (before / after) and the new regex pair in `PlinkTunnelRunner.cs`.
3. The list of tests added (class name + each method name), grouped per fix.
4. The final test counts (passed / failed / skipped) for both the targeted runs (`PageantHostAlgorithmTests`, the new sanitize tests, the augmented `HostKeyStoreTests`) and the full suite.
5. Confirm the four grep checks above produce the expected counts.
6. Any decision that diverged from this prompt, with a one-line rationale (especially: whether `PageantClient.SignData` was promoted to `virtual` or whether a new `IPageantSigner` interface was introduced, and where the M5 sanitize tests landed — augmented `PlinkTunnelRunnerTests` or new file).
