# Prompt 11 — SshShellSession teardown hygiene (H4)

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

The SSH/SFTP audit identified an unsafe disposal order in `SshShellSession.StopReadLoop` (`src/Heimdall.Ssh/SshShellSession.cs` ~lines 269-294). The current code:

```csharp
private void StopReadLoop()
{
    if (_readCts is not null)
    {
        try
        {
            _readCts.Cancel();
            _readLoopTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException or ObjectDisposedException))
        {
            // Expected from task cancellation during teardown
        }
        catch (AggregateException ex)
        {
            Core.Logging.FileLogger.Warn(...);
        }
        finally
        {
            _readCts.Dispose();      // ← unsafe if Wait timed out
            _readCts = null;
            _readLoopTask = null;    // ← reference dropped before we know task ended
        }
    }
}
```

Two problems:

1. **Unconditional CTS dispose**: if the 500 ms `Wait` times out (rare but possible — SSH.NET can block inside a native pipe `ReadAsync` waiting for the other end to close the channel), the loop is still running and is now coupled to a freshly-disposed `CancellationTokenSource`. The next `cancellationToken.IsCancellationRequested` access from inside the loop hits `ObjectDisposedException`, which the loop's catch-all `Exception` block then routes to `SshSessionFailureDispatcher.Dispatch(...)` — surfacing a "security disconnect" that did not happen.
2. **Lost task reference**: `_readLoopTask = null` in the `finally` drops our handle to the still-running task. If `Dispose()` is called next, we have nothing to wait on; the orphan task is left to crash on its own and the unobserved exception lands in `TaskScheduler.UnobservedTaskException`.

The fix preserves the task reference, defers the CTS disposal until either the task finished or `Dispose()` runs out of patience, and adds defensive `_disposed` checks inside the read loop so a late completion does not raise `DataReceived` / `Disconnected` on a torn-down session.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P2 #14 (H4) only. Prompts 1-10 have already shipped.

## Goal

1. Rewrite `StopReadLoop` so it cancels the CTS, waits **without** disposing the CTS or dropping the task reference, and logs a clear warning when the wait times out.
2. Move the CTS disposal into `Dispose()`, after a final longer wait (2 s). If the task is still running at that point, log an error and accept the leak rather than block shutdown.
3. Add `_disposed` guards inside `ReadLoopAsync` so a late-arriving read or a stuck `ReadAsync` that completes after disposal does not raise events on a torn-down session.
4. Make `Disconnect()` (the public method) call into the same teardown path so external callers get the same disposal-ordering guarantees.
5. Tests pin the disposal contract: `Dispose()` is idempotent, the read loop completes cleanly on cancellation, and a stuck loop does not block `Dispose()` longer than ~2.5 s.

## Background — relevant files

- `src/Heimdall.Ssh/SshShellSession.cs`:
  - `Disconnect()` (~lines 195-207).
  - `Dispose()` (~lines 209-221).
  - `ReadLoopAsync` (~lines 227-267) — already wired through `SshSessionFailureDispatcher.Dispatch` from Prompt 6.
  - `StopReadLoop` (~lines 269-294) — the main change site.
  - `CleanupStream` and `DisconnectClient` (~lines 297-330) — leave these unchanged; they already handle their own idempotent disposal.
- `tests/Heimdall.Ssh.Tests/SshShellSessionResizeTests.cs` — the only file that constructs `SshShellSession` directly today; the new tests can live in the same project as a sibling file.

`Heimdall.Ssh.csproj` already declares `<InternalsVisibleTo Include="Heimdall.Ssh.Tests" />`.

## Implementation steps

### Step 1 — Add a private read-loop teardown timeout constant

At the top of `SshShellSession.cs`, near the existing `private const int ReadBufferSize = 8192;`:

```csharp
/// <summary>
/// Best-effort wait for <see cref="ReadLoopAsync"/> to honour cancellation
/// before we proceed with stream / client disposal. A loop blocked inside
/// a native pipe read may exceed this window; that is logged but does not
/// block <see cref="Disconnect"/>.
/// </summary>
private static readonly TimeSpan StopReadLoopGraceful = TimeSpan.FromMilliseconds(500);

/// <summary>
/// Final wait inside <see cref="Dispose"/> for the read loop to complete
/// before we force-dispose the cancellation source. After this window, the
/// task is considered leaked and we log an error rather than block forever.
/// </summary>
private static readonly TimeSpan StopReadLoopFinal = TimeSpan.FromSeconds(2);
```

### Step 2 — Rewrite `StopReadLoop`

