# Prompt 9 — RemoteFileEditor upload task tracking + cancellation (M3 / A5)

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

The SSH/SFTP audit identified that `RemoteFileEditor` triggers auto-uploads via `_ = OnFileChangedAsync(session)` (`src/Heimdall.Sftp/RemoteFileEditor.cs` ~line 329). Three problems flow from this fire-and-forget shape:

1. **Cancellation does not reach in-flight uploads.** When the user closes an edit session via `CloseEdit(remotePath)` or the editor itself is disposed, the active upload `Task` continues running until it succeeds or the network drops. For sudo uploads this can mean a privileged write completes after the user thought they had aborted.
2. **Exceptions are unobserved.** The bare `throw;` at the end of the existing `catch (HostKeyRejectedException)` block (added by Prompt 4) re-raises the security exception **into a fire-and-forget task** that nothing awaits. The exception lands in the `TaskScheduler.UnobservedTaskException` event with no signal back to the caller. The `HostKeyRotatedDuringUpload` event we added in Prompt 4 is the actual UI signal we want, so the bare `throw;` adds zero value and pollutes the unobserved-task pipeline.
3. **`Dispose()` walks `_activeSessions` and disposes each session synchronously**, but it never cancels the in-flight upload tasks. A `RemoteFileEditor` disposed during a save can leave background tasks holding open SSH connections that outlive the editor.

This prompt makes uploads observable and cancellable: a per-session `CancellationTokenSource` plus a tracked `Task?` field, observed exceptions on every code path, and a `CloseEdit` / `Dispose` that cancels and joins.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P1 #10 (M3 / A5) only — the last P1 item. Prompts 1-8 have already shipped.

## Goal

1. Add `UploadCts` (a `CancellationTokenSource`) and `CurrentUpload` (a `Task?`) fields to `EditSession`.
2. Make `OnFileChanged` track the launched upload `Task` on `session.CurrentUpload` and attach a continuation that observes any uncaught exception (so the unobserved-task pipeline is no longer a sink).
3. Pipe `session.UploadCts.Token` all the way through `OnFileChangedAsync` so cancellation reaches every awaitable point.
4. Make `CloseEdit(remotePath)` cancel the in-flight upload and wait briefly (≤ 2 s) for the task to drain before the session is disposed.
5. Make `Dispose()` cancel and drain every active session's upload before clearing `_activeSessions`.
6. Drop the bare `throw;` from the `HostKeyRejectedException` catch in `OnFileChangedAsync` — `HostKeyRotatedDuringUpload?.Invoke(...)` plus `FileUploaded?.Invoke(remotePath, false)` is the surface the UI consumes; re-raising into a fire-and-forget task is dead code now.
7. Tests pin: cancellation propagates, in-flight uploads are observed, the typed event still fires on host-key rotation, and `Dispose` does not leak running tasks.

## Background — relevant files

- `src/Heimdall.Sftp/RemoteFileEditor.cs`:
  - `OnFileChanged` (~line 327) and `OnFileChangedAsync` (~line 332).
  - The `HostKeyRejectedException` catch (~line 369-380) added by Prompt 4 — this is where the bare `throw;` lives.
  - `CloseEdit` (~line 213-221).
  - `Dispose` (~line 230-246).
  - `EditSession` record (bottom of the file, ~line 524).
- `src/Heimdall.Core/Logging/FileLogger.cs` — used for `Warn` / `Error` logging.
- `tests/Heimdall.App.Tests/RemoteFileEditorRotationTests.cs` — Prompt 4's tests; reuse the `EditSession` access patterns from there. The non-sudo upload path goes through `IRemoteBrowser.UploadFileAsync`, so a hand-rolled fake `IRemoteBrowser` is enough to exercise tracking and cancellation without touching real SSH.

`Heimdall.Sftp.csproj` already declares `<InternalsVisibleTo Include="Heimdall.App.Tests" />` after Prompt 4. The new fields you add to `EditSession` are reachable from the test project as `internal`.

## Implementation steps

### Step 1 — Augment `EditSession`

In `src/Heimdall.Sftp/RemoteFileEditor.cs`, extend the `EditSession` record-class:

```csharp
internal sealed class EditSession : IDisposable
{
    public required string RemotePath { get; init; }
    public required string LocalPath { get; init; }
    public bool IsSudo { get; init; }
    public SshConnectionParams? SshParams { get; init; }
    public PinnedFingerprintVerifier? Verifier { get; init; }
    public FileSystemWatcher? Watcher { get; set; }
    public SemaphoreSlim UploadSemaphore { get; } = new(1, 1);
    public DateTime LastUploadTime { get; set; }

    /// <summary>
    /// Cancellation source for in-flight upload tasks tied to this session.
    /// Cancelled by <see cref="RemoteFileEditor.CloseEdit"/> and
    /// <see cref="RemoteFileEditor.Dispose"/> so background uploads do not
    /// outlive the edit session.
    /// </summary>
    public CancellationTokenSource UploadCts { get; } = new();

    /// <summary>
    /// The currently-running upload <see cref="Task"/>, or null when no
    /// upload is in flight. Set by <see cref="RemoteFileEditor.OnFileChanged"/>
    /// and observed during teardown so exceptions are not swallowed by the
    /// unobserved-task pipeline.
    /// </summary>
    public Task? CurrentUpload { get; set; }

    public bool ShouldUpload =>
        (DateTime.UtcNow - LastUploadTime) >= RemoteFileEditor.UploadDebounceInterval;

    public void Dispose()
    {
        Watcher?.Dispose();
        UploadSemaphore.Dispose();
        try { UploadCts.Dispose(); }
        catch (ObjectDisposedException) { /* tolerated */ }
        GC.SuppressFinalize(this);
    }
}
```

### Step 2 — Track and observe the upload task

Replace the body of `OnFileChanged`:

```csharp
private void OnFileChanged(EditSession session)
{
    if (_disposed)
    {
        return;
    }

    var token = session.UploadCts.Token;
    var task = OnFileChangedAsync(session, token);

    // Attach a synchronous observer so any uncaught exception is logged and
    // the task is marked observed (no UnobservedTaskException). The continuation
    // runs on the thread pool and never throws.
    task.ContinueWith(
        static (t, state) =>
        {
            if (t.IsFaulted && t.Exception is { } agg)
            {
                var inner = agg.Flatten().InnerException ?? agg;
                Heimdall.Core.Logging.FileLogger.Warn(
                    $"RemoteFileEditor: upload task observed exception for session: {inner.GetType().Name}: {inner.Message}");
            }
        },
        state: null,
        cancellationToken: CancellationToken.None,
        continuationOptions: TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        scheduler: TaskScheduler.Default);

    session.CurrentUpload = task;
}
```

### Step 3 — Pipe cancellation through `OnFileChangedAsync`

Change the signature of `OnFileChangedAsync` to accept a `CancellationToken`:

```csharp
private async Task OnFileChangedAsync(EditSession session, CancellationToken ct)
{
    if (!session.ShouldUpload)
    {
        return;
    }

    if (!await session.UploadSemaphore.WaitAsync(0, ct).ConfigureAwait(false))
    {
        return;
    }

    bool success;
    try
    {
        ct.ThrowIfCancellationRequested();
        session.LastUploadTime = DateTime.UtcNow;

        if (session.IsSudo && session.SshParams is not null)
        {
            await UploadWithSudoAsync(session, ct).ConfigureAwait(false);
        }
        else
        {
            await _browser.UploadFileAsync(session.LocalPath, session.RemotePath, ct).ConfigureAwait(false);
        }

        success = true;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        Heimdall.Core.Logging.FileLogger.Info(
            $"RemoteFileEditor: upload cancelled for {session.RemotePath}.");
        FileUploaded?.Invoke(session.RemotePath, false);
        return;
    }
    catch (Heimdall.Ssh.HostKeyRejectedException ex)
    {
        Heimdall.Core.Logging.FileLogger.Error(
            $"RemoteFileEditor: host key rejected during upload of {session.RemotePath} "
            + $"({ex.Host}:{ex.Port}, presented={ex.PresentedFingerprint}, stored={ex.StoredFingerprint ?? "<none>"}). Upload aborted.");

        HostKeyRotatedDuringUpload?.Invoke(new HostKeyRotationEvent(
            session.RemotePath,
            ex.PresentedFingerprint,
            ex.StoredFingerprint,
            ex.Host,
            ex.Port));

        FileUploaded?.Invoke(session.RemotePath, false);
        return;  // ← drop the bare `throw;` from Prompt 4
    }
    catch (Exception ex)
    {
        success = false;
        Heimdall.Core.Logging.FileLogger.Warn(
            $"RemoteFileEditor auto-upload failed for {session.RemotePath}: {ex.Message}");
    }
    finally
    {
        try { session.UploadSemaphore.Release(); }
        catch (ObjectDisposedException) { /* race with Dispose, tolerated */ }
    }

    FileUploaded?.Invoke(session.RemotePath, success);
}
```

Notes:

- `ct` flows into `_browser.UploadFileAsync` so cancellation interrupts the underlying SFTP transfer at the next await point.
- For sudo uploads: `UploadWithSudoAsync` currently does not accept a `CancellationToken`. Add an optional `CancellationToken ct = default` parameter to it and propagate the token to the existing `Task.Run(...)` call sites and to the `RunCommand` calls inside.
- The `OperationCanceledException` catch is placed before the `HostKeyRejectedException` catch because cancellation can occur during host-key resolution; we want it classified as cancellation, not as a security event.
- The bare `throw;` is gone. The earlier rationale ("keep the existing rethrow so the unhandled-task pipeline still sees it") is obsolete: the task is now tracked on `session.CurrentUpload`, and the continuation in `OnFileChanged` observes it explicitly. Re-raising into the fire-and-forget pipeline added zero value.

