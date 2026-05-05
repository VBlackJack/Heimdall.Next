# Prompt 13 — `docs/DEVELOPMENT.md` + `SECURITY.md` inversion

## Context

You are working on **Heimdall.Next**. Read the project README first if you have not already.

The repository ships several documentation files at the root and under `docs/`. After the SSH/SFTP audit (12 prompts archived under `prompts/01-*` through `prompts/12-*`), the docs are functionally synchronised — `README.md`, `docs/CHANGELOG.md`, `docs/ARCHITECTURE.md`, `docs/TROUBLESHOOTING.md`, and the long-form `SECURITY.md` (root) all reflect the new state. Two structural problems remain:

1. **No top-level developer reference doc.** Build commands, test baseline, version conventions, code standards (Apache 2.0, English-only, nullable, `TreatWarningsAsErrors`, i18n key conventions, namespace collision rules) are currently scattered between `README.md` (marketing-leaning) and an internal AI context file that is intentionally not committed to git. New contributors have nowhere to find a curated dev reference.
2. **`SECURITY.md` is inverted relative to GitHub conventions.** GitHub auto-detects `SECURITY.md` at the repo root and surfaces a "Report a vulnerability" button pointing to it. By convention that root file should be **short**: vulnerability reporting channel, scope statement, and a pointer to the canonical long-form doc. Today the root `SECURITY.md` carries the entire 229-line threat model, while `docs/SECURITY.md` is a 19-line stub redirecting back to root. The intent is correct (single canonical source) but the wiring is reversed.

This prompt fixes both. It does **not** create `docs/TOOLS.md` (a separate prompt will own that) and it does **not** modify `CLAUDE.md` (that file is intentionally git-ignored as local-machine context).

## Goals

1. Create `docs/DEVELOPMENT.md` carrying the curated developer reference. Source the content from the existing repository state (README, ARCHITECTURE, audit-era knowledge) — **do not** import or reference any local-machine context file. The new file must compile cleanly under whatever Markdown linter the repo already uses.
2. Invert the `SECURITY.md` pair: the root file becomes a short GitHub-friendly entry point; the long canonical content moves to `docs/SECURITY.md`. Existing inbound links from the codebase / docs to either file must continue to work.
3. Verify zero references to the local-machine context file (`CLAUDE.md`) and zero hard-coded absolute machine paths leaked into the committed docs.

## Background — relevant files

- `README.md` — keep as-is; it is the marketing-leaning entry. Optional: a single line under "Development" pointing to `docs/DEVELOPMENT.md`.
- `SECURITY.md` (root, 229 lines) — current canonical; will be split.
- `docs/SECURITY.md` (19 lines) — current stub; will become the canonical long form.
- `docs/ARCHITECTURE.md` (~102 KB) — left alone; it is the global architecture reference.
- `docs/CHANGELOG.md` — already up to date; the new doc files should appear in the next changelog entry.
- `docs/TROUBLESHOOTING.md` — left alone in this prompt.
- `docs/CONTRIBUTING.md` (if it exists — check) — should not duplicate `docs/DEVELOPMENT.md`. If it exists, add a cross-reference rather than restating.

## Implementation steps

### Step 1 — Create `docs/DEVELOPMENT.md`

Create a new file `docs/DEVELOPMENT.md` with the standard Apache 2.0 HTML comment header at the top:

```markdown
<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Heimdall.Next — Developer Reference
```

The body must cover the following sections in order. **Source the content from the existing repo state (README, csproj/props files, the `Build.ps1` script, the existing `audit-ssh-sftp-action-plan.md`, and direct inspection of the source tree).** Do not invent values; if a constant is needed (test baseline count, locale key count, .NET version), look it up.

#### Section 1 — Project overview (3-5 lines)

One paragraph: .NET 10 + WPF, MVVM via CommunityToolkit.Mvvm, the 8 protocols, the test baseline count (currently **5,453 passing / 6 skipped**). Source the count from `audit-ssh-sftp-action-plan.md`.

#### Section 2 — Build, test, release

- Batch shortcuts: `Run.bat`, `Test.bat`, `Build.bat`, `Release.bat` — what each does in one line.
- `Build.ps1` modes: `Debug` (default), `-Mode Release`, `-Publish`, `-DryRun`, `-Version`, `-SkipTests` — pulled from inspecting the script.
- The full test command: `dotnet test Heimdall.slnx --no-build`.
- The expected baseline at the time of writing.
- The well-known **gotcha**: `Build.ps1 -SkipTests` followed by `dotnet test --no-build` runs stale binaries; an explicit `dotnet build Heimdall.slnx -c Debug -p:nodeReuse=false` is required between the two.

#### Section 3 — Version conventions