```csharp
/// <summary>
/// Signals the read loop to stop and waits up to
/// <see cref="StopReadLoopGraceful"/> for it to honour the cancellation.
/// Does <b>not</b> dispose the cancellation source or drop the task
/// reference — that happens in <see cref="Dispose"/> after a final wait,
/// so a slow loop cannot observe a disposed CTS mid-await.
/// </summary>
/// <returns>
/// <c>true</c> if the read loop completed within the grace window;
/// <c>false</c> if the wait timed out (the task is still running and
/// <see cref="_readLoopTask"/> remains non-null).
/// </returns>
private bool StopReadLoop()
{
    var cts = _readCts;
    if (cts is null)
    {
        return true;
    }

    try
    {
        cts.Cancel();
    }
    catch (ObjectDisposedException)
    {
        // CTS was already disposed by a previous teardown attempt;
        // the read loop has either ended or is observing a stale token.
        return _readLoopTask is null;
    }

    var task = _readLoopTask;
    if (task is null)
    {
        return true;
    }

    try
    {
        if (!task.Wait(StopReadLoopGraceful))
        {
            Core.Logging.FileLogger.Warn(
                "SshShellSession: read loop did not honour cancellation within "
                + $"{StopReadLoopGraceful.TotalMilliseconds:F0} ms; will retry during Dispose.");
            return false;
        }
    }
    catch (AggregateException ex) when (ex.InnerExceptions.All(static e => e is OperationCanceledException or ObjectDisposedException))
    {
        // Expected: cancellation observed cleanly.
    }
    catch (AggregateException ex)
    {
        Core.Logging.FileLogger.Warn(
            $"SshShellSession read loop stop: {ex.InnerException?.Message ?? ex.Message}");
    }

    return true;
}
```

### Step 3 — Rewrite `Dispose`

The existing `Dispose` calls `StopReadLoop`, then `CleanupStream`, then `DisconnectClient`. We change the order so the final wait happens after `StopReadLoop` returns false, and the CTS dispose moves here:

```csharp
public void Dispose()
{
    if (_disposed)
    {
        return;
    }

    _disposed = true;

    var loopExited = StopReadLoop();

    if (!loopExited && _readLoopTask is { } pending)
    {
        try
        {
            if (!pending.Wait(StopReadLoopFinal))
            {
                Core.Logging.FileLogger.Error(
                    "SshShellSession: read loop is still running after a "
                    + $"{StopReadLoopFinal.TotalSeconds:F0}-second wait during Dispose. "
                    + "Underlying SSH.NET pipe may be stuck; task will be leaked.");
            }
        }
        catch (AggregateException)
        {
            // Already observed by the loop's own catch handlers.
        }
    }

    // Now safe to dispose the CTS: either the task completed, or it has
    // exceeded our patience and we accept the leak rather than block
    // application shutdown.
    try { _readCts?.Dispose(); }
    catch (ObjectDisposedException) { /* tolerated */ }
    _readCts = null;
    _readLoopTask = null;

    CleanupStream();
    DisconnectClient();
}
```

### Step 4 — Make `Disconnect()` go through the same path

```csharp
public void Disconnect()
{
    if (_disposed || _client is null)
    {
        return;
    }

    var loopExited = StopReadLoop();
    if (!loopExited && _readLoopTask is { } pending)
    {
        // Disconnect is best-effort: give the loop the same final window
        // as Dispose so the SSH client teardown does not race with a
        // still-running read.
        try { pending.Wait(StopReadLoopFinal); }
        catch (AggregateException) { /* observed */ }
    }

    try { _readCts?.Dispose(); }
    catch (ObjectDisposedException) { /* tolerated */ }
    _readCts = null;
    _readLoopTask = null;

    CleanupStream();
    DisconnectClient();

    Disconnected?.Invoke(null);
}
```

The `Disconnected?.Invoke(null)` at the end stays — `Disconnect()` is the user-initiated graceful disconnect, and consumers expect the legacy event to fire.

### Step 5 — Add `_disposed` guards inside `ReadLoopAsync`

Modify the existing read loop so a late-completing `ReadAsync` cannot raise events on a torn-down session:

```csharp
private async Task ReadLoopAsync(CancellationToken cancellationToken)
{
    var buffer = new byte[ReadBufferSize];

    try
    {
        while (!cancellationToken.IsCancellationRequested
               && !_disposed
               && _stream is not null)
        {
            int bytesRead;

            try
            {
                bytesRead = await _stream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            // Re-check after the await: if Dispose ran while we were
            // blocked on ReadAsync, do not surface the result.
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (bytesRead <= 0)
            {
                break;
            }

            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            DataReceived?.Invoke(chunk);
        }
    }
    catch (OperationCanceledException)
    {
        // Normal cancellation during Disconnect/Dispose.
    }
    catch (Exception ex)
    {
        if (!_disposed)
        {
            SshSessionFailureDispatcher.Dispatch(
                ex,
                SecurityEventOccurred,
                Disconnected);
        }
    }
}
```

