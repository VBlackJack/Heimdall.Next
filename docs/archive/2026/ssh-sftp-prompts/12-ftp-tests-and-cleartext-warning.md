# Prompt 12 — FtpBrowser tests + FtpHandler validation + UI cleartext warning + FluentFTP roadmap (M2 / A4)

## Context

You are working on **Heimdall.Next**. Read `CLAUDE.md` first if you have not already.

This prompt closes the last item of the SSH/SFTP audit consolidated plan. Today the FTP path has three weaknesses:

1. **Zero unit-test coverage.** `src/Heimdall.Sftp/FtpBrowser.cs` has parsing helpers (Unix LIST, DOS LIST, `ParseUnixDate` with a year-rollover heuristic, `NormalizePath`, `ResolvePath`, `MaxFtpFilenameLength` filename cap) that are exercised only at runtime against real FTP servers. None of them have a unit test. Regressions ship silently.
2. **Thin host/port validation in `FtpHandler`.** `src/Heimdall.App/Services/Handlers/FtpHandler.cs` rejects only `string.IsNullOrWhiteSpace(server.RemoteServer)`. The SSH-side handlers run `InputValidator.ValidateDomain` + `IPAddress.TryParse` and `InputValidator.ValidatePortRange`; the FTP handler does not.
3. **Cleartext FTP credentials are logged but not surfaced to the user.** `FtpBrowser.ConnectAsync` already emits a `FileLogger.Warn` when `useSsl == false && !string.IsNullOrEmpty(username)`. Operators reading the log later will see it; the user actively starting a connection will not. Heimdall is a security-conscious connection manager — a cleartext FTP session must produce a visible UI banner.

Beyond the immediate fixes, the audit recommends tracking a longer-term migration off the deprecated `FtpWebRequest` to `FluentFTP` (MIT). This prompt files that as a roadmap item rather than starting the migration here — out of scope.

The audit consolidated plan is in `audit-ssh-sftp-action-plan.md`. This prompt covers item P2 #12 (M2 / A4) only. Prompts 1-11 have already shipped.

## Goals

1. **Tests**: promote the four `private static` parsing helpers in `FtpBrowser` to `internal static` and add unit tests covering Unix LIST, DOS LIST, malformed lines, the filename-length cap, the year-rollover heuristic, and the path-normalisation helpers.
2. **Validation**: add `InputValidator.ValidateDomain || IPAddress.TryParse` for the host and `InputValidator.ValidatePortRange` for the port in `FtpHandler.ConnectAsync`, before the `browser.ConnectAsync` call. Reject early with a localized error.
3. **UI cleartext warning**: extend `ConnectionResult` with a new optional `Warning` field. `FtpHandler` populates it with a localized "cleartext FTP" message when the connection succeeded over a non-TLS transport with credentials. The view layer that already displays `ErrorMessage` for failures shows `Warning` as a non-fatal banner on success.
4. **Roadmap**: create `docs/audit/ftp-fluentftp-migration.md` documenting the migration plan and constraints. Add a one-line `// TODO(audit-A4):` reference in `FtpBrowser.cs` near the `#pragma warning disable SYSLIB0014`.

## Background — relevant files

- `src/Heimdall.Sftp/FtpBrowser.cs`:
  - `NormalizePath` (~line 426) — `private static`.
  - `ResolvePath` (~line 448) — `private` (instance, but uses only `CurrentDirectory` so it can be made `internal static` taking `currentDirectory` as a parameter).
  - `ParseListLine` (~line 501) — `private static`.
  - `ParseUnixDate` (~line 570) — `private static`.
  - `MaxFtpFilenameLength` — already `internal const int` (line 495).
  - `UnixListRegex` / `DosListRegex` — `private static partial` regex methods; exercised through `ParseListLine` so they do not need to be promoted.
  - The cleartext warning: `ConnectAsync` already logs at line ~91. We will expose the same condition via a new public read-only property.
- `src/Heimdall.App/Services/Handlers/FtpHandler.cs` — handler under change for validation + warning surface.
- `src/Heimdall.App/Services/ProtocolSessionResults.cs` — `ConnectionResult` record gains an optional `Warning` field.
- `src/Heimdall.App/Services/Handlers/SshHandler.cs` — reference for the validation pattern (`IsValidSshHost`, `InputValidator.ValidatePortRange`).
- `locales/en.json`, `locales/fr.json` — gain new localization keys.
- `docs/audit/` — likely already contains audit-related docs (the directory exists at `docs/audit-2026-04-11.md`); place the new roadmap doc there.

