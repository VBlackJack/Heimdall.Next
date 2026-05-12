# Gap Analysis — SSH / Terminal / SFTP vs MobaXterm + PuTTY

**Date**: 2026-04-19
**Batch**: b52 (SSH / Terminal / SFTP scope, RDP excluded)
**Next batches**: b53 = RDP audit, b54 = synthesis + priority roadmap
**Scope**: Heimdall.Next current baseline vs expectations of a seasoned MobaXterm / PuTTY user. Legacy `RDPManager` is **not** used as a benchmark; it may only be consulted as an archive if a specific historical detail is needed.

---

## 0. Executive summary

Heimdall.Next has a **solid protocol core** that is competitive with or ahead of PuTTY on several axes (typed failure classification, reference-counted tunnel reuse, multi-hop gateway chains with TOFU per hop, SmartPasteGuard, xterm.js WebGL rendering, sudo-fallback SFTP editing, auto-open SFTP on SSH connect). It is visibly behind MobaXterm on **power-user UX depth** (scrollback configurability, regex search, custom color-scheme editor, transfer queue with resume, recursive upload/sync, serial/COM, session restore, auto-reconnect) and behind PuTTY on a small but symbolic set of **low-level knobs** (cipher/MAC/kex selection, SSH key generation in-app, OpenSSH config import, character set / terminal-type overrides).

The single biggest migration blocker for a PuTTY power user is **the absence of in-app SSH key generation** (PuTTYgen equivalent), closely followed by the **lack of automatic reconnect on dropped session**. The single biggest migration blocker for a MobaXterm power user is **the absence of a transfer queue with resume** for SFTP, closely followed by **non-configurable scrollback** and **missing session restore across app restarts**.

The audit identifies **41 distinct gaps** clustered into 9 roadmap candidates. None of the gaps is architecturally blocked — Heimdall.Next's 4-layer architecture, `ToolRegistry`, and split-system already provide the hooks needed. The next step (b54) is to convert these 9 clusters into a prioritized batch sequence once b53 (RDP) adds the final input.

---

## 1. Scope and methodology

### 1.1 In scope

- **SSH**: authentication, key management, agent integration, tunnels, jump hosts, host-key verification, failure handling, connection options, session lifecycle. Excludes SFTP (covered separately) and terminal rendering (covered separately).
- **Terminal**: rendering stack, emulation, input/selection/search, appearance, macros, session recording, protocol hosts (SSH interactive, Telnet, local shell), notifications.
- **SFTP / FTP**: protocol coverage, browser UX, transfer operations, file operations, remote editing, search, sync, auth reuse, sudo elevation, settings.

### 1.2 Out of scope

- RDP (handled in b53).
- Legacy `RDPManager` features (not a baseline).
- Non-terminal tool panes (`ToolRegistry` tools) unless they directly interact with SSH/Terminal/SFTP.
- Theming architecture, i18n architecture, accessibility — covered in earlier audits (`audit-ux-*.md`).

### 1.3 Reference products

- **Primary**: MobaXterm (Professional features referenced, Home free-edition limits noted where relevant), PuTTY 0.8x + PuTTYgen + Pageant + Plink + PSCP/PSFTP.
- **Secondary**, used to cross-check specific axes only: mRemoteNG (multi-protocol session manager), Royal TS (enterprise session manager), SecureCRT (paid terminal with macros/scripting).

### 1.4 Method

