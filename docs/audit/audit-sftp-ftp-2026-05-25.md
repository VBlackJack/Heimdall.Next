# SFTP / FTP transfer subsystem — quality audit

- **Date**: 2026-05-25
- **Mode**: pair-architect (supervisor = Cowork, implementation = Julien)
- **Scope (validated)**: the transfer/protocol core — `Heimdall.Sftp` project
  (`SftpBrowser`, `FtpBrowser`, `RemoteFileEditor`, `PathEscaper`,
  `IRemoteBrowser`) plus the two protocol handlers (`SftpHandler`, `FtpHandler`).
  ~2 400 lines.
- **Out of scope**: the App-side UI/VM layer (`EmbeddedSftpViewModel`,
  `EmbeddedSftpView`, `LocalFileBrowser*`, `EmbeddedEditorView`),
  `EphemeralFileServer`, `FileShareService`. Candidate for a separate audit.
- **Method**: full read of every in-scope file, each finding re-verified against
  `src/` by the supervisor (file + line + trigger). Tunnel-lifecycle and
  session-bundle claims cross-checked against `SplitService`, `TunnelManager`
  and the handler git history.

## Verdict

Good overall health. **0 P1, 4 P2, ~14 P3.** The security-critical primitives
hold up: `PathEscaper` is a correct POSIX single-quote escaper that also rejects
control characters (newline-injection safe); the sudo *upload* path is
binary-safe and fully shell-escaped with `--` end-of-options; host-key pinning
and mid-session rotation detection are wired; no credential logging was found in
the perimeter.

The four P2s are concentrated in `RemoteFileEditor` — three of them in the
auto-upload / edit-session lifecycle — plus one symlink-delete hazard in
`SftpBrowser`. None is a security breach; all are correctness / data-integrity
defects that can silently damage a remote file or drop a save.

The P3s are the usual mix: FTP listing/URI correctness, time-semantics
inconsistency between the two browsers, resource cleanup on failure, and an
incomplete `IRemoteBrowser` surface.

One structural fact is noted but **not** treated as a finding (architecture-first):
`SftpBrowser` has no unit tests because it needs a live SSH server. Its pure
helpers could be tested behind a seam, but that is a deliberate refactor on its
own merits, not a test-driven fix — same call as the SSH/tunnel audit's S11.

---

## P1 — none

---

## P2 — 4 findings

### P2-1 — Sudo edit download corrupts file content (UTF-8 BOM + non-UTF-8 loss)

- **File**: `src/Heimdall.Sftp/RemoteFileEditor.cs`, `EditFileSudoAsync`, lines 165-181.
- **Trigger**: open any root-owned file with the sudo editor.
- **Defect, facet A (BOM)**: the downloaded content is written with
  `File.WriteAllTextAsync(localPath, result, System.Text.Encoding.UTF8, ct)`.
  `Encoding.UTF8` is the BOM-emitting singleton; `StreamWriter` writes the 3-byte
  preamble (`EF BB BF`) to the freshly-created temp file. The local copy gains a
  BOM the server file never had. On save, the watcher re-uploads via
  `UploadWithSudoAsync` (`cat tmp | sudo tee -- path`), so the server file is
  overwritten **with the BOM prefix**. A BOM at the head of `/etc/fstab`, a shell
  script, an `*.conf` parsed by tooling, etc. breaks them.
- **Defect, facet B (binary / non-UTF-8)**: `sudo cat` output is captured as
  `downloadCmd.Result`, a `string` decoded by SSH.NET's `SshCommand` using its
  UTF-8 encoding. Any byte sequence that is not valid UTF-8 is replaced with
  U+FFFD and is unrecoverable; the content is then written back as UTF-8 text.
  Editing a latin-1 config or a file with embedded binary via sudo silently
  destroys it on the next save. It also buffers the whole file in memory as a
  string. (The non-sudo path uses the binary-safe stream copy in
  `IRemoteBrowser.DownloadFileAsync` — only the sudo download corrupts.)