`Heimdall.Sftp.csproj` already declares `<InternalsVisibleTo Include="Heimdall.App.Tests" />` after Prompt 4.

## Implementation steps

### Step 1 — Promote parsing helpers to `internal static`

In `src/Heimdall.Sftp/FtpBrowser.cs`:

```csharp
// Was: private static string NormalizePath(string path)
internal static string NormalizePath(string path)

// Was: private string ResolvePath(string path) — uses CurrentDirectory
//      Convert to a static helper so the test does not need an FtpBrowser instance.
internal static string ResolvePath(string path, string currentDirectory)
{
    if (path.StartsWith('/'))
    {
        return NormalizePath(path);
    }

    string basePath = currentDirectory.TrimEnd('/');
    return NormalizePath($"{basePath}/{path}");
}

// Was: private static SftpFileInfo? ParseListLine(string line, string parentPath)
internal static SftpFileInfo? ParseListLine(string line, string parentPath)

// Was: private static DateTime ParseUnixDate(string dateStr)
internal static DateTime ParseUnixDate(string dateStr)
```

Update the call site in `ChangeDirectoryAsync` (~line 144) to pass `CurrentDirectory` to the new `ResolvePath` signature.

### Step 2 — Tests for the parsing helpers

Create `tests/Heimdall.App.Tests/FtpBrowserParsingTests.cs`:

