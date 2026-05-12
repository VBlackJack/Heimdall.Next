# Prompt 14 — `docs/TOOLS.md` — tools layer reference

## Context

You are working on **Heimdall.Next**. Read the project README and `docs/DEVELOPMENT.md` first if you have not already. Prompt 13 (`prompts/13-docs-development-and-security-inversion.md`) created the developer reference and inverted `SECURITY.md`; the test baseline is **5,453 passing / 6 skipped / 0 failed**.

The repository ships 59 built-in tools (Network, Security, Encoding, System, External categories) plus a dynamic external-tools provider that detects locally-installed NirSoft / Sysinternals utilities at startup, plus a SecNumCloud audit engine, plus a Command Library that bridges the bundled TwinShell projects. None of that is documented anywhere a developer can read in one place. Architecture details are scattered across the code, README focuses on user-facing capability, and `docs/ARCHITECTURE.md` is global.

This prompt creates `docs/TOOLS.md` as the curated developer reference for the tools layer. It does not change any code.

## Goals

1. Create `docs/TOOLS.md` covering: tool counts per category, the `ToolRegistry` / `IToolView` / `ToolDescriptor` architecture, the gateway-routing pattern that lets network tools tunnel via SSH, the sidebar panel + Tools tab UX, the NirSoft / Sysinternals external provider, the SecNumCloud audit engine, and the Command Library / TwinShell integration.
2. Source every concrete fact (counts, class names, file paths, registration sites) from the **current repository state**. Do not invent counts or names; if a number disagrees with an older write-up, trust the repo and update the doc accordingly.
3. Add a one-line entry to `docs/CHANGELOG.md` and a "Tools layer reference" pointer in `docs/DEVELOPMENT.md`'s "Where to find things" section.

## Background — relevant files (read these before writing)

- `src/Heimdall.App/Services/ToolRegistry.cs` — single source of truth for built-in tool registration. Each tool is an `Entry(...)` call. Read it to confirm the exact category split and tool counts.
- `src/Heimdall.App/Services/ExternalToolProviderService.cs` — startup scanner that injects NirSoft / Sysinternals tools as `ToolCategory.External`.
- `src/Heimdall.App/Services/ToolGatewayConnector.cs` — the helper that opens an SSH-tunnelled `SshClient` for tools that opt in via the "Route via" ComboBox.
- `src/Heimdall.App/Services/SecNumCloudAuditEngine.cs` — the orchestrator for the SecNumCloud audit (15 checks across 4 chapters).
- `src/Heimdall.App/Views/Tools/ExternalToolWrapperView.xaml(.cs)` — the generic CLI-tool wrapper view used for detected external tools.
- The Command Library lives in `src/TwinShell.Core/`, `src/TwinShell.Persistence/`, and `src/TwinShell.Infrastructure/`. Inspect those projects' csproj + top-level types to summarise the integration surface.
- `src/Heimdall.App/Themes/IconGeometries.xaml` — `Geo.Tool.*` geometry resource keys for tool icons.
- `src/Heimdall.App/Localization/` and `locales/en.json` — search for `Tool*`, `ToolDesc*`, `BtnRouteVia` keys to confirm the i18n conventions described below.
- `src/Heimdall.App/ViewModels/SidebarTool*` — `SidebarToolCategoryViewModel` + `SidebarToolItemViewModel` are the sidebar Tools tab structure.

If a type referenced in the structure outline below is not where you expect (e.g. `IToolView` could be inside `ToolRegistry.cs` rather than its own file, or under `Views/Tools/`), find it via grep and document it where it actually lives. Do **not** carry forward stale references.

## Implementation steps

### Step 1 — Create `docs/TOOLS.md`

Standard Apache 2.0 HTML-comment header at the top, then `# Heimdall.Next — Tools Layer Reference`. Body sections in this order:

#### Section 1 — Overview (3-5 lines)

One paragraph: what the tools layer is for (sysadmin tasks alongside connection sessions), how many built-in tools ship today, the five categories. Pull the exact count by running `grep -c 'Entry(' src/Heimdall.App/Services/ToolRegistry.cs` minus any lines that are obviously not tool entries (constructors, comments inside the file). If the count differs from the README's badge, prefer the repo truth and flag the discrepancy in the report.

