# Prompt 4 — Cache the pinned verifier per sudo edit session (C2) + test T4

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

The SSH/SFTP audit identified a security-relevant inefficiency in `RemoteFileEditor`. The current `EditFileSudoAsync` resolves a `PinnedFingerprintVerifier` once when the user opens a privileged file, but `UploadWithSudoAsync` then **re-runs the full TOFU resolution** every time the file is auto-saved (via the `FileSystemWatcher` + debounce). Three problems flow from this:

1. If the gateway rotates its host key between opening and any subsequent save, the user can be re-prompted (or, worse, silently re-trust depending on the verifier UI), even though they have an active edit session that was already authenticated against a specific key.
2. There is no clean signal back to the UI when a save fails because the host key changed mid-session — the upload just throws into a fire-and-forget task and is swallowed by `OnFileChangedAsync`'s catch-all.
3. Re-resolving on every save means a `none-auth` SSH probe per save, which is wasteful and noisy on bastion logs.

This prompt resolves the verifier **once** per sudo edit session, caches it on `EditSession`, and reuses it for every subsequent upload. If the gateway presents a different host key during a later upload, the cached verifier rejects the connection — the save fails fail-closed and the failure is surfaced via a new typed event so the UI can react.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P0 #5 (C2 + T4) only. Prompts 1, 2, and 3 have already shipped.

## Goal

1. Add a non-public `PinnedFingerprintVerifier? Verifier` init-only property to `EditSession` and capture the resolved verifier there during `EditFileSudoAsync`.
2. Remove the now-redundant `EditSession.HostKeyStore` and `EditSession.HostKeyVerifier` properties (they were used solely to re-resolve the verifier on each upload — that is exactly what we are eliminating).
3. Refactor `UploadWithSudoAsync` to consume `session.Verifier` directly. Drop the call to `SshConnectionFactory.ResolveHostKeyAsync`. If `session.Verifier` is null at that point (only possible if the session was constructed without a sudo flow), throw `InvalidOperationException` — sudo upload requires a cached verifier.
4. Surface mid-session host-key rotation via a new typed event on `RemoteFileEditor`:

   ```csharp
   public event Action<HostKeyRotationEvent>? HostKeyRotatedDuringUpload;
   ```

   Where `HostKeyRotationEvent` is a record carrying `(string RemotePath, string PresentedFingerprint, string? StoredFingerprint, string Host, int Port)`. Raise it from the catch block that already handles `HostKeyRejectedException` in `OnFileChangedAsync`. Existing `FileUploaded?.Invoke(session.RemotePath, false)` continues to fire — the new event is **additional**, not a replacement.
5. Add the T4 tests pinning the new contract.

## Background — relevant files

- `src/Heimdall.Sftp/RemoteFileEditor.cs`:
  - `EditFileSudoAsync` (~lines 117-211) — resolves the verifier and constructs `EditSession`.
  - `OnFileChangedAsync` (~lines 333-393) — fire-and-forget upload trigger; existing `catch (HostKeyRejectedException ex)` is the place to raise the new event.
  - `UploadWithSudoAsync` (~lines 395-484) — currently re-resolves the verifier; this is the main change site.
  - `EditSession` (~lines 524-567) — record being modified.
- `src/Heimdall.Core/Ssh/PinnedFingerprintVerifier.cs` — already exposes `Matches(host, port, fingerprint)` and `VerifyAsync(...)`.
- `src/Heimdall.Ssh/HostKeyRejectedException.cs` — already carries `Host`, `Port`, `PresentedFingerprint`, `StoredFingerprint`.

After Prompt 3, `RemoteFileEditor._hostKeyStore` and `_hostKeyVerifier` are non-nullable fields. `EditFileSudoAsync` now unconditionally calls `SshConnectionFactory.ResolveHostKeyAsync(...)` once at the top of the method. Reuse that resolved value.

## Implementation steps

### Step 1 — Modify `EditSession`

In `src/Heimdall.Sftp/RemoteFileEditor.cs`:

```csharp
// before (after Prompt 3)
internal sealed class EditSession : IDisposable
{
    public required string RemotePath { get; init; }
    public required string LocalPath { get; init; }
    public bool IsSudo { get; init; }
    public SshConnectionParams? SshParams { get; init; }
    public required HostKeyStore HostKeyStore { get; init; }
    public required IHostKeyVerifier HostKeyVerifier { get; init; }
    // ...
}

// after
internal sealed class EditSession : IDisposable
{
    public required string RemotePath { get; init; }
    public required string LocalPath { get; init; }
    public bool IsSudo { get; init; }
    public SshConnectionParams? SshParams { get; init; }

    /// <summary>
    /// Pinned host-key verifier resolved when the sudo edit session opened.
    /// Non-null for sudo sessions; null for direct-browser sessions that do
    /// not need to re-establish an SSH transport on save.
    /// </summary>
    public PinnedFingerprintVerifier? Verifier { get; init; }
    // ...
}
```

`HostKeyStore` and `HostKeyVerifier` are gone from the record. Their job — feeding `SshConnectionFactory.ResolveHostKeyAsync` — is no longer needed because the verifier is already resolved.

### Step 2 — Capture the verifier in `EditFileSudoAsync`

```csharp
// before (relevant block, after Prompt 3)
var pinnedVerifier = await SshConnectionFactory.ResolveHostKeyAsync(
    sshParams, _hostKeyStore, _hostKeyVerifier, ct).ConfigureAwait(false);

// ... download via SSH ...

var session = new EditSession
{
    RemotePath = remotePath,
    LocalPath = localPath,
    IsSudo = true,
    SshParams = sshParams,
    HostKeyStore = _hostKeyStore,
    HostKeyVerifier = _hostKeyVerifier,
    LastUploadTime = DateTime.UtcNow
};
```

becomes:

```csharp
var pinnedVerifier = await SshConnectionFactory.ResolveHostKeyAsync(
    sshParams, _hostKeyStore, _hostKeyVerifier, ct).ConfigureAwait(false);

// ... download via SSH (still uses pinnedVerifier locally) ...

var session = new EditSession
{
    RemotePath = remotePath,
    LocalPath = localPath,
    IsSudo = true,
    SshParams = sshParams,
    Verifier = pinnedVerifier,
    LastUploadTime = DateTime.UtcNow
};
```

### Step 3 — Refactor `UploadWithSudoAsync`

```csharp
private static async Task UploadWithSudoAsync(EditSession session)
{
    if (session.SshParams is null)
    {
        throw new InvalidOperationException("SSH parameters required for sudo upload.");
    }

    if (session.Verifier is null)
    {
        throw new InvalidOperationException(
            "Sudo edit session must have a cached pinned verifier; was the session created via EditFileSudoAsync?");
    }

    string escapedPath = PathEscaper.EscapeForShell(session.RemotePath);
    string tempRemotePath = $"{RemoteTempPrefix}edit_{Guid.NewGuid():N}";

    // No more SshConnectionFactory.ResolveHostKeyAsync here.
    var connectionInfo = SshConnectionFactory.Create(session.SshParams);
    using var sftpClient = new SftpClient(connectionInfo);
    using var sshClient = new SshClient(connectionInfo);

    SshConnectionFactory.AttachPinnedHostKeyVerification(
        sftpClient,
        session.SshParams.Host,
        session.SshParams.Port,
        session.Verifier);
    SshConnectionFactory.AttachPinnedHostKeyVerification(
        sshClient,
        session.SshParams.Host,
        session.SshParams.Port,
        session.Verifier);

    // ... rest unchanged ...
}
```

If the gateway rotates its key between the initial download and this upload, `sftpClient.Connect()` (or `sshClient.Connect()`) will raise `HostKeyRejectedException` from inside the `HostKeyReceived` callback wired by `AttachPinnedHostKeyVerification`. This is exactly the behaviour we want.

### Step 4 — Add the `HostKeyRotatedDuringUpload` event

In `src/Heimdall.Sftp/RemoteFileEditor.cs`, declare the event near the existing `FileUploaded` event:

```csharp
/// <summary>
/// Raised when an auto-upload was rejected because the gateway presented a
/// different host key from the one captured at edit-session open. The save
/// is aborted; the user must explicitly re-open the file to re-establish
/// trust against the new key.
/// </summary>
public event Action<HostKeyRotationEvent>? HostKeyRotatedDuringUpload;
```