- **Fix**: transport bytes, not decoded text, on the sudo download. Run
  `sudo cat <escaped> | base64`, base64-decode locally, write the raw bytes; a
  single change kills both facets. The sudo *upload* (`UploadWithSudoAsync`) is
  already binary-safe and needs no change.

### P2-2 — Auto-upload debounce has no trailing edge — the last save can be lost

- **File**: `src/Heimdall.Sftp/RemoteFileEditor.cs` — `OnFileChangedAsync`
  lines 382-385, `EditSession.ShouldUpload` lines 682-683, `LastUploadTime`
  set at line 398.
- **Trigger**: save an edited file twice within `UploadDebounceInterval` (2 s),
  then stop.
- **Defect**: `ShouldUpload` rejects an upload when `< 2 s` have elapsed since
  the *start* of the previous upload, and nothing re-checks afterwards. The
  debounce is leading-edge only. Save A at t=0 uploads and sets
  `LastUploadTime`; save B at t=1.5 is rejected by `ShouldUpload` and is
  **never retried** — no trailing timer. Save B's content never reaches the
  server while the editor shows the file as saved. `LastUploadTime` is also set
  *before* the upload runs and is **not** rolled back on failure, so a failed
  upload likewise blocks the next 2 s.
- **Fix**: add a trailing-edge re-check — when a change is dropped by the
  debounce, arm a one-shot timer for the remaining window that re-evaluates the
  session; or compare against the last *successful* upload and re-fire. Update
  CLAUDE.md afterwards (see P3-14).

### P2-3 — `EditFileAsync` / `EditFileSudoAsync` fall through after a failed `TryAdd`

- **File**: `src/Heimdall.Sftp/RemoteFileEditor.cs` — `EditFileAsync`
  lines 108-116, `EditFileSudoAsync` lines 198-206.
- **Trigger**: two concurrent `EditFile*` calls for the same `remotePath`
  (e.g. a double-click) — `CloseEdit` and `TryAdd` are not atomic.
- **Defect**: on `TryAdd` failure the code disposes the session and deletes the
  temp file, **then still falls through** to `StartWatcher(session)` and
  `LaunchEditor(_editorPath, localPath)`. It watches a disposed session and
  builds a `FileSystemWatcher` on a directory that `CleanupTempFile` just
  removed — the watcher constructor throws on the missing directory and the
  exception escapes `EditFileAsync`. A `return` is missing after the cleanup
  block.
- **Fix**: `return` immediately after `session.Dispose()` + `CleanupTempFile`.

### P2-4 — `SftpBrowser.DeleteAsync` recursively deletes a symlink's *target*

- **File**: `src/Heimdall.Sftp/SftpBrowser.cs`, `DeleteAsync`, lines 344-353.
- **Trigger**: delete a symlink that points at a directory.
- **Defect**: `client.GetAttributes(path)` issues `SSH_FXP_STAT`, which
  **follows** symlinks. For a symlink-to-directory `attrs.IsDirectory` is `true`,
  so `DeleteDirectoryRecursive` lists and deletes the **target directory's
  contents** instead of just unlinking the symlink. Deleting a link should never
  destroy what it points at. (Nested symlinks discovered *inside* a listed
  directory are safe: `ListDirectory` carries lstat-style attributes, so a
  symlink entry has `IsDirectory == false` and is `DeleteFile`'d. Only the
  top-level `path` argument has the stat-follow hazard.)
- **Fix**: resolve the top-level entry with lstat semantics —
  `client.Get(path)` exposes `ISftpFile.IsSymbolicLink`; if it is a symlink,
  `DeleteFile(path)` (unlink) regardless of the link target type.

---

## P3 — ~14 findings

### Correctness / behaviour

- **P3-1 — FTP request URI is built without escaping the path.**
  `FtpBrowser.CreateRequest` (lines 409-428) interpolates
  `ftp://{_host}:{_port}{normalizedPath}` straight into a `Uri`. A filename
  containing `#`, `?`, `%`, spaces or non-ASCII produces a wrong or failing
  request (fragment/query interpretation, double-encoding). Escape the path
  segment, or note it is covered by the planned FluentFTP migration
  (`docs/audit/ftp-fluentftp-migration.md`).