#### Section 2 — Categories and tool listings

For each of the five categories — **Network**, **Security**, **Encoding**, **System**, **External** — list the registered tools by their display name. Source the names from `ToolRegistry.cs` (the i18n key on each `Entry` resolves through `locales/en.json` — read the English value to get the user-facing name, not the key).

Format suggestion (Markdown table per category, two columns):

```markdown
### Network (N tools)

| Tool | Notes |
|------|-------|
| Ping | Standard ICMP ping |
| ... | ... |
```

The "Notes" column is short — one line per tool, only when there is something the source comment or i18n description says is non-obvious. If a tool is just "what it sounds like" leave Notes blank rather than padding.

Include a short callout for tools that are flagged as **gateway-routable** (those whose XAML carries a `CmbRouteVia` ComboBox and which call `ToolGatewayConnector.Connect()`). The audit listed five at the time of writing: Network Cartography, Port Scanner, Banner Grabber, Firewall Tester, Default Credential Scanner. Verify by `grep -rn "ToolGatewayConnector" src/Heimdall.App` and use the actual current set — do not copy the list from this prompt.

#### Section 3 — Tool infrastructure

A short subsection per architectural building block. Source from the actual code:

- **`ToolRegistry.cs`**: single source of truth — one `Entry(...)` per tool. Document the `Entry` signature exactly as it is in the source (ID, category, i18n keys, aliases, factory delegate, icon). If the signature has changed, the doc reflects the current shape.
- **`IToolView` interface**: lifecycle methods. Find the file (likely under `src/Heimdall.App/Views/Tools/` or co-located in `ToolRegistry.cs`) and quote the actual method signatures.
- **`ToolDescriptor`**: the metadata record. Document what `DescriptionKey` defaults to and how the convention `ToolDesc{Id}` resolves.
- **Gateway routing**: how `ToolGatewayConnector.Connect()` works, what it returns, what the `CmbRouteVia` ComboBox is bound to. One paragraph plus a code reference.
- **Icons**: the `Geo.Tool.*` resource-key convention in `Themes/IconGeometries.xaml`. Show one example.
- **Sidebar panel**: the Sessions / Tools tab toggle pattern (`SidebarTabStyle` RadioButtons, the `Ctrl+Shift+T` shortcut, the `SidebarToolCategoryViewModel` → `SidebarToolItemViewModel` tree, single-click launch). Cross-reference the gotcha section in `docs/TROUBLESHOOTING.md` if a relevant entry exists; otherwise just describe the structure.
- **Tools tab**: full-page browser with favorites, recents, search, 280px cards. Note that favorites are persisted via `AppSettings.PinnedTools` (or whatever the actual property is — verify).
- **Onboarding**: 3-step first-launch overlay, persisted via `AppSettings.OnboardingCompleted`.
- **Launch flow**: `OpenToolTabAsync` cleans up orphaned tabs on `CreateToolControl` failure.
- **Design token gotcha**: `SpacingRowGap` (sys:Double) versus `SpacingRowGapGrid` (GridLength). Keep this short — it is a single trap, not a section.

#### Section 4 — External tool provider (NirSoft / Sysinternals)

Document the architecture as it currently exists:

- `IExternalToolProvider` → concrete providers (`SysinternalsToolProvider`, `NirSoftToolProvider`). Find the files and confirm the tool counts each provides — the previous write-up said 16 + 16. If the source disagrees, document the source's number.
- `ExternalToolProviderService` (singleton): startup scan strategy — checks PATH and standard install directories.
- `ToolRegistry.RegisterExternalTools()`: how detected tools are dynamically added as `ToolCategory.External`.
- `ExternalToolWrapperView`: generic launcher that captures stdout, switches between DataGrid (CSV output) and TextBox (text output).
- Elevation: tools with `RequiresElevation=true` use the `cmd /c "tool args > tempfile"` + `Verb=runas` pattern (stdout-redirect workaround for the UAC fork).
- Context menu: detected tools appear in the session right-click → "Detected Tools" submenu, grouped by provider.
- Settings: `AppSettings.SysinternalsPath` and `AppSettings.NirSoftPath` are user-configurable; a "Rescan" button forces re-detection.
- **Licensing note**: NirSoft and Sysinternals tools cannot be redistributed. Heimdall **detects and wraps** local installations only — the user must install them separately.
- ID convention: external tool IDs use the format `EXT:PROVIDER:TOOLID` (e.g. `EXT:SYSINTERNALS:PSEXEC`).

