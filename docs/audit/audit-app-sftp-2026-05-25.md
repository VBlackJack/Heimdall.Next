# Quality Audit — App-layer SFTP/FTP (file browser, editor, file sharing)

**Date:** 2026-05-25
**Mode:** pair-architect (supervisor = Cowork, implementation = Julien via Claude Code)
**Scope:** the App-layer surface deliberately left out of the core SFTP/FTP audit
(`audit-sftp-ftp-2026-05-25.md`).

## Perimeter (validated)

11 production files, ~6 000 lines, plus 4 existing test files.

| Cluster | Files | Lines |
|---|---|---|
| Embedded SFTP | `Views/EmbeddedSftpView.xaml` (539) + `.xaml.cs` (1506) + `ViewModels/EmbeddedSftpViewModel.cs` (1063) | 3108 |
| Remote editor | `Views/EmbeddedEditorView.xaml` (66) + `.xaml.cs` (260) + `ViewModels/EmbeddedEditorViewModel.cs` (239) | 565 |
| Local browser | `Views/LocalFileBrowserView.xaml` (261) + `.xaml.cs` (524) + `ViewModels/LocalFileBrowserViewModel.cs` (638) | 1423 |
| HTTP/TFTP server | `Services/EphemeralFileServer.cs` (675) | 675 |
| File sharing | `Services/FileShareService.cs` (252) | 252 |

Out of perimeter: `SftpBrowser`/`FtpBrowser`/`RemoteFileEditor`/`PathEscaper` (core,
audited 2026-05-25); `MainWindow` wiring; `EmbeddedSessionManager`; theming XAML.

## Verdict

**0 P1 / 6 P2 / 24 P3.** The layer is in reasonable health. Credential discipline
is correct everywhere (host + protocol only in logs, no username/password material).
Shell-injection defence is consistent (`PathEscaper.EscapeForShell` on every sudo
path; `ProcessStartInfo.ArgumentList` on the hardened editor-launch path). The
`EphemeralFileServer` HTTP path has a sound security model — 256-bit CSPRNG bearer
token, constant-time comparison, canonicalise-then-recheck traversal defence with a
trailing-separator base.

The defects cluster in four places: the local browser does all filesystem I/O
synchronously on the UI thread; the App layer carries its **own** copy of the
`sudo cat` binary-corruption bug that the core audit just fixed elsewhere; the
embedded-edit path in `EmbeddedSftpView` leaks temp directories and shares one
`CancellationTokenSource` across concurrent transfers; and the remote-save contract
clears the modified flag before the upload is confirmed.

The agreed P3 on architectural debt (`EmbeddedSftpView.xaml.cs` = 1506 lines of
code-behind) is recorded as **AD-1** with a refactor framing; it is a follow-up
mini-chantier, not a remediation chunk.

---

## P2 findings

### P2-1 — `CopyDirectoryRecursive` is unbounded and follows reparse points
`ViewModels/LocalFileBrowserViewModel.cs:597-612`. Hand-rolled recursive copy with
no depth cap and no `FileAttributes.ReparsePoint` check. `Directory.EnumerateDirectories`
returns junctions/symlinks; a directory junction that points at an ancestor produces
infinite recursion → `StackOverflowException`, which is **uncatchable** and terminates
the process, possibly after writing a deep partial copy to disk. Reached from
`PasteFilesAsync` (`:441-444`) when a pasted clipboard source is a directory; the
local browser defaults its start path to the user profile (`View` ctor `:78`), the
folder most likely to contain junctions. Trigger is uncommon (needs a junction loop
in the pasted tree) but the crash is total. The core audit established the fix
pattern (`DeleteDirectoryRecursive` is bounded by `MaxDeleteDepth = 256`).
*Fix direction:* add a depth cap and skip reparse-point directories; mirror the core
`MaxDeleteDepth` constant.

