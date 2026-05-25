# Settings UX Audit — 2026-05-23

## Scope and method

Static UX audit of the **global Settings tab** of Heimdall.Next. It does **not**
cover the per-profile `ServerDialog` (restructured separately in chantier G) nor
the About tab.

Surface audited:

- `src/Heimdall.App/MainWindow.xaml` lines ~1799-2958 — the Settings tab markup
  (sticky action bar, search box, `Mw_SettingsSubTabControl` with 6 sub-tabs).
- `src/Heimdall.App/MainWindow.xaml.cs` lines ~921-1004 — settings search code-behind.
- `src/Heimdall.App/ViewModels/SettingsViewModel.cs` (1617 lines) — bindings,
  validation, Save/Reset commands, dirty tracking.
- `locales/en.json` / `fr.json` — `Settings*` keys (211 in en.json).

Method: code reading + reasoning. No runtime observation (WPF app not launched);
items flagged "runtime-check" should be confirmed visually before/after a fix.

### Surface map

The Settings tab is a single `Grid` with a sticky action bar, an optional search
hint, and a top-placed `TabControl` with six sub-tabs:

| Sub-tab | Cards / sections | Approx. controls |
|---|---|---|
| General | Appearance (language, theme, accent) + max sessions + 2 checkboxes | 1 card |
| Terminal | Terminal (font, size, color scheme, PowerShell policy) | 1 card |
| SSH & SFTP | SSH defaults + Trusted host keys (DataGrid) + SSH Gateways (ListBox) | 3 cards |
| RDP | Defaults, Display, Audio, Performance, Devices, Advanced timeouts (Expander), Dialog | 7 cards |
| Security | Credential provider + Credential Guard | 1 card |
| Advanced | Logging, Timeouts, Health Monitor, File Sharing, External Editor, External Tool Providers, Command Library Git Sync, External Tools list | 8 sections |

## What works well

- Card-based grouping inside each tab is visually consistent (`CardBrush`
  surface, `BorderBrush`, `CornerRadiusLg`).
- Theming is correct throughout — all colours go through `DynamicResource`, so
  the surface inherits the ThemeForge + chantier-G contrast fixes
  (`InputBorderBrush`, checkbox/radio focus) for free.
- Accessibility is broadly applied — `AutomationProperties.Name` on virtually
  every interactive control, `LabeledBy` on most input/label pairs.
- A search affordance, a clear-search button and a result hint already exist.
- Per-field validators (`[Range]` + `[NotifyDataErrorInfo]`) cover the seven
  numeric fields; dirty state is tracked automatically via an
  `OnPropertyChanged` override.
- Empty state and status messaging exist for the Trusted Host Keys grid.

## Findings

Severity: **P1** = broken expectation / data-loss risk; **P2** = significant
friction; **P3** = polish / consistency.

### Summary

| # | Severity | Finding |
|---|---|---|
| F1 | P1 | Save silently no-ops on a validation error — the computed summary is never shown |
| F2 | P1 | Global "Reset defaults" wipes everything with no confirmation; scoped "Reset RDP defaults" does confirm |
| F3 | P2 | "Advanced" is an incoherent catch-all; tab weighting is lopsided |
| F4 | P2 | Settings search only filters whole tabs, never locates a control; keyword list drifts |
| F5 | P2 | No per-tab error badges — diverges from the reworked ServerDialog |
| F6 | P2 | Server import/export buttons live in the Settings action bar |
| F7 | P2 | Settings did not follow the ServerDialog (chantier G) restructure |
| F8 | P3 | Stale "left-navigation" comment; top tabs will not scale past 6 |
| F9 | P3 | The global-vs-profile "defaults" relationship is invisible |
| F10 | P3 | `UpdateSourceTrigger` is inconsistent across text fields |
| F11 | P3 | A few combos/text fields miss `AutomationProperties.LabeledBy` |
| F12 | P3 | The "General" landing tab is sparse |

---

### F1 — P1 — Save silently fails on a validation error

`SaveAsync` (`SettingsViewModel.cs:666-674`) runs `ValidateAllProperties()` +
`RefreshValidationSummary()`, then `if (HasErrors) return;`. The command bound to
the **Save settings** button (`MainWindow.xaml:1832`) therefore does nothing when
any of the seven `[Range]` fields is out of bounds — no toast, no dialog, no
banner.