#### Section 5 — SecNumCloud audit engine

- File: `src/Heimdall.App/Services/SecNumCloudAuditEngine.cs`.
- Orchestrates 15 checks across 4 SecNumCloud v3.2 chapters (verify chapter / check counts directly in the source if they have changed).
- Constructor accepts `Func<string, string> localize` for runtime i18n of all audit messages — this is how the engine stays free of `LocalizationManager` dependencies.
- `HtmlReportGenerator.Generate()` accepts the same `localize` parameter for report content localization.
- Calls Core APIs directly: `CartographyEngine`, `NtlmProbe`, `UdpProbeEngine`, `SshFingerprinter`, `HttpFingerprinter`. Verify each name in source.
- Progress events: `PhaseProgress`, `StatusChanged`, `CheckCompleted`.
- Exports: HTML standalone report (`HtmlReportGenerator`), CSV evidence (`CsvEvidenceExporter`), Draw.io diagram (`DrawIoExporter`).

#### Section 6 — Command Library (TwinShell integration)

- Source: bundled TwinShell projects under `src/TwinShell.Core/`, `src/TwinShell.Persistence/`, `src/TwinShell.Infrastructure/`, all `net10.0-windows`.
- Database: SQLite at `%LOCALAPPDATA%\TwinShell\twinshell.db` (shared with the standalone TwinShell tool when both are installed).
- Seed data: pre-configured PowerShell / Bash commands seeded on first launch from `data/seed/actions/*.json`. Verify the count by counting the JSON files (`find data/seed/actions -name '*.json' | wc -l`); the previous write-up said 514, but treat the repo as the source of truth.
- Bootstrapper: `TwinShellBootstrapper.cs` registers all TwinShell services in the Heimdall DI container. Includes a `HeimdallLocalizationBridge` (JSON locales → `ILocalizationService`) and a `HeimdallSettingsBridge` (AppSettings → `ISettingsService`).
- Tool ID: `CMDLIB` in the registry, category System.
- Feature surface (one paragraph): fuzzy search with HashSet O(1) filter, platform / category / risk filters with `X/Y` counter, parameterised command generation with inline validation (Required `*`, type tooltips, host pre-fill from `ToolContext.TargetHost`), Windows / Linux template switcher, notes / examples / links panel, favorites (★), command history, import / export, Git Sync via `IGitSyncService` (LibGit2Sharp).
- CRUD UX: `CommandActionDialog` modal with conditional platform sections.
- Send to Terminal: `ToolContext.SendCommandAction` delegate wired by `EmbeddedSessionManager.CreateToolControl()`, walks `SplitTreeHelper.EnumerateLeaves()` to find a sibling `EmbeddedSshView`.
- Git Sync settings: `AppSettings.CmdLibGitSync*` (URL, Token DPAPI-encrypted, Branch, Author, OnStartup, AutoPush). Configurable in Settings → Tools → Command Library, with a token status indicator + clear button.
- System action protection: Edit / Delete buttons hidden for non-user-created (seed) actions. Import skips system actions in merge mode.
- Layout: 7-row grid (Header / Help / Filters / LoadingBar / ActionList / Generator / History). Verify the row layout in the actual `CommandLibraryView.xaml` if it has changed.

#### Section 7 — Where to find things

A small bullet list pointing to:

- `src/Heimdall.App/Services/ToolRegistry.cs` for the registry.
- `src/Heimdall.App/Views/Tools/` for tool view implementations.
- `src/Heimdall.App/ViewModels/Tools/` for tool view-models (if the directory exists; otherwise the view-models are co-located).
- `docs/SECURITY.md` for sandboxing / credential policies that affect tools (SecNumCloud audit, gateway routing).
- `docs/ARCHITECTURE.md` for the wider connection / split / session architecture that surrounds the tools layer.

