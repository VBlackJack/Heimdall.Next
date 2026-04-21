# Audit — Connection Sequences

Date: 2026-04-19
Context: post-b56 (`.rdp` import green), pre-b57. Cluster pivot from
"Remote Access migration ergonomics" to "Daily-use ergonomics".
Source: roadmap b54 prioritisation + user's directive on `connection sequences`
as the next differentiating axis before PuTTY/OpenSSH imports.

## 1. Executive summary

Heimdall.Next already ships a minimal post-connect mechanism
(`ServerProfileDto.PostConnectCommand`), but it is single-string, SSH-Embedded-only,
fail-silent, and invisible to the user. It is far short of what PuTTY/MobaXterm/Royal
TS offer on daily admin journeys, and the existing Command Library
(514 curated commands) is totally disconnected from session lifecycle.

This audit recommends a **deliberately narrow MVP**: promote the existing
`PostConnectCommand` to a structured, ordered list of post-connect steps for
**terminal/SSH sessions only**, visible during execution, per-step-disabling,
per-step failure policy, and optional integration with Command Library for
parameterised reuse. Everything outside that box (pre-connect, jump chains beyond
`SshGatewayId`, local scripts, VPN triggers, WoL sequencing, tunnel choreography)
is explicitly **out of scope** for the MVP and routed to later batches.

Three batch candidates are proposed, sized so the first one ships the differentiating
daily-use win in a single supervised batch, and the other two incrementally extend
coverage without rewriting the model.

## 2. Scope boundaries — what this audit is NOT

This audit is about **commands sent to a just-established terminal session**.
The following are explicitly **out of scope** and tracked as separate future clusters:

| Out-of-scope axis | Why separated | Future cluster |
|---|---|---|
| Pre-connect local scripts (PowerShell, Python, `.exe`) | Different trust model, different UX, different failure semantics | "Pre-connect hooks" |
| VPN bring-up / tear-down | Touches OS-level network config, requires elevation, UX is opinionated | "Network preflight" |
| Wake-on-LAN sequencing | Already exists as a standalone tool and one-shot action; chaining into connect is a separate UX | "Connect preflight actions" |
| Jump-host chains beyond `SshGatewayId` | Current model already supports one gateway. Multi-hop chaining (A → B → C) needs its own data model and test surface | "Chained jump hosts" |
| Tunnel choreography (ad-hoc tunnels opened/closed per session) | `TunnelManager` is ref-counted; connection-scoped tunnels need dedicated lifecycle | "Session-scoped tunnels" |
| Pre-connect credential fetch (vault, keyring) | Belongs to the vault cluster already deferred in roadmap b54 | "Credential vault" |
| Post-disconnect hooks | Symmetric but independent surface; low demand | "Disconnect hooks" |
| Macro recorder / replay for live terminal (TerminalMacro) | Already exists as a standalone feature; merging with post-connect is a later consolidation, not MVP | "Macro-sequence unification" |

The MVP stays strictly on the axis: **"When a terminal session reaches the Connected
state, replay this ordered, configured list of commands into its input stream with
visibility and per-step control."**

Separating this axis keeps the MVP shippable in a single supervised batch and avoids
the trap of building a generic workflow engine.

## 3. MVP faisable vs système de workflow trop ambitieux

This section exists because the gravitational pull towards a general-purpose task
engine is strong, and the user explicitly flagged it as a risk to verrouille in the
audit.

### 3.1 MVP faisable (target)

Structured post-connect command list:

- Attached to a `ServerProfileDto` (persisted in `servers.json`).
- N entries, ordered, reorderable.
- Per entry: `Input` (string or Command Library reference), `DelayMs` before send,
  `Enabled` flag, `OnFailure` policy (Continue / Stop).
- Applied automatically when the session reaches `Connected` on supported protocols
  (SSH Embedded MVP; Plink/Telnet/LocalShell later).
- Status ping to the user while the sequence runs (status bar / indicator in session
  header), click-to-cancel during execution.