- **P3-2 — FTP symlink LIST entries get a bogus name.** `FtpBrowser.ParseListLine`
  Unix branch (lines 505-534): group 6 `(.+)$` captures `link -> /target`
  wholesale, so a symlink entry's `Name` becomes `"link -> /target"`. Split on
  `" -> "` for `l`-type entries.
- **P3-3 — The two browsers disagree on timestamp semantics.** `SftpBrowser`
  fills `SftpFileInfo.LastModified` from `LastWriteTimeUtc`; `FtpBrowser.ParseUnixDate`
  (lines 574-590) returns a `DateTimeKind.Unspecified` value and uses
  `DateTime.Now` for its future-date correction — non-deterministic and
  potentially off by the local UTC offset. Pick one (UTC) and make the FTP
  parser deterministic.
- **P3-4 — FTP cannot delete a non-empty directory and masks the real error.**
  `FtpBrowser.DeleteAsync` (lines 307-338) tries `DeleteFile`, and on any
  `WebException` retries as `RemoveDirectory` — which fails on a non-empty
  directory (no recursive delete, unlike `SftpBrowser`) and reports the
  directory error even when the original failure was a file permission error.
- **P3-5 — `SftpBrowser.ChmodAsync` silently drops setuid/setgid/sticky bits.**
  Lines 368-398 map only the low 9 permission bits. Likely intentional; document
  it or surface it in the chmod UI.

### Resource / lifecycle

- **P3-6 — Temp directory + partial file leak when the edit download fails.**
  `EditFileAsync` (lines 96-98) and `EditFileSudoAsync` (lines 137-181) create
  the temp directory, then download. If the download throws, the method
  propagates and the session was never registered, so `CleanupTempFile` is never
  reached — the directory and any partial file leak. Wrap the download in a
  `try` that cleans up on failure.
- **P3-7 — A cancelled download leaves a truncated local file.** Both
  `SftpBrowser.DownloadFileAsync` (lines 226-246) and `FtpBrowser.DownloadFileAsync`
  (lines 203-225) open the destination with `FileMode.Create`; on cancellation
  the partially-written file stays on disk. Delete it on the cancellation path.
- **P3-8 — `SftpBrowser.ConnectAsync` client-leak and event-wiring order.**
  Lines 77-104: a second `ConnectAsync` on the same instance after a failed
  connect overwrites `_client` without disposing the previous `SftpClient`
  (latent — the handler creates a fresh browser per attempt). Separately,
  `ErrorOccurred` is subscribed *after* `Connect()` (line 104), so an error
  raised during connect is not surfaced.

### Architecture / consistency

- **P3-9 — `IRemoteBrowser` is an incomplete surface.** It exposes neither
  `SftpBrowser.SecurityEventOccurred` nor `FtpBrowser.IsTlsEnabled`; any consumer
  holding the interface must down-cast to the concrete type to reach security
  events or TLS state.
- **P3-10 — `RemoteFileEditor.UploadDebounceInterval` is `public static`
  mutable.** Line 33 — process-global mutable config, almost certainly a test
  seam. Prefer an instance field or a constructor parameter.
- **P3-11 — `SftpHandler` does not validate host/port.** `FtpHandler` validates
  with `IsValidFtpHost` + `ValidatePortRange`; `SftpHandler` relies on SSH.NET to
  reject bad input later and less localized. Align the two.
- **P3-12 — `CurrentUpload` tracks only the most recent watcher event.**
  `OnFileChanged` (lines 362-363) overwrites `session.CurrentUpload` on every
  event; a later semaphore-busy fast-return task can replace the in-flight
  upload task, so `DrainSession` (lines 547-583) may wait on a completed no-op
  instead of the real upload.

### Async / doc

- **P3-13 — Blocking SSH.NET / `FtpWebRequest` calls inside `Task.Run(…, ct)`
  do not honour `ct` mid-flight.** Cancellation only takes effect at the
  delegate boundary. Structural and consistent with the SSH layer — flag for a
  doc note, not a code change.