### P2-2 — Local browser directory enumeration and file operations block the UI thread
`ViewModels/LocalFileBrowserViewModel.cs`. `LoadDirectoryCore` (`:484-541`) is a
synchronous `void`: `Directory.EnumerateDirectories`/`EnumerateFiles` plus a
`new DirectoryInfo`/`new FileInfo` per entry (a `stat` each) run on the calling
thread. It is called from the ctor (`:96`) and every navigation handler — all on the
UI thread. On a slow SMB share, a dead junction target, or a directory with tens of
thousands of entries the UI hard-freezes for the full I/O timeout. `IsLoading` is set
and cleared inside the same synchronous block, so the indeterminate `ProgressBar`
never renders. The same applies to `DeleteEntriesAsync` (`:287/291`), `PasteFilesAsync`
(`:439/443`), `RenameEntryAsync` (`:342/346`) and `CopyDirectoryRecursive` — declared
`async Task` but `await`-ing only the dialog prompts, never the I/O.
*Fix direction:* move enumeration and bulk file operations onto a background thread
(`Task.Run`), marshal results back to the UI thread, and support cancelling a stale
load when the user navigates again. Note: `Files` is an `ObservableCollection` and
the `[ObservableProperty]` setters must be marshalled back once this becomes truly
async (otherwise a cross-thread `NotSupportedException`).

### P2-3 — `DownloadViaSudoAsync` corrupts binary files and adds a BOM
`ViewModels/EmbeddedSftpViewModel.cs:509-531`. The sudo download fallback runs
`sudo cat <path>` and writes `cmd.Result` (a decoded `string`) with
`File.WriteAllTextAsync(localPath, ..., Encoding.UTF8)`. Round-tripping arbitrary
file bytes through a UTF-8 string decode/encode silently corrupts any non-UTF-8 file
(images, archives, executables, firmware), and `File.WriteAllText` with `Encoding.UTF8`
prepends a BOM. The non-sudo path (`_browser.DownloadFileAsync`) is byte-faithful.
**This is the same defect class the core audit closed in `RemoteFileEditor.EditFileSudoAsync`
(Chunk A, `814bbfb`)** — the App layer carries an independent copy that the core
perimeter excluded. Reached from `OnCtxDownloadClick` (`Views/EmbeddedSftpView.xaml.cs:706`)
on a permission-denied download.
*Fix direction:* mirror the core fix exactly — `sudo base64 -- <path>`, decode with
`Convert.FromBase64String` (tolerant of line wrapping), `File.WriteAllBytesAsync`;
check `ExitStatus` before decoding.

### P2-4 — Embedded-edit temp directory leaked on several exit paths
`Views/EmbeddedSftpView.xaml.cs:880-998` (`EditFileAsync`). A temp dir
`%TEMP%\Heimdall\edit\<guid>` is created at `:893-894`; cleanup lives only in the
`CloseRequested` handler (`:987-991`) and is skipped while `isSaving` is true. If the
SFTP tab/session is disposed while the embedded editor is still open, `Dispose()`
(`:164`) never touches these dirs — they accumulate under `%TEMP%` holding downloaded
remote file content. The sudo fallback branch (`:907-913`) returns early at `:912`
**after** the temp dir was created at `:894`, orphaning an empty dir every time.
*Fix direction:* track active edit temp dirs in a field and clean them in `Dispose()`;
clean the sudo early-return path; prefer a deterministic owner over a UI event.

### P2-5 — One shared `_transferCts` across concurrent upload and download
`Views/EmbeddedSftpView.xaml.cs`. `UploadFilesAsync` (`:603-606`) and
`OnCtxDownloadClick` (`:673-676`) each do `_transferCts?.Cancel(); _transferCts?.Dispose();
_transferCts = new …`. A download started while an upload is running (reachable: drag-drop
upload runs in the background, then right-click → Download → pick a folder) disposes the
upload's still-referenced token → `ObjectDisposedException` on the upload's
`ct.ThrowIfCancellationRequested()`, surfaced as a misleading generic transfer error.
The shared `TransferPanel`/`TransferText`/`TransferProgressBar` are also interleaved.
*Fix direction:* gate transfers behind a single in-progress flag (disable upload and
download entry points while a transfer runs), or give each transfer its own CTS and
exclusive ownership of the progress panel.