### Step 4 — Cancel and drain on `CloseEdit`

Replace `CloseEdit` body:

```csharp
public void CloseEdit(string remotePath)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);

    if (!_activeSessions.TryRemove(remotePath, out var session))
    {
        return;
    }

    DrainSession(session);
    CleanupTempFile(session.LocalPath);
}

private static readonly TimeSpan UploadDrainTimeout = TimeSpan.FromSeconds(2);

private static void DrainSession(EditSession session)
{
    try
    {
        session.UploadCts.Cancel();
    }
    catch (ObjectDisposedException) { /* tolerated */ }

    var pending = session.CurrentUpload;
    if (pending is not null)
    {
        try
        {
            // Wait briefly for the in-flight upload to observe the cancellation.
            // The continuation attached in OnFileChanged guarantees any thrown
            // exception is already logged; this Wait is purely for ordering so
            // the temp file is not deleted while a task still holds it open.
            pending.Wait(UploadDrainTimeout);
        }
        catch (AggregateException ex)
            when (ex.InnerExceptions.All(static e => e is OperationCanceledException or TaskCanceledException))
        {
            // Expected: cancellation observed cleanly.
        }
        catch (AggregateException ex)
        {
            // Already logged by the continuation. Swallow here so CloseEdit stays sync-safe.
            Heimdall.Core.Logging.FileLogger.Warn(
                $"RemoteFileEditor: drain saw exception while closing edit: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    session.Dispose();
}
```

### Step 5 — Update `Dispose`

```csharp
public void Dispose()
{
    if (_disposed)
    {
        return;
    }

    _disposed = true;

    foreach (var kvp in _activeSessions)
    {
        DrainSession(kvp.Value);
        CleanupTempFile(kvp.Value.LocalPath);
    }

    _activeSessions.Clear();
}
```

`DrainSession` is the single source of truth for tearing a session down. `CloseEdit` and `Dispose` route through it.

### Step 6 — Tests

Create `tests/Heimdall.App.Tests/RemoteFileEditorTaskTrackingTests.cs`. Tests use a hand-rolled fake `IRemoteBrowser` that lets each test control how `UploadFileAsync` behaves (success / delay / throw / cancellation-aware).

The fake browser should expose hooks the test can use:

```csharp
internal sealed class FakeRemoteBrowser : IRemoteBrowser
{
    public Func<string, string, CancellationToken, Task>? UploadCallback { get; set; }
    public int UploadCallCount;
    public bool IsConnected => true;
    public string CurrentDirectory => "/";

    public event Action<string>? DirectoryChanged;
    public event Action<SftpTransferProgress>? TransferProgress;
    public event Action<string?>? Disconnected;

    public Task UploadFileAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        Interlocked.Increment(ref UploadCallCount);
        return UploadCallback?.Invoke(localPath, remotePath, ct) ?? Task.CompletedTask;
    }

    public Task DownloadFileAsync(string remotePath, string localPath, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task<IReadOnlyList<SftpFileInfo>> ListDirectoryAsync(string? path = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SftpFileInfo>>(Array.Empty<SftpFileInfo>());
    public Task<string> GetCurrentDirectoryAsync(CancellationToken ct = default) => Task.FromResult("/");
    public Task ChangeDirectoryAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
    public Task ChmodAsync(string path, short mode, CancellationToken ct = default) => Task.CompletedTask;
    public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default) => Task.CompletedTask;
    public void Disconnect() { }
    public void Dispose() { }
}
```

Adjust the interface members to match the actual `IRemoteBrowser` shape — open `src/Heimdall.Sftp/IRemoteBrowser.cs` if any signature drifted.

Required test methods:

1. **`OnFileChanged_Tracks_CurrentUpload_OnSession`** — Construct an editor with the fake browser. Drive `OnFileChanged` indirectly by triggering the watcher path (or by calling an `internal` test hook — see below). Assert `session.CurrentUpload` is non-null after the trigger and becomes complete after the fake browser's task completes.

   To drive the path without setting up `FileSystemWatcher`, expose an `internal void TriggerOnFileChangedForTesting(EditSession session)` method on `RemoteFileEditor` that just calls the existing private `OnFileChanged(session)`. Document this as the test seam in the report.

2. **`CloseEdit_Cancels_InFlightUpload`** — The fake browser's `UploadCallback` honours `ct` (waits with `Task.Delay(infinite, ct)`). After triggering an upload, call `editor.CloseEdit(remotePath)`. Assert:
   - The upload task completes (faulted with `TaskCanceledException` or completes due to cancellation).
   - `session.UploadCts.IsCancellationRequested` is true.
   - `_activeSessions` no longer contains `remotePath`.