- `<Version>1.0.MMDD.xx</Version>` — Win32 version-info limit is 65,535 per field, so `MMDD` ranges 0101..1231 are safe.
- `<InformationalVersion>YYYY.MMDDxx</InformationalVersion>` — used for the "informational" surface.
- `Build.ps1 -Version <value>` overrides the auto-increment.
- Output: `Dist/debug/` or `Dist/release/`, installers in `Dist/installers/`. Both directories are gitignored.

#### Section 4 — Code standards

- License header: Apache 2.0, author "Julien Bombled" on every new file.
- Language: **English only** in code, comments, XML docs, identifiers, log messages.
- No hardcoded user-facing strings — route through `LocalizationManager`. Add new keys to both `locales/en.json` and `locales/fr.json`; CI enforces parity. Current count is **5,489 leaf keys per locale**.
- No hardcoded URLs / paths / magic numbers — promote to `AppSettings` or a config file.
- Async by default: no blocking calls on the UI thread.
- MVVM: business logic in ViewModels; code-behind is reserved for minimal event wiring (e.g. `Loaded` / `Unloaded` plumbing for native components).
- Nullable reference types are enabled project-wide.
- `TreatWarningsAsErrors` is on via `Directory.Build.props`. Targeted suppressions are allowed via file-scoped `#pragma warning disable <CSXXXX>` with a justification comment; bare disables are not.
- `ConfigureAwait(false)` on every `await` in non-UI projects (`Heimdall.Core`, `Heimdall.Ssh`, `Heimdall.Sftp`, `Heimdall.Terminal`, `TwinShell.*`). UI projects (`Heimdall.App`, the WPF assemblies) intentionally omit it.

#### Section 5 — i18n key conventions

- Convention: `<Context><Element>` CamelCase. Examples: `ErrorPlinkNotFound`, `BtnConnect`, `WarnFtpCleartext`, `SftpHostKeyMismatchMidSession`.
- New XAML uses the `{loc:Translate Key}` markup extension (live-updates on locale change via `LocalizationSource` singleton).
- Legacy `ApplyLocalization()` coexists; new views use `{loc:Translate}`.
- CI enforces key parity between `en.json` and `fr.json`.

#### Section 6 — Namespace conventions

- BCL collision guard: before creating a new sub-namespace under `Heimdall.Core.*`, check the chosen segment against BCL top-level namespaces (`System`, `IO`, `Net`, `Threading`, `Linq`, `Text`, `Collections`, `Diagnostics`, `Security`, `Runtime`, `Globalization`, etc.). On collision, disambiguate (e.g. `SystemInfo` instead of `System`, `NetDiag` instead of `Net`) and align the folder path to match the disambiguated namespace.
- Reference: `Heimdall.Core.SystemInfo` lives under `src/Heimdall.Core/SystemInfo/` precisely because the alternative would shadow `System`.

#### Section 7 — Where to find things

A small table or bullet list pointing at:

- Architecture overview: `docs/ARCHITECTURE.md`.
- Security policy: `docs/SECURITY.md` (the long canonical) and `SECURITY.md` (the GitHub vulnerability-reporting entry).
- Changelog: `docs/CHANGELOG.md`.
- Audit history: `audit-*.md` at the repo root and `docs/audit/`.
- Per-file gotchas the SSH/SFTP audit produced: see the items closed in `audit-ssh-sftp-action-plan.md`.

#### Section 8 — CI

A two-line description: GitHub Actions runs build, test, i18n key parity, lint. Local CI parity command for fast feedback: `dotnet build Heimdall.slnx -c Debug && dotnet test Heimdall.slnx --no-build`.

#### What `docs/DEVELOPMENT.md` must NOT contain

- No reference to any local-machine context file (file name starts with `CLAUDE.`).
- No absolute machine paths (`G:\...`, `/sessions/...`, `%USERPROFILE%\AppData\...`).
- No mention of agent or AI workflow conventions ("pair architect", "Codex", "Claude Code", etc.).
- No reproduction of the 12 audit prompts; just point to `audit-ssh-sftp-action-plan.md`.

### Step 2 — Invert `SECURITY.md` and `docs/SECURITY.md`

Today: `SECURITY.md` (root) is the long canonical (229 lines), `docs/SECURITY.md` is a stub.
Target: `SECURITY.md` (root) is the short GitHub-friendly entry, `docs/SECURITY.md` is the long canonical.

#### Step 2a — Move the long content

Copy the **entire current contents** of root `SECURITY.md` into `docs/SECURITY.md`, replacing the existing 19-line stub. Keep the Apache 2.0 HTML comment header. Update the title to remain `# Security Notes` (or rename to `# Security Policy` if you prefer; pick one and justify in the report).

After the copy, fix any inbound link targets inside the document that previously assumed the file was at root. Specifically: search for relative paths starting with `./` or just bare filenames and verify they still resolve from the new location. Most likely none need fixing because the existing doc references `tests/...` paths anchored at the repo root, which are unchanged from `docs/SECURITY.md`'s relative root (`../tests/...`). Update each such reference to the `../` form.