### P2-6 — `EmbeddedEditorViewModel.SaveAsync` clears the modified flag before a remote save is confirmed
`ViewModels/EmbeddedEditorViewModel.cs:141-175`. For remote files (`IsRemote == true`)
`SaveAsync` does no I/O — it sets `IsModified = false` (`:155`), raises `FileSaved`
(`:156`) and returns `true` unconditionally. The actual upload runs in the `FileSaved`
subscriber (`Views/EmbeddedSftpView.xaml.cs:929-949`); when the upload throws, the
subscriber shows a status error, but `IsModified` is already `false` and `SaveAsync`
already returned success. The editor title star disappears, the user believes the
file is saved, and closing it raises no unsaved-changes prompt — the edit can be lost.
`OnSaveClick` (`Views/EmbeddedEditorView.xaml.cs:137`) ignores the return value anyway.
*Fix direction:* do not clear `IsModified` / return success for remote files until the
consumer confirms the write — make `FileSaved` an awaitable confirmable contract
(`Func<…, Task<bool>>`) or have the consumer call back into the VM to set saved state.

---

## P3 findings

### Hardcoding (strings / magic numbers)
- **P3-1** `EmbeddedSftpViewModel.cs:259-261` — `ItemCountText` built as `"{n} items"` /
  `"{v}/{t} items"`, hardcoded English, permanently visible in the status bar.
- **P3-2** `EmbeddedSftpViewModel.cs:694` — `$"{entries.Count} items"` hardcoded fallback.
- **P3-3** `EmbeddedSftpViewModel.cs:812-813` — `ShowProperties` emits `"Owner: … Group: …"`
  and `"Path: …"` hardcoded while the lines above use `L10n(...)`.
- **P3-4** `EmbeddedSftpView.xaml.cs:1109` — `OnTransferProgress` builds the whole
  `TransferText` layout inline while other transfer texts use `_localizer.Format`.
- **P3-5** `EmbeddedEditorViewModel.cs:213` (`"Ln {l}, Col {c}"`) and `:235` (`"Untitled"`)
  — user-facing, not localised.
- **P3-6** `EmbeddedEditorView.xaml.cs:213` — `"Error loading file: …"` rendered into the
  editor body, not localised.
- **P3-7** `FileShareService.cs:92,95,96` — `?? 2000`, `?? 8080`, `?? 69` duplicate the
  `AppSettings` defaults; two sources of truth.
- **P3-8** `EphemeralFileServer.cs:248` — `socket.Connect("8.8.8.8", 80)` hardcodes a
  third-party IP for local-IP discovery.
- **P3-9** Magic layout constants: `EmbeddedSftpView.xaml.cs:515,517` and
  `LocalFileBrowserView.xaml.cs:148,150` (`- 10`, `> 200`); `LocalFileBrowserViewModel.cs`
  editor fallback `%windir%\system32\notepad.exe` (`:422`).

### Resource lifetime
- **P3-10** `LocalFileBrowserView` is **not** `IDisposable` — `Loaded += OnViewLoaded`
  (`:96`) is never detached, and the VM's three public events (`NavigateToPathRequested`,
  `RunInShellRequested`, `EditInEditorRequested`) wired by `EmbeddedSessionManager` are
  never detached. Accumulating handler leak per local-shell session. Inconsistent with
  `EmbeddedSftpView`, which is `IDisposable` and detaches everything.
- **P3-11** `EmbeddedSftpView.xaml.cs:1474-1481` — `_errorResetTimer` is a one-shot timer
  never disposed after it fires (only the next error or `Dispose()` disposes it), and its
  dispatcher callback (`:1477-1480`) is the only one in the file missing the `_disposed`
  guard.
- **P3-12** `EmbeddedSftpView.xaml.cs:929-991` — `editorView.FileSaved` (`:929`) and
  `CloseRequested` (subscribed twice, `:954` and `:987`) are never detached. Not a true
  leak (the `EmbeddedEditorView` is transient and self-collects after the pane swaps
  back), but there is no detach contract.
- **P3-13** `EphemeralFileServer.cs:224-226` — `Dispose()` has three empty
  `catch { /* … */ }` blocks; the async stop paths log, the sync path does not
  (violates "no swallowed exceptions").

### Correctness / robustness
- **P3-14** `LocalFileBrowserViewModel.cs:334` and `:386` — containment guards use
  `newPath.StartsWith(parentDir/CurrentPath, OrdinalIgnoreCase)` with no separator
  boundary (`C:\Users\Bob` matches `C:\Users\BobEvil`). Not currently exploitable —
  the upstream `GetInvalidFileNameChars()` + `".."` name check (`:324`, `:377`) blocks
  any separator in the name — but the guard is written wrong. *Fix:* compare against a
  base with a guaranteed trailing separator, or use `Path.GetRelativePath`.
