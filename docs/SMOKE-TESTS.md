<!--
Copyright 2026 Julien Bombled
Licensed under the Apache License, Version 2.0
-->
# Smoke Tests

This repo now carries a small UIAutomation smoke harness for high-signal desktop checks that were previously rebuilt ad hoc during refactors.

## Files

- `scripts/smoke/uia-common.ps1`
  Shared helpers for launching Heimdall, backing up `settings.json`, waiting for UIA elements, clicking controls, reading combo/list content, and restoring state.
- `scripts/smoke/settings-smoke.ps1`
  Focused Settings page smoke.
- `scripts/smoke/navigation-a11y-smoke.ps1`
  Focused nav-tab and gateway-button accessibility smoke.
- `scripts/smoke/move-to-group-smoke.ps1`
  Focused session-tree move-to-group parity smoke (context menu, expansion retention, destination validation, no-group reachability coverage split between UIA and human checks).
- `scripts/smoke/sidebar-favorites-smoke.ps1`
  Focused sidebar Favorites smoke (section presence, alphabetical ordering, filter interaction, persistence round-trip; ContextMenu-specific flows partially delegated to human checks because WPF popup exposure is inconsistent under UIA).

## Prerequisites

- Windows desktop session with UIAutomation available.
- A Debug build of Heimdall.Next under `src/Heimdall.App/bin/Debug/...`.
- The app must be launchable without first-run dialogs blocking the main window.

## Run

From the repo root:

```powershell
pwsh -File .\scripts\smoke\settings-smoke.ps1
pwsh -File .\scripts\smoke\navigation-a11y-smoke.ps1
pwsh -File .\scripts\smoke\move-to-group-smoke.ps1
pwsh -File .\scripts\smoke\sidebar-favorites-smoke.ps1
```

To include a build and test pass first:

```powershell
pwsh -File .\scripts\smoke\settings-smoke.ps1 -RunBuild -RunTests
pwsh -File .\scripts\smoke\navigation-a11y-smoke.ps1 -RunBuild -RunTests
pwsh -File .\scripts\smoke\move-to-group-smoke.ps1 -RunBuild -RunTests
pwsh -File .\scripts\smoke\sidebar-favorites-smoke.ps1 -RunBuild -RunTests
```

Each script prints a JSON report and returns a non-zero exit code on failure.

Run them sequentially. They launch and stop the same desktop app and temporarily mutate the same `settings.json`; the tree/favorites scripts also back up and restore `servers.json` when they need to seed or inspect persisted state.

## Coverage

`settings-smoke.ps1` covers:

- opening the Settings page
- credential provider preset population
- preset selection updating the command field
- save + restart persistence for the credential provider command
- command-library token status empty-state rendering
- external-tool provider status rendering
- external-tool placeholder legend population
- external-tool preview refresh when selection changes
- EN/FR localized rendering checks after restart

Notes:

- The locale switch is save-gated by current product design, but the scripted smoke uses `settings.json` + restart for stability.
- Credential provider preset labels are intentionally treated as a static catalog. The script records them in EN and FR but does not fail if they stay identical.
- Runtime invocation of external tools from a live session/context menu is still a manual smoke.

`navigation-a11y-smoke.ps1` covers:

- top nav-tab `AutomationProperties.Name` values in EN
- top nav-tab `AutomationProperties.Name` values in FR
- gateway action button `AutomationProperties.Name` values in EN
- gateway action button `AutomationProperties.Name` values in FR

`move-to-group-smoke.ps1` covers:

- session-tree context-menu move to another group
- in-memory expansion retention across the unified move path
- destination-set parity against project-scoped group targets
- no-group entry presence in the move submenu
- persistence-backed verification that the move reached the expected target group

Notes:

- The script uses the shared `uia-common.ps1` helpers plus local tree/menu helpers.
- Some drag/drop-adjacent checks remain manual by design: drag cursor feedback, Escape cancel, scroll perception, and the no-group drop-zone affordance.
- The report uses per-scenario statuses (`Green`, `Red`, `Skipped`) because some WPF popup interactions are intentionally delegated to human smoke.

`sidebar-favorites-smoke.ps1` covers:

- Favorites section presence in the sidebar Tools tree
- alphabetical ordering of favorited tools by localized display name
- sidebar filter interaction against the Favorites section
- persistence round-trip for `FavoriteToolIds` across restart

Notes:

- The script pre-seeds `FavoriteToolIds` in `settings.json` for deterministic ordering/filter checks.
- WPF programmatic ContextMenus are not reliably exposed in the UIA tree, so pin/unpin and right-click-no-launch verification remain human smoke scenarios.
- The report follows the same `Green` / `Red` / `Skipped` scenario model as `move-to-group-smoke.ps1`.

## Manual Checks Still Worth Keeping

These are still better as manual smokes:

- session-tab drag reorder / merge / detach
- drag cancel by `Escape`
- visual drop-target highlight confirmation
- session-tree drag cursor / no-group drop-zone affordance
- sidebar tool ContextMenu pin/unpin flows and right-click no-launch behavior
- command palette close on cross-process foreground change
- Narrator or NVDA pass on newly added controls

## Extending The Harness

- Dot-source `scripts/smoke/uia-common.ps1` instead of reimplementing launch/wait/click logic.
- Prefer stable `x:Name` or `AutomationProperties.AutomationId` on controls that matter for smoke coverage.
- Back up and restore `config/settings.json`, and `config/servers.json` as well when a smoke seeds or mutates the session tree.
- Keep reports structured: a top-level `Result`, then per-scenario `Green` / `Red` / `Skipped` statuses with sampled strings or reasons explaining what was observed.
- Keep durable repo-versioned smokes separate from one-off diagnostic probes.
