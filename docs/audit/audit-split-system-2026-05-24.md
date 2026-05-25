# Quality audit — Split system (N-pane binary tree)

**Date:** 2026-05-24
**Scope:** the session split subsystem only — tree models, tree helper,
services, host controls, and their tests.
**Mode:** pair-architect (supervisor audit, static analysis of the real code).

## Surface audited

| Layer | File | Lines |
|---|---|---|
| Model | `src/Heimdall.Core/Models/ISplitContent.cs` | 24 |
| Model | `src/Heimdall.Core/Models/SessionPaneModel.cs` | 90 |
| Model | `src/Heimdall.Core/Models/SplitContainerModel.cs` | 59 |
| Helper | `src/Heimdall.Core/Models/SplitTreeHelper.cs` | 342 |
| Persistence | `src/Heimdall.Core/Configuration/SplitLayoutMemory.cs` | 207 |
| Service | `src/Heimdall.App/Services/ISplitService.cs` | 102 |
| Service | `src/Heimdall.App/Services/SplitService.cs` | 855 |
| Service | `src/Heimdall.App/Services/SessionSplitService.cs` | 263 |
| View | `src/Heimdall.App/Views/SessionPaneControl.xaml(.cs)` | 156 / 242 |
| View | `src/Heimdall.App/Views/SplitContainerControl.xaml(.cs)` | 33 / 246 |
| Policy | `src/Heimdall.App/Views/EmbeddedRdp/RdpSplitWarningPolicy.cs` | 30 |
| Tests | `SplitServiceTests`, `SplitTreeHelperTests`, `RdpSplitWarningPolicyTests` | 1234 |

## Verdict

The split subsystem is in **good overall shape**. The pure tree core
(`SplitTreeHelper`, `SplitContainerModel`, `SplitLayoutMemory`) is clean,
defensive, and well tested. The orchestration layer (`SplitService`) carries
the real risk: it is large, and its three async methods do not handle failure
consistently.

No P1 (active, severe) defect was found. **12 findings: 2 P2, 10 P3.**

| Code | Priority | Theme | One-line |
|---|---|---|---|
| S1 | **P2** | Robustness | Inconsistent async exception handling → silent failures, crash, state leak |
| S2 | **P2** | Tests | Server-pane close/cleanup paths have zero coverage (sealed dependencies) |
| S3 | P3 | Dead code | `SplitTreeHelper.ReplaceContainer` + a `PromoteInTree` branch are unreachable |
| S4 | P3 | Dead code | `FindSibling` / `FindPaneByServerId` have no production callers |
| S5 | P3 | Docs | `ClosePane` comment + `CLAUDE.md` contradict the actual disposal order |
| S6 | P3 | Architecture | `SplitService` vs `SessionSplitService` — confusing names, no interface/tests on the latter |
| S7 | P3 | Lifecycle | `SplitContainerControl.OnUnloaded` is reload-unsafe; asymmetric with `SessionPaneControl` |
| S8 | P3 | Async | `DisconnectSessionAsync` is fake-async; `DisconnectPaneHost` does sync-over-async |
| S9 | P3 | Edge case | `MergeExistingSession` can orphan a still-connecting source pane |
| S10 | P3 | Robustness | `SwapSplitPanesAsync` has no cancellation / post-await guard |
| S11 | P3 | Consistency | `var` pervasive vs the documented "explicit types" C# standard |
| S12 | P3 | Minor | Silent double-registration; duplicated protocol dispatch |

---

## P2 findings

### S1 — Inconsistent async exception handling

The three async entry points of `SplitService` handle failure three different
ways:

- `SplitSessionWithServerAsync` — `catch (OperationCanceledException)` **and**
  `catch (Exception)` (logs the error, sets status text). Correct.
- `ReconnectPaneAsync` — `catch (OperationCanceledException)` **only**. No
  general catch. In addition, the pre-`try` region (lines ~488-522, including
  `await DisconnectSessionAsync` and the placeholder-state mutations) is
  entirely outside the `try`.
- `SwapSplitPanesAsync` — **no `try`/`catch` at all**.