The infrastructure to tell the user *why* exists and is fully localized:
`ValidationSummary` and `HasValidationErrors` (`SettingsViewModel.cs:1492-1537`),
plus a localized-message map (`SettingsValidationKeyMap`,
`SettingsViewModel.cs:1498-1507`). **But `ValidationSummary` has zero references
in `MainWindow.xaml`** (verified by grep) — it is computed and discarded.

Result: a user edits, say, "Max embedded sessions" to `99`, clicks Save, and the
window simply does not close the dirty state. The only signal is a red border on
the offending field, which may be on a sub-tab that is not currently visible.

Recommendation: bind `ValidationSummary` to a visible error banner in the sticky
action bar (Row 0 of the Settings grid, next to the Save button), shown when
`HasValidationErrors` is true. Reuse the warning/error brush already used by the
TFTP no-auth notice (`WarningTextBrush`) or `ErrorTextBrush`.

Caveat: `RefreshValidationSummary()` is only called inside `SaveAsync`, so the
banner would update on Save attempts. That is acceptable for a first fix; a
nicer version recomputes on each field change.

### F2 — P1 — Global "Reset defaults" has no confirmation

`ResetToDefaultsAsync` (`SettingsViewModel.cs:832-838`) loads factory defaults
and marks the VM dirty **immediately, with no confirmation prompt**. The button
is in the always-visible action bar (`MainWindow.xaml:1835`), one click away at
all times, and it discards every setting on every tab.

By contrast, the much narrower `ResetRdpDefaultsAsync`
(`SettingsViewModel.cs:840-853`) *does* call `_dialogService.ShowConfirmAsync`
before resetting only the RDP defaults.

The guard is on the small action and missing on the large one — backwards. A
mis-click on "Reset defaults" silently stages a full wipe (the user still has to
click Save, but the in-memory state and all unsaved edits are already gone).

Recommendation: add a `ShowConfirmAsync` to `ResetToDefaultsAsync`, mirroring the
RDP one (new i18n keys `SettingsResetDefaultsConfirmTitle` /
`...ConfirmBody`). Low risk, ~5 lines.

### F3 — P2 — "Advanced" is a catch-all; tab weighting is lopsided

The Advanced tab (`MainWindow.xaml:2624-2955`) stacks **eight unrelated
sections**: Session Logging, Timeouts, Session Health Monitor, File Sharing
(TFTP), External Editor, External Tool Providers (NirSoft/Sysinternals/NanaRun),
Command Library Git Sync, and the custom External Tools list. They share no
theme beyond "didn't fit elsewhere". Three of them (External Editor, External
Tool Providers, External Tools list) are all about external executables and
could form a coherent group; "File Sharing / TFTP" carries a security warning
and arguably belongs near Security; "Logging" is diagnostics.

Meanwhile General, Terminal and Security each hold a **single card** of 3-5
controls. The tab strip presents six equal-weight tabs that are anything but
equal: two near-empty, two overloaded.

Recommendation (needs its own cadrage — see backlog Chunk B): re-cut the tabs so
each has a coherent purpose and comparable weight. One workable cut:

- **General** — appearance, sessions, sleep, logging.
- **Terminal** — unchanged.
- **SSH & SFTP** — unchanged.
- **RDP** — unchanged.
- **Security** — credential provider, Credential Guard, File Sharing/TFTP.
- **Integrations / Tools** — External Editor, External Tool Providers, External
  Tools list, Command Library Git Sync.
- **Advanced** — Timeouts, Session Health Monitor (genuine power-user knobs).

This is a proposal, not a decision — the exact cut is Julien's call.

### F4 — P2 — Settings search filters tabs only; keyword list drifts

`OnSettingsSearchTextChanged` (`MainWindow.xaml.cs:937-998`) matches the query
against a hand-written keyword array (`SettingsTabKeywords`,
`MainWindow.xaml.cs:921-935`) and **shows/hides whole `TabItem`s**. The hint
reads "X / 6". The search never scrolls to or highlights the matching control.

Two problems:

1. **It does not solve the real pain.** Going from 6 tabs to 3 tabs does not
   help a user who cannot find one checkbox inside the RDP tab's seven cards.
   The valuable behaviour — jump to the control and highlight it — is absent.
