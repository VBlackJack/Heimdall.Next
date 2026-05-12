# Prompt 6 — Typed mid-session security events (C4)

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

The SSH/SFTP audit identified that `SftpBrowser` and `SshShellSession` both collapse mid-session SSH errors into a plain `Disconnected(string? message)` event. When SSH.NET raises a `HostKeyRejectedException` from inside the established session (for example, after a renegotiation that picks a different host key), the UI sees the same event shape as a benign TCP reset. The user has no way to know they may have been MITM'd in mid-session.

The two collapse sites:

- `src/Heimdall.Sftp/SftpBrowser.cs` — `OnErrorOccurred` (~lines 488-491):

  ```csharp
  private void OnErrorOccurred(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
  {
      Disconnected?.Invoke(e.Exception.Message);
  }
  ```

- `src/Heimdall.Ssh/SshShellSession.cs` — the catch-all in `ReadLoopAsync` (~lines 261-267):

  ```csharp
  catch (Exception ex)
  {
      if (!_disposed)
      {
          Disconnected?.Invoke(ex.Message);
      }
  }
  ```

This prompt introduces a typed `SshSessionSecurityEvent` record, exposes a `SecurityEventOccurred` event on both classes, and routes any typed security exception (starting with `HostKeyRejectedException`) through a shared `SshSessionFailureDispatcher` so both sites stay consistent. The existing `Disconnected` event keeps firing — the new event is **additional**, not a replacement.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P1 #6 (C4) only. Prompts 1-5 have already shipped.

## Goal

1. Introduce a public record `SshSessionSecurityEvent` carrying enough information for the UI to decide what banner to show (failure code, fingerprints, host/port).
2. Extract a static helper `SshSessionFailureDispatcher.Dispatch(...)` that maps a raw exception to either a security event, a plain disconnect, or both.
3. Add `event Action<SshSessionSecurityEvent>? SecurityEventOccurred` to `SftpBrowser` and `SshShellSession`. Both call sites route through the shared dispatcher.
4. Wire the new event in `EmbeddedSftpView.xaml.cs` and the SSH terminal view code-behind so the UI shows a localized MITM banner when the dispatcher reports `HostKeyMismatch`.
5. Add a localization key for the new banner.
6. Add unit tests pinning the dispatcher behaviour.

## Background — relevant files

- `src/Heimdall.Ssh/HostKeyRejectedException.cs` — already carries `Host`, `Port`, `Algorithm`, `PresentedFingerprint`, `StoredFingerprint`, `IsMismatch`. We map directly from these properties.
- `src/Heimdall.Ssh/SshFailureCode.cs` — `HostKeyMismatch` already exists; reuse it. Do not introduce a new code in this prompt.
- `src/Heimdall.Sftp/SftpBrowser.cs` — `Disconnected` event at line 44, `OnErrorOccurred` handler at line 488.
- `src/Heimdall.Ssh/SshShellSession.cs` — `Disconnected` event at line 45, `ReadLoopAsync` catch block at line 261.
- `src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs` — already wires `editor.HostKeyRotatedDuringUpload` (Prompt 4); follow the same pattern for the new browser-level event.
- The SSH terminal view code-behind (look for the file that constructs `SshShellSession` — search `new SshShellSession`) for the symmetric wiring on the SSH side.

## Implementation steps

### Step 1 — Create the security event record

New file `src/Heimdall.Ssh/SshSessionSecurityEvent.cs`:

