# Prompt 8 — Streaming app-side known_hosts importer (A3)

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

The SSH/SFTP audit identified that the **app-layer** known_hosts importer reads the entire file into memory before parsing. The Core parser already exposes a streaming overload and the Core importer (`Heimdall.Ssh.KnownHostsImporter.ImportFile`) already uses it correctly. The App importer (`Heimdall.App.Services.Import.KnownHostsImporter.ParseFileAsync`) has not been migrated.

Today, in `src/Heimdall.App/Services/Import/KnownHostsImporter.cs:50`:

```csharp
public async Task<KnownHostsParseResult> ParseFileAsync(string filePath, CancellationToken ct = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

    var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct).ConfigureAwait(false);
    return KnownHostsParser.Parse(content);
}
```

A 50 MB or larger known_hosts file (malicious or corrupted) lands as a single managed string before `KnownHostsParser.Parse(content)` even sees it. The parser has a per-line cap (`KnownHostsParser.MaxLineLength = 64 KiB`) and a per-file cap (`KnownHostsParser.MaxFileSizeBytes = 50 MiB`), but only the **streaming** entry points enforce the file cap — the string overload trusts whatever the caller hands in.

This prompt brings the App importer in line with the Core importer: stream the file via `StreamReader`, enforce the file-size cap before opening, and surface a diagnostic to the caller when the file is rejected.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P1 #9 (A3) only. Prompts 1-7 have already shipped.

## Goal

1. Replace `File.ReadAllTextAsync` + `KnownHostsParser.Parse(string)` with streaming `File.OpenRead` + `StreamReader` + `KnownHostsParser.Parse(TextReader)`.
2. Pre-check the file size against `KnownHostsParser.MaxFileSizeBytes`. Files exceeding the cap return an empty `KnownHostsParseResult` with a single `FileTooLarge` diagnostic, plus a `FileLogger.Warn` matching the Core importer's wording.
3. Add `KnownHostsDiagnosticCode.FileTooLarge` to the existing enum in `Heimdall.Core` so the diagnostic can be expressed as a typed code (no string-match downstream).
4. Mirror the Core importer's IO error handling (`IOException`, `UnauthorizedAccessException`, `DecoderFallbackException`) so the App importer surfaces the same diagnostics rather than letting raw exceptions bubble through to the dialog layer.
5. Add tests pinning the new behaviour: file-too-large rejection, streaming correctness on large-but-valid files, IO error handling, cancellation propagation.

## Background — relevant files

- `src/Heimdall.App/Services/Import/KnownHostsImporter.cs` — file under change. `ParseFileAsync` ~lines 46-52.
- `src/Heimdall.Core/Ssh/KnownHostsParser.cs` — already exposes:
  - `MaxLineLength` (64 KiB) and `MaxFileSizeBytes` (50 MiB) constants.
  - `Parse(TextReader reader)` streaming overload (line 84).
  - `LineTooLongContext` constant for diagnostic context.
- `src/Heimdall.Core/Ssh/KnownHostsImportDiagnostic.cs` — `KnownHostsDiagnosticCode` enum to extend, plus the `KnownHostsImportDiagnostic` record shape `(Level, SourceLineNumber, Code, Context?)`.
- `src/Heimdall.Core/Ssh/KnownHostsParseResult.cs` — record with `(Entries, Diagnostics)` we can instantiate empty.
- `src/Heimdall.Ssh/KnownHostsImportExport.cs` — Core `KnownHostsImporter.ImportFile` (lines 44-83) is the **template** to mirror. It already does the size check + streaming + IO error catches with the right log messages. Lift its shape into the App layer.

## Implementation steps

### Step 1 — Extend the diagnostic code enum

In `src/Heimdall.Core/Ssh/KnownHostsImportDiagnostic.cs`:

```csharp
public enum KnownHostsDiagnosticCode
{
    HashedEntryNotSupported,
    CertAuthorityNotSupported,
    RevokedEntryNotSupported,
    UnsupportedHostPattern,
    UnsupportedKeyType,
    LegacyKeyType,
    MalformedLine,
    DuplicateFingerprintInSourceMerged,
    IntraFileFingerprintConflict,
    FileTooLarge,           // ← new
    FileReadError,          // ← new (covers IOException / UnauthorizedAccessException / DecoderFallbackException)
}
```