2. **The keyword array is hand-maintained and already drifted.** It is a static
   `string[][]` decoupled from the actual controls. Verified gaps:
   - Searching `accent` returns nothing — the General keyword row
     (`MainWindow.xaml.cs:924`) has `theme`, `appearance` but not `accent`,
     even though the accent selector is on that tab.
   - Searching `tunnel` matches only SSH & SFTP, missing General's "Collapse
     tunnels panel by default" checkbox and Advanced's "Tunnel establishment
     delay" field.

Every new setting needs a manual keyword edit or it becomes unsearchable.

Recommendation: either (a) make search walk the visual tree of the tab content
and match on the rendered label text, scroll the first hit into view and flash
it — the genuinely useful version; or (b) at minimum, drive matching from the
i18n label keys rather than a parallel hand-kept array. Option (a) is the right
target; option (b) is the cheap stopgap.

### F5 — P2 — No per-tab error badges

The seven validated numeric fields are spread across General
(`MaxEmbeddedSessions`), Terminal (`TerminalFontSize`), SSH
(`AntiIdleInterval`, `SshTmoutResetInterval`, `SshAutoReconnectAttempts`) and
Advanced (`TunnelEstablishmentDelayMs`, `EmbeddedRdpTimeoutMs`,
`ExternalToolTimeoutMs`). When one is invalid, nothing on the tab strip says
*which* tab holds it — the user must open each tab and scroll.

Chantier G2b solved exactly this for the ServerDialog with per-tab error-count
badges in the `TabItem.Header`. The Settings tab predates that pattern and never
got it.

Recommendation: add per-tab error-count badges to `Mw_SettingsSubTabControl`,
reusing the ServerDialog badge approach. Pairs naturally with F1.

### F6 — P2 — Server import/export buttons live in the Settings action bar

The sticky action bar (`MainWindow.xaml:1831-1850`) holds five buttons: **Save
settings**, **Reset defaults**, **Export servers**, **Import servers**, **Import
Citrix apps**. Only the first two act on settings. The other three mutate the
**server inventory** and have nothing to do with the Settings dirty state.

Five buttons, two meanings, one toolbar — a category error. A user looking to
back up their settings finds "Export servers" and reasonably assumes it does
that. A user on the Servers tab looking to import servers does not think to look
under Settings.

Recommendation: move Export/Import servers and Import Citrix to the Servers tab
(near the server list / its toolbar or the existing Add menu, where
`AddGatewayCommand` etc. already live). The Settings action bar then holds only
Save and Reset — and the dirty-dot semantics become honest.

### F7 — P2 — Settings did not follow the ServerDialog restructure (chantier G)

Chantier G rebuilt the ServerDialog: four tabs, **segmented sub-tabs** for the
dense RDP options (G4), per-tab error badges (G2b), single-level scrolling (G3c),
free resize. The Settings tab — the app's *other* primary configuration
surface — kept its original shape: flat top tabs, no sub-tabs, the seven-card
RDP tab is one long scroll, no badges, no in-page locator.

The two surfaces now feel like different apps. The RDP sub-tab is the clearest
case: chantier G4 split the ServerDialog's RDP options into Display & Audio /
Devices / Performance / Behavior segmented sub-tabs precisely because ~30 stacked
controls were illegible — the Settings RDP tab still stacks all seven cards the
old way (`MainWindow.xaml:2310-2582`).

Recommendation: adopt the chantier-G vocabulary in Settings — at minimum the
segmented sub-tab pattern for the RDP tab, and the per-tab error badges (F5).
Treat this as deliberate parity work, not a blanket rewrite.

### F8 — P3 — Stale comment; top tabs will not scale

`MainWindow.xaml:1799` labels the region "SETTINGS TAB: Left-navigation
sub-tabs", but the control is `TabStripPlacement="Top"` (`MainWindow.xaml:1862`).
Either the comment is stale, or a left-rail layout was intended and never built.

With six tabs today and parity pressure for more (WinRM has no global settings
*yet*, but the pattern is RDP/SSH each own a tab), a vertical left rail reads
better than a horizontal strip and matches the comment's apparent intent.

Recommendation: fix the comment now (trivial); consider a left rail if the F3
re-cut adds a seventh tab.

### F9 — P3 — The global-vs-profile relationship is invisible

