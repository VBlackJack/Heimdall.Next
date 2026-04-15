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

## Prerequisites

- Windows desktop session with UIAutomation available.
- A Debug build of Heimdall.Next under `src/Heimdall.App/bin/Debug/...`.
- The app must be launchable without first-run dialogs blocking the main window.

## Run

From the repo root:

```powershell
pwsh -File .\scripts\smoke\settings-smoke.ps1
pwsh -File .\scripts\smoke\navigation-a11y-smoke.ps1
```

To include a build and test pass first:

```powershell
pwsh -File .\scripts\smoke\settings-smoke.ps1 -RunBuild -RunTests
pwsh -File .\scripts\smoke\navigation-a11y-smoke.ps1 -RunBuild -RunTests
```

Each script prints a JSON report and returns a non-zero exit code on failure.

Run them sequentially. They launch and stop the same desktop app and temporarily mutate the same `settings.json`.

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

## Manual Checks Still Worth Keeping

These are still better as manual smokes:

- session-tab drag reorder / merge / detach
- drag cancel by `Escape`
- visual drop-target highlight confirmation
- command palette close on cross-process foreground change
- Narrator or NVDA pass on newly added controls

## Extending The Harness

- Dot-source `scripts/smoke/uia-common.ps1` instead of reimplementing launch/wait/click logic.
- Prefer stable `x:Name` or `AutomationProperties.AutomationId` on controls that matter for smoke coverage.
- Back up and restore `config/settings.json` when a smoke mutates settings.
- Keep reports structured: a top-level `Result`, then booleans plus sampled strings that explain what was observed.
- Keep durable repo-versioned smokes separate from one-off diagnostic probes.