1. Automated reconnaissance of `src/Heimdall.Ssh/`, `src/Heimdall.Terminal/`, `src/Heimdall.Sftp/`, their handlers in `src/Heimdall.App/Services/Handlers/`, related ViewModels/Views, and configuration surface. Raw inventories produced by three parallel exploratory passes.
2. Consolidation into a flat capability map per domain (Section 2).
3. Cross-reference against known feature lists of MobaXterm and PuTTY, using authoritative product documentation and community-known features as the reference. When a feature is listed as "MobaXterm" without further qualification, it refers to behaviours present in both the free (Home) and paid (Professional) editions unless stated otherwise.
4. Gap classification via three dimensions:
   - **Parity**: ✓ Parity / ~ Partial / ✗ Missing.
   - **Migration impact** (from a MobaXterm/PuTTY user's POV): **Blocker** (cannot migrate), **High** (daily pain, workaround possible), **Medium** (occasional friction), **Low** (nice-to-have).
   - **Implementation effort**: **S** (≤ 1 day, single file, pattern exists), **M** (≤ 1 week, 1–2 files + tests), **L** (1–3 weeks, multi-file, new design), **XL** (> 3 weeks, architectural, cross-cutting).
5. Clustering into roadmap candidates (Section 6).

### 1.5 Non-goals

This audit does **not** produce a ranked backlog with due dates — that is b54's role after RDP is audited. It also does not prescribe which clusters to adopt; priorities are Julien's call.

---

## 2. Heimdall.Next baseline inventory

### 2.1 SSH

**Authentication** (`src/Heimdall.Ssh/SshConnectionFactory.cs`). Password auth (RFC 4252) with automatic fallback to keyboard-interactive (RFC 4256) for servers with `PasswordAuthentication` disabled. Public key auth via SSH.NET `PrivateKeyAuthenticationMethod`, supporting OpenSSH and PEM (PKCS#1/#8, legacy EC/DSA) formats with passphrase. Pageant integration via native Win32 shared-memory IPC (`Pageant/PageantClient.cs`), including identity enumeration (`SSH2_AGENTC_REQUEST_IDENTITIES`), delegated signing (`SSH2_AGENTC_SIGN_REQUEST`), RSA SHA-2-256/512 negotiation, and agent-process spoof protection (trusted-process whitelist: pageant/putty/plink/pscp/psftp/kitty/winscp/keepassxc-proxy). Automatic fallback to `plink.exe` when SSH.NET cannot handle agent-only flows (`PlinkTunnelRunner.cs`).

**Key management** (`src/Heimdall.Ssh/SshKeyAuditEngine.cs`). Parse-and-audit only — identifies algorithm (RSA, ECDSA, Ed25519, DSA), size, encryption status (bcrypt KDF for OpenSSH, DES for legacy PEM), and emits security ratings (Strong/Acceptable/Weak/Deprecated). Exposed as a Tools-tab utility. **No in-app key generation**.

**Host key verification** (`src/Heimdall.Ssh/HostKeyStore.cs`). Trust-on-first-use with per-`host:port` SHA-256 fingerprint storage, persistent via `ConfigManager`. Fingerprints rendered in the OpenSSH-standard `SHA256:<base64-no-padding>` format. `HostKeyEvent` raised on every verification for audit logging.

**Tunnelling** (`src/Heimdall.Ssh/TunnelManager.cs`). Local forward (`ForwardedPortLocal`), remote/reverse (`ForwardedPortRemote`), dynamic SOCKS5 (`ForwardedPortDynamic`). Reference-counted sharing — multiple sessions can reuse the same live tunnel, teardown happens only when the last consumer disconnects. Plink fallback for Pageant-only scenarios.

**Gateway chains** (`src/Heimdall.Ssh/GatewayChainResolver.cs`). Multi-hop ProxyJump via `ParentGatewayId` links; detects cycles, enforces a configurable max depth (default 5), applies TOFU per hop, DPAPI-decrypts per-gateway credentials at resolution time.

**Failure taxonomy** (`src/Heimdall.Ssh/SshFailureCode.cs` + `FailureClassifier.cs`). 26 structured codes covering auth (5 variants), network (5), protocol (3), tunnel (2), chain resolution (2), interactive prompts (2), plus `Cancelled` and `Unknown`. Each code maps to an `ErrorSsh{Code}` i18n key. Exception-type and message introspection pipeline for mapping.

**Preflight** (`src/Heimdall.Ssh/AuthPreflightChecker.cs`). Validates key file existence and Pageant availability **before** opening a TCP connection, preventing cascading errors.

**Session lifecycle** (`src/Heimdall.Ssh/SshShellSession.cs`, `src/Heimdall.App/Services/Handlers/SshHandler.cs`). PTY-allocated interactive shell (default 80×24), stdout read loop, `DataReceived` / `Disconnected` events. Terminal resize via reflection into SSH.NET internal `IChannelSession` (brittle but working). Post-connect command support with configurable inter-line delay.

**Connection options** (`src/Heimdall.Ssh/SshConnectionParams.cs`, `src/Heimdall.Core/Configuration/ServerProfileDto.cs`). Per-server: username, port, compression flag, agent-forwarding flag, X11-forwarding flag, keep-alive interval, connect timeout, key path, DPAPI-encrypted password, SOCKS proxy port, remote bind/local ports. **No cipher/MAC/KEX selection.**

**Monitoring** (`src/Heimdall.Ssh/ServerHealthMonitor.cs`). Multiplexes channels on an existing `SshClient` to poll CPU (`top`), RAM (`free`), disk (`df`). Regex-based metric extraction. Event-driven.

**Tools** (`Heimdall.App`). SSH Key Auditor, OpenSSH Config **Generator** (export only — no import). Plink availability check via `ConnectionHelpers.ResolvePuttyPath()`.

**Visibly absent**. In-app SSH key generation; cipher/MAC/KEX selection; GSSAPI/Kerberos auth; certificate-based auth (user/host certs); OpenSSH config **import**; `ControlMaster` equivalent; session resumption or auto-reconnect on unexpected disconnect; shell-escaping helpers for user-entered commands; exposed exec-mode UI.

### 2.2 Terminal

**Rendering stack** (`src/Heimdall.App/Assets/terminal.html`, `Assets/Terminal/xterm.min.js` v5.5.0). xterm.js 5.5.0 hosted in WebView2, Fit and WebGL addons active, canvas fallback. ANSI/VT passthrough. OSC 8 hyperlink click-to-open. OSC 52 **not** supported (clipboard goes through WebView2 `postMessage` bridge instead).

**Byte pipes** (`src/Heimdall.Terminal/`). Three hosting modes:
- **ConPTY** (`ConPty/ConPtySession.cs`) — Windows 10/2019+ pseudo-console, with API-availability probe. Used for local shell only (VT double-conversion breaks arrow keys over SSH, per CLAUDE.md gotcha).
- **Pipe mode** (`PipeModeSession.cs`) — raw stdin/stdout/stderr redirect to the child process, used for SSH (`plink -t`) and any scenario needing binary passthrough.
- **Telnet** (`TelnetSession.cs`) — raw TCP with minimal IAC negotiation (WILL/WONT/DO/DONT/SB/SE, NAWS for window size), 15 s connect timeout, no ANSI pre-processing.

**Hosted protocols**. SSH interactive (`SshHandler.cs`), local shell (`LocalShellHandler.cs` — cmd, PowerShell with configurable execution policy, bash/WSL, with elevation via gsudo/runas and mode enum Auto/Gsudo/Runas), Telnet (`TelnetHandler.cs`). **No serial/COM port**. **No Mosh / eternal terminal**.

**Input safety** (`src/Heimdall.Terminal/SmartPasteGuard.cs`). Classifies clipboard content into Safe / Multiline / Dangerous, with 16 hard-coded destructive-pattern regexes (`rm -rf`, `mkfs`, `dd if=`, `shutdown`, `reboot`, fork-bomb, etc.). In production mode, multi-line pastes escalate to Dangerous and require confirmation.

**Keyboard & clipboard** (`terminal.html`). Ctrl+Shift+C (copy), Ctrl+V / Ctrl+Shift+V (paste), right-click paste, Ctrl+Shift+F (search), Ctrl++ / Ctrl+- / Ctrl+0 (font zoom). Drag-selection auto-copies to system clipboard (`copyOnSelection` semantics). Bidirectional clipboard via WebView2 `postMessage`.

**Appearance** (`src/Heimdall.App/Configuration/AppSettings.cs`). Configurable font family (sanitized to alphanumeric/space/hyphen only), font size 8–28 (default 14), and color scheme chosen from 5 hard-coded presets (Dracula, Solarized Dark, Monokai, Nord, Default). Block cursor with blink, hard-coded. Scrollback **hard-coded at 10 000 lines**. No cursor-shape selector, no transparency, no background image, no custom color-scheme editor.

**Search** (`terminal.html` lines 204–243). In-buffer, literal substring match, case-insensitive, forward and backward navigation, visual feedback on no-match. **No regex search, no highlight-all-occurrences**.

**Macros and broadcast** (`src/Heimdall.App/Services/MacroService.cs`, `EmbeddedSshView.xaml.cs`). JSON-persisted macros keyed by name; per-session keystroke recording with per-entry delay for playback. Broadcast-input flag relays keys across open terminals; toggled per session with a badge UI.

**Session recording** (`EmbeddedSshView.xaml.cs` lines 1000–1047). Transcript to file with ANSI stripping via regex; concurrent queue + lock for thread safety; output directory configurable (`AppSettings.SessionLogDirectory`). **No video/animation recording, no session replay.**

**Anti-idle** (`AppSettings.AntiIdleIntervalSeconds` default 60 s, `SshTmoutResetIntervalSeconds` default 240 s). Periodic CR keepalive to prevent idle disconnects.

**Visibly absent**. Configurable scrollback; regex search; rectangular/column selection; custom color-scheme editor; cursor-shape selector; transparency; background image; bell (audio or visual); session restore across app restarts; OSC 52; bracketed-paste mode explicit negotiation; drag-drop file paste (clipboard paste only); serial/COM; Mosh; middle-click paste.

### 2.3 SFTP / FTP

**Protocols**. SFTP via SSH.NET `SftpClient` (`src/Heimdall.Sftp/SftpBrowser.cs`, full POSIX: permissions, owner/group IDs, symlink detection). FTP via .NET `FtpWebRequest` (`FtpBrowser.cs`), passive/active configurable, explicit FTPS (`EnableSsl`) — **no implicit FTPS, no SCP, no WebDAV, no S3**.

**Browser UX** (`src/Heimdall.App/Views/Sessions/EmbeddedSftpView.xaml.cs`, `LocalFileBrowserView.xaml.cs`, `EmbeddedSftpViewModel.cs`). Dual-pane (local + remote). Column sorting (name / size / modified / permissions / owner, directories first). Real-time case-insensitive filter. Hidden-files toggle. Item count + selection summary with aggregate size. Empty-directory overlay. **No file preview, no thumbnails.**

**Transfer** (`EmbeddedSftpView.xaml.cs` lines 984–1086). Sequential, single-file-at-a-time. Drag-drop upload from Windows Explorer (folders rejected). Upload/download via file/save-file dialogs. Real-time progress bar with filename, percentage, direction arrow, cancellation button (`_transferCts` token). **No speed/bandwidth indicator, no queue, no parallel transfers, no resume, no checksum verification, no recursive-upload for folders.**

**File operations**. Create directory, delete (recursive walk for SFTP; best-effort DeleteFile/RemoveDirectory for FTP), rename (SFTP `RenameFile`, FTP `Rename` request), chmod (SFTP only — FTP `ChmodAsync` is a no-op per RFC-compliance note), view properties (name, size, modified, rwxrwxrwx, owner/group IDs). F2 rename shortcut, Delete key for remove. **No chown, no symlink creation, no setfacl.**

**Remote editing** (`src/Heimdall.Sftp/RemoteFileEditor.cs`). Download to temp → launch external editor (default `notepad.exe`, configurable via `AppSettings.ExternalEditorPath`) → `FileSystemWatcher` with debounce (default 2 s, configurable) → auto-upload. Per-file serialized uploads via `SemaphoreSlim`. Re-edit of an already-open file closes the previous watcher. Sudo elevation — **download via `sudo cat` over SSH**, **upload via temp SFTP + `sudo tee` pipe**, cleanup of the pivot file after `tee`. SSH-only (requires an active `SshConnectionParams`).

**Navigation**. Editable path bar with explicit navigation. Back (via in-memory `_navigationHistory` stack), Up, Home buttons. Bookmarks in-memory only — **not persisted across sessions**.

**Search**. None beyond the in-pane filter. **No recursive find, no content search.**

**Auth reuse**. SFTP handler reuses `ServerProfileDto` SSH credentials (username, DPAPI-encrypted password, key path, agent-forwarding, compression). FTP handler uses separate `FtpUsername`/`FtpPasswordEncrypted` fields; anonymous fallback if both empty. TOFU host-key store shared with SSH (`HostKeyStore` reference threaded through `SftpBrowser.ConnectAsync`).

**Integration**. `AppSettings.SftpAutoOpenOnSsh` (default `true`) opens the SFTP browser in a sibling pane when an SSH session is connected. `AppSettings.SftpBrowserEnabled` gates the feature entirely.

**Visibly absent**. Transfer queue; parallel transfers; resume; checksum verification; bandwidth throttling; folder / recursive upload; one-way or two-way sync; directory compare/diff; archive / extract on transfer; in-app text editor; persisted bookmarks; SSH URI drag-drop; remote search; file preview; thumbnails; SCP; WebDAV; S3; FTPS implicit.

---

## 3. Reference products

### 3.1 MobaXterm (Professional features referenced)

MobaXterm is the dominant Windows SSH / X11 / SFTP workstation for power users. Its feature surface that's relevant here:

- **Session manager**: tabs, session groups, folder tree, drag-drop re-organisation, session templates, import/export of session trees.
- **SSH**: password, pubkey, keyboard-interactive, **in-app key generator**, Pageant-compatible agent (MobAgent), jump hosts chain, SOCKS proxy, pre/post commands, **auto-reconnect on drop**, keep-alive, X11 forwarding **with bundled X-server**.
- **Terminal**: configurable scrollback (lines or infinite), in-pane find **with regex**, multi-execution (send to all), macro recording and playback, **session restore across app restarts**, **cursor shape selector**, **custom color-scheme editor**, bell (audio + visual), transparency, background image, Cygwin / WSL bundled shell, character-set override, **MobaTextEditor** (built-in editor for remote files, no external editor needed), **session recording with replay** (per-session log file with timestamped VT stream).
- **SFTP**: opens automatically on SSH, drag-drop upload from and to both panes, **transfer queue with per-item progress**, parallel transfers, **transfer resume**, recursive upload/download, **bookmarks persisted** in session tree, **MobaDiff** for file compare.
- **Other protocols**: RDP, VNC, FTP(S), Telnet, Rlogin, Xdmcp, serial (COM, telnet-to-serial, cisco-break), Mosh, AWS S3, browser tab.
- **Extras**: built-in `sudo` helper, port scanner, file compare, built-in text editor, **cron-style scheduled scripts**, session password generator, multi-tabbed x11 apps, Cygwin-compatible /usr/bin toolchain (Home edition limits the toolchain; Professional uncaps it).

Free (Home) edition limitations relevant here: max 12 sessions, max 2 SSH tunnels, max 4 macros, 1 day of personal-use licence. Heimdall.Next does not need to match these limits — its differentiator is the absence of arbitrary caps.

### 3.2 PuTTY + sidecars

PuTTY is the reference raw-terminal and SSH client. Sidecar binaries: PuTTYgen (key generation), Pageant (SSH agent), Plink (CLI SSH), PSCP/PSFTP (file transfer).

PuTTY features relevant here:

- **Protocols**: Raw, Telnet, Rlogin, SSH (v2 only in modern versions), **Serial**, **ADB** (recent).
- **SSH**: password, pubkey (`.ppk` format), keyboard-interactive, GSSAPI/Kerberos, **cipher/MAC/KEX ordering configurable**, **compression-level selector**, **SSH protocol-version selector**, TCP-forward configurable per session (local / remote / dynamic), X11 forwarding.
- **Terminal**: configurable scrollback (lines), **character-set / translation override** (UTF-8, ISO-8859-*, CP437, etc.), **terminal-type override** (`$TERM`), bell (none / beep / visual / sound file / taskbar), **font configurable** (face + size + bold/italic), **cursor shape** (block/underline/vertical, blinking toggle), right-click paste, copy-on-select, **keyboard mappings** (Home/End/Backspace behaviours).
- **PuTTYgen**: **key generation** (RSA, DSA, ECDSA, Ed25519), import/export OpenSSH ↔ SSH.com ↔ `.ppk`, passphrase change, comment edit, fingerprint display.
- **Pageant**: key loading, SSH agent IPC (the protocol Heimdall.Next already speaks).
- **PSFTP / PSCP**: CLI file transfer with glob support, `reget`/`reput` resume.

Heimdall.Next's SSH core (SSH.NET + Pageant protocol + TOFU + typed failures + multi-hop chains) is already **superior** to PuTTY on breadth of error handling and jump-host ergonomics, but lags on: cipher/MAC/KEX configurability, key generation, terminal character-set override, bell, and session save-load round-trip parity.

### 3.3 Secondary references

- **mRemoteNG**: multi-protocol tabbed session manager (RDP, VNC, SSH, Telnet, HTTP/S, rlogin, raw). Drives the "one app, many protocols" expectation and session-tree UI. Heimdall.Next already has session-tree parity conceptually.
- **Royal TS**: enterprise session manager with credential vault, team sharing, connection sequences. Sets the bar for credential management (out of scope here).
- **SecureCRT**: scriptable terminal with Python/JS macros, extensive session-options panel. Sets the bar for macros beyond simple keystroke-replay.

---

## 4. Gap matrix

Parity: ✓ = at parity, ~ = partial, ✗ = missing.
Impact: **Bl** = Blocker for migration, **H** = High, **M** = Medium, **L** = Low.
Effort: **S** / **M** / **L** / **XL** per the rubric in §1.4.

### 4.1 SSH gaps

| # | Capability | Heimdall.Next | MobaXterm | PuTTY | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|
| S1 | Password auth | ✓ | ✓ | ✓ | ✓ | — | — |
| S2 | Public key auth (OpenSSH / PEM) | ✓ | ✓ | ✓ (`.ppk`) | ✓ | — | — |
| S3 | Keyboard-interactive / MFA prompts | ✓ auto-filled with password | ✓ | ✓ | ~ (no dedicated MFA UI) | M | M |
| S4 | Pageant integration | ✓ (native IPC) | ✓ (MobAgent) | ✓ | ✓ | — | — |
| S5 | OpenSSH ssh-agent (Unix socket) | ✗ | ✓ | ✗ | ~ | L (Windows 10+ OpenSSH) | M |
| S6 | In-app key generation (RSA/ECDSA/Ed25519) | ✗ — audit only | ✓ | ✓ (PuTTYgen) | ✗ | **Bl** for PuTTY users | M |
| S7 | Key format conversion (`.ppk` ↔ OpenSSH) | ✗ | ✓ | ✓ (PuTTYgen) | ✗ | H | M |
| S8 | GSSAPI / Kerberos auth | ✗ | ✓ | ✓ | ✗ | M (enterprise) | L |
| S9 | Certificate-based auth (user/host certs) | ✗ | ✓ | ✓ (recent) | ✗ | M | L |
| S10 | Multi-hop ProxyJump (chain) | ✓ with TOFU per hop | ✓ | ~ (single `ProxyCommand`) | ✓ **ahead** | — | — |
| S11 | Local port forward | ✓ | ✓ | ✓ | ✓ | — | — |
| S12 | Remote (reverse) port forward | ✓ | ✓ | ✓ | ✓ | — | — |
| S13 | Dynamic SOCKS5 forward | ✓ | ✓ | ✓ | ✓ | — | — |
| S14 | Tunnel reference counting / reuse | ✓ | ~ (per-session) | ✗ | ✓ **ahead** | — | — |
| S15 | TOFU host-key verification | ✓ | ~ (prompt only) | ~ (prompt only) | ✓ **ahead** | — | — |
| S16 | Host-key management UI (view / remove / pin) | ✗ (in-code only) | ~ | ~ | ✗ | M | S |
| S17 | Cipher / MAC / KEX selection | ✗ (SSH.NET defaults) | ✓ | ✓ (ordered) | ✗ | M (compliance / hardened env) | M |
| S18 | Compression toggle | ✓ | ✓ | ✓ | ✓ | — | — |
| S19 | Compression level selector | ✗ | ~ | ✓ | ✗ | L | S |
| S20 | Connect timeout | ✓ | ✓ | ✓ | ✓ | — | — |
| S21 | Keep-alive configurable | ✓ | ✓ | ✓ | ✓ | — | — |
| S22 | Post-connect command(s) | ✓ | ✓ | ✗ | ✓ | — | — |
| S23 | Auto-reconnect on unexpected drop | ✗ | ✓ | ✗ | ✗ | **Bl** for MobaXterm users | M |
| S24 | Session resumption across app restart | ✗ | ✓ | ✗ | ✗ | H | M |
| S25 | X11 forwarding (channel handling) | ~ (flag sent, X-server detected, channel handling unclear) | ✓ (bundled X-server) | ~ (requires external X-server) | ~ | H (Linux-centric users) | L |
| S26 | OpenSSH config import (`~/.ssh/config`) | ✗ (export only) | ✓ | ✗ | ✗ | H | M |
| S27 | OpenSSH config export | ✓ | ~ | ✗ | ✓ **ahead** | — | — |
| S28 | Typed failure classification (26 codes) | ✓ | ✗ | ✗ | ✓ **ahead** | — | — |
| S29 | Preflight check (key exists, agent up) | ✓ | ✗ | ✗ | ✓ **ahead** | — | — |
| S30 | Terminal resize (`SSH_MSG_CHANNEL_REQUEST window-change`) | ✓ via SSH.NET reflection | ✓ | ✓ | ✓ (brittle) | — | — |
| S31 | Exec-mode UI (one-off commands) | ✗ (internal only) | ✓ | ~ (Plink) | ✗ | L | M |

### 4.2 Terminal gaps

| # | Capability | Heimdall.Next | MobaXterm | PuTTY | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|
| T1 | xterm-compatible rendering | ✓ (xterm.js 5.5.0 + WebGL) | ✓ | ✓ (native) | ✓ | — | — |
| T2 | Color-scheme presets | 5 hard-coded | ~20+ plus editor | ~10 plus editor | ~ | M | S |
| T3 | Custom color-scheme editor | ✗ | ✓ | ✓ | ✗ | H | M |
| T4 | Configurable scrollback size | ✗ (hard-coded 10 000) | ✓ (incl. infinite) | ✓ | ✗ | H | S |
| T5 | Scrollback search (literal) | ✓ | ✓ | ✓ | ✓ | — | — |
| T6 | Scrollback search (regex) | ✗ | ✓ | ~ | ✗ | M | S |
| T7 | Highlight-all matches in search | ✗ | ✓ | ~ | ✗ | L | S |
| T8 | Scrollback save to file | ~ (transcript only) | ✓ (explicit dump) | ✓ | ~ | M | S |
| T9 | Session recording (transcript) | ✓ (ANSI-stripped) | ✓ | ✓ | ✓ | — | — |
| T10 | Session replay (VT-faithful) | ✗ | ✓ | ✗ | ✗ | L | M |
| T11 | Font family + size | ✓ | ✓ | ✓ | ✓ | — | — |
| T12 | Font bold / italic / weight | ✗ | ✓ | ✓ | ✗ | L | S |
| T13 | Cursor shape selector (block / bar / underline) | ✗ | ✓ | ✓ | ✗ | M | S |
| T14 | Cursor blink toggle | ✗ (always blink) | ✓ | ✓ | ✗ | L | S |
| T15 | Terminal transparency | ✗ | ✓ | ✗ | ✗ | L | M |
| T16 | Background image | ✗ | ✓ | ✗ | ✗ | L | M |
| T17 | Bell (audio / visual / taskbar) | ✗ | ✓ | ✓ | ✗ | M | S |
| T18 | Activity / silence monitor | ✗ | ✓ | ✗ | ✗ | M | M |
| T19 | SmartPasteGuard (destructive-pattern check) | ✓ | ✗ | ✗ | ✓ **ahead** | — | — |
| T20 | Multi-line paste confirmation | ✓ | ~ | ✗ | ✓ | — | — |
| T21 | Bracketed paste negotiation | ✗ | ✓ | ✓ | ✗ | M | S |
| T22 | Right-click paste | ✓ | ✓ | ✓ | ✓ | — | — |
| T23 | Middle-click paste (Unix-style) | ✗ | ✓ | ✓ | ✗ | L | S |
| T24 | Drag-drop file (auto-paste path or upload) | ✗ | ✓ | ✗ | ✗ | M | M |
| T25 | Rectangular / column selection | ✗ | ✓ | ✓ | ✗ | L | M |
| T26 | OSC 8 hyperlinks (click-to-open) | ✓ | ✓ | ✗ | ✓ | — | — |
| T27 | OSC 52 clipboard | ✗ | ~ | ✗ | ✗ | L | S |
| T28 | Broadcast input (send to all) | ✓ | ✓ | ✗ | ✓ | — | — |
| T29 | Macro recording + playback | ✓ | ✓ | ✗ | ✓ | — | — |
| T30 | Scripted macros (Python / JS / Lua) | ✗ | ~ (shell scripts) | ✗ | ✗ | L | XL |
| T31 | Session restore across app restart | ✗ | ✓ | ~ (saved sessions reopen manually) | ✗ | **Bl** for MobaXterm users | M |
| T32 | Serial / COM port | ✗ | ✓ | ✓ | ✗ | H (hardware users) | L |
| T33 | Mosh / eternal terminal | ✗ | ✓ | ✗ | ✗ | M | XL |
| T34 | Telnet interactive | ✓ | ✓ | ✓ | ✓ | — | — |
| T35 | Local shell (cmd / pwsh / bash / WSL) | ✓ (with policy override + elevation) | ✓ | ✗ | ✓ | — | — |
| T36 | Character-set / encoding override | ✗ (UTF-8 implicit) | ✓ | ✓ | ✗ | M | S |
| T37 | `$TERM` override | ✗ (implicit) | ✓ | ✓ | ✗ | L | S |
| T38 | Anti-idle keep-alive (CR injection) | ✓ | ✓ | ~ | ✓ | — | — |

### 4.3 SFTP / FTP gaps

| # | Capability | Heimdall.Next | MobaXterm | PuTTY (PSFTP) | Parity | Impact | Effort |
|---|---|---|---|---|---|---|---|
| F1 | SFTP protocol | ✓ (SSH.NET) | ✓ | ✓ | ✓ | — | — |
| F2 | FTP protocol | ✓ (passive/active, explicit TLS) | ✓ | ✗ | ✓ | — | — |
| F3 | FTPS implicit | ✗ | ✓ | ✗ | ✗ | L | S |
| F4 | SCP | ✗ (SFTP only) | ✓ | ✓ (PSCP) | ✗ | L | M |
| F5 | WebDAV | ✗ | ✓ | ✗ | ✗ | L | L |
| F6 | S3 | ✗ | ✓ | ✗ | ✗ | L | L |
| F7 | Dual-pane browser | ✓ | ✓ | ✗ | ✓ | — | — |
| F8 | Drag-drop upload (local → remote) | ✓ (files only, folders rejected) | ✓ | ✗ | ~ | H | M |
| F9 | Drag-drop download (remote → local) | ~ (via explicit button) | ✓ (drag from remote pane to Explorer) | ✗ | ~ | H | M |
| F10 | Recursive upload (folders) | ✗ | ✓ | ✓ | ✗ | **Bl** for MobaXterm users | M |
| F11 | Recursive download (folders) | ~ (via explicit batch) | ✓ | ✓ | ~ | H | M |
| F12 | Transfer queue with per-item status | ✗ (single sequential) | ✓ | ✗ | ✗ | **Bl** for MobaXterm users | L |
| F13 | Parallel transfers | ✗ | ✓ | ✗ | ✗ | H | M |
| F14 | Transfer resume (partial / reget / reput) | ✗ | ✓ | ✓ | ✗ | H | M |
| F15 | Checksum verification | ✗ | ✓ | ✗ | ✗ | M | S |
| F16 | Bandwidth throttling | ✗ | ✓ | ✗ | ✗ | L | M |
| F17 | Speed / bytes-per-second indicator | ✗ | ✓ | ~ | ✗ | M | S |
| F18 | Cancel in flight | ✓ | ✓ | ✗ | ✓ | — | — |
| F19 | Create / delete / rename | ✓ | ✓ | ✓ | ✓ | — | — |
| F20 | chmod (SFTP) | ✓ | ✓ | ✓ | ✓ | — | — |
| F21 | chmod (FTP SITE CHMOD fallback) | ✗ | ✓ | ✗ | ✗ | L | S |
| F22 | chown | ✗ | ✓ | ✗ | ✗ | L | S |
| F23 | Symlink creation | ✗ | ✓ | ~ | ✗ | L | S |
| F24 | File preview / thumbnails | ✗ | ~ | ✗ | ✗ | L | M |
| F25 | Hidden-files toggle | ✓ | ✓ | ✗ | ✓ | — | — |
| F26 | Column sort + filter | ✓ | ✓ | ✗ | ✓ | — | — |
| F27 | Recursive find in directory | ✗ | ✓ | ✗ | ✗ | H | M |
| F28 | Content search in files | ✗ | ✓ | ✗ | ✗ | M | L |
| F29 | Remote edit with auto-upload | ✓ (external editor + sudo pivot) | ✓ (MobaTextEditor built-in, plus external) | ✗ | ✓ | — | — |
| F30 | In-app text editor (no external) | ✗ | ✓ | ✗ | ✗ | M | XL |
| F31 | Sudo elevation (view + mutate) | ✓ (sudo cat / tee / ls) | ✓ | ✗ | ✓ **ahead** | — | — |
| F32 | Auto-open SFTP on SSH | ✓ | ✓ | ✗ | ✓ | — | — |
| F33 | Bookmarks persisted | ✗ (in-memory only) | ✓ (stored in session tree) | ✗ | ✗ | H | S |
| F34 | Sync (one-way local↔remote) | ✗ | ✓ | ✗ | ✗ | H | L |
| F35 | Sync (two-way) | ✗ | ✓ | ✗ | ✗ | M | L |
| F36 | Directory compare | ✗ | ✓ (MobaDiff) | ✗ | ✗ | M | L |
| F37 | Archive / extract on transfer | ✗ | ~ | ✗ | ✗ | L | L |
| F38 | URI drag-drop (`ssh://`, `ftp://`) | ✗ | ~ | ✗ | ✗ | L | S |

---

## 5. Top critical gaps

The following gaps are the most acute for a user migrating from MobaXterm or PuTTY. Ranking combines migration impact and effort — items listed here are **Blocker** or **High** impact and feasible within a reasonable batch budget (S or M effort where possible; L is flagged).

### 5.1 SSH — Top 10

1. **S6 — In-app SSH key generation** (Blocker for PuTTY users, effort M). RSA / ECDSA / Ed25519 with passphrase, export OpenSSH + `.ppk`, fingerprint display. The existing `SshKeyAuditEngine` already parses all target formats; generation is a symmetric addition.
2. **S23 — Auto-reconnect on drop** (Blocker for MobaXterm users, effort M). Bounded retry with back-off, user-visible state (Connecting → Retrying → Disconnected), cancellable. `SshFailureCode` already enables the "is this recoverable?" decision.
3. **S7 — Key format conversion** (High, effort M). `.ppk` ↔ OpenSSH, passphrase change, comment edit. Natural pairing with S6 since they share the same UI surface.
4. **S26 — OpenSSH config import** (High, effort M). Parse `~/.ssh/config`, propose to bulk-import as server profiles or gateways. Complements the existing exporter (S27) for round-trip parity.
5. **S24 — Session resumption across app restart** (High, effort M). Remember open sessions, their panes, their state machine positions; offer "Restore previous session?" on launch. Requires a session-state JSON + handler-level resume hooks.
6. **S25 — X11 forwarding end-to-end** (High for Linux-centric users, effort L). Today the flag is sent, X-server is detected, but channel handling is unclear. Needs a complete X11 channel plumbing or an embedded X-server (VcXsrv bundle?).
7. **S16 — Host-key management UI** (Medium, effort S). List stored fingerprints, edit/remove, mark as "pinned". `HostKeyStore` already exposes `GetAllTrusted()`.
8. **S3 — Dedicated MFA prompt UI** (Medium, effort M). Today `kb-interactive` auto-fills with the stored password; real MFA (OTP, push) requires a modal prompt per challenge.
9. **S17 — Cipher / MAC / KEX selection** (Medium, effort M). Per-server override with preset profiles (default / hardened / legacy-compat).
10. **S31 — Exec-mode UI** (Low but quick win, effort M). One-off command execution on an existing SSH client with captured output — `ServerHealthMonitor` already does this internally.

### 5.2 Terminal — Top 10

1. **T31 — Session restore across app restart** (Blocker for MobaXterm users, effort M). Same underlying work as S24 but surfaced at the terminal/pane level.
2. **T4 — Configurable scrollback** (High, effort S). Replace the hard-coded `10000` in `terminal.html` with a binding to `AppSettings.TerminalScrollback` (with an "unlimited" sentinel value that maps to a sane max like 1,000,000).
3. **T3 — Custom color-scheme editor** (High, effort M). JSON-backed scheme, editor view under Settings, hot-reload into xterm.js.
4. **T32 — Serial / COM port** (High for hardware users, effort L). A new `SerialSession : ITerminalSession`, a new handler, a port-picker dialog, no SSH dependency.
5. **T6 — Regex search in scrollback** (Medium, effort S). Swap the literal substring match for a JavaScript `RegExp` constructor with a toggle in the search bar.
6. **T13 — Cursor shape selector** (Medium, effort S). `AppSettings.TerminalCursorStyle` → `xterm.js` option.
7. **T17 — Bell (audio + visual)** (Medium, effort S). OSC `\x07` handler → `AppSettings.TerminalBellMode` (None / Beep / Visual / Taskbar).
8. **T18 — Activity / silence monitor** (Medium, effort M). Per-tab badges that light up when output is received while tab is inactive, or when output stops unexpectedly.
9. **T21 — Bracketed paste negotiation** (Medium, effort S). Emit `\x1b[?2004h` on PTY open, strip the magic on paste.
10. **T36 — Character-set / encoding override** (Medium, effort S). Per-session setting for non-UTF-8 legacy systems.

### 5.3 SFTP — Top 10

1. **F12 — Transfer queue with per-item status** (Blocker for MobaXterm users, effort L). Queue view, per-item progress, pause/resume/cancel, persist across disconnections. Foundational for F10/F11/F13/F14.
2. **F10 — Recursive upload (folders)** (Blocker for MobaXterm users, effort M). Walk local tree, create remote dirs, upload files. Natural fit on top of F12.
3. **F14 — Transfer resume (reget / reput)** (High, effort M). `SftpClient.AppendAllText` / partial-range semantics already available in SSH.NET.
4. **F8/F9 — Drag-drop both ways with folders** (High, effort M). Extend the current drag handler to accept folders; implement a reverse `DragQueryFile` emitter for remote → Explorer.
5. **F33 — Persisted bookmarks** (High, effort S). Add a `SftpBookmarks` list to `AppSettings` or per-server profile.
6. **F27 — Recursive find in remote directory** (High, effort M). SSH exec `find` with user filter, display results with click-to-navigate.
7. **F34 — One-way sync (local → remote)** (High, effort L). Checksum or mtime-based, dry-run preview, conflict handling.
8. **F13 — Parallel transfers** (High, effort M). Configurable concurrency limit, shared progress widget. Depends on F12.
9. **F17 — Speed indicator** (Medium, effort S). EWMA bytes-per-second computation in the existing progress callback.
10. **F36 — Directory compare** (Medium, effort L). Side-by-side tree diff with color-coding for mismatches.

---

## 6. Proposed roadmap clusters

Clusters are candidate groupings. Sizing is indicative; actual batch sizing should match our usual 1–2 files + tests cadence. The `>>` marker indicates a natural dependency / prerequisite.

### Cluster A — Key management (PuTTYgen equivalent)
**Gaps**: S6, S7, S16. **Effort**: M + M + S. **User value**: unblocks PuTTY migration, rounds out the existing audit tool. **Batches**: ~2.

### Cluster B — Resilience (reconnect + restore)
**Gaps**: S23, S24, T31. **Effort**: M + M + M (shared plumbing). **User value**: unblocks MobaXterm migration on the "daily reliability" axis. **Batches**: ~2–3.

### Cluster C — Terminal configurability
**Gaps**: T3, T4, T6, T13, T14, T17, T21, T36, T37, T12. **Effort**: mostly S, one M (T3). **User value**: removes nine "PuTTY users miss this" papercuts at once. **Batches**: ~2–3.

### Cluster D — SFTP transfer engine
**Gaps**: F12, F10, F11, F13, F14, F17. **Effort**: L (F12 is the anchor, others bolt on). **User value**: blocks-to-unblocks the MobaXterm SFTP expectation. **Batches**: ~3–4.

### Cluster E — SFTP browser UX
**Gaps**: F8, F9, F27, F33, F24. **Effort**: S/M/M/S/M. **User value**: completes the browser ergonomics. **Batches**: ~2.

### Cluster F — SFTP sync & diff
**Gaps**: F34, F35, F36. **Effort**: L + L + L. **User value**: advanced, niche but heavily used by infra/devops. **Batches**: ~3.
**Dependency**: >> Cluster D (transfer queue is the foundation).

### Cluster G — OpenSSH config round-trip
**Gaps**: S26 (complement S27 already in place). **Effort**: M. **User value**: lowers the migration barrier for users who already manage configs via `~/.ssh/config`. **Batches**: ~1.

### Cluster H — X11 end-to-end
**Gaps**: S25. **Effort**: L. **User value**: Linux-centric users expect it; today it's a half-step. **Batches**: ~2.

### Cluster I — Serial / COM
**Gaps**: T32. **Effort**: L. **User value**: hardware users (embedded, network gear consoles) — a tier of users PuTTY explicitly serves. **Batches**: ~1–2.

Clusters **not** proposed (discarded as low-priority or out-of-scope for a migration-readiness roadmap): T15 transparency, T16 background image, T30 scripted macros (XL, niche), F30 in-app text editor (XL, external editor is already wired).

---

## 7. Strengths to preserve

Several Heimdall.Next behaviours are genuinely ahead of both MobaXterm and PuTTY and must survive any refactor:

- **Typed failure taxonomy** (`SshFailureCode` × 26). Neither MobaXterm nor PuTTY expose a structured error surface; they raise string errors that callers must pattern-match. Heimdall's model is the basis for auto-reconnect (S23) and richer preflight UX.
- **Multi-hop gateway chains with TOFU per hop**. MobaXterm's jump-host is single-level in practice; PuTTY's `ProxyCommand` requires manual config. Heimdall's typed chain resolver + cycle detection is superior.
- **Reference-counted tunnel reuse**. Multiple sessions can share the same live tunnel, teardown is automatic when the last consumer disconnects. This is a correctness-level difference vs both competitors.
- **SmartPasteGuard**. The 16 destructive-pattern regexes + production-mode multi-line escalation is a safety surface that MobaXterm / PuTTY do not have. It is a product differentiator worth advertising.
- **Sudo-fallback for SFTP editing**. `sudo cat` / `sudo tee` pipeline in `RemoteFileEditor` is more ergonomic than MobaXterm's equivalent and absent in PSFTP.
- **Auto-open SFTP on SSH connect**. MobaXterm has this too, PuTTY does not — but Heimdall's dual-pane sibling is tighter than MobaXterm's fixed left dock.
- **Preflight checks** (`AuthPreflightChecker`). Catching "key file doesn't exist" before the TCP connect is a better user experience than the `Permission denied` post-facto error.

---

## 8. Out-of-scope observations

Flagging these for b53 (RDP) or b54 (synthesis) to keep track of:

- **Credential vault** (Royal TS / mRemoteNG territory). Heimdall's per-server DPAPI-encrypted fields are correct but not a "vault" in the enterprise sense (sharing, rotation, auditing). A vault architecture would be an XL effort and a separate product decision.
- **Team sharing / multi-user config sync**. Absent; out of scope for a single-user tool as currently positioned.
- **Scripting/automation API** (SecureCRT territory). Scripted macros (T30) only; no external API surface.
- **Built-in text editor** (MobaTextEditor). The external-editor pivot is fine for most users; a built-in editor would be XL and arguably out of the "connection manager" scope.
- **Session-tree import/export compat with MobaXterm `.mxtsessions` or PuTTY registry**. A migration-readiness feature that deserves its own batch if prioritized.
- **`.ppk` round-trip** is part of Cluster A but touches the key-generation, key-audit, and OpenSSH-config import/export surfaces — might deserve a cross-cluster review in b54.
- **`ControlMaster` equivalent**. SSH multiplexing over a single TCP connection. SSH.NET does not expose this; it would be an architectural change. Low priority given that tunnel reuse already covers the common use case.

---

## 9. Open questions for b54 synthesis

To be decided once b53 (RDP) has produced its own cluster list:

1. **Blocker-first vs quick-wins-first?** Cluster A (keygen) and Cluster C (terminal config) are quick wins that unlock PuTTY migration claims. Cluster B (resilience) and Cluster D (transfer engine) are harder but unlock MobaXterm migration. Order matters for any public "Heimdall.Next is ready" announcement.
2. **Single-app positioning vs multi-app** (the PuTTY model uses separate `.exe` for PuTTYgen/Pageant/Plink). Heimdall should stay single-app IMO, but this confirms Cluster A should be a `ToolRegistry` tool, not a standalone binary.
3. **Session-restore scope.** Does "restore previous session" mean re-open tabs + connect, or just re-open tabs and let the user confirm each connect? The latter is safer (no unattended creds going out), the former matches MobaXterm's default behaviour.
4. **Transfer-queue scope.** Does it persist across app restarts? Across network drops? Is "resume a transfer that was interrupted by shutdown" in scope, or only "resume a transfer that was cancelled in-session"?
5. **Serial / COM** prioritization — is this a "MobaXterm parity" feature or a "nice to have later" feature? User population dependent.
6. **X11** prioritization — Linux-centric users are a clear MobaXterm user segment; if the target audience is more Windows-sysadmin / mixed, X11 can wait.
7. **Target audience resolution.** Several priority calls above depend on whether Heimdall.Next primarily serves (a) Windows sysadmin / RDP-heavy users coming from mRemoteNG, (b) Unix-centric engineers coming from MobaXterm, or (c) PuTTY power users looking for tabs + session tree. b54 will need an explicit user-segment call.

---

## 10. Appendix — Cross-reference index

- **SSH baseline source**: parallel recon pass on `src/Heimdall.Ssh/`, `src/Heimdall.App/Services/Handlers/SshHandler.cs`, `TunnelService.cs`, `tests/Heimdall.Ssh.Tests/`, config defaults.
- **Terminal baseline source**: parallel recon pass on `src/Heimdall.Terminal/`, `src/Heimdall.App/Assets/terminal.html`, `Assets/Terminal/xterm.min.js`, `EmbeddedSshView.xaml.cs`, `MacroService.cs`, `EmbeddedSessionManager.cs`, `SplitService.cs`, `AppSettings.cs`.
- **SFTP baseline source**: parallel recon pass on `src/Heimdall.Sftp/`, `src/Heimdall.App/Views/Sessions/EmbeddedSftpView.xaml.cs`, `LocalFileBrowserView.xaml.cs`, `EmbeddedSftpViewModel.cs`, `RemoteFileEditor.cs`, `SftpHandler.cs`, `FtpHandler.cs`, `AppSettings.cs`.
- **Competitor references**: MobaXterm Professional feature list (well-known product documentation), PuTTY 0.8x release notes and manual (official PuTTY docs), mRemoteNG / Royal TS / SecureCRT feature lists used only for triangulation.

End of b52 audit. b53 (RDP) begins after Julien's validation of this document. b54 (priority synthesis + roadmap) consumes b52 + b53 outputs.