```csharp
public sealed class FtpBrowserParsingTests
{
    // ── NormalizePath ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("/", "/")]
    [InlineData("/var/log", "/var/log")]
    [InlineData("var/log", "/var/log")]
    [InlineData("/var/log/", "/var/log")]
    [InlineData("/var/log///", "/var/log")]
    public void NormalizePath_ProducesCanonicalForm(string? input, string expected)
    {
        Assert.Equal(expected, FtpBrowser.NormalizePath(input!));
    }

    // ── ResolvePath ───────────────────────────────────────────────────

    [Theory]
    [InlineData("/etc/passwd", "/var/log", "/etc/passwd")]   // absolute → as-is
    [InlineData("logs", "/var", "/var/logs")]                 // relative
    [InlineData("logs", "/", "/logs")]                        // relative from root
    [InlineData("../etc", "/var/log", "/var/log/../etc")]     // no path collapsing
    public void ResolvePath_HandlesAbsoluteAndRelative(
        string input, string currentDirectory, string expected)
    {
        Assert.Equal(expected, FtpBrowser.ResolvePath(input, currentDirectory));
    }

    // ── ParseListLine: Unix-style ─────────────────────────────────────

    [Fact]
    public void ParseListLine_UnixFile_ParsedCorrectly()
    {
        const string line = "-rw-r--r-- 1 root root 4096 Jan 15 12:34 hosts";
        var entry = FtpBrowser.ParseListLine(line, "/etc");

        Assert.NotNull(entry);
        Assert.Equal("hosts", entry!.Name);
        Assert.Equal("/etc/hosts", entry.FullPath);
        Assert.False(entry.IsDirectory);
        Assert.Equal(4096, entry.Size);
        Assert.Equal("root", entry.Owner);
        Assert.Equal("root", entry.Group);
    }

    [Fact]
    public void ParseListLine_UnixDirectory_ParsedCorrectly()
    {
        const string line = "drwxr-xr-x 2 user staff 0 Mar 10 09:00 archive";
        var entry = FtpBrowser.ParseListLine(line, "/srv");

        Assert.NotNull(entry);
        Assert.True(entry!.IsDirectory);
        Assert.Equal(0, entry.Size);  // directories are reported as size 0
        Assert.Equal("/srv/archive", entry.FullPath);
    }

    // ── ParseListLine: DOS-style ──────────────────────────────────────

    [Fact]
    public void ParseListLine_DosFile_ParsedCorrectly()
    {
        const string line = "01-15-26  12:34PM             1024 readme.txt";
        var entry = FtpBrowser.ParseListLine(line, "/");

        Assert.NotNull(entry);
        Assert.False(entry!.IsDirectory);
        Assert.Equal(1024, entry.Size);
        Assert.Equal("readme.txt", entry.Name);
    }

    [Fact]
    public void ParseListLine_DosDirectory_ParsedCorrectly()
    {
        const string line = "01-15-26  12:34PM       <DIR>          subfolder";
        var entry = FtpBrowser.ParseListLine(line, "/");

        Assert.NotNull(entry);
        Assert.True(entry!.IsDirectory);
        Assert.Equal("subfolder", entry.Name);
    }

    // ── ParseListLine: edge cases ─────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage line")]
    [InlineData("-rw-r--r-- 1 root root")]                    // truncated unix line
    public void ParseListLine_MalformedLine_ReturnsNull(string line)
    {
        Assert.Null(FtpBrowser.ParseListLine(line, "/"));
    }

    [Fact]
    public void ParseListLine_FilenameExceedingMaxLength_ReturnsNull()
    {
        var giantName = new string('a', FtpBrowser.MaxFtpFilenameLength + 1);
        var line = $"-rw-r--r-- 1 user user 1 Jan 01 12:00 {giantName}";

        Assert.Null(FtpBrowser.ParseListLine(line, "/"));
    }

    // ── ParseUnixDate ─────────────────────────────────────────────────

    [Fact]
    public void ParseUnixDate_TimeFormat_AppliesCurrentYear()
    {
        // "Mar 15 09:00" → year is current year (or previous if in the future).
        var result = FtpBrowser.ParseUnixDate("Mar 15 09:00");

        Assert.NotEqual(DateTime.MinValue, result);
        Assert.Equal(3, result.Month);
        Assert.Equal(15, result.Day);
    }

    [Fact]
    public void ParseUnixDate_YearFormat_UsesParsedYear()
    {
        var result = FtpBrowser.ParseUnixDate("Mar 15  2024");

        Assert.Equal(2024, result.Year);
        Assert.Equal(3, result.Month);
        Assert.Equal(15, result.Day);
    }

    [Fact]
    public void ParseUnixDate_FutureDateInTimeFormat_RollsBackOneYear()
    {
        // Build a "Dec 31 23:59" parsed in a January context. The heuristic
        // subtracts one year if the parsed date is in the future for the
        // current year. This is a regression test for the audit finding L3.
        var result = FtpBrowser.ParseUnixDate("Dec 31 23:59");
        var now = DateTime.Now;

        // If it's currently early in the year, a "Dec 31" entry must be
        // attributed to the previous year, not the current one.
        if (now.Month <= 2)
        {
            Assert.True(result.Year <= now.Year,
                $"ParseUnixDate('Dec 31 23:59') returned {result:O}; expected previous-year rollover.");
        }
        else
        {
            // Outside the early-year window the heuristic does not roll back.
            Assert.Equal(now.Year, result.Year);
        }
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("")]
    [InlineData("Foo 99 99:99")]
    public void ParseUnixDate_Garbage_ReturnsMinValue(string input)
    {
        Assert.Equal(DateTime.MinValue, FtpBrowser.ParseUnixDate(input));
    }
}
```

The test for the year-rollover heuristic (`ParseUnixDate_FutureDateInTimeFormat_RollsBackOneYear`) gates its assertion on the current month so the test does not become time-dependent. If a future refactor fixes L3 by replacing the heuristic with an explicit "treat as previous year if month > current month" rule, this test will need to be updated; that is intentional and correct.

### Step 3 — Add validation to `FtpHandler`

In `src/Heimdall.App/Services/Handlers/FtpHandler.cs`, before the `browser.ConnectAsync(...)` call:

```csharp
public async Task<ConnectionResult> ConnectAsync(
    ServerProfileDto server,
    AppSettings settings,
    CancellationToken ct,
    RdpModeOverride rdpModeOverride = RdpModeOverride.UseProfile)
{
    ArgumentNullException.ThrowIfNull(server);
    ArgumentNullException.ThrowIfNull(settings);

    _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);

    if (string.IsNullOrWhiteSpace(server.RemoteServer))
    {
        var msg = _localizer["ErrorInvalidTargetHost"];
        _connectionSm.SetError(server.Id, msg);
        return new ConnectionResult(false, msg, null);
    }

    if (!IsValidFtpHost(server.RemoteServer))
    {
        var msg = _localizer["ErrorInvalidTargetHost"];
        _connectionSm.SetError(server.Id, msg);
        return new ConnectionResult(false, msg, null);
    }

    var port = server.FtpPort > 0 ? server.FtpPort : DefaultPorts.Ftp;
    if (!InputValidator.ValidatePortRange(port))
    {
        var msg = _localizer.Format("ErrorInvalidPort", port.ToString());
        _connectionSm.SetError(server.Id, msg);
        return new ConnectionResult(false, msg, null);
    }

    _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingFtp);

    var host = server.RemoteServer;
    var username = server.FtpUsername;
    var password = ConnectionHelpers.DecryptPassword(server.FtpPasswordEncrypted);

    var browser = new FtpBrowser();

    try
    {
        await browser.ConnectAsync(
                host,
                port,
                username,
                password,
                server.FtpPassiveMode,
                server.FtpUseSsl,
                ct)
            .ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        browser.Dispose();
        Core.Logging.FileLogger.Warn($"FTP connect failed: {ex.Message}");
        var userMsg = _localizer.Format("ErrorFtpConnectionFailed", ex.Message);
        _connectionSm.SetError(server.Id, userMsg);
        return new ConnectionResult(false, userMsg, null);
    }

    _connectionSm.TryTransition(server.Id, ConnectionState.Connected);

    string? warning = null;
    if (!server.FtpUseSsl && !string.IsNullOrEmpty(username))
    {
        warning = _localizer.Format("WarnFtpCleartext", host, port.ToString());
    }

    return new ConnectionResult(
        true,
        null,
        new FtpSessionBundle(browser),
        Failure: null,
        Warning: warning);
}

private static bool IsValidFtpHost(string host)
{
    return !string.IsNullOrWhiteSpace(host)
        && (InputValidator.ValidateDomain(host) || System.Net.IPAddress.TryParse(host, out _));
}
```

The `IsValidFtpHost` helper mirrors `SshHandler.IsValidSshHost` (private static in the same project — copy the shape, do not extract a shared helper for this small one-off; cross-handler refactoring is out of scope for the audit).

If `ErrorInvalidPort` does not yet exist as a localization key, add it (see Step 5 for the locale work).

### Step 4 — Extend `ConnectionResult` with a `Warning` field

In `src/Heimdall.App/Services/ProtocolSessionResults.cs`:

```csharp
/// <summary>
/// Immutable result of a connection attempt.
/// </summary>
/// <param name="Success">Whether the connection was established.</param>
/// <param name="ErrorMessage">Error description on failure; null on success.</param>
/// <param name="Session">Typed session result on success; null on failure.</param>
/// <param name="Failure">Optional structured failure details when the connection fails.</param>
/// <param name="Warning">
/// Optional non-fatal warning to display on a successful connection (e.g. cleartext
/// transport, deprecated protocol). Null when no warning applies. Consumers must
/// treat this as informational; <see cref="Success"/> remains true and the session
/// is usable.
/// </param>
public sealed record ConnectionResult(
    bool Success,
    string? ErrorMessage,
    ISessionResult? Session,
    SessionDiagnostic? Failure = null,
    string? Warning = null);
```

The new field is optional with a `null` default, so every existing call site in the codebase keeps compiling unchanged.

### Step 5 — UI surface for the warning

The `ConnectionResult.Warning` is now populated. The view layer needs to display it after a successful connection.

The actual surface depends on how connection results are consumed. Search for the dispatch site:

```bash
grep -rn "result.ErrorMessage\|result\.Success" src/Heimdall.App/Services/ src/Heimdall.App/ViewModels/
```

Find the place that already shows `ErrorMessage` to the user (most likely a `ConnectionService` or a `SessionCoordinator` style class). At the symmetric point — right after a successful connection — display the `Warning` if non-null. Use the same status / banner mechanism that already exists for errors (do not invent a new dialog).

A reasonable shape:

```csharp
if (result.Success && !string.IsNullOrEmpty(result.Warning))
{
    SetStatusText?.Invoke(result.Warning);   // or whatever status surface is in use
    Core.Logging.FileLogger.Warn($"Connection warning for {server.DisplayName}: {result.Warning}");
}
```

Document the wiring choice in the report. If the connection-result consumer does not have an obvious status surface (e.g. it just returns the result up the stack), the wiring may stop at the consumer's caller — find the next layer that does have a UI surface and wire it there. The audit cares that the warning **reaches a user-visible surface**, not specifically which layer routes it.

