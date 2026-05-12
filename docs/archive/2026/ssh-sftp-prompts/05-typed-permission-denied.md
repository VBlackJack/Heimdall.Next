# Prompt 5 — Typed permission-denied classification (H1) + test T3

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

The SSH/SFTP audit identified a dangerous heuristic in `EmbeddedSftpViewModel.IsPermissionDenied` (`src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs`, ~lines 851-868). The helper currently classifies an exception as "permission denied" using substring matching on the type name and the message:

```csharp
return message.Contains("Failure", StringComparison.Ordinal)
    && (typeName.Contains("Sftp", StringComparison.OrdinalIgnoreCase)
        || typeName.Contains("Ssh", StringComparison.OrdinalIgnoreCase));
```

The output of this helper is the **gate that decides whether to escalate to `sudo rm -rf`, `sudo mv`, `sudo chmod`, etc.** at the call sites in `LoadDirectoryCoreAsync`, `CreateFolderAsync`, `RenameEntryAsync`, `DeleteEntriesAsync`, `ChmodAsync`. SSH.NET surfaces dozens of `SshException` subclasses whose messages can contain the literal word "Failure" (channel failures, disconnect-by-application, agent failures, key-exchange failures, etc.). Any of those can mis-trigger a sudo escalation that the user did not intend — destructive in the case of `sudo rm -rf`.

This prompt replaces the heuristic with a **typed-exception** check. The `Contains("Failure")` catch-all is deleted. Tests pin the new behaviour, including a regression test proving that `SshException("Channel failure: ...")` no longer triggers sudo.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P0 #4 (H1 + T3) only. Prompts 1, 2, 3, and 4 have already shipped.

## Goal

1. Rewrite `EmbeddedSftpViewModel.IsPermissionDenied(Exception)` so it returns `true` only for typed permission-denied exceptions:
   - `Renci.SshNet.Common.SftpPermissionDeniedException` (the canonical SFTP path).
   - `System.UnauthorizedAccessException` (covers `File.WriteAllText`-style failures on the local temp file).
   - Optionally any other typed permission exception that exists in the SSH.NET 2025.1.0 surface — verify via `Renci.SshNet.Common.*` namespace inspection.
2. Drop the substring fallback entirely. No more `Contains("Failure")`, `Contains("permission denied")`, `Contains("access denied")`, or `Contains("not permitted")`. The helper either matches a typed exception or returns `false`.
3. Keep the public surface (`public static bool IsPermissionDenied(Exception ex)`) — call sites do not change.
4. Add the T3 test class proving the new behaviour, including the regression case for `SshException("Channel failure: ...")`.

## Background — relevant files

- `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs` — `IsPermissionDenied` method ~lines 851-868. The five callers that gate sudo escalation on this helper:
  - `LoadDirectoryCoreAsync` ~lines 905-912
  - `CreateFolderAsync` ~lines 600-606
  - `RenameEntryAsync` ~lines 645-651
  - `DeleteEntriesAsync` ~lines 695-702
  - `ChmodAsync` ~lines 750-756
- `src/Heimdall.Ssh/Heimdall.Ssh.csproj` — pinned to `SSH.NET 2025.1.0` (so the typed exceptions are stable).
- The existing tests for `EmbeddedSftpViewModel` live at `tests/Heimdall.App.Tests/EmbeddedSftpViewModelTests.cs` — follow the same style.

`Heimdall.App.csproj` already declares `<InternalsVisibleTo Include="Heimdall.App.Tests" />`. The helper is `public static` today, so the test does not need internals — but the new test file goes alongside the existing app tests.

## Verification before coding

Before you start writing, briefly inspect the SSH.NET 2025.1.0 exception surface to confirm which typed permission classes exist. The canonical one is `Renci.SshNet.Common.SftpPermissionDeniedException`. You can verify by searching the project's NuGet cache or by writing a one-liner test that tries to instantiate the type:

```csharp
typeof(Renci.SshNet.Common.SftpPermissionDeniedException) // should compile
```

If the SSH.NET version exposes additional typed permission exceptions (e.g. for SCP), match those too. **Do not** invent a typed exception that does not exist — if the only typed permission exception in this version is `SftpPermissionDeniedException`, that is fine, list only the ones that compile. The shell-exec sudo paths (`UploadViaSudoAsync`, etc.) signal failure via `cmd.ExitStatus != 0` not via exceptions, so they are not in scope here.

## Implementation steps

### Step 1 — Rewrite `IsPermissionDenied`

Replace the entire body with a typed-exception check:

```csharp
/// <summary>
/// Returns true when the supplied exception unambiguously represents a
/// "permission denied" outcome from a remote file operation. Used by the
/// embedded SFTP view to decide whether a failed operation can be safely
/// retried via <c>sudo</c>. Substring heuristics on the message are not
/// trusted: SSH.NET surfaces many unrelated failures whose messages
/// happen to contain the word "Failure" or "denied", and those must not
/// trigger a destructive sudo retry.
/// </summary>
public static bool IsPermissionDenied(Exception ex)
{
    ArgumentNullException.ThrowIfNull(ex);

    return ex is Renci.SshNet.Common.SftpPermissionDeniedException
        or UnauthorizedAccessException;
}
```

Add any other typed permission exceptions that you confirmed exist in SSH.NET 2025.1.0 to the pattern — but only if they compile. Do not include `Renci.SshNet.Common.SshException` itself (too broad).

If a typed exception you would like to match is `internal` to SSH.NET (and therefore not usable in a `is` pattern from outside the assembly), do **not** fall back to substring matching to capture it. Instead, leave it out and document the gap in the report — we accept a tighter helper that occasionally fails to detect a real permission denied case (the user gets a clearer error message and can manually retry with sudo) over a loose helper that auto-escalates on unrelated failures.

