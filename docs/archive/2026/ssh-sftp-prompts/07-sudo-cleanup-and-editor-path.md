# Prompt 7 — M9 sudo temp cleanup + H2 explicit notepad path

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

This prompt bundles two small P1 fixes from the SSH/SFTP audit. They are independent of each other but both touch the privileged-file edit flow, so it is convenient to ship them together.

**M9** — `EmbeddedSftpViewModel.UploadViaSudoAsync` (`src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs`, ~lines 546-575) currently runs:

```csharp
ssh.RunCommand($"cat {escapedTemp} | sudo tee -- {escaped} > /dev/null && sudo rm -f {escapedTemp}");
```

When `tee` fails (target disk full, sudo policy denies the write, target on a read-only mount, etc.), the `&&` short-circuits and `sudo rm -f` never runs. The remote `/tmp/.heimdall_upload_*` orphan accumulates forever. Over a long-lived session a user can leak hundreds of these files into `/tmp`. The fix is to run the two commands separately and execute the cleanup unconditionally.

**H2** — `RemoteFileEditor.LaunchEditor` (`src/Heimdall.Sftp/RemoteFileEditor.cs`, ~lines 486-518) takes the default editor path (`"notepad.exe"`) and launches it via `Process.Start` with `UseShellExecute=true` and `FileName = localPath`. This delegates to the user's Windows file association: a user who has set `.conf → VS Code` or `.yaml → some-other-tool.exe` will see root-owned files opened in **that** application, not Notepad. For privileged file edits this is a privacy / integrity bug — the file content goes to whatever app the OS thinks should handle the extension. The fix is to resolve `notepad.exe` to its `System32` absolute path and pass the local path via `ArgumentList` with `UseShellExecute=false`.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers items P1 #7 (M9) and P1 #8 (H2). Prompts 1-6 have already shipped.

## Goals

1. **M9** — split the single-string `cat | sudo tee && sudo rm -f` into two `ssh.RunCommand` calls, with the cleanup wrapped in a guaranteed-execution block so it runs even when `tee` failed or threw. Surface the `tee` failure to the caller; log (do not throw on) `rm` failures.
2. **H2** — resolve `notepad.exe` to its absolute `System32` path. Always launch the editor with `UseShellExecute=false` and the local file path passed via `ArgumentList`. Eliminate the `UseShellExecute=true` branch.
3. Add unit tests for the helpers introduced by both fixes. Both helpers must be reachable from `tests/Heimdall.App.Tests` (M9) and `tests/Heimdall.Sftp.Tests` if it exists, otherwise `tests/Heimdall.App.Tests` (H2).

## Background — relevant files

- `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs` — `UploadViaSudoAsync` ~lines 546-575. The method is currently `internal async Task` so it is reachable from tests via `InternalsVisibleTo`.
- `src/Heimdall.Sftp/RemoteFileEditor.cs` — `LaunchEditor` ~lines 486-518. Currently `private static`. We need a tiny refactor so the path-resolution piece is testable.
- `src/Heimdall.Sftp/Heimdall.Sftp.csproj` — should already declare `<InternalsVisibleTo Include="Heimdall.App.Tests" />` after Prompt 4. If that is still in place, reuse it; otherwise add it.
- `src/Heimdall.Sftp/PathEscaper.cs` — already used to escape shell args. No change needed.
- `tests/Heimdall.App.Tests/PlinkFailClosedTests.cs` — example test style for the App test project; uses hand-rolled fakes, not Moq.

## Implementation steps

### M9 — Step 1: Split the `&&` and run cleanup unconditionally

Refactor the body of `UploadViaSudoAsync`:

```csharp
internal async Task UploadViaSudoAsync(string localPath, string remotePath, CancellationToken ct)
{
    if (_browser is null)
    {
        throw new InvalidOperationException("Browser not available for sudo upload.");
    }

    string escaped = PathEscaper.EscapeForShell(remotePath);
    string tempRemote = $"{RemoteTempPrefix}upload_{Guid.NewGuid():N}";

    await _browser.UploadFileAsync(localPath, tempRemote, ct).ConfigureAwait(false);

    using var ssh = await CreateSudoSshClientAsync(ct);
    try
    {
        string escapedTemp = PathEscaper.EscapeForShell(tempRemote);
        int teeExitStatus;
        string teeError;

        try
        {
            using var teeCmd = await Task.Run(
                () => ssh.RunCommand($"cat {escapedTemp} | sudo tee -- {escaped} > /dev/null"),
                ct).ConfigureAwait(false);

            teeExitStatus = teeCmd.ExitStatus;
            teeError = teeCmd.Error ?? string.Empty;
        }
        finally
        {
            // Always cleanup the temp file, even when tee failed or threw.
            // The cleanup is best-effort: a failure to remove the temp file
            // is logged but not propagated, because the user's primary
            // concern is whether tee succeeded.
            await TryRemoveSudoTempAsync(ssh, escapedTemp, tempRemote, ct).ConfigureAwait(false);
        }

        if (teeExitStatus != 0)
        {
            throw new InvalidOperationException(
                $"sudo tee failed (exit {teeExitStatus}): {teeError}");
        }
    }
    finally
    {
        ssh.Disconnect();
    }
}

private static async Task TryRemoveSudoTempAsync(
    Renci.SshNet.SshClient ssh,
    string escapedTempPath,
    string tempPathForLog,
    CancellationToken ct)
{
    try
    {
        using var rmCmd = await Task.Run(
            () => ssh.RunCommand($"sudo rm -f {escapedTempPath}"),
            ct).ConfigureAwait(false);

        if (rmCmd.ExitStatus != 0)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"EmbeddedSftpViewModel: failed to remove sudo upload temp file '{tempPathForLog}' "
                + $"(exit {rmCmd.ExitStatus}): {rmCmd.Error}");
        }
    }
    catch (Exception ex)
    {
        Heimdall.Core.Logging.FileLogger.Warn(
            $"EmbeddedSftpViewModel: exception while removing sudo upload temp file '{tempPathForLog}': {ex.Message}");
    }
}
```

Notes:

- The two `Task.Run` calls happen sequentially. The `try / finally` around the tee call guarantees the cleanup runs even when the tee call throws (cancellation, network drop) — not only when it returns a non-zero exit code.
- The cleanup is done over the same already-connected `ssh` client; we do not open a second SSH session.
- The `tempPathForLog` parameter exists only so the log message uses the unescaped path (more readable for the operator). Do not log the escaped form.

### M9 — Step 2: Extract a testable command-building helper

To make the contract testable without a real SSH server, factor out the two shell command strings into a tiny pure helper. Place it in the same file (no need for a separate file):

```csharp
internal static class SudoUploadCommands
{
    /// <summary>
    /// Builds the two shell commands used by <see cref="UploadViaSudoAsync"/>:
    /// the tee-write command that promotes the temp file to the target path,
    /// and the cleanup command that removes the temp file unconditionally.
    /// Both commands receive their paths shell-escaped via
    /// <see cref="PathEscaper.EscapeForShell"/>.
    /// </summary>
    internal static (string Write, string Cleanup) Build(
        string tempRemotePath,
        string targetRemotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRemotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRemotePath);

        var escapedTemp = PathEscaper.EscapeForShell(tempRemotePath);
        var escapedTarget = PathEscaper.EscapeForShell(targetRemotePath);
        return (
            Write: $"cat {escapedTemp} | sudo tee -- {escapedTarget} > /dev/null",
            Cleanup: $"sudo rm -f {escapedTemp}");
    }
}
```

Then update `UploadViaSudoAsync` to call `SudoUploadCommands.Build(...)` and pass the resulting `Write` / `Cleanup` strings to the `RunCommand` invocations. The body becomes a few lines shorter and the contract — "two commands, one for write, one for cleanup, no `&&` between them" — is now expressible as a unit test.

### M9 — Step 3: Tests for `SudoUploadCommands.Build`

In `tests/Heimdall.App.Tests/SudoUploadCommandsTests.cs`:

```csharp
public sealed class SudoUploadCommandsTests
{
    [Fact]
    public void Build_ProducesTwoSeparateCommands_NoLogicalAnd()
    {
        var (write, cleanup) = SudoUploadCommands.Build("/tmp/.heimdall_upload_xyz", "/etc/hosts");

        Assert.DoesNotContain("&&", write);
        Assert.DoesNotContain(";", write);
        Assert.DoesNotContain("&&", cleanup);

        // Pin exact shape so a future refactor cannot silently re-introduce && fallthrough.
        Assert.Equal("cat '/tmp/.heimdall_upload_xyz' | sudo tee -- '/etc/hosts' > /dev/null", write);
        Assert.Equal("sudo rm -f '/tmp/.heimdall_upload_xyz'", cleanup);
    }

    [Fact]
    public void Build_EscapesPathsContainingSingleQuotes()
    {
        var (write, cleanup) = SudoUploadCommands.Build("/tmp/o'reilly", "/var/log/oh's.log");

        // PathEscaper produces: 'o'\''reilly' for "o'reilly"
        Assert.Contains(@"'/tmp/o'\''reilly'", write);
        Assert.Contains(@"'/var/log/oh'\''s.log'", write);
        Assert.Contains(@"'/tmp/o'\''reilly'", cleanup);
    }

    [Fact]
    public void Build_ThrowsForNullOrWhitespaceTempPath()
    {
        Assert.Throws<ArgumentException>(() => SudoUploadCommands.Build("", "/etc/hosts"));
        Assert.Throws<ArgumentException>(() => SudoUploadCommands.Build(" ", "/etc/hosts"));
    }

    [Fact]
    public void Build_ThrowsForNullOrWhitespaceTargetPath()
    {
        Assert.Throws<ArgumentException>(() => SudoUploadCommands.Build("/tmp/x", ""));
        Assert.Throws<ArgumentException>(() => SudoUploadCommands.Build("/tmp/x", " "));
    }
}
```

The string-literal expectations in test #1 pin the exact wire form. If a future refactor changes how paths are escaped or quoted, that test fails first and forces the change to be intentional.

### H2 — Step 1: Extract a testable editor-path resolver

In `src/Heimdall.Sftp/RemoteFileEditor.cs`, factor the path resolution into a static helper. Place it just above `LaunchEditor`:

```csharp
/// <summary>
/// Resolves the editor command supplied at construction time to an absolute
/// executable path. The default value <c>"notepad.exe"</c> (and any null /
/// whitespace value) is rewritten to the absolute path of
/// <c>%WINDIR%\System32\notepad.exe</c>; explicit caller-supplied paths are
/// returned unchanged. The helper is platform-aware: on non-Windows runtimes
/// it falls back to whatever the caller supplied, since the System32 path
/// does not exist there.
/// </summary>
internal static string ResolveEditorPath(string? editorPath)
{
    var trimmed = editorPath?.Trim();

    var isDefault =
        string.IsNullOrEmpty(trimmed)
        || string.Equals(trimmed, "notepad.exe", StringComparison.OrdinalIgnoreCase);

    if (isDefault && OperatingSystem.IsWindows())
    {
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Path.Combine(systemDir, "notepad.exe");
    }

    // Caller supplied an explicit editor path, or we're not on Windows
    // and cannot resolve System32. Return as-is.
    return string.IsNullOrEmpty(trimmed) ? "notepad.exe" : trimmed;
}
```

### H2 — Step 2: Rewrite `LaunchEditor` to a single launch path

Replace the current branching `LaunchEditor` body with one path:

```csharp
private static void LaunchEditor(string editorPath, string localPath)
{
    Process? proc = null;
    try
    {
        var resolved = ResolveEditorPath(editorPath);
        var psi = new ProcessStartInfo
        {
            FileName = resolved,
            UseShellExecute = false
        };
        // ArgumentList performs proper Win32-aware quoting per arg, so a
        // local path containing quotes / spaces / special chars cannot
        // break out of the editor argument.
        psi.ArgumentList.Add(localPath);
        proc = Process.Start(psi);
    }
    finally
    {
        proc?.Dispose();
    }
}
```

The old `else { proc = Process.Start(new ProcessStartInfo { FileName = localPath, UseShellExecute = true }); }` branch is gone. Privileged file content can no longer be redirected through a Windows file association.

### H2 — Step 3: Tests for `ResolveEditorPath`