Consequences:

- `ReconnectPaneAsync` is invoked fire-and-forget from `SessionPaneControl`
  (`_ = vm.ReconnectPaneAsync(...)`, line 176). An unexpected exception
  (`IOException` from `LoadServersAsync`, a protocol handler throwing,
  `CreateHostControl` throwing) escapes the method and becomes an **unobserved
  task exception**: the reconnect fails silently — no log, no status text — and
  the pane is left permanently in the "connecting" placeholder state.
- `SwapSplitPanesAsync` is invoked from an `async void` event handler
  (`SessionTabContextMenuFactory.cs:546`,
  `swapItem.Click += async (_, _) => await vm.SwapSplitPanesAsync(...)`). An
  exception there — e.g. `dispatcher.InvokeAsync` raising `TaskCanceledException`
  during shutdown — propagates out of an `async void` and **crashes the app**.

Linked to S1, **`ReconnectPaneAsync` leaks old connection state on unexpected
failure**: `ReleaseOldConnectionState(oldServerId)` is called on the
no-server, normal-failure, post-await-guard, and OCE paths — but there is no
unhandled-exception path, so an exception after `oldServerId` is captured and
before success leaves the old tunnel reference count and the old state-machine
entry **leaked**. This is exactly the bug class the recent tunnel ref-count
chantier addressed in the protocol handlers.

**Recommendation.** Give `ReconnectPaneAsync` a general `catch (Exception)` that
mirrors `SplitSessionWithServerAsync` (log, status text, `pane.Status = "Error"`,
`ReleaseOldConnectionState(oldServerId)`), and move the pre-`try` region inside
the guarded block. Add a `try`/`catch` to `SwapSplitPanesAsync`. This is small,
mechanical, and the reference implementation already exists in the same file.

### S2 — Server-pane close/cleanup paths are untested

`SplitTreeHelper` and the synchronous guard logic of `SplitService` are tested.
But the **server-pane lifecycle** is not: `ClosePane` for a real server pane,
`CloseAllPanes` for a server pane, `CleanupOrphanedPane`, and the tunnel-release
plus state-machine-reset logic have **zero unit coverage**.

Root cause: `ConnectionStateMachine` and `TunnelManager` are `sealed` concrete
classes with no interface. Moq cannot mock them; the `SplitServiceTests`
fixture passes `null!` for both and the fixture comment is explicit that every
test deliberately stays on the tool-pane / empty-pane path to avoid
dereferencing them. The tunnel-reference release on pane close — the same area
the recent ref-count-leak chantier had to fix in the handlers — is therefore
exercised by no test at all.

**Recommendation.** Treat this as an architecture decision, not a test-only
refactor (per the `CLAUDE.md` architecture-first rule): decide whether to
extract `IConnectionStateMachine` / `ITunnelManager` seams. If yes, the
server-pane close/cleanup paths become unit-testable and the highest-value gap
in the subsystem closes. If no, document the conscious choice.

---

## P3 findings

### S3 — Unreachable dead code in `SplitTreeHelper`

In `RemovePane`, the local `container` is always reference-equal to `root`
(it is `root` via a type-pattern cast). Every call therefore reaches
`PromoteInTree(root, container, ...)` with `root == target`, so
`PromoteInTree` always returns `promotion` from its first branch and the
`ReplaceContainer` walk (~21 lines, plus the `else` path of `PromoteInTree`) is
**never executed**. The recursion inside `RemovePane` already rebuilds the tree.
`SplitTreeHelperTests` confirms it — every deep-removal test passes through the
`root == target` branch; `ReplaceContainer` has no coverage because it is dead.

**Recommendation.** Simplify `PromoteInTree` to `return promotion;` and delete
`ReplaceContainer` (~25 lines removed). Also fix the class doc comment: it
claims "All operations are side-effect free except where noted", but `RemovePane`
and `ReplacePane` both mutate the tree in place.

### S4 — Unused public API