The RDP, SSH and Terminal tabs configure *defaults*. Per the resolver model
(`RdpProfileResolver`: profile value → global default → hard-coded), these
values are what a new profile inherits and what an existing profile falls back
to for unset fields. Nothing in the UI says so. A user changing
`RdpDefaultNla` here has no cue about whether it affects existing profiles.

Recommendation: one localized hint line per "defaults" section, e.g. "Applied to
new profiles and to profiles that leave this field unset."

### F10 — P3 — `UpdateSourceTrigger` is inconsistent

Text fields use three different update triggers with no pattern: `PropertyChanged`
(e.g. `SshAutoReconnectAttempts` `MainWindow.xaml:2103`,
`RdpAutoReconnectMaxAttempts` `:2457`), `LostFocus` (e.g. `SysinternalsPath`
`:2784`, `CmdLibGitSyncUrl` `:2833`), and the WPF default (most others). The
dirty dot therefore lights up on the first keystroke in some fields and only on
blur in others.

Recommendation: pick one trigger for plain settings text fields (`LostFocus` is
the sane default for free text) and apply it uniformly; keep `PropertyChanged`
only where live validation needs it.

### F11 — P3 — A few inputs miss `LabeledBy`

Accessibility is good overall, but inconsistent: the General tab's Language,
Theme and Accent combos (`MainWindow.xaml:1880`, `:1887`, `:1915`) and the
Terminal font / font-size text boxes (`:1962`, `:1967`) have no
`AutomationProperties.LabeledBy` to their label `TextBlock`, whereas the SSH and
RDP equivalents do. Screen-reader users get an unlabelled control on those rows.

Recommendation: add `LabeledBy` (or `AutomationProperties.Name`) to the listed
controls for parity with the rest of the surface.

### F12 — P3 — The "General" landing tab is sparse

Settings opens on General: a single card with three combos and two checkboxes in
a 900px-max column on a wide window — a lot of empty canvas for a first
impression. After the F3 re-cut (which folds logging into General) this
improves on its own; flagged so the re-cut keeps it in mind rather than as a
standalone fix.

## Remediation backlog

Grouped into pair-architect-sized chunks. Recommended order top to bottom.

### Chunk A — P1 quick wins (1 short prompt, low risk)

- F1: bind `ValidationSummary` to an error banner in the action bar.
- F2: add a confirmation dialog to `ResetToDefaultsAsync`.

Both are small, self-contained, and need no structural decision. Good first
prompt — ends with build + `dotnet test`.

### Chunk B — P2 information architecture (needs cadrage first)

- F3: re-cut the sub-tabs into coherent, balanced groups.
- F6: move server import/export out of the Settings action bar.
- F12: folds in naturally.

This one needs a scoping decision from Julien before any prompt — the exact tab
cut, and where the server import/export buttons land. Do **not** start coding
this without an agreed target structure.

### Chunk C — P2 parity with ServerDialog

- F5: per-tab error badges.
- F7: segmented sub-tabs for the RDP tab (chantier-G4 pattern).

Mechanical once Chunk B settles the tab structure — best done after B so the
badges/sub-tabs are built on the final layout, not the old one.

### Chunk D — P2 search

- F4: make search locate and highlight the control (target), or at least drive
  matching from i18n keys (stopgap).

Independent of B/C; can run in parallel or last.

### Chunk E — P3 polish batch

- F8 (comment + left-rail decision), F9 (defaults hints), F10
  (`UpdateSourceTrigger` uniformity), F11 (`LabeledBy` gaps).

One sweep prompt at the end.

## Verification notes

Claims verified against source:

- `ValidationSummary` absent from `MainWindow.xaml` — `grep -c` returned 0.
- `ResetToDefaultsAsync` has no `ShowConfirmAsync`; `ResetRdpDefaultsAsync` has
  one — read at `SettingsViewModel.cs:832-853`.
- Search operates on `TabItem.Visibility` — read at `MainWindow.xaml.cs:937-998`.
- Keyword array gaps ("accent", "tunnel") — read at `MainWindow.xaml.cs:921-935`.
- Action-bar buttons — read at `MainWindow.xaml:1831-1850`.
- Stale comment vs `TabStripPlacement` — `MainWindow.xaml:1799` vs `:1862`.

Not verified (needs a running build): exact runtime behaviour of the Save
no-op (whether any other channel surfaces feedback), and the visual density of
the RDP tab. Confirm with a screenshot pass before and after Chunk C.