Add the supporting record at the bottom of the file (or in a new file `HostKeyRotationEvent.cs` next to `RemoteFileEditor.cs` — your call, prefer same-file if it stays under ~600 LOC):

```csharp
/// <summary>
/// Carries information about a host-key rotation detected during a sudo
/// auto-upload. Consumers (UI layer) can use this to show a localized
/// security banner and force-close the edit session.
/// </summary>
public sealed record HostKeyRotationEvent(
    string RemotePath,
    string PresentedFingerprint,
    string? StoredFingerprint,
    string Host,
    int Port);
```

Modify the existing `catch (Heimdall.Ssh.HostKeyRejectedException ex)` block in `OnFileChangedAsync` to also raise the event:

```csharp
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
    throw;  // keep the existing rethrow so the unhandled-task pipeline still sees it
}
```

Do **not** silence the rethrow — Prompt 5 will own task cancellation cleanup. For now we just want the event surface ready.

### Step 5 — Production call site updates

`src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs` constructs `RemoteFileEditor` (lines 133, 1185 from the Prompt 3 audit). After this prompt, the code-behind should subscribe to the new event and show a localized error banner. Implement the wiring:

1. In the same code-behind that already handles `editor.FileUploaded`, add:

   ```csharp
   _editor.HostKeyRotatedDuringUpload += OnHostKeyRotatedDuringUpload;
   ```

2. The handler shows a status banner and force-closes the edit session for the affected path:

   ```csharp
   private void OnHostKeyRotatedDuringUpload(HostKeyRotationEvent evt)
   {
       Dispatcher.InvokeAsync(() =>
       {
           ViewModel?.SetErrorStatus(
               _localizer.Format(
                   "SftpHostKeyRotatedDuringUpload",
                   evt.RemotePath,
                   evt.Host,
                   evt.Port,
                   evt.PresentedFingerprint));
           _editor?.CloseEdit(evt.RemotePath);
       });
   }
   ```

3. Add the new locale key to `locales/en.json` and `locales/fr.json`. Use placeholders `{0}` (path), `{1}` (host), `{2}` (port), `{3}` (presented fingerprint).

   `locales/en.json`:

   ```json
   "SftpHostKeyRotatedDuringUpload": "Host key for {1}:{2} changed since you opened {0}. Save aborted. Presented fingerprint: {3}. Re-open the file to re-establish trust."
   ```

   `locales/fr.json`:

   ```json
   "SftpHostKeyRotatedDuringUpload": "La clé d'hôte de {1}:{2} a changé depuis l'ouverture de {0}. Sauvegarde refusée. Empreinte présentée : {3}. Ré-ouvrez le fichier pour rétablir la confiance."
   ```

4. Don't forget to detach the handler in the same disposal path that detaches `FileUploaded` to avoid leaking subscriptions.

If `EmbeddedSftpView.xaml.cs` already routes status text through a different localization layer (e.g. `SftpLocalizationKeys` constants), follow the existing convention rather than introducing new patterns.

### Step 6 — Tests T4

Create `tests/Heimdall.App.Tests/RemoteFileEditorRotationTests.cs` (the App test project — `RemoteFileEditor` already lives behind `IRemoteBrowser`, and `EditSession` is `internal` and reachable via the existing `InternalsVisibleTo Include="Heimdall.App.Tests"` declaration on `Heimdall.Sftp.csproj`).

> Verify the `InternalsVisibleTo` is wired on `Heimdall.Sftp.csproj` for `Heimdall.App.Tests`. If it isn't, **add it** before writing the tests rather than promoting members to `public`.

Required tests:

1. **`PinnedFingerprintVerifier_Matches_RejectsDifferentFingerprint`** — pure unit test on the verifier:
   - Construct `var v = new PinnedFingerprintVerifier("gw.example.com", 22, "SHA256:AAA")`.
   - Assert `v.Matches("gw.example.com", 22, "SHA256:AAA")` is true.
   - Assert `v.Matches("gw.example.com", 22, "SHA256:BBB")` is false.
   - Assert `v.Matches("other.example.com", 22, "SHA256:AAA")` is false (host mismatch).
   - Assert `v.Matches("gw.example.com", 23, "SHA256:AAA")` is false (port mismatch).
   - This test may already exist in `Heimdall.Ssh.Tests`; if so, do not duplicate — instead, add the missing cases to the existing test class.