`SplitTreeHelper.FindSibling` and `SplitTreeHelper.FindPaneByServerId` have
**zero production callers** — they are exercised only by `SplitTreeHelperTests`.
Their tests give a false impression of "covered behaviour" for code nothing
depends on. Either remove them, or, if they are deliberately kept for planned
use, make that a conscious decision.

### S5 — Stale disposal-order documentation

`ClosePane`'s doc comment says *"Fixed disposal order: detach → remove →
dispose"* and `CLAUDE.md` (Split System section) says *"detach HostControl
(null) → remove from tree → dispose"*. The actual code does the inverse:
`DisconnectPaneHost` (dispose/teardown, line 473) → `pane.HostControl = null`
(detach, line 474) → `RemovePane` (remove, line 476) — i.e. **dispose → detach
→ remove**.

The airspace-sensitive WPF child detach (`Child = null` before `Dispose()`)
does happen, but *inside* `DisconnectSessionAsync` / the RDP teardown sequence,
not at the model level. Given the extensive validated RDP teardown work on
record, the code is almost certainly the source of truth and the documentation
is stale.

**Recommendation.** Correct the `ClosePane` comment and the `CLAUDE.md` line to
describe what the code actually does and why. Misleading disposal-order docs in
airspace-sensitive teardown code are a real trap for the next maintainer.

### S6 — Confusing dual-service naming

Two services with near-identical names and opposite shapes:

- `SplitService` — 855 lines, `ISplitService` interface, DI singleton. Owns
  tree mutation, per-session CTS, tunnel lifecycle, connection routing.
- `SessionSplitService` — 263 lines, **no interface**, concrete `sealed`. Owns
  detach-to-floating-window, the split palette request, unsplit.

Nothing in the names tells a reader which does what. `SessionSplitService` also
has **no tests** and no interface, unlike every other service of comparable
weight.

**Recommendation.** Rename `SessionSplitService` to something intent-revealing
(e.g. `SessionDetachService` or `SessionWindowService`) and give it an interface
for consistency and testability.

### S7 — `SplitContainerControl.OnUnloaded` is reload-unsafe

`SplitContainerControl.OnUnloaded` unsubscribes its **own** lifecycle events
(`Loaded`, `Unloaded`, `DataContextChanged`) and nulls `_model`. If WPF ever
reloads that same instance after an unload — which TabControl tab-switching can
do depending on visual-tree retention — the reloaded control is dead: no
`Loaded` to run `ApplyLayout`, no `DataContextChanged` to rebind.

This is the **opposite** of its sibling `SessionPaneControl`, which deliberately
keeps these handlers attached (it is explicitly documented as reuse-safe and
guards every handler body with `IsLoaded`). One of the two strategies is wrong.

**Recommendation.** Verify at runtime whether `SplitContainerControl` instances
are ever reloaded after unload. If they are, this is a "blank/broken split after
tab switch" bug; align the control with `SessionPaneControl`'s reuse-safe
pattern (keep lifecycle handlers, guard bodies with `IsLoaded`). If they are
never reloaded, the teardown is harmless dead weight but still worth removing
for consistency.

### S8 — Fake-async method + sync-over-async call site

`EmbeddedSessionManager.DisconnectSessionAsync` is named `...Async`, returns
`Task`, but is 100% synchronous — it ends with `return Task.CompletedTask` and
contains no `await`. `SplitService.DisconnectPaneHost` calls it via
`.GetAwaiter().GetResult()`.

Harmless **today** (the returned task is already completed, so the blocking
call does not block). But it is a latent landmine: the moment anyone makes
`DisconnectSessionAsync` genuinely async, `DisconnectPaneHost` — invoked on the
UI thread from `ClosePane` and `CloseAllPanes` — becomes a **UI-thread
deadlock**.

**Recommendation.** Make the method honestly synchronous (`void DisconnectSession`,
drop the `Async` suffix and the `Task` return). `DisconnectPaneHost` can then
drop its `.GetAwaiter().GetResult()` wrapper entirely. (Note: the method itself
lives in `EmbeddedSessionManager`, just outside this subsystem, but the risky
call site is `SplitService`.)