### Step 2 — Inspect the call sites

The five `catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))` clauses in the view model are the entire blast radius. After the rewrite:

- They will only catch when SSH.NET genuinely raised a typed permission error (or `UnauthorizedAccessException` from a local file op).
- For previously-mis-classified failures (`SshException("Channel failure: ...")`, `SshException("Disconnect by application")`, etc.), the catch falls through to the **outer** `catch (Exception ex)` block which calls `SetErrorStatus(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message)`. The user gets a localized error message and can retry manually if appropriate.

This behaviour change is intentional and is the **whole point** of H1. Verify by reading each of the five call sites that the outer catch exists and produces a sensible UI outcome. If any of them does not have a sensible fallback, mention it in the report — but do not change the structure in this prompt; that is out of scope.

### Step 3 — Add T3 tests

Create `tests/Heimdall.App.Tests/IsPermissionDeniedTests.cs`:

```csharp
using Heimdall.App.ViewModels;
using Renci.SshNet.Common;

namespace Heimdall.App.Tests;

public sealed class IsPermissionDeniedTests
{
    // ── Positive cases ────────────────────────────────────────────────

    [Fact]
    public void Returns_True_For_SftpPermissionDeniedException()
    {
        var ex = new SftpPermissionDeniedException("permission denied");
        Assert.True(EmbeddedSftpViewModel.IsPermissionDenied(ex));
    }

    [Fact]
    public void Returns_True_For_UnauthorizedAccessException()
    {
        var ex = new UnauthorizedAccessException("Access to the path is denied.");
        Assert.True(EmbeddedSftpViewModel.IsPermissionDenied(ex));
    }

    // ── Regression cases — the whole point of H1 ──────────────────────

    [Fact]
    public void Returns_False_For_SshException_With_ChannelFailure_Message()
    {
        // Before H1 this would have triggered sudo escalation because the
        // type name contains "Ssh" and the message contains "Failure".
        var ex = new SshException("Channel failure: open chan request failed");
        Assert.False(EmbeddedSftpViewModel.IsPermissionDenied(ex));
    }

    [Fact]
    public void Returns_False_For_SshException_With_Disconnect_Message()
    {
        var ex = new SshException("Disconnect by application");
        Assert.False(EmbeddedSftpViewModel.IsPermissionDenied(ex));
    }

    [Fact]
    public void Returns_False_For_SshException_With_Generic_Failure_Message()
    {
        var ex = new SshException("Some other failure");
        Assert.False(EmbeddedSftpViewModel.IsPermissionDenied(ex));
    }

    [Fact]
    public void Returns_False_For_Plain_Exception_With_Permission_Denied_Message()
    {
        // Substring heuristic is gone; we no longer classify by message.
        var ex = new Exception("permission denied");
        Assert.False(EmbeddedSftpViewModel.IsPermissionDenied(ex));
    }

    // ── Other negative cases ──────────────────────────────────────────

    [Fact]
    public void Returns_False_For_SftpPathNotFoundException()
    {
        var ex = new SftpPathNotFoundException("/var/log/missing");
        Assert.False(EmbeddedSftpViewModel.IsPermissionDenied(ex));
    }

    [Fact]
    public void Returns_False_For_Generic_IOException()
    {
        var ex = new IOException("disk full");
        Assert.False(EmbeddedSftpViewModel.IsPermissionDenied(ex));
    }

    // ── Argument validation ───────────────────────────────────────────

    [Fact]
    public void Throws_For_Null_Exception()
    {
        Assert.Throws<ArgumentNullException>(
            () => EmbeddedSftpViewModel.IsPermissionDenied(null!));
    }
}
```

Adjust the `SftpPermissionDeniedException` and `SftpPathNotFoundException` constructor calls if SSH.NET 2025.1.0 requires a different signature — read the type's public constructors and adapt.

### Step 4 — Drop unused locale keys (if any)

Search for any locale key tied to the substring-based behaviour that is no longer reachable. The current code uses `SftpStatusTransferFailed` for the outer catch, which is still relevant. If you find a key that referenced the heuristic specifically (none expected, but check), remove it from both `locales/en.json` and `locales/fr.json` to keep parity green.

### Step 5 — Verify call-site contract is unchanged

Run `grep -n "IsPermissionDenied" src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs`. Expected count: 6 occurrences — one declaration plus five call sites. The signature and the call sites must not change.

## Coding standards

Same as previous prompts:

- Apache 2.0 header on the new test file.
- English only.
- Nullable reference types stay enabled.
- `TreatWarningsAsErrors` is on.
- `ConfigureAwait(false)` is irrelevant here (no async added).
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build is green with zero new warnings; test count goes up by 9 (the eight `IsPermissionDenied` cases plus the null guard). The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite once if it fails on first attempt.

Run the targeted check to make sure the heuristic is gone:

```bash
grep -n "Contains(\"Failure\"" src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs
grep -n "Contains(\"permission denied\"" src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs
grep -n "Contains(\"access denied\"" src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs
grep -n "Contains(\"not permitted\"" src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs
grep -n "SSH_FX_PERMISSION_DENIED" src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs
```

All five must return zero matches. If any matches remain, you missed a branch.

## Reporting back

When you finish, report:

1. The list of source files modified and created.
2. The exact diff of the new `IsPermissionDenied` body (before / after).
3. The list of typed permission exceptions you matched, with a one-line note for each saying whether you confirmed it exists in SSH.NET 2025.1.0 (and how — compile check, NuGet decompile, etc.).
4. The list of tests added (class name + method names).
5. The final test counts (passed / failed / skipped) for both the targeted run on `IsPermissionDeniedTests` and the full suite.
6. Confirm all five `grep` commands above return zero matches.
7. Any decision that diverged from this prompt, with a one-line rationale.