- Per-step visibility in logs.
- Backward compatibility: old `PostConnectCommand` (single string) continues to
  deserialise into the new list as a single synthetic entry.

Complexity: bounded, shippable in one batch, directly addresses the daily admin
journey ("land in $HOME → cd /var/log → tail -f app.log").

### 3.2 Système de workflow trop ambitieux (anti-target)

The following features are explicitly **REJECTED** for MVP because they introduce a
workflow engine without yet having product signal:

- Conditional branching (`if output contains X`, `if exit code != 0`).
- Loops (`for each host in group`, `retry until`).
- Variable capture from output (`${hostname} = $(hostname)`).
- Cross-step data passing.
- DAG scheduling / parallel steps.
- Declarative syntax (YAML/JSON authoring of sequences outside the UI).
- Per-step timeouts with distinct semantics from `OnFailure`.
- Interactive prompts during sequence (pause-for-input).
- Shared sequences at the group level inheriting to servers.
- Sequence versioning / history / diff.

Rationale: none of these are needed for the 80 % case. Adding them now locks the
data model into a shape that is hard to evolve later. They become a separate
"Sequence engine v2" cluster if and when real user signal arises.

**Decision rule for future asks**: if an incoming feature request can be expressed
as "list of commands, executed in order, one at a time", it belongs in MVP.
Anything else gets a separate audit.

## 4. État de l'art concurrents

Table below captures how the three main competitors handle post-connect on
terminal sessions. Intentionally tight — the goal is to situate Heimdall.Next,
not to exhaustively document competitors.

| Tool | Post-connect mechanism | Strengths | Limits |
|---|---|---|---|
| **PuTTY** | Single "Remote command" string, sent as SSH exec command (not typed into interactive shell) | Zero config, works on any OpenSSH server | One-shot only; fires a non-interactive exec, session exits after; no sequence |
| **MobaXterm** | "Execute command" field + "Follow SSH path" (auto-cd to a directory) | Simple, per-session, visible in UI | Single command; `Follow SSH path` is hard-coded to cd, not a general hook |
| **Royal TS** | Tasks (dedicated object type) with pre/post-connect bindings, variable substitution, script attachments | Flexible, powerful variable system | Separate Tasks management UI; steep learning curve; per-server config friction |
| **Termius** | Snippets (parameterised commands) with optional "run on connect" toggle | Integrated with a snippet library (similar spirit to our Command Library) | Limited to one snippet per session; no ordered list |
| **mRemoteNG** | "External Tools" (local commands triggered by hotkey) | N/A for post-connect | External Tools are **local-side** actions, not post-connect terminal commands — explicit gap on the target axis |
| **Heimdall v1 (legacy)** | None | — | — |