2. **`PinnedFingerprintVerifier_VerifyAsync_RejectsRotation`** — pure unit test on the async path:
   - Construct verifier as above.
   - Call `await v.VerifyAsync("gw.example.com", 22, "ssh-ed25519", presentedFingerprint: "SHA256:BBB", storedFingerprint: "SHA256:AAA", CancellationToken.None)`.
   - Assert the result is `HostKeyDecision.Reject`.

3. **`EditSession_Verifier_IsCachedAfterSudoEditSessionConstruction`** — structural test:
   - Construct an `EditSession` directly (record initialization) with `IsSudo = true`, `SshParams = ...`, `Verifier = new PinnedFingerprintVerifier(...)`.
   - Assert `session.Verifier` matches the constructed verifier and is `IsSudo == true`.
   - Construct another session with `IsSudo = false`, `Verifier = null`. Assert the record permits this (non-sudo sessions don't need a verifier).

4. **`UploadWithSudoAsync_ThrowsWhenVerifierMissing`** — invariant test:
   - Build an `EditSession` with `IsSudo = true`, `SshParams = MakeFakeParams()`, `Verifier = null`.
   - Invoke `UploadWithSudoAsync` (it is `private static` — promote it to `internal static` for testability, or expose an `internal static Task UploadWithSudoForTestingAsync(EditSession)` wrapper that calls into it).
   - Assert it throws `InvalidOperationException` with a message mentioning "verifier".

5. **`HostKeyRotationEvent_RecordEqualsByValue`** — pinning the value-record contract:
   - Construct two `HostKeyRotationEvent` instances with identical fields.
   - Assert they are `==` (record equality).
   - Construct one with a different `PresentedFingerprint` and assert inequality.

If you can't promote `UploadWithSudoAsync` to `internal static` cleanly (e.g. it captures `_editorPath` or other instance state), drop test #4 and document the omission in the report. The other four tests are mandatory.

Use the existing test style: hand-rolled fakes for `IRemoteBrowser` if needed (`tests/Heimdall.App.Tests/PlinkFailClosedTests.cs` is a good template for the fake style). Do not introduce Moq if the surrounding tests don't already use it.

### Step 7 — Update existing tests if needed

The `EditSession` shape changed (`HostKeyStore` and `HostKeyVerifier` removed, `Verifier` added). Search for any test that constructs `EditSession` directly:

```bash
grep -rn 'new EditSession' tests/ src/
```

Update each construction. For tests that don't exercise sudo flow, set `Verifier = null`. For sudo-flow tests, build a real `PinnedFingerprintVerifier` (it is a value object — cheap to instantiate).

## Coding standards

Same as previous prompts:

- Apache 2.0 header on any new file (the new locale keys live in JSON; if you create a new C# file for `HostKeyRotationEvent`, give it the standard header).
- English only.
- Nullable reference types stay enabled.
- `TreatWarningsAsErrors` is on.
- `ConfigureAwait(false)` on every `await` you introduce in non-UI projects (`Heimdall.Sftp`).
- i18n key parity is CI-enforced — both `en.json` and `fr.json` must gain `SftpHostKeyRotatedDuringUpload`.
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build is green with zero new warnings; test count rises by your new tests (4-5 new methods). The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite if it fails on first attempt.

Also run the targeted greps to confirm the dead resolution paths are gone:

```bash
grep -rn 'session.HostKeyStore\|session.HostKeyVerifier' src/
```

Should return zero matches. The session no longer exposes those properties.

```bash
grep -n 'ResolveHostKeyAsync' src/Heimdall.Sftp/RemoteFileEditor.cs
```

Should match exactly **once** — inside `EditFileSudoAsync`, never inside `UploadWithSudoAsync`.

## Reporting back

When you finish, report:

1. The list of source files modified (and any new files created — likely just the test class and possibly a `HostKeyRotationEvent.cs`).
2. The list of tests added (class name + each method name).
3. The final test counts (passed / failed / skipped) for both the targeted run on `RemoteFileEditorRotationTests` and the full suite.
4. Confirm both grep checks above return the expected counts.
5. Any decision that diverged from this prompt, with a one-line rationale.
6. The exact diff of the modified `EditSession` record + `UploadWithSudoAsync` body, inline in the report.