Append at the end. Do not reorder existing values — the enum is referenced by name from view models and existing tests.

### Step 2 — Rewrite `ParseFileAsync`

Replace the body of `ParseFileAsync` in `src/Heimdall.App/Services/Import/KnownHostsImporter.cs`:

```csharp
public async Task<KnownHostsParseResult> ParseFileAsync(string filePath, CancellationToken ct = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

    return await Task.Run(() => ParseFileStreaming(filePath), ct).ConfigureAwait(false);
}

private static KnownHostsParseResult ParseFileStreaming(string filePath)
{
    var fileInfo = new FileInfo(filePath);

    if (fileInfo.Length > KnownHostsParser.MaxFileSizeBytes)
    {
        Heimdall.Core.Logging.FileLogger.Warn(
            $"known_hosts import refused: file '{filePath}' exceeds {KnownHostsParser.MaxFileSizeBytes} bytes ({fileInfo.Length} bytes).");

        return new KnownHostsParseResult(
            Entries: Array.Empty<KnownHostsRawEntry>(),
            Diagnostics: new[]
            {
                new KnownHostsImportDiagnostic(
                    KnownHostsDiagnosticLevel.Warning,
                    SourceLineNumber: 0,
                    Code: KnownHostsDiagnosticCode.FileTooLarge,
                    Context: $"{fileInfo.Length} bytes")
            });
    }

    try
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return KnownHostsParser.Parse(reader);
    }
    catch (IOException ex)
    {
        Heimdall.Core.Logging.FileLogger.Warn(
            $"known_hosts import skipped: I/O error reading '{filePath}': {ex.Message}");
        return EmptyResultWithReadError(ex);
    }
    catch (UnauthorizedAccessException ex)
    {
        Heimdall.Core.Logging.FileLogger.Warn(
            $"known_hosts import skipped: access denied to '{filePath}': {ex.Message}");
        return EmptyResultWithReadError(ex);
    }
    catch (DecoderFallbackException ex)
    {
        Heimdall.Core.Logging.FileLogger.Warn(
            $"known_hosts import skipped: decoding error in '{filePath}': {ex.Message}");
        return EmptyResultWithReadError(ex);
    }
}

private static KnownHostsParseResult EmptyResultWithReadError(Exception ex)
{
    return new KnownHostsParseResult(
        Entries: Array.Empty<KnownHostsRawEntry>(),
        Diagnostics: new[]
        {
            new KnownHostsImportDiagnostic(
                KnownHostsDiagnosticLevel.Warning,
                SourceLineNumber: 0,
                Code: KnownHostsDiagnosticCode.FileReadError,
                Context: ex.Message)
        });
}
```

The `Task.Run` wrap exists because `Parse(TextReader)` is synchronous; the file I/O happens inside `Task.Run` so the public method stays awaitable and respects the caller's `CancellationToken`. Cancellation between `ParseFileAsync` and the start of `Task.Run` honours the token directly; cancellation during the parse itself stops at the next `Task.Run` boundary.

Add the appropriate `using` directives to the file (`Heimdall.Core.Ssh` for the new types, `Heimdall.Core.Logging`, `System.IO`, `System.Text`). The existing `using System.IO; using System.Text;` block from earlier in the file should already cover most.

### Step 3 — Tests

Create `tests/Heimdall.App.Tests/KnownHostsImporterStreamingTests.cs`. The test uses real temp files because `ParseFileAsync` is a thin file-reading wrapper — mocking the file system would only obscure the contract under test.

Required test methods:

1. **`ParseFileAsync_ParsesValidKnownHosts_FromTempFile`**
   - Write a small known_hosts content to a temp file (3-5 well-formed entries — copy the format from the existing `KnownHostsImportExportTests` if it has fixtures).
   - Call `ParseFileAsync`.
   - Assert `result.Entries.Count` matches the number of entries written and `result.Diagnostics` is empty (or contains only `Info`-level entries).

2. **`ParseFileAsync_FileTooLarge_ReturnsEmptyResultWithDiagnostic`**
   - Create a temp file at exactly `KnownHostsParser.MaxFileSizeBytes + 1` bytes (use `FileStream.SetLength`; do not write 50 MB of real bytes).
   - Call `ParseFileAsync`.
   - Assert `result.Entries` is empty.
   - Assert `result.Diagnostics` contains exactly one entry with `Code == KnownHostsDiagnosticCode.FileTooLarge` and `Level == Warning`.