```csharp
/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * ...
 */

namespace Heimdall.Ssh;

/// <summary>
/// Carries information about a security-relevant SSH session failure that
/// callers (UI layer) must distinguish from a benign network disconnect.
/// Currently produced for mid-session host-key rejections; the type stays
/// open so future security events (e.g. detected key downgrades) can be
/// added without breaking the event surface.
/// </summary>
/// <param name="Code">Structured failure code from <see cref="SshFailureCode"/>.</param>
/// <param name="Message">Human-readable description (often the original exception message).</param>
/// <param name="Host">Remote host the session was using.</param>
/// <param name="Port">Remote port the session was using.</param>
/// <param name="Algorithm">Presented host-key algorithm, when available.</param>
/// <param name="PresentedFingerprint">Fingerprint the server presented during the failure, when available.</param>
/// <param name="StoredFingerprint">Fingerprint Heimdall had pinned, when available.</param>
public sealed record SshSessionSecurityEvent(
    SshFailureCode Code,
    string Message,
    string Host,
    int Port,
    string? Algorithm = null,
    string? PresentedFingerprint = null,
    string? StoredFingerprint = null);
```

### Step 2 — Create the dispatcher

New file `src/Heimdall.Ssh/SshSessionFailureDispatcher.cs`:

```csharp
/*
 * Apache 2.0 header
 */

namespace Heimdall.Ssh;

/// <summary>
/// Routes a raw SSH session exception to the right handler. Typed security
/// failures (currently <see cref="HostKeyRejectedException"/>) are surfaced
/// via the security handler with a structured event; everything else falls
/// through to the legacy disconnect handler with the raw message. Both
/// handlers can be called for the same exception when the failure is also
/// a hard disconnect.
/// </summary>
public static class SshSessionFailureDispatcher
{
    /// <summary>
    /// Dispatch a session failure. The order of invocation is deliberate:
    /// typed security handlers fire first so a UI banner can be shown
    /// before the connection-state event redirects focus elsewhere.
    /// </summary>
    /// <param name="ex">The exception captured by the SSH/SFTP layer.</param>
    /// <param name="securityHandler">Optional handler for typed security events.</param>
    /// <param name="disconnectedHandler">Optional legacy disconnect handler.</param>
    public static void Dispatch(
        Exception ex,
        Action<SshSessionSecurityEvent>? securityHandler,
        Action<string?>? disconnectedHandler)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is HostKeyRejectedException hkr)
        {
            var code = hkr.IsMismatch
                ? SshFailureCode.HostKeyMismatch
                : SshFailureCode.Cancelled;

            securityHandler?.Invoke(new SshSessionSecurityEvent(
                code,
                hkr.Message,
                hkr.Host,
                hkr.Port,
                hkr.Algorithm,
                hkr.PresentedFingerprint,
                hkr.StoredFingerprint));
        }

        // Always notify Disconnected: a security failure is also a session
        // termination, and existing consumers rely on the Disconnected event
        // to drive reconnect / cleanup logic.
        disconnectedHandler?.Invoke(ex.Message);
    }
}
```

This helper is the single source of truth. New typed security cases get added here without rewriting `SftpBrowser` or `SshShellSession` again.

### Step 3 — Add the event surface to `SftpBrowser`

In `src/Heimdall.Sftp/SftpBrowser.cs`:

```csharp
/// <summary>
/// Raised when a security-relevant failure (e.g. mid-session host-key
/// rejection) occurs. Fired in addition to <see cref="Disconnected"/>.
/// </summary>
public event Action<SshSessionSecurityEvent>? SecurityEventOccurred;
```

Replace the `OnErrorOccurred` body with a call to the dispatcher:

```csharp
private void OnErrorOccurred(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
{
    SshSessionFailureDispatcher.Dispatch(
        e.Exception,
        SecurityEventOccurred,
        Disconnected);
}
```

### Step 4 — Add the event surface to `SshShellSession`

In `src/Heimdall.Ssh/SshShellSession.cs`:

```csharp
/// <summary>
/// Raised when a security-relevant failure (e.g. mid-session host-key
/// rejection) occurs. Fired in addition to <see cref="Disconnected"/>.
/// </summary>
public event Action<SshSessionSecurityEvent>? SecurityEventOccurred;
```

Replace the catch block in `ReadLoopAsync`:

```csharp
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
```

### Step 5 — Wire the UI

`src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs` already follows the pattern for `HostKeyRotatedDuringUpload` from Prompt 4. Mirror it for the browser-level event.