### Step 6 — Locale keys

`locales/en.json`:

```json
"WarnFtpCleartext": "FTP session to {0}:{1} is using a cleartext channel. Username and password are transmitted unencrypted. Prefer SFTP when available.",
"ErrorInvalidPort": "Invalid port: {0}. Port must be between 1 and 65535."
```

`locales/fr.json`:

```json
"WarnFtpCleartext": "La session FTP vers {0}:{1} utilise un canal en clair. Le nom d'utilisateur et le mot de passe sont transmis sans chiffrement. Préférez SFTP lorsque c'est possible.",
"ErrorInvalidPort": "Port invalide : {0}. Le port doit être compris entre 1 et 65535."
```

If `ErrorInvalidPort` already exists with a different wording, reuse it as-is rather than overwriting — i18n parity rules forbid removing keys.

### Step 7 — Tests for `FtpHandler` validation + warning surface

In `tests/Heimdall.App.Tests/FtpHandlerValidationTests.cs`:

```csharp
public sealed class FtpHandlerValidationTests
{
    [Fact]
    public async Task ConnectAsync_RejectsWhitespaceHost()
    {
        var handler = BuildHandler(out var localizer);
        var server = new ServerProfileDto { ... RemoteServer = "   ", FtpPort = 21 };

        var result = await handler.ConnectAsync(
            server, BuildSettings(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ConnectAsync_RejectsInvalidHost()
    {
        var handler = BuildHandler(out _);
        var server = new ServerProfileDto { ... RemoteServer = "this is not a host", FtpPort = 21 };

        var result = await handler.ConnectAsync(
            server, BuildSettings(), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ConnectAsync_RejectsOutOfRangePort()
    {
        var handler = BuildHandler(out _);
        var server = new ServerProfileDto { ... RemoteServer = "ftp.example.com", FtpPort = 70_000 };

        var result = await handler.ConnectAsync(
            server, BuildSettings(), CancellationToken.None);

        Assert.False(result.Success);
    }

    // ... helper builders for handler / settings / DTO defaults ...
}
```

Constructing `FtpHandler` requires `ConnectionStateMachine` and `LocalizationManager` — copy the test fixture style from `tests/Heimdall.App.Tests/PlinkFailClosedTests.cs` (which already builds those collaborators).

The cleartext-warning-surface test is harder to write because the actual `browser.ConnectAsync` would attempt a real TCP connection. **Two acceptable strategies**:

- **Strategy A (preferred)**: extract the "compute warning" decision into an `internal static` helper on `FtpHandler` and unit-test that helper directly:

  ```csharp
  internal static string? ComputeCleartextWarning(
      bool useSsl,
      string? username,
      string host,
      int port,
      Func<string, string[], string> formatLocalized)
  {
      if (useSsl || string.IsNullOrEmpty(username))
      {
          return null;
      }
      return formatLocalized("WarnFtpCleartext", new[] { host, port.ToString() });
  }
  ```

  Then test `ComputeCleartextWarning(useSsl: false, username: "anonymous", ...) == null` (anonymous is not credential-bearing? — keep the existing semantics; if `string.IsNullOrEmpty` is the check, "anonymous" is non-empty and produces a warning. That is fine — anonymous still discloses that a login attempt happened. The test pins whatever shape the production code chose.) and `ComputeCleartextWarning(useSsl: true, ...) == null`.

- **Strategy B (skip)**: do not test the warning surface end-to-end. The unit test on the handler validation is enough; the warning routing is exercised by the existing manual smoke tests. Document the omission in the report.

Strategy A adds maybe 10 lines of code and one tiny test class — prefer it.

### Step 8 — Roadmap doc

Create `docs/audit/ftp-fluentftp-migration.md`:

```markdown
# FTP / FluentFTP migration — roadmap

## Why

`Heimdall.Sftp.FtpBrowser` is built on `System.Net.FtpWebRequest`, which is
marked `[Obsolete]` since .NET 6 and explicitly retains a `#pragma warning
disable SYSLIB0014` to silence the warning. The audit (item M2 / A4) flagged
three concrete consequences:

- Synchronous I/O wrapped in `Task.Run` (no real async).
- `EnableSsl=true` issues `AUTH TLS` but does not enforce `PROT P` for the
  data channel — depending on the server, file payloads can transit
  cleartext on a "TLS-enabled" session.
- LIST output parsing relies on regex for Unix and DOS formats; servers in
  non-English locales (German months, Cyrillic dates) silently produce
  empty entries.

## Recommended target

[FluentFTP](https://github.com/robinrodricks/FluentFTP) (MIT licensed, active
maintenance, real async, FTPS with `PROT P` enforcement, robust LIST parsing
across locales).

## Scope sketch

1. Replace `FtpBrowser` internals with FluentFTP's `AsyncFtpClient`.
2. Keep `IRemoteBrowser` as the public surface — no view-model changes.
3. Map FluentFTP exceptions to typed Heimdall exceptions where reasonable.
4. Carry over the cleartext warning + `IsTlsEnabled` property.
5. Migrate `tests/Heimdall.App.Tests/FtpBrowserParsingTests` to drive
   FluentFTP's `FtpListItem` rather than the home-grown regex helpers.

## Out of scope until migration starts

- Behaviour-equivalent SCP / FXP support.
- Resumable transfers (FluentFTP supports them; the current `FtpBrowser`
  does not, so this is a pure addition).

## Tracking

Open as a GitHub issue when ready to pick up. Reference the audit P2 #12
entry in the audit consolidated plan.
```

Then add a `// TODO(audit-A4):` reference inside `FtpBrowser.cs` near the existing `#pragma warning disable SYSLIB0014`:

```csharp
// TODO(audit-A4): migrate to FluentFTP — see docs/audit/ftp-fluentftp-migration.md
#pragma warning disable SYSLIB0014 // FtpWebRequest is obsolete but no built-in replacement exists
```

### Sanity checks

```bash
grep -n "private static string NormalizePath\|private static SftpFileInfo\|private static DateTime ParseUnixDate" src/Heimdall.Sftp/FtpBrowser.cs
```

Should return zero matches — the helpers are now `internal static`.

```bash
grep -n "internal static.*NormalizePath\|internal static.*ParseListLine\|internal static.*ParseUnixDate\|internal static.*ResolvePath" src/Heimdall.Sftp/FtpBrowser.cs
```

Should return four matches.

```bash
grep -n "Warning" src/Heimdall.App/Services/ProtocolSessionResults.cs
```

Should match the new `Warning` parameter on `ConnectionResult`.

```bash
grep -rn "WarnFtpCleartext" locales/
```

Should return two matches (en + fr).

```bash
grep -n "TODO(audit-A4)" src/Heimdall.Sftp/FtpBrowser.cs
```

Should return one match.

## Coding standards

Same as previous prompts:

- Apache 2.0 header on the new test files and the new doc (markdown does not need a license header, but you may include a one-line note at the top noting the file is part of the Heimdall.Next audit work).
- English only.
- Nullable reference types stay enabled.
- `TreatWarningsAsErrors` is on.
- `ConfigureAwait(false)` on every new `await` you introduce in non-UI projects.
- i18n key parity is CI-enforced — both `en.json` and `fr.json` must gain `WarnFtpCleartext` and `ErrorInvalidPort` (if not already present).
- No `[Co-Authored-By]` or AI attribution.

## Build & verify

```powershell
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build green with zero new warnings; suite passing count rises by ~25 (depends on how many `[Theory]` rows you ship). The known flaky `TracerouteViewModelTests` test is unrelated — re-run the suite once if it fails on first attempt.

## Reporting back

When you finish, report:

1. The list of source files modified or created (split by goal: tests / validation / warning surface / roadmap).
2. The exact diff of the new `FtpHandler.ConnectAsync` body (validation + warning population) and the new `ConnectionResult` declaration.
3. The list of tests added (class name + each method name), with `[Theory]` row counts where applicable.
4. The final test counts (passed / failed / skipped) for both the targeted runs (`FtpBrowserParsingTests`, `FtpHandlerValidationTests`, and any cleartext-warning helper test) and the full suite.
5. Confirm the five grep checks above produce the expected counts.
6. The path of the new roadmap markdown file.
7. The chosen wiring for the warning surface (Step 5) — which file/class displays the `ConnectionResult.Warning` to the user.
8. Any decision that diverged from this prompt, with a one-line rationale (especially: Strategy A vs Strategy B for the cleartext-warning test).

This prompt closes the audit consolidated plan. After Claude Code's report, the SSH/SFTP audit work is complete.