3. **`ParseFileAsync_AtMaxSize_StreamsCorrectly`**
   - Create a temp file at exactly `KnownHostsParser.MaxFileSizeBytes` bytes (boundary). Fill it with a small, valid known_hosts entry repeated, padded with comments so the line cap is never tripped. The point is to prove the importer accepts the boundary, not to over-stress the parser.
   - Call `ParseFileAsync`.
   - Assert `result.Entries.Count > 0`. (Exact count depends on padding strategy; pin it loosely.)

   **Realistic shortcut**: this test can use a much smaller file (e.g. 100 KiB) and just assert the streaming path works end-to-end. The "exactly MaxFileSizeBytes" boundary is hard to construct reliably; if you can't write a fast version of this test, replace it with a 100-line valid file and rename to `ParseFileAsync_StreamingPath_ParsesAllEntries`. Document the substitution in the report.

4. **`ParseFileAsync_FileNotFound_PropagatesIOException`**
   - Pass a path that does not exist.
   - The Core `FileStream(...)` constructor throws `FileNotFoundException` (a subtype of `IOException`).
   - **Expected behaviour:** the `IOException` catch block runs, the result is empty with a `FileReadError` diagnostic. (Do not let `FileNotFoundException` bubble through.)

5. **`ParseFileAsync_HonorsCancellationToken`**
   - Create a small valid temp file.
   - Pass an already-cancelled `CancellationToken`.
   - Assert `OperationCanceledException` is thrown.

6. **`ParseFileAsync_ThrowsForNullOrWhitespacePath`**
   - Empty string, whitespace string → `ArgumentException`.

7. **`ParseFileAsync_DiagnosticForTooLongLine_StillProducedByCoreParser`**
   - Write a temp file containing one line larger than `KnownHostsParser.MaxLineLength` (64 KiB).
   - Call `ParseFileAsync`.
   - Assert `result.Diagnostics` contains a `MalformedLine` entry with `Context == KnownHostsParser.LineTooLongContext`. (This exercises the Core parser's own per-line cap, confirming the streaming path still passes through the line-level diagnostics.)

Each test must clean up its temp file in a `try / finally` so test failures do not leave orphans in `Path.GetTempPath()`.

The test class must use the existing `Heimdall.App.Tests` xUnit + FluentAssertions style (check `tests/Heimdall.App.Tests/PlinkFailClosedTests.cs` for the conventions). The `KnownHostsImporter` constructor takes `(IConfigManager config, IHostKeyTrustService hostKeyTrustService)` — for these tests, `ParseFileAsync` does not consume those, so pass minimal stubs (hand-rolled fakes; if `IConfigManager` and `IHostKeyTrustService` already have a `Null` or test-only implementation, use it; otherwise hand-roll a tiny `class StubConfigManager : IConfigManager` whose every method throws `NotImplementedException` — the test never calls those methods).

### Step 4 — Sanity checks

After the change, the following greps must hold:

```bash
grep -n "ReadAllTextAsync" src/Heimdall.App/Services/Import/KnownHostsImporter.cs
grep -n "ReadAllText\b" src/Heimdall.App/Services/Import/KnownHostsImporter.cs
```

Both must return zero matches — the streaming path no longer reads the whole file as a string.

```bash
grep -n "FileTooLarge\|FileReadError" src/Heimdall.Core/Ssh/KnownHostsImportDiagnostic.cs
```

Must return two matches (the two new enum values).

## Coding standards

Same as previous prompts:

- Apache 2.0 header on the new test file.
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

Expected: build green with zero new warnings; suite passing count rises by 7 (or 6 if you collapsed test #3 as documented). The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite once if it fails on first attempt.

## Reporting back

When you finish, report:

1. The list of source files modified or created.
2. The exact diff of the new `ParseFileAsync` body, inline in the report.
3. The list of tests added (class name + each method name).
4. The final test counts (passed / failed / skipped) for both the targeted run on `KnownHostsImporterStreamingTests` and the full suite.
5. Confirm the three grep checks above produce the expected counts.
6. Any decision that diverged from this prompt, with a one-line rationale (especially: whether you collapsed test #3 to a smaller file and why).