When the SFTP browser is constructed (search for `new SftpBrowser()` and the lines that do `_browser.Disconnected += ...`), add:

```csharp
_browser.SecurityEventOccurred += OnBrowserSecurityEvent;
```

Handler:

```csharp
private void OnBrowserSecurityEvent(SshSessionSecurityEvent evt)
{
    Dispatcher.InvokeAsync(() =>
    {
        if (evt.Code == SshFailureCode.HostKeyMismatch)
        {
            ViewModel?.SetErrorStatus(
                _localizer.Format(
                    "SftpHostKeyMismatchMidSession",
                    evt.Host,
                    evt.Port,
                    evt.PresentedFingerprint ?? "?",
                    evt.StoredFingerprint ?? "?"));
        }
        // Other security codes can route to other UI surfaces in the future.
    });
}
```

Detach the handler in the same disposal path that detaches the existing `Disconnected` handler so subscriptions do not leak.

For the SSH terminal view, mirror the wiring on whichever code-behind constructs `SshShellSession`. Search:

```bash
grep -rn 'new SshShellSession\|SshShellSession(' src/Heimdall.App/ tests/Heimdall.Ssh.Tests/
```

For each production-code call site, attach a `SecurityEventOccurred` handler that surfaces the same MITM banner. If multiple SSH views share a single helper, fold the wiring into that helper rather than duplicating it.

### Step 6 — Add the locale key

`locales/en.json`:

```json
"SftpHostKeyMismatchMidSession": "Security warning: host key for {0}:{1} changed during the session. Possible MITM. Presented fingerprint: {2}. Trusted fingerprint: {3}. Disconnect and reconnect to re-establish trust."
```

`locales/fr.json`:

```json
"SftpHostKeyMismatchMidSession": "Avertissement de sécurité : la clé d'hôte de {0}:{1} a changé en cours de session. Possible MITM. Empreinte présentée : {2}. Empreinte de confiance : {3}. Déconnectez-vous et reconnectez-vous pour rétablir la confiance."
```

Reuse the same key for the SSH terminal view if the message is the same; otherwise add a sibling `SshHostKeyMismatchMidSession` with phrasing adapted to the SSH terminal UX.

### Step 7 — Tests

Create `tests/Heimdall.Ssh.Tests/SshSessionFailureDispatcherTests.cs`:

```csharp
using Heimdall.Ssh;

namespace Heimdall.Ssh.Tests;

public sealed class SshSessionFailureDispatcherTests
{
    [Fact]
    public void Dispatch_HostKeyRejectedException_Mismatch_RaisesSecurityEventAndDisconnect()
    {
        var ex = new HostKeyRejectedException(
            "gw.example.com", 22, "ssh-ed25519", "SHA256:NEW", "SHA256:OLD");

        SshSessionSecurityEvent? captured = null;
        string? disconnectMessage = null;

        SshSessionFailureDispatcher.Dispatch(
            ex,
            evt => captured = evt,
            msg => disconnectMessage = msg);

        Assert.NotNull(captured);
        Assert.Equal(SshFailureCode.HostKeyMismatch, captured!.Code);
        Assert.Equal("gw.example.com", captured.Host);
        Assert.Equal(22, captured.Port);
        Assert.Equal("ssh-ed25519", captured.Algorithm);
        Assert.Equal("SHA256:NEW", captured.PresentedFingerprint);
        Assert.Equal("SHA256:OLD", captured.StoredFingerprint);
        Assert.NotNull(disconnectMessage);  // Disconnected still fires
    }

    [Fact]
    public void Dispatch_HostKeyRejectedException_FirstUseRefused_RaisesCancelledAndDisconnect()
    {
        // No stored fingerprint => IsMismatch == false => first-use refusal
        var ex = new HostKeyRejectedException(
            "gw.example.com", 22, "ssh-ed25519", "SHA256:NEW", storedFingerprint: null);

        SshSessionSecurityEvent? captured = null;
        SshSessionFailureDispatcher.Dispatch(ex, evt => captured = evt, _ => { });

        Assert.NotNull(captured);
        Assert.Equal(SshFailureCode.Cancelled, captured!.Code);
    }

    [Fact]
    public void Dispatch_GenericException_OnlyRaisesDisconnect()
    {
        var ex = new InvalidOperationException("connection reset");

        SshSessionSecurityEvent? captured = null;
        string? disconnectMessage = null;

        SshSessionFailureDispatcher.Dispatch(
            ex,
            evt => captured = evt,
            msg => disconnectMessage = msg);

        Assert.Null(captured);
        Assert.Equal("connection reset", disconnectMessage);
    }

    [Fact]
    public void Dispatch_NullSecurityHandler_StillRaisesDisconnect()
    {
        var ex = new HostKeyRejectedException(
            "gw.example.com", 22, "ssh-ed25519", "SHA256:NEW", "SHA256:OLD");

        string? disconnectMessage = null;
        SshSessionFailureDispatcher.Dispatch(
            ex,
            securityHandler: null,
            disconnectedHandler: msg => disconnectMessage = msg);

        Assert.NotNull(disconnectMessage);
    }

    [Fact]
    public void Dispatch_NullDisconnectHandler_StillRaisesSecurityEvent()
    {
        var ex = new HostKeyRejectedException(
            "gw.example.com", 22, "ssh-ed25519", "SHA256:NEW", "SHA256:OLD");

        SshSessionSecurityEvent? captured = null;
        SshSessionFailureDispatcher.Dispatch(
            ex,
            securityHandler: evt => captured = evt,
            disconnectedHandler: null);

        Assert.NotNull(captured);
    }

    [Fact]
    public void Dispatch_NullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => SshSessionFailureDispatcher.Dispatch(null!, null, null));
    }
}
```

`Heimdall.Ssh.csproj` already declares `<InternalsVisibleTo Include="Heimdall.Ssh.Tests" />`, so the test project can reach internal helpers. The dispatcher is `public` though, so internals visibility is not strictly required for these tests.

If existing `SftpBrowserTests` exist (search for them — the audit noted there were none), do not invent the file just to add a `SecurityEventOccurred` test. Verifying via the dispatcher is enough; the wiring in `OnErrorOccurred` is a one-liner whose correctness follows from the dispatcher tests + a manual review of the diff.

## Coding standards

- Apache 2.0 header on the two new files (`SshSessionSecurityEvent.cs`, `SshSessionFailureDispatcher.cs`) and the new test file.
- English only.
- Nullable reference types stay enabled.
- `TreatWarningsAsErrors` is on.
- `ConfigureAwait(false)` is irrelevant (no async added).
- i18n key parity is CI-enforced — both `en.json` and `fr.json` must gain `SftpHostKeyMismatchMidSession` (and any sibling key you add).
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build green, zero new warnings; suite passing count rises by 6 (the new dispatcher tests). The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite once if it fails on first attempt.

Run a sanity check confirming the old `Disconnected?.Invoke(e.Exception.Message)` collapse is gone:

```bash
grep -n 'Disconnected?.Invoke(e.Exception.Message)' src/Heimdall.Sftp/SftpBrowser.cs
grep -n 'Disconnected?.Invoke(ex.Message)' src/Heimdall.Ssh/SshShellSession.cs
```

Both should return zero matches. The dispatcher now owns the `Disconnected` invocation in those paths.

## Reporting back

When you finish, report:

1. The list of source files modified or created.
2. The list of tests added (class name + each method name).
3. The final test counts (passed / failed / skipped) for both the targeted run on `SshSessionFailureDispatcherTests` and the full suite.
4. Confirm both grep checks above return zero matches.
5. Any decision that diverged from this prompt, with a one-line rationale (especially: which SSH terminal view code-behind you wired the event to, and whether you reused `SftpHostKeyMismatchMidSession` or added a sibling key).
6. The exact diff of `OnErrorOccurred` (`SftpBrowser`) and the catch block (`SshShellSession`) inline in the report.