3. **`Dispose_Cancels_All_InFlightUploads`** — Same as #2 but with two active sessions. After `editor.Dispose()`, both upload tasks observe cancellation.

4. **`OnFileChangedAsync_HostKeyRejected_RaisesEvent_DoesNotRethrow`** — `UploadCallback` throws `HostKeyRejectedException`. After the upload task completes:
   - `session.CurrentUpload.IsFaulted` is **false** — the task completes normally because the exception is caught and converted to events.
   - Exactly one `HostKeyRotatedDuringUpload` event was raised with the expected fingerprints / host / port.
   - Exactly one `FileUploaded(remotePath, false)` event was raised.
   - `TaskScheduler.UnobservedTaskException` was **not** triggered for this task. (You can probe this by attaching to `TaskScheduler.UnobservedTaskException` for the duration of the test and asserting no event arrived after a `GC.Collect(); GC.WaitForPendingFinalizers();` cycle. If that proves flaky, drop this assertion and document it in the report; the explicit check on `IsFaulted == false` already covers the "exception observed" contract.)

5. **`OnFileChangedAsync_Cancellation_RaisesFileUploadedFalse`** — `UploadCallback` honours `ct`; cancel via `session.UploadCts.Cancel()`. Assert `FileUploaded(remotePath, false)` was raised exactly once and the task completes (does not fault on `OperationCanceledException`).

6. **`EditSession_Dispose_DisposesUploadCts`** — Pure structural test. Construct an `EditSession` directly. Dispose it. Assert calling `session.UploadCts.Token.IsCancellationRequested` after disposal does not throw — the property reads still work post-dispose, but `Cancel()` would throw.

7. **`CloseEdit_AfterUploadCompletes_DoesNotThrow`** — Trigger an upload that completes immediately. Wait for completion. Then call `CloseEdit`. Assert no exception is raised; the session is removed from `_activeSessions`.

Tests #1 to #5 may need a few hundred ms of `Task.Delay` or `Task.WhenAny(task, Task.Delay(500))` to give the upload task time to reach the cancellation point. Use 500 ms as the default test timeout for synchronisation; if a CI machine proves flaky, raise to 2 s but document the change.

To inspect a session from a test (the dictionary `_activeSessions` is private), add a tiny `internal IReadOnlyDictionary<string, EditSession> ActiveSessionsForTesting => _activeSessions;` helper on `RemoteFileEditor`. This is the same pattern already used by other tests in the repo where private state needs structural assertions.

If exposing the test seams (`TriggerOnFileChangedForTesting`, `ActiveSessionsForTesting`) feels invasive, you may inline-define them as `internal` partial-class members in a new file `RemoteFileEditor.TestHooks.cs` so the test surface is visually separated from the production logic. Either approach is acceptable — pick the one that keeps the production file readable.

### Step 7 — Sanity checks

After the change:

```bash
grep -n "throw;" src/Heimdall.Sftp/RemoteFileEditor.cs
```

Should return zero matches. The bare rethrow is gone. (Other `throw new ...` lines are fine.)

```bash
grep -n "_ = OnFileChangedAsync" src/Heimdall.Sftp/RemoteFileEditor.cs
```

Should return zero matches. The fire-and-forget pattern is replaced by tracked-task assignment.

```bash
grep -n "session.CurrentUpload\|session.UploadCts" src/Heimdall.Sftp/RemoteFileEditor.cs
```

Should return at least four matches (one assignment in `OnFileChanged`, one cancellation in `DrainSession`, one wait in `DrainSession`, one disposal in `EditSession.Dispose`).

## Coding standards

Same as previous prompts:

- Apache 2.0 header on any new file.
- English only.
- Nullable reference types stay enabled.
- `TreatWarningsAsErrors` is on.
- `ConfigureAwait(false)` on every new `await` you introduce in non-UI projects.
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build green with zero new warnings; suite passing count rises by 7. The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite once if it fails on first attempt.

## Reporting back

When you finish, report:

1. The list of source files modified or created.
2. The exact diff of the new `OnFileChanged` body, the new `OnFileChangedAsync` signature + body, and the new `DrainSession` helper, inline in the report.
3. The list of tests added (class name + each method name) and whether you kept or dropped the `UnobservedTaskException` probe in test #4.
4. The final test counts (passed / failed / skipped) for both the targeted run on `RemoteFileEditorTaskTrackingTests` and the full suite.
5. Confirm the three grep checks above return the expected counts.
6. Any decision that diverged from this prompt, with a one-line rationale (especially: which test seam approach you chose for `TriggerOnFileChangedForTesting` / `ActiveSessionsForTesting`).