#### What `docs/TOOLS.md` must NOT contain

- No reference to any local-machine context file (file name starts with `CLAUDE.`).
- No absolute machine paths (`G:\...`, `/sessions/...`, `%USERPROFILE%\AppData\...`).
- No mention of agent or AI workflow conventions ("pair architect", "Codex", "Claude Code", etc.).
- No verbatim copy of any private knowledge that is not already in the repo state.
- No outdated or speculative class names — every name in the doc must resolve under `grep -rn '<name>' src/`.

### Step 2 — Cross-link from `docs/DEVELOPMENT.md`

In `docs/DEVELOPMENT.md`, find the "Where to find things" section (added by Prompt 13). Add one bullet:

```markdown
- Tools layer reference (the 59 built-in tools, external providers, SecNumCloud audit, Command Library): `docs/TOOLS.md`.
```

Insert in alphabetical / topical order — match the existing list's convention.

### Step 3 — Add a CHANGELOG entry

Append a short bullet to the `2026-05-05` block in `docs/CHANGELOG.md` (or to whatever today's dated subsection now is — the previous prompt may have created one):

```markdown
- **Tools layer reference** — extracted the tool architecture, external
  provider, SecNumCloud audit, and Command Library / TwinShell
  integration documentation into a new `docs/TOOLS.md`.
```

### Step 4 — Sanity checks

```bash
test -f docs/TOOLS.md && grep -c '^## ' docs/TOOLS.md
```

Should print >= 7 (Overview, Categories, Tool infrastructure, External tool provider, SecNumCloud, Command Library, Where to find things).

```bash
grep -nE '[A-Z]:\\\\|/sessions/' docs/TOOLS.md
```

Zero matches.

```bash
grep -nE 'CLAUDE\.md|CLAUDE\.MD|pair[- ]architect|Claude Code|Codex' docs/TOOLS.md
```

Zero matches.

```bash
# Every class name mentioned in the doc resolves in source:
for sym in ToolRegistry IToolView ToolDescriptor ToolGatewayConnector \
          SecNumCloudAuditEngine ExternalToolProviderService \
          ExternalToolWrapperView IExternalToolProvider \
          SidebarToolCategoryViewModel SidebarToolItemViewModel \
          TwinShellBootstrapper; do
  if ! grep -rn --include='*.cs' --include='*.xaml' "\b${sym}\b" src/ > /dev/null; then
    echo "MISSING: $sym"
  fi
done
```

Should print nothing (every symbol resolves). If a symbol is reported missing, either the doc names it incorrectly or the repo has drifted. **Drop the symbol from the doc rather than invent it.**

```bash
# Cross-link added to DEVELOPMENT.md
grep -n 'docs/TOOLS.md' docs/DEVELOPMENT.md
```

Should return one match.

```bash
# CHANGELOG entry exists
grep -n 'Tools layer reference' docs/CHANGELOG.md
```

Should return one match.

```bash
# Build / test signal still green (this prompt does not change code)
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build green, **5,453 / 6 / 0**.

## Coding standards

- Apache 2.0 HTML-comment header on the new file.
- English only.
- No `[Co-Authored-By]` or AI attribution.
- No new locale keys.
- Markdown formatting: 80-column wrap preferred; tables for category listings; reference-style links if you have more than two links to the same target.

## Reporting back

When you finish, report:

1. The list of files created or modified.
2. The final tool count per category, sourced from `ToolRegistry.cs` (call out any discrepancy with the previous write-up's "59 tools").
3. The list of class names you verified exist in source (the loop in Step 4) — confirm zero MISSING.
4. The output of each sanity-check command from Step 4.
5. The final test counts from the build/test run, confirming the signal stays at **5,453 / 6 / 0**.
6. Any decision that diverged from this prompt, with a one-line rationale (especially: which tool counts you found in source if they disagree with the README badge, and how you handled symbols that did not exist in source).