#### Step 2b — Replace root `SECURITY.md` with a GitHub-friendly stub

Replace the entire content of root `SECURITY.md` with:

```markdown
<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Security Policy

## Reporting a vulnerability

Report suspected vulnerabilities **privately** to the maintainer. This
repository does not currently publish a dedicated security email address;
use the private channel through which you obtained the source, or see
`LICENSE` for maintainer and licensing context. **Do not** file public
issues for security problems.

We aim to acknowledge reports within five business days.

## Scope

Heimdall.Next is a single-user desktop application that stores SSH, RDP,
SFTP, FTP, VNC, and Citrix credentials locally using DPAPI plus
HMAC-SHA256. The full threat model, known limitations, defense-in-depth
decisions, security-relevant test references, and the post-2026-05-05
audit results live in [docs/SECURITY.md](docs/SECURITY.md).

GitHub surfaces this file as the repository's security policy entry.
```

The root file is now ~25 lines and tells GitHub's security UI exactly what it needs (reporting channel, scope, pointer to long form).

#### Step 2c — Verify inbound links

Search the repository for inbound references to either file:

```bash
grep -rn "SECURITY.md" --include='*.md' --include='*.cs' --include='*.xml' --include='*.csproj' .
```

For each match:

- A reference to `docs/SECURITY.md` — keep as-is, that is now the canonical.
- A reference to `SECURITY.md` from inside `docs/` — must become `../SECURITY.md` if it intends to point to the GitHub stub, or `./SECURITY.md` if it intends to point to the canonical (depends on intent — read each match case by case).
- A reference to `SECURITY.md` from outside `docs/` — keep as-is, that is the GitHub stub.

The only file likely to need a fix is `docs/CONTRIBUTING.md` if it exists, or any `docs/*.md` file that links to "the security policy". Resolve each occurrence on its merit; do not perform a blanket replace.

### Step 3 — Add the new files to `docs/CHANGELOG.md`

Append a short entry to the existing `2026-05-05` changelog block (or create a new dated subsection if today's date differs):

```markdown
- **Documentation reorganisation** — extracted developer reference into
  `docs/DEVELOPMENT.md`; inverted `SECURITY.md` so the root file is the
  short GitHub-friendly entry and `docs/SECURITY.md` becomes the
  canonical long-form policy.
```

Do not duplicate the audit closure entry; this is a follow-on documentation task, not a re-statement of the audit.

### Step 4 — Sanity checks

```bash
# DEVELOPMENT.md exists and has all required sections (rough heuristic)
test -f docs/DEVELOPMENT.md && grep -c '^## ' docs/DEVELOPMENT.md
```

Should print a count >= 7 (at least seven `## ` section headers).

```bash
# No absolute paths leaked
grep -nE '[A-Z]:\\\\|/sessions/' docs/DEVELOPMENT.md
```

Should return zero matches.

```bash
# No reference to the local-machine context file
grep -nE 'CLAUDE\.md|CLAUDE\.MD' docs/DEVELOPMENT.md docs/SECURITY.md SECURITY.md
```

Should return zero matches.

```bash
# No mention of AI workflow conventions
grep -niE 'pair[- ]architect|Claude Code|Codex' docs/DEVELOPMENT.md docs/SECURITY.md SECURITY.md
```

Should return zero matches.

```bash
# Root SECURITY.md is short
wc -l SECURITY.md
```

Should report less than 50 lines.

```bash
# docs/SECURITY.md is the long canonical
wc -l docs/SECURITY.md
```

Should report at least 200 lines (matches the original 229-line size, allowing for whitespace edits).

```bash
# Existing build/test signal still green
powershell -File Build.ps1
dotnet test Heimdall.slnx --no-build
```

Expected: build green, tests at **5,453 passed / 6 skipped / 0 failed** (this prompt does not change any code, so the count is unchanged).

## Coding standards

- Apache 2.0 HTML-comment header on every new and modified Markdown file.
- English only.
- No `[Co-Authored-By]` or AI attribution.
- No new locale keys (this prompt is doc-only).
- Markdown formatting: prefer 80-column wraps; lists with two-space continuation indent for sub-items; reference-style links if you have more than two links to the same target.

## Reporting back

When you finish, report:

1. The list of files created or modified, grouped by goal (DEVELOPMENT.md, SECURITY inversion, CHANGELOG entry).
2. A one-line summary per inbound link you fixed during Step 2c (or a confirmation that none needed fixing).
3. The output of each sanity-check command from Step 4.
4. The final test counts from the build/test run, confirming the signal stays at **5,453 / 6 / 0**.
5. Any decision that diverged from this prompt, with a one-line rationale (especially: did you keep the title `# Security Notes` or rename to `# Security Policy`, and why).