In `tests/Heimdall.App.Tests/ResolveEditorPathTests.cs` (the helper lives in `Heimdall.Sftp` and `Heimdall.Sftp.csproj` already exposes internals to `Heimdall.App.Tests` after Prompt 4):

```csharp
public sealed class ResolveEditorPathTests
{
    [Fact]
    public void NullInput_ReturnsNotepad()
    {
        var result = RemoteFileEditor.ResolveEditorPath(null);

        if (OperatingSystem.IsWindows())
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "notepad.exe");
            Assert.Equal(expected, result);
        }
        else
        {
            Assert.Equal("notepad.exe", result);
        }
    }

    [Fact]
    public void EmptyInput_ReturnsNotepad()
    {
        var result = RemoteFileEditor.ResolveEditorPath(string.Empty);
        AssertResolvedToNotepad(result);
    }

    [Fact]
    public void WhitespaceInput_ReturnsNotepad()
    {
        var result = RemoteFileEditor.ResolveEditorPath("   ");
        AssertResolvedToNotepad(result);
    }

    [Fact]
    public void ExplicitNotepad_ReturnsAbsoluteSystemPath()
    {
        var result = RemoteFileEditor.ResolveEditorPath("notepad.exe");
        AssertResolvedToNotepad(result);
    }

    [Fact]
    public void CustomEditorPath_IsReturnedUnchanged()
    {
        const string custom = @"C:\Program Files\Notepad++\notepad++.exe";
        Assert.Equal(custom, RemoteFileEditor.ResolveEditorPath(custom));
    }

    [Fact]
    public void CustomEditorPath_TrimsWhitespace()
    {
        const string custom = @"C:\Program Files\VS Code\Code.exe";
        Assert.Equal(custom, RemoteFileEditor.ResolveEditorPath("  " + custom + "  "));
    }

    private static void AssertResolvedToNotepad(string actual)
    {
        if (OperatingSystem.IsWindows())
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "notepad.exe");
            Assert.Equal(expected, actual);
        }
        else
        {
            Assert.Equal("notepad.exe", actual);
        }
    }
}
```

The platform branch matters because CI may run the test suite on Linux for cross-checking; the Windows-only paths must not break those runs.

If `RemoteFileEditor.ResolveEditorPath` cannot be reached from the test project (i.e. the helper ends up `private static`), promote it to `internal static`. `Heimdall.Sftp.csproj` already declares `<InternalsVisibleTo Include="Heimdall.App.Tests" />` after Prompt 4 — verify that is still in place.

### Sanity checks

After both fixes, the following greps must return zero matches (the old patterns we eliminated):

```bash
grep -n "&& sudo rm -f" src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs
grep -n "UseShellExecute = true" src/Heimdall.Sftp/RemoteFileEditor.cs
grep -n "FileName = localPath" src/Heimdall.Sftp/RemoteFileEditor.cs
```

All three must be zero in their respective files. (`UseShellExecute = true` is fine elsewhere in the codebase if other features rely on it — only `RemoteFileEditor.cs` is in scope here.)

## Coding standards

- Apache 2.0 header on the new test files and any new C# file you create (this prompt likely creates two test files plus possibly a small helper file — keep helpers inside their parent file when they fit).
- English only.
- Nullable reference types stay enabled.
- `TreatWarningsAsErrors` is on.
- `ConfigureAwait(false)` on every new `await` in non-UI projects (the `Heimdall.App` project keeps its existing convention).
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build green, zero new warnings; suite passing count rises by 10 (4 `SudoUploadCommands` tests + 6 `ResolveEditorPath` tests). The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite once if it fails on first attempt.

## Reporting back

When you finish, report:

1. The list of source files modified or created.
2. The list of tests added (class name + each method name).
3. The final test counts (passed / failed / skipped) for both the targeted runs (`SudoUploadCommandsTests`, `ResolveEditorPathTests`) and the full suite.
4. Confirm the three grep checks above return zero matches.
5. Any decision that diverged from this prompt, with a one-line rationale.
6. The exact diff of the new `UploadViaSudoAsync` body (M9) and the new `LaunchEditor` body (H2), inline in the report.