### S9 — `MergeExistingSession` can orphan a connecting pane

`MergeExistingSession` admits a source as soon as **one** of its leaves has a
host control (`sourceHasContent`). A multi-pane source with one connected pane
and one pane still mid-connection passes the check. The merge proceeds,
`CancelSession(source)` cancels the in-flight connection, and the connecting
pane — now re-parented into the target tree — is left permanently in the
"connecting" placeholder with no host control that will ever arrive.

Rare, but possible. **Recommendation.** Either block the merge while any source
pane is still connecting, or surface the cancellation into the merged pane
(`Status = "Error"`).

### S10 — `SwapSplitPanesAsync` has no cancellation / post-await guard

`SwapSplitPanesAsync` neither takes nor checks the session cancellation token,
and after its two `AwaitVisualTreeAsync` awaits it re-attaches host controls
without re-verifying that the session and tree still exist. If the tab is
closed mid-swap, host controls are re-parented into a torn-down tree. Low
probability (the swap is fast), low impact. Apply the same post-await guard the
other two async methods use (`ActiveSessionsProvider` contains + `FindPane`
null check).

### S11 — `var` vs the documented "explicit types" standard

`SplitService`, `SessionSplitService`, and the two host controls use `var`
pervasively. Heimdall's `.editorconfig` has `csharp_style_var_* = true:suggestion`
(it *prefers* `var`), and Heimdall has no `var` pre-push gate — so this is
consistent with the project's actual configuration. It does, however,
contradict the documented cross-project C# standard *"types explicites partout,
var interdit"* (adopted 2026-05-19, enforced in ThemeForge).

This is a **project-wide configuration gap**, not a split-system defect.
**Recommendation.** A decision for Julien: either accept `var` as the Heimdall
norm and update the stated standard, or flip the `.editorconfig` and plan a
repo-wide sweep. Out of scope to "fix" inside this audit.

### S12 — Minor

- `RegisterSession` uses `ConcurrentDictionary.TryAdd` and silently ignores a
  failed (duplicate) registration. Tested as "idempotent", and benign — but a
  double-registration can mask a lifecycle bug elsewhere. A `Debug`-level log
  would make it visible.
- `ConnectByProtocolAsync`'s protocol `switch` duplicates the protocol → handler
  dispatch that `ConnectionService` already owns. A 10th protocol means two
  edits. Minor DRY.

---

## What is solid (no action needed)

- **`SplitTreeHelper`** — pure, no WPF dependency, defensive against null
  `First`/`Second`, and **excellently tested** (~40 tests covering every helper,
  8-leaf trees, deep removal, short-circuit, null-children edge cases).
- **`SplitLayoutMemory`** — thread-safe (single lock), atomic save
  (unique-temp-then-rename), versioned schema with legacy-array fallback,
  capacity eviction. Clean.
- **`SplitContainerModel.SplitRatio`** — manual property that clamps to
  `[MinRatio, MaxRatio]` *before* `PropertyChanged` fires. Correct and tested.
- **Post-await guard pattern** — `SplitSessionWithServerAsync` and
  `ReconnectPaneAsync` correctly re-check session membership and pane existence
  after every async boundary, and dispose the orphaned result.
- **Deferred CTS dispose** — `CancelSession` cancels immediately and disposes
  after a delay so in-flight operations can still observe the token.
- Apache 2.0 headers on every file, English throughout, thorough XML doc
  comments, nullable annotations (`[NotNullWhen]`).

---

## Recommended sequencing

1. **S1 first.** Small, mechanical, high value — the reference implementation
   (`SplitSessionWithServerAsync`'s catch block) already exists in the same
   file. One tight pair-architect chantier closes a silent-failure path, a
   crash path, and a state leak.
2. **S3 + S4 + S5 as one low-risk cleanup commit** — delete dead code, drop or
   justify unused API, correct the disposal-order docs. Near-zero regression
   risk.
3. **S2** — needs an architecture decision (sealed-dependency seams) before any
   prompt. Scope it separately.
4. The remaining P3 items (S6-S12) are polish; batch or defer per appetite.