- **P3-15** `EphemeralFileServer.cs:446-450` — `HandleTftpReadRequest` parses the RRQ
  filename/mode **before** its try block (`try` starts `:482`). A malformed RRQ with no
  null terminator makes `ReadNullTerminatedString` compute `offset > data.Length`, so
  the second call throws `ArgumentOutOfRangeException` from `Array.IndexOf`, escaping
  the fire-and-forget `Task.Run` (`:440`) as an unobserved exception. *Fix:* validate
  offsets before the second parse and wrap the whole handler body.
- **P3-16** `EphemeralFileServer.cs:440` — every RRQ spawns an unbounded
  `_ = Task.Run(…)`; a UDP RRQ flood spawns unbounded tasks/sockets (local DoS on an
  opt-in feature). *Fix:* bound concurrent TFTP transfers with a semaphore.
- **P3-17** `EphemeralFileServer.cs` — fixed ports (`DefaultPorts.Http`/`Tftp`) with no
  collision handling: the HTTP catch (`:109`) only retries `localhost` on the **same**
  port (URL-ACL fallback, not port-in-use); `new UdpClient(port)` (`:179`) throws on
  collision. Predictable share failure if 8080/69 is taken.
- **P3-18** `EmbeddedSftpViewModel.cs` sudo helpers — `ssh.Disconnect()` in a `finally`
  (`:501,529,571,957`) can throw and mask the primary exception; redundant with the
  `using var ssh`. *Fix:* wrap `Disconnect()` in its own try/catch (log + swallow).
- **P3-19** `EmbeddedSftpViewModel.cs:961-1015` (`ParseLsOutput`) — silently `continue`s
  on malformed `ls` lines with no diagnostic and assumes `--time-style=long-iso` is
  honoured (BusyBox `ls` differs). *Fix:* log skipped lines at debug; document the
  distro assumption.
- **P3-20** `EmbeddedSftpView.xaml.cs:1155-1236` (`OnReconnectClick`) — if
  `newBrowser.ConnectAsync` throws (`:1185`), the old `_browser` is already disposed
  (`:1179`) but `_browser`/`_editor` still reference disposed objects; the view is
  left half-wired (the catch shows an error but does not call `ShowDisconnectedState`).
  A second Reconnect recovers, double-dispose is tolerated. *Fix:* build the
  replacement fully before tearing down the old, or null the fields on failure.
- **P3-21** `EphemeralFileServer.cs:409` — `Content-Disposition` interpolates the raw
  filename. Not exploitable on Windows (NTFS filenames cannot contain `"`/CR/LF), but
  encode it for defence in depth (RFC 5987). No `Cache-Control: no-store` on served
  files or the directory listing — sensitive content stays browser/proxy-cacheable.
- **P3-22** `EphemeralFileServer` token in query string (`AppendTokenToUrl`,
  `:637-641`) — leaks into browser history / `Referer` / proxy logs. Partly inherent
  (browser-clickable links need the token in the URL; the `curl` template correctly
  uses an `Authorization: Bearer` header). Per-share token rotation (a fresh server
  per share) bounds the blast radius. Document the trade-off.
- **P3-23** Async-void event handlers `await` ViewModel methods whose pre-`try` region
  (the dialog `await`) is unguarded: `EmbeddedSftpView.xaml.cs` `OnNewFolderClick`
  /`OnCtxRenameClick`/`OnCtxDeleteClick`/`OnCtxChmodClick`; `LocalFileBrowserView.xaml.cs`
  `OnCtxPaste`/`OnCtxDelete`/`OnCtxRename`/`OnCtxNewFolder`; `EmbeddedEditorView.xaml.cs`
  `OnSaveClick`/`OnCloseClick`. A throwing dialog service crashes the app. *Fix:*
  defensive try/catch→log in each handler, or a shared safe-invoke wrapper.