- **P3-14 — CLAUDE.md drift.** "`RemoteFileEditor`: `FileSystemWatcher` + 2s
  debounce auto-upload" describes a debounce that, as implemented, has no
  trailing edge (P2-2). Refresh once P2-2 lands.

---

## Verified clean (non-findings)

- **`PathEscaper.EscapeForShell`** — correct POSIX single-quote escaping
  (`'` → `'\''`), rejects all control characters (blocks newline/`\0`
  injection). Well covered by `PathEscaperTests` (252 lines).
- **Sudo *upload* path** (`UploadWithSudoAsync`) — binary-safe (SFTP upload of
  the local file to a temp path, then `sudo tee`), every shell argument goes
  through `PathEscaper`, `sudo tee --` uses end-of-options, the temp file is
  cleaned up in `finally`, host-key is pinned and rotation is detected
  (`HostKeyRotatedDuringUpload`).
- **Tunnel reference, failure paths** — already fixed for SFTP (commit
  `cb703c1`); both `catch` blocks in `SftpHandler` call `ReleaseTunnelIfNeeded`.
  FTP has no tunnel.
- **Tunnel reference, successful SFTP-via-gateway close** — released generically
  by `SplitService` via `ConnectionState` `StateData.TunnelLocalPort`
  (`SplitService` lines 491, 741, 786, 873), not by the session bundle.
  `SftpSessionBundle` deliberately carries no tunnel handle — consistent with
  the SSH path. **Not a leak.**
- **`DeleteDirectoryRecursive`** — iterative explicit-stack post-order traversal
  with a `MaxDeleteDepth = 256` cap; a hostile deep remote tree cannot blow the
  managed stack.
- **`FtpHandler.ComputeCleartextWarning`** — emits `WarnFtpCleartext` only when
  credentials would actually traverse in clear; well-tested.
- **No credential logging** — connect logs reference host + protocol only;
  `FtpBrowser`'s cleartext-transport warning logs host:port without credentials.

---

## Test coverage

- **Solid**: `PathEscaperTests` (252), `FtpBrowserParsingTests` (170 —
  `ParseListLine` + date parsing), `FtpHandlerValidationTests` (137),
  `SftpHandlerConnectTests` (160 — includes tunnel release on failure),
  `RemoteFileEditorRotationTests` (136), `RemoteFileEditorTaskTrackingTests`
  (299), `ResolveEditorPathTests` (86).
- **Gaps**: no test for FTP symlink LIST parsing (P3-2); no test for the
  debounce trailing edge (P2-2). `SftpBrowser` has no unit tests (needs a live
  SSH server) — its pure helpers (`FormatPermissions`, `ToSftpFileInfo`,
  `DeleteDirectoryRecursive` depth cap) are testable only behind a seam; per
  architecture-first that is a deliberate decision, not a test-driven refactor.

---

## Proposed remediation chunks (pair-architect)

| Chunk | Content | Findings |
|---|---|---|
| A | Sudo edit download — base64 byte transport + BOM-less write | P2-1 |
| B | `RemoteFileEditor` lifecycle — trailing-edge debounce, `TryAdd` `return`, temp cleanup on download failure, `CurrentUpload` tracking | P2-2, P2-3, P3-6, P3-12 |
| C | `SftpBrowser` — symlink-safe delete, partial-file cleanup on cancel, connect-retry leak + event-wiring order | P2-4, P3-7, P3-8 |
| D | `FtpBrowser` correctness — URI escaping, symlink LIST parsing, date semantics, directory delete | P3-1..P3-4 |
| E | Consistency & doc — chmod special bits, `IRemoteBrowser` surface, `UploadDebounceInterval` seam, `SftpHandler` validation, CLAUDE.md | P3-5, P3-9, P3-10, P3-11, P3-13, P3-14 |

Suggested order: A → B → C → D → E (value-first; A and B carry all the
data-integrity defects).