The two new guards are: `&& !_disposed` in the loop condition, and the `if (_disposed || cancellationToken.IsCancellationRequested) break;` immediately after the `ReadAsync` await.

### Step 6 — Tests

Create `tests/Heimdall.Ssh.Tests/SshShellSessionTeardownTests.cs`. The tests focus on contract invariants we **can** verify without a real SSH server:

1. **`Dispose_OnUnconnectedSession_DoesNotThrow`**

   ```csharp
   var session = new SshShellSession();
   session.Dispose();
   // No assertion needed: must not throw.
   session.Dispose();  // Idempotent — second call is a no-op.
   ```

2. **`Disconnect_OnUnconnectedSession_DoesNotThrow`**

   Same shape: a freshly-constructed session has no `_client`, so `Disconnect` must short-circuit and not throw.

3. **`Dispose_AfterDisconnect_IsIdempotent`**

   ```csharp
   var session = new SshShellSession();
   session.Disconnect();
   session.Dispose();
   session.Dispose();
   ```

4. **`IsConnected_ReturnsFalse_WhenNotConnected`**

   ```csharp
   using var session = new SshShellSession();
   Assert.False(session.IsConnected);
   ```

5. **`Dispose_DoesNotBlockBeyondTotalWait`** (timing contract)

   ```csharp
   using var session = new SshShellSession();
   var sw = Stopwatch.StartNew();
   session.Dispose();
   sw.Stop();

   // Without a connected client the dispose path is essentially a no-op,
   // so this is a sanity bound rather than a real timing test.
   Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1),
       $"Dispose took {sw.ElapsedMilliseconds} ms on an unconnected session.");
   ```

6. **`Resize_OnDisposedSession_Throws`**

   ```csharp
   var session = new SshShellSession();
   session.Dispose();
   Assert.Throws<ObjectDisposedException>(() => session.Resize(80, 24));
   ```

   This exercises `ObjectDisposedException.ThrowIf(_disposed, this)` already present in `Resize`, and confirms the new `_disposed` flag flips correctly.

7. **`Write_OnDisposedSession_Throws`** — analogous to the Resize test.

The harder contract invariants — "the loop does not raise `DataReceived` after disposal" and "the loop does not surface an `ObjectDisposedException` from a disposed CTS as a security event" — would require injecting a stub `ShellStream`, which SSH.NET does not expose for mocking. A future refactor could introduce a `IShellStreamReader` seam, but it is out of scope here. Document the gap in the report.

### Step 7 — Sanity checks

After the change:

```bash
grep -n "_readCts.Dispose" src/Heimdall.Ssh/SshShellSession.cs
```

Should return exactly **one** match — the dispose now lives only inside `Dispose()` (or wrapped in a `try` around `_readCts?.Dispose()`). The old finally-block dispose inside `StopReadLoop` is gone.

```bash
grep -n "_readLoopTask = null" src/Heimdall.Ssh/SshShellSession.cs
```

Should return matches only inside `Dispose` and `Disconnect`, **not** inside `StopReadLoop`.

```bash
grep -n "if (_disposed" src/Heimdall.Ssh/SshShellSession.cs
```

Should now show at least one match inside `ReadLoopAsync` (the post-await guard) on top of the existing top-of-method guards in `Write`, `Resize`, etc.

## Coding standards

Same as previous prompts:

- Apache 2.0 header on the new test file.
- English only.
- Nullable reference types stay enabled.
- `TreatWarningsAsErrors` is on.
- `ConfigureAwait(false)` already applies to all awaits in the file; preserve it.
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build green with zero new warnings; suite passing count rises by 7 (or 6 if you fold the `Write`/`Resize` disposed tests into one parameterised theory). The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite once if it fails on first attempt.

## Reporting back

When you finish, report:

1. The list of source files modified or created.
2. The exact diff of the new `StopReadLoop` body, the new `Dispose` body, and the modified `ReadLoopAsync` (the two guard insertions).
3. The list of tests added (class name + each method name).
4. The final test counts (passed / failed / skipped) for both the targeted run on `SshShellSessionTeardownTests` and the full suite.
5. Confirm the three grep checks above produce the expected counts.
6. Any decision that diverged from this prompt, with a one-line rationale.