**Observation**: the real differentiator Heimdall.Next can claim is the
**combination** of:
- An ordered list (beyond PuTTY's single command / MobaXterm's single field).
- Deep integration with an existing Command Library (which Royal TS's Tasks and
  Termius's Snippets approximate but not with 514 curated commands out of the box).
- Visibility + cancellability during execution (nobody does this well).

This is the angle the MVP must ship.

## 5. État actuel Heimdall.Next

Inventory of the relevant existing bricks, read from the codebase on 2026-04-19:

### 5.1 `ServerProfileDto.PostConnectCommand` (existing)

File: `src/Heimdall.Core/Configuration/ServerProfileDto.cs:53-54`

```csharp
public string PostConnectCommand { get; set; } = "";
public int PostConnectDelayMs { get; set; } = 800;
```

- Single multiline string, split on `\n`.
- Stored at root of `ServerProfileDto`, available for all protocols that may read it.
- Bound in `ServerDialog.xaml` (line ~1567) with a `UpdateSourceTrigger=PropertyChanged`
  TextBox and a numeric field for `PostConnectDelayMs`.

### 5.2 `SshHandler` — current implementation

File: `src/Heimdall.App/Services/Handlers/SshHandler.cs:155-174`

```csharp
if (!string.IsNullOrWhiteSpace(server.PostConnectCommand))
{
    var delayMs = server.PostConnectDelayMs > 0 ? server.PostConnectDelayMs : 800;
    await Task.Delay(delayMs, ct);

    var lines = server.PostConnectCommand
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    foreach (var line in lines)
    {
        session.Write(line + "\n");
        if (lines.Length > 1) await Task.Delay(150, ct);
    }
    FileLogger.Info($"Post-connect: sent {lines.Length} command(s) to {server.DisplayName}");
}
```

Properties:
- **Only SSH Embedded (SSH.NET path)**. Plink fallback path does NOT replay this.
- Delay is fixed 150 ms between lines (not configurable).
- No status UI — user does not see the sequence running.
- No per-line `Enabled` control — workaround is to remove the line from the string.
- Any SSH.NET write exception is not caught at this level — it bubbles up and aborts
  the whole connect flow. Fail-silent at higher scopes (the catch in the calling
  ConnectionService).
- `FileLogger.Info` logs count only, not per-line content (intentional, for privacy).

### 5.3 `TerminalMacro` + `MacroService` — adjacent but disconnected

Files:
- `src/Heimdall.Core/Models/TerminalMacro.cs` — `Id / Name / Description / Entries[]`
  where `MacroEntry = { Input, DelayMs }`.
- `src/Heimdall.App/Services/MacroService.cs` — static, persists macros as individual
  `{id}.json` files under `AppContext.BaseDirectory/macros/`.

This is a **manual-replay** feature attached to the terminal UI (user opens a macro
menu, picks one, it replays). It is **not** attached to any server profile and is not
triggered on connect. The data model (`MacroEntry { Input, DelayMs }`) is the closest
shape to what a post-connect entry needs to look like — it is a natural candidate to
harmonise on later, but merging them in MVP is explicitly rejected (see §3.2).

### 5.4 `ToolContext.SendCommandAction`

File: `src/Heimdall.Core/Models/ToolContext.cs:37`

```csharp
Action<string>? SendCommandAction = null
```

Wired by `EmbeddedSessionManager.CreateToolControl()` to find the sibling embedded
SSH view via `SplitTreeHelper.EnumerateLeaves()` and expose a write endpoint.
Currently consumed only by `CommandLibraryView` to implement "Send to terminal".

This delegate is **already** the right level of abstraction for a post-connect
step to write into the session. The MVP should reuse it internally in the sequence
runner rather than call `session.Write` directly from a handler — this decouples the
runner from protocol specifics and makes it naturally testable.

### 5.5 `AutoOpenSftpAsync` (pattern precedent)

File: `src/Heimdall.App/ViewModels/Session/SessionCoordinator.cs`

Post-connect hook pattern already exists: when SSH is established and
`AppSettings.SftpAutoOpenOnSsh = true`, the coordinator auto-opens a sibling SFTP
pane. This is the only current post-connect-triggered action. It proves the hook
point (`OnSessionReady`) is already known and reliable.

### 5.6 Command Library (TwinShell integration)

514 pre-curated PowerShell/Bash commands persisted in SQLite at
`%LOCALAPPDATA%\TwinShell\twinshell.db`. `ToolContext.TargetHost` prefills host into
parameter templates. Favorites, history, import/export, Git Sync.

**Currently no link** between a Command Library entry and a server profile's
post-connect list. This is the Termius-equivalent feature hiding in plain sight,
ready to be cross-wired.

### 5.7 Summary of the existing gap surface

| Capability | Exists? | Where | Gap |
|---|---|---|---|
| Persist post-connect command on server | Yes | `ServerProfileDto.PostConnectCommand` | Single string, flat |
| Ordered steps | Partial | `\n`-split lines | No per-step metadata |
| Per-step delay | No | 150 ms hardcoded | — |
| Per-step enabled flag | No | — | — |
| Failure policy | No | Errors bubble up | — |
| Protocol coverage | Partial | SSH Embedded only | Plink / Telnet / LocalShell untouched |
| Status UI | No | Log line only | Invisible to user |
| Cancel during execution | No | — | Cancel cancels session, not just sequence |
| Command Library integration | No | — | Disconnected |
| UI to author sequences | Partial | Single TextBox multiline | Not structured |
| Applies to RDP | No | RDP has no terminal input | Out of scope by design |

## 6. Parcours quotidiens — pain points we target

Three daily journeys the MVP must measurably improve:

### 6.1 "Land and navigate" (SSH admin)

User opens SSH session → wants to `cd /var/log/app` → wants to `tail -F current.log`.
Today: typed manually every time, or crammed into `PostConnectCommand` textbox
with no per-line control. MVP: two ordered steps, Enabled flags, visible progress.

### 6.2 "Morning smoke check" (ops)

User opens a production host → wants to run a fixed smoke: `systemctl status nginx`
then `df -h` then `uptime`. Today: either manual (painful) or a copy-pasted blob.
MVP: three steps, each linkable to a Command Library entry so the list stays
authoritative even when the command itself is edited in the library.

### 6.3 "Context switcher" (dev)

User opens a dev VM → wants `tmux attach || tmux new -s work` + `cd ~/projects/foo`
+ `source venv/bin/activate`. Today: muscle memory. MVP: saved once per server,
auto-runs. This is the killer daily win — it turns the tool from a connection
manager into a context-preserving environment.

## 7. Gaps identifiés — ranked by MVP relevance

Top-5 gaps to close in the MVP batch:

1. **Structured ordered list** replacing the single-string field. Backward-compatible
   deserialisation of the legacy `PostConnectCommand` string.
2. **Per-step `Enabled` toggle** so users can disable a step temporarily without
   deleting it.
3. **Per-step `OnFailure` policy** (Continue / Stop) so one bad step does not abort
   a whole daily startup. Default: Continue.
4. **Visible progress** during sequence execution — status bar line + session header
   indicator showing `Step 2 of 4: tail -F current.log`.
5. **Cancellable sequence** — clicking the indicator (or a hotkey) stops the
   sequence without disconnecting the session.

Gaps intentionally **deferred** to future batches (not MVP blockers):

- Protocol coverage extension (Plink, Telnet, LocalShell) — MVP = SSH Embedded only.
- Command Library linkage — MVP = inline strings only; linkage in b58.
- Reorderable UI drag-and-drop — MVP = up/down arrow buttons only.
- Variable substitution in step text — MVP = literal strings only.
- Group-level shared sequences — MVP = per-server only.
- Sequence templates / presets — MVP = manual authoring only.

## 8. MVP scope proposal (locked)

### 8.1 Data model

Add to `ServerProfileDto`:

```csharp
public List<PostConnectStep> PostConnectSteps { get; set; } = [];
```

And keep the existing single-string field (never remove):

```csharp
public string PostConnectCommand { get; set; } = "";   // legacy, deserialised only
public int PostConnectDelayMs { get; set; } = 800;
```

New type in `Heimdall.Core.Models`:

```csharp
public enum PostConnectFailurePolicy { Continue, Stop }

public sealed class PostConnectStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Input { get; set; } = "";
    public int DelayMs { get; set; } = 150;
    public bool Enabled { get; set; } = true;
    public PostConnectFailurePolicy OnFailure { get; set; } = PostConnectFailurePolicy.Continue;
}
```

### 8.2 Backward compatibility rule

On load, if `PostConnectSteps` is empty AND `PostConnectCommand` is non-empty, build a
synthetic list from the string (split on `\n`). On save:
- Always serialise the new `PostConnectSteps` list.
- Stop writing `PostConnectCommand` once the new list has been populated (write empty
  string). Keep deserialising it for a transition window — same pattern as the
  `EnableSessionPersistence` retirement in b55.1 and `LocalShellElevated` →
  `ElevationMode` in `ServerProfileDto`.

### 8.3 Runner architecture

New service `PostConnectSequenceRunner` in `Heimdall.App/Services/PostConnect/`:

```csharp
public interface IPostConnectSequenceRunner
{
    Task<PostConnectRunResult> RunAsync(
        IReadOnlyList<PostConnectStep> steps,
        Action<string> writeCallback,
        IProgress<PostConnectRunProgress>? progress,
        CancellationToken ct);
}
```

- Takes an abstract `writeCallback` (the same shape as `ToolContext.SendCommandAction`)
  so it is decoupled from SSH.NET / Plink / any protocol.
- Emits progress events (`current step index`, `total`, `stepName`, `status`) so the
  UI indicator can update.
- Respects `OnFailure` policy.
- Fully cancellable via `CancellationToken`.

Handler wiring: `SshHandler` constructs the runner and awaits it after
`_connectionSm.TryTransition(..., Connected)`. The existing inline
`PostConnectCommand` replay block is deleted and replaced by a single runner call.

### 8.4 UI — ServerDialog

Replace the current single-TextBox + delay-integer pair with:
- A `ListView` of steps with columns: `Enabled` checkbox, `Input` textbox,
  `Delay` integer, `OnFailure` combo.
- `Add` / `Remove` / `Move up` / `Move down` buttons.
- The existing fields remain in the dialog as a migration affordance for the current
  session (greyed out + read-only) — they vanish once migrated.

### 8.5 UI — runtime indicator

While the sequence runs, the session tab header shows a small progress indicator
(`⟳ 2/4`) with a tooltip describing the current step. Click cancels the sequence
without disconnecting. Status bar reflects the same info.

### 8.6 Out of scope for MVP (explicit)

- Any protocol other than SSH Embedded. (Plink requires input marshalling through
  the `PipeModeSession`; Telnet and LocalShell similar. Follow-up batch.)
- Command Library linkage (`PostConnectStep.CommandLibraryId` + parameter binding).
- Variable substitution (`${hostname}`, `${username}`, `${date}`).
- Reorder via drag-drop (arrow buttons only).
- Step templates / cloning across servers.
- Pre-connect steps.

## 9. Architecture — risk map

| Risk | Mitigation |
|---|---|
| Backward-compat drift on old `servers.json` | Migration rule locked in §8.2 — test-covered with fixtures (3+ legacy samples). Mirror of `ElevationMode` migration pattern, already proven in codebase. |
| Sequence blocking SSH session if a step writes junk | `OnFailure = Continue` default + per-step cancel + overall cancel via session-scoped CTS. |
| UI regressions in `ServerDialog` (large surface) | Scope the XAML change to the post-connect block only; the rest of the tab layout is untouched. Smoke test covers it. |
| `writeCallback` indirection breaking existing Plink path | Plink is **not** touched in MVP. It keeps its current behaviour (no post-connect). This is called out in the prompt. |
| Progress indicator airspace on top of embedded sessions | Put the indicator in the session tab header (WPF-native, no airspace), not on the WebView2 surface. |
| User cancels mid-step, leaving partial input in shell buffer | Acceptable trade-off — document in release notes. Alternative (flushing the input buffer) requires protocol-specific write-cancellation semantics that are not worth building in MVP. |

## 10. Batch candidates

### Batch candidate A — "Post-connect v1" (MVP, ~8-10 h)

Deliverable:
- Data model (`PostConnectStep` + enum) + backward-compat migration.
- Runner service + interface.
- `SshHandler` rewiring (Embedded path only).
- `ServerDialog` UI for authoring steps.
- Runtime indicator in session tab header + status bar line.
- Cancel via click-on-indicator.
- i18n (~15 keys).
- Tests: runner unit tests (cancellation, failure policies, progress emission, empty
  list), migration tests (legacy string → synthetic list), dialog VM tests.
- No documentation beyond the existing `ARCHITECTURE.md` section update.

Value: ships the daily-use win on the main SSH admin journey. Differentiating vs
PuTTY/MobaXterm.

### Batch candidate B — "Post-connect — protocol coverage" (~4-6 h)

Prerequisite: A green.

Deliverable:
- Wire the runner into Plink fallback path (marshall writes through
  `PipeModeSession`).
- Wire into Telnet session path.
- Wire into LocalShell (ConPTY stdin).
- Add per-protocol tests.

Value: removes the "works on Embedded SSH only" footnote. Necessary but not
glamorous.

### Batch candidate C — "Post-connect — Command Library linkage" (~6-8 h)

Prerequisite: A green.

Deliverable:
- Add `PostConnectStep.CommandLibraryId` + resolver.
- UI in `ServerDialog` step editor: inline text OR pick from Command Library.
- On run, resolve library entry to its current text — so editing the library edits
  all bindings.
- Parameter binding using the existing Command Library parameter system.
- Tests covering the resolver + a deleted-library-entry graceful fallback.

Value: turns post-connect into a first-class use of the Command Library asset.
Directly addresses the Termius differentiator.

### Recommended sequence

`A → C → B`, not `A → B → C`.

Rationale:
- A alone already removes most pain on the main journey (SSH admin).
- C on top of A delivers the differentiating promise ("your 514 commands are one
  click away from auto-running on connect").
- B is necessary for protocol completeness but lower daily signal (most users who
  need post-connect are on Embedded SSH already).

## 11. Answer to the framing question

> "What does Heimdall.Next need to be credibly better than PuTTY/MobaXterm on
> the main journey of opening a session and working in it?"

PuTTY has one command. MobaXterm has one command + a `cd`. Neither offers a
visible, cancellable, per-step-controllable, library-backed sequence. Shipping
batch candidate A alone already makes Heimdall.Next strictly superior to both on
this axis for the SSH admin journey. Shipping A + C makes it strictly superior
to Royal TS's Tasks model for the cases where a company has invested in a
curated command library — which is exactly Heimdall.Next's bet with the 514
TwinShell commands.

This is the cheapest differentiating win available in the current backlog.

## 12. Open decisions — to discuss before drafting the b57 prompt

1. **Default `DelayMs` for new steps** — current implicit is 150 ms. Keep, or bump
   to a more conservative 250 ms?
2. **Indicator placement** — session tab header vs dedicated `Sequences` status
   strip? Tab header is cheaper; strip is more discoverable.
3. **Cancel scope** — does clicking cancel the current sequence only, or offer a
   second "and disable future runs on this session" option? Lean: only the current
   run, no extra config.
4. **Migration persistence timing** — on first save of a migrated server, the legacy
   field becomes empty. Do we also offer a one-shot migration tool to sweep all
   servers proactively? Lean: no, let migration happen lazily on save.
5. **Per-step `Enabled = false` behavior in export** — does exporting a server
   profile include disabled steps? Lean: yes, user chose to keep them.
6. **Hotkey for "cancel current sequence"** — worth allocating a dedicated shortcut?
   Lean: no for MVP, indicator click is sufficient.
7. **Logging granularity** — log per-step content (privacy risk if steps contain
   secrets) or only step count? Lean: opt-in verbose logging, default count-only,
   matching the current `PostConnectCommand` behavior.

Seven minor decisions, all tractable. None of them gates the batch — they can be
answered in the b57 pre-prompt phase.

## 13. Recommendation

Ship batch candidate A as b57 (MVP). Do b55-style QA (LocalShell + real SSH if
available) before b57.1 (= candidate C). Keep candidate B for when a Plink/Telnet
user complains or when we touch Plink for another reason.

The existing `PostConnectCommand` field already proves the feature's demand — it was
implemented as a workaround for exactly this gap and is already wired into the
ServerDialog UI. Promoting it to a structured list is an incremental, low-risk
move with outsized daily-use impact.