- **P3-24** Minor: `EmbeddedSftpViewModel.cs` sets `CurrentPath`/`ShowHidden`/`SortColumn`
  /`SortDirection` twice (field initialiser + ctor `:59-63`); `LocalFileBrowserViewModel.cs:479`
  dead `_ = value;`; `EmbeddedSftpView.xaml.cs:372-391` `OnColumnHeaderClick` first
  reads `Header.ToString()` then overwrites it via an exhaustive index switch (the
  string read survives only as an emptiness guard); `EmbeddedEditorViewModel`
  `LoadContent(content)`/`RequestClose(currentText)` discard their parameters
  (`:126`,`:183`); `LocalFileBrowserView.xaml.cs:254` `OnCtxOpen` forges
  `OnFileDoubleClick(sender, null!)` (safe today, latent NRE); `LocalFileBrowserView`
  `OnCtxOpenWith`/`OnCtxOpenInExplorer` build command lines with naive `"…"` quoting
  (not exploitable — NTFS paths cannot contain quotes — but inconsistent with the
  hardened `ArgumentList` editor path); doc drift: `CLAUDE.md` overstates the local
  browser path validation, `EmbeddedEditorView.xaml.cs:149-150` comment says "All 7
  themes" (16 ThemeForge themes now); inaccessible top folder in `LoadDirectoryCore`
  catch (`:533`) leaves the listing stale with no UI error.

### Security model — needs an explicit decision (not a code defect)
- **P3-25** TFTP is unauthenticated by protocol (RFC 1350 has no auth); the HTTP path
  is token-gated, the TFTP path is not, and both bind all interfaces. This is TFTP
  working as designed (network devices only speak plain TFTP) and the listener is
  opt-in (`FileShareEnableTftp` defaults `false`). The finding is the **asymmetry**:
  a user may assume the same token gates both. *Decision needed:* surface the
  "TFTP is unauthenticated on the LAN" warning explicitly when the toggle is enabled.

### Test coverage gaps
- **P3-26** `EmbeddedSftpViewModelTests.cs` (50 lines) covers almost nothing — the
  pure, dispatcher-free helpers `GetParentPath`, `CombineRemotePath`, `PermissionsToOctal`,
  `ApplyFilterAndSort`, `ParseLsOutput`, `IsPermissionDenied`, `SudoUploadCommands.Build`
  are all untestable-by-omission. `LocalFileBrowserViewTests.cs` covers only the editor
  start-info; the traversal guards (P3-14) are untested. `EphemeralFileServerTests.cs`
  has **no directory-traversal test** for either protocol — the highest-value gap.
  `FileShareServiceTests.cs` does not test the double-start exception, post-dispose
  behaviour, or `StopAsync` idempotency.

---

## Architectural debt

### AD-1 — `EmbeddedSftpView.xaml.cs` is 1506 lines of code-behind
The View/ViewModel split is half-done; the VM doc (`EmbeddedSftpViewModel.cs:35`)
itself states "File operations, transfers, and connection lifecycle remain in the
code-behind." Logic misplaced in the view, by bucket: file transfers
(`UploadFilesAsync`, `OnCtxDownloadClick` body — CTS lifecycle, progress, sudo
fallback); connection lifecycle (`OnReconnectClick` ~80 lines, `OnBrowserDisconnected`,
`ShowDisconnectedState`, health timer); embedded editing (`EditFileAsync` ~120 lines —
temp files, editor construction, pane-tree surgery, save/close closures); event
marshalling and status plumbing; column-width/header logic.

A separate MVVM-extraction refactor would involve: (1) a transfer service/VM commands
owning CTS + progress (removes P2-5); (2) a reconnect/lifecycle service so browser
swap + event re-wiring lives in one testable place (removes P3-20); (3) an
editor-launch coordinator owning temp-dir lifecycle (removes P2-4); (4) `ICommand`
bindings instead of imperative `Click=` handlers. Risk areas: `EditFileAsync` is
tightly coupled to the split-pane tree (`SplitTreeHelper`/`HostControl` swap) —
extracting it risks pane-identity bugs; browser event wiring must not double-subscribe
during reconnect; the two `System.Threading.Timer`s must move atomically with their
callbacks. Recommended as its own mini-chantier, scoped on its own merits — **not**
folded into a remediation chunk. Same treatment as `EmbeddedRdpView` (3815 lines) at
the RDP audit.

---

## Dismissed / false positives (verified)

- **`var` usage** — `.editorconfig` sets `csharp_style_var_*` to `:suggestion`, not
  `:warning`; `var` is permitted and used project-wide. Already declared out of scope
  by the SSH (S11) and RDP audits. Not a finding.
- **Nullable flow `_sshParams` → `EditFileSudoAsync`** (`EmbeddedSftpView.xaml.cs:849,911`)
  — the build is green under `TreatWarningsAsErrors`; the `when`-filter null-state
  flows correctly. No `CS8604`. Not a finding.
- **`FileShareService` TFTP command in localhost-only fallback** — TFTP binds all
  interfaces regardless of the HTTP URL-ACL fallback, so `TftpCommand` correctly uses
  the routable IP. Not a bug.
- **`FileShareService` Start/Stop race** — theoretically a check-then-act race could
  leak a zombie server, but the start path completes synchronously (no `await` yields
  on the success path) on the single UI thread, so it is not reachable from the known
  caller. Recorded as a latent hardening item inside Chunk E, not a P2.

---

## Remediation chunks (proposed)

| Chunk | Theme | Findings | Files |
|---|---|---|---|
| A | Local browser robustness & async | P2-1, P2-2, P3-14, + inaccessible-folder UX | `LocalFileBrowserViewModel.cs` (+ minor view) |
| B | SFTP ViewModel sudo & parsing | P2-3, P3-18, P3-19, P3-24 (defaults), P3-1/2/3 | `EmbeddedSftpViewModel.cs` |
| C | SFTP view transfer & edit lifecycle | P2-4, P2-5, P3-11, P3-20 | `EmbeddedSftpView.xaml.cs` |
| D | Remote-save contract | P2-6, P3-23 (editor handlers), P3-24 (dead params), P3-5/6 | `EmbeddedEditorViewModel.cs`, `EmbeddedEditorView.xaml.cs`, FileSaved handler in `EmbeddedSftpView.xaml.cs` |
| E | HTTP/TFTP server & sharing hardening | P3-13, P3-15, P3-16, P3-17, P3-21, P3-22, P3-25, P3-7, P3-8, FileShareService Start/Stop guard | `EphemeralFileServer.cs`, `FileShareService.cs` |
| F | P3 sweep & tests | P3-9, P3-10, P3-12, P3-23 (remaining), P3-24 (remaining), P3-26 | all |

Proposed order: **A → B → C → D → E → F** (severity/value decreasing, mostly
file-coherent). One chunk at a time, each ending with `dotnet build -c Release` +
`dotnet test`. AD-1 is a separate follow-up mini-chantier, to be cadré after F.

---

## Remediation — CLOSED (2026-05-25)

All six chunks delivered in pair-architect mode (supervisor = Cowork, each delivery
re-verified against the real code). Verdict 0 P1 / 6 P2 / 24 P3 fully treated.

14 atomic commits on `master` (local, not yet pushed):

- Chunk A: `fef54af` `213a222`
- Chunk B: `466db09` `4c4f79d`
- Chunk C: `592cbf7` `7a5f2a3`
- Chunk D: `63a11c3` `0ee4731`
- Chunk E: `431f8a7` `e45d4af` `5fdb736`
- Chunk F: `1855e1a` `a8e2ae5` `da4590f`

Test suite 5775 → 5812 (+37). Build Release 0 warnings / 0 errors at every step.

**Motivated dismissals** — P3-10 and P3-12 (Chunk F): with `EmbeddedSessionManager`
in view, `LocalFileBrowserView` holds no disposable resources, its event subscribers
are view-rooted (collected on session teardown), and the `EditInEditorRequested`
handler already has explicit attach/detach (prior audit PERF-01). No leak — no code.

**Scoping decisions** — P3-17: fixed ports kept and documented as deliberate
(firewall predictability on SecNumCloud networks; auto-selecting a port would defeat
firewall rules and publish an unreachable URL). P3-25: code-doc plus a share-time UI
warning (the Settings toggle already carried `LblTftpNoAuthWarning`). P3-22:
query-string token trade-off documented in code.

**Follow-up** — AD-1 (MVVM extraction of the 1506-line `EmbeddedSftpView`
code-behind) remains a separate mini-chantier, to be scoped on its own merits.
