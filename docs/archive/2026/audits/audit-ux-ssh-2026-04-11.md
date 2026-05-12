# UX Audit Report — Heimdall.Next / SSH Subsystem

**Date:** 2026-04-11
**Stack:** WPF / .NET 10 / C# 14 / SSH.NET / Plink / xterm.js
**Mode:** Targeted — UX (Nielsen's 10 Heuristics)
**Scope:** ServerDialog.xaml, ServerDialogViewModel.cs, ConnectionService.Ssh.cs, MainWindow.xaml.cs (Settings), SettingsViewModel.cs, ServerProfileDto.cs, SchemaValidator.cs

---

## Executive Summary

The SSH UX has one **critical dead-feature bug**: `SshMode = "External"` is selectable in the UI, saved to disk, and has a global "Apply to all" button in Settings — but `ConnectionService.Ssh.cs` never reads it. The user who configures "External" expecting PuTTY or Windows Terminal gets the embedded terminal silently. This is inconsistent with RDP, which fully implements both modes. Beyond that, two important issues degrade the experience: the password field is always labeled "Passphrase" regardless of whether a key file is set, and the Plink auth fallback gives zero user feedback while it retries under the hood.

---

## Findings by Severity

### 🔴 Critical (1)

**SSH External mode is a no-op — UI feature is fully unimplemented in the connection layer.**

### 🟠 Important (2)

- Password field always labeled "Passphrase" — wrong for password-only auth (the most common case)
- Silent Plink fallback — no user feedback during auth retry

### 🟡 Minor (1)

- Static `SshAuthHint` — doesn't adapt to the current auth configuration

---

## Detailed Findings

### UX-01 — System Status Visibility

**Score: 2/3**

| # | Check | Verdict | Severity | Details |
|---|-------|---------|----------|---------|
| 1 | Loading indicator during connection | ✅ PASS | — | `_connectionSm.TryTransition(ValidatingConfig → LaunchingSsh)` — state machine drives UI |
| 2 | Error messages communicated clearly | ✅ PASS | — | `SetError(server.Id, failure.Message)` surfaces localized errors via `StatusText` |
| 3 | Feedback during async fallback | ❌ FAIL | 🟠 | `ConnectionService.Ssh.cs:111` — when SSH.NET rejects auth, the code silently retries via Plink. The state machine stays at `LaunchingSsh` throughout. If Plink also fails (e.g., `ErrorPlinkNotConfigured`), the error appears after a total delay with no intermediate "retrying…" feedback. |

---

### UX-02 — User Control and Freedom

**Score: 3/3**

| # | Check | Verdict | Severity | Details |
|---|-------|---------|----------|---------|
| 1 | Cancel during long connection | ✅ PASS | — | `CancellationToken ct` threaded through `ConnectSshAsync`, `EstablishTunnelAsync`, and `ProbeHostKeyFingerprintAsync` |
| 2 | Confirmation on destructive actions | ✅ PASS | — | "Apply SSH mode to all" in SettingsViewModel uses a confirmation dialog (`ConfirmApplySshModeMessage`) |
| 3 | Back navigation / escape from dialog | ✅ PASS | — | Standard WPF dialog cancellation |

---

### UX-03 — Error Prevention and Recovery

**Score: 1/3**

| # | Check | Verdict | Severity | Details |
|---|-------|---------|----------|---------|
| 1 | Input validation on dialog fields | ✅ PASS | — | `[NotifyDataErrorInfo]`, `[Required]`, `[Range]` on all critical fields; per-field inline errors surfaced via `DisplayNameError`, `EndpointPortError`, etc. |
| 2 | Password field label matches actual usage | ❌ FAIL | 🟠 | `ServerDialog.xaml.cs:386` always sets `DlgSrv_BasicPassphraseLabel.Text = _localizer["ServerDialogLabelPassphrase"]`. When `SshKeyPath` is empty, this field is the **SSH password** (plain-text auth, RFC 4252 §8). Calling it "Passphrase" implies it decrypts a key — but there is no key. The label must be dynamic: "Passphrase" when `SshKeyPath` is set, "Password" when it isn't. Compare: RDP, VNC, FTP, Telnet all say "Password". |
| 3 | Actionable error messages on Plink fallback failure | ❌ FAIL | 🟠 | When `plink.exe` is not configured, `ErrorPlinkNotConfigured` appears — but only after SSH.NET already failed silently. The user doesn't know whether the original error was auth, network, or config. The error message should explain the chain: "SSH.NET: auth rejected. Plink: not configured — set plink.exe path in Settings." |

---

### UX-04 — Consistency and Standards

**Score: 2/3**

| # | Check | Verdict | Severity | Details |
|---|-------|---------|----------|---------|
| 1 | SSH and SFTP credential panels consistent | ✅ PASS | — | Both use the same `SshFamilySectionStyle` DataTrigger and the same form layout |
| 2 | SSH External mode consistent with RDP External mode | ❌ FAIL | 🔴 | **See UX-07.** RDP External mode launches `mstsc.exe` (implemented in `ConnectionService.Rdp.cs:75`). SSH External mode is selectable in the same UI pattern but launches nothing — `ConnectionService.Ssh.cs` ignores `server.SshMode` entirely. A user who discovers External mode in RDP and tries it in SSH will get no result and no error. |
| 3 | Settings "Apply to all" consistent between RDP and SSH | ✅ PASS | — | Both modes have the "Apply to all" pattern in `SettingsViewModel.ApplySshModeToAllAsync()` |

---

### UX-05 — Recognition Over Recall

**Score: 3/3**

| # | Check | Verdict | Severity | Details |
|---|-------|---------|----------|---------|
| 1 | Main auth options visible without deep navigation | ✅ PASS | — | Username, key path, and password/passphrase are on the main tab of the dialog |
| 2 | Advanced options (compression, X11, agent forwarding) accessible | ✅ PASS | — | Advanced toggle expands the Advanced section — discoverable |
| 3 | Gateway routing diagram visible | ✅ PASS | — | Connection path diagram with nodes and arrows rendered in the dialog |

---

### UX-06 — Efficiency for Power Users

**Score: 2/2**

| # | Check | Verdict | Severity | Details |
|---|-------|---------|----------|---------|
| 1 | Tab order set on SSH fields | ✅ PASS | — | `TabIndex="10"` (username), `TabIndex="11"` (key path), `TabIndex="12"` (password) — sequential |
| 2 | "Apply SSH mode to all" bulk action | ✅ PASS | — | `SettingsViewModel.ApplySshModeToAllAsync()` — power user feature present |

---

### UX-07 — Critical User Flows

**Score: 0/1**

| # | Check | Verdict | Severity | Details |
|---|-------|---------|----------|---------|
| 1 | SSH External mode flow works end-to-end | ❌ FAIL | 🔴 | **This is the main finding.** `ServerProfileDto.SshMode` is defined (`Core/Configuration/ServerProfileDto.cs:42`, default `"Embedded"`). Schema validation accepts `"External"` (`SchemaValidator.cs:53`). The UI shows a ComboBox with "Embedded / External" (`ServerDialog.xaml:1379-1380`). SettingsViewModel can bulk-apply the mode to all servers. The `SshMode` enum even exists: `Core/Models/Enums.cs:68`. **But `ConnectionService.Ssh.cs` never reads `server.SshMode`** — the method `ConnectSshAsync` goes straight to SSH.NET → Plink fallback regardless. There is no branch for `"External"` that would launch PuTTY, `ssh.exe`, Windows Terminal, or any external client. Contrast with RDP: `ConnectionService.Rdp.cs:62` reads `server.RdpMode` and branches explicitly to `mstsc.exe` launch when `"External"`. The user flow is broken: configure External → click Connect → embedded terminal opens silently. |

---

### UX-08 — Help and Documentation

**Score: 1/2**

| # | Check | Verdict | Severity | Details |
|---|-------|---------|----------|---------|
| 1 | Auth hint text present | ✅ PASS | — | `DlgSrv_BasicSshAuthHint` is rendered below the password field |
| 2 | Hint adapts to current auth state | ❌ FAIL | 🟡 | `ServerDialog.xaml.cs:387` sets `DlgSrv_BasicSshAuthHint.Text` once at load time. The hint is static regardless of whether a key is selected (passphrase mode), no key (password mode), or Pageant is available (agent mode). A dynamic hint — computed from `SshKeyPath` and Pageant availability — would significantly reduce credential confusion. |

---

## Action Plan

### 🔴 UX-EXT-01 — Implement SSH External mode in ConnectionService (Critical)

**File:** `src/Heimdall.App/Services/ConnectionService.Ssh.cs`

At the top of `ConnectSshAsync`, read `server.SshMode` and branch:

```csharp
if (string.Equals(server.SshMode, "External", StringComparison.OrdinalIgnoreCase))
{
    return await ConnectSshExternalAsync(server, settings, ct);
}
```

Implement `ConnectSshExternalAsync` following the same pattern as `ConnectRdpAsync` external mode:
- Resolve the external client (configurable: PuTTY, `ssh.exe`, Windows Terminal)
- Build the command line with `-ssh`, `-P port`, `user@host`, key `-i`, etc.
- Use `Process.Start()` — no session result (no embedded terminal)
- Return `new ConnectionResult(true, null, new ExternalSshResult(processId))`

The `AppSettings` already has `PlinkPath` — a `SshExternalClientPath` or reuse of `PlinkPath` for PuTTY GUI mode (plink vs putty) is a design decision.

---

### 🟠 UX-LBL-01 — Dynamic password/passphrase label (Important)

**File:** `src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs`

Add a computed property:

```csharp
public string SshPasswordLabel => string.IsNullOrWhiteSpace(SshKeyPath)
    ? L("ServerDialogLabelPassword")
    : L("ServerDialogLabelPassphrase");
```

Add `partial void OnSshKeyPathChanged(string? value) => OnPropertyChanged(nameof(SshPasswordLabel));`

**File:** `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml.cs`

Replace the static localization call at line 386:

```csharp
// Remove: DlgSrv_BasicPassphraseLabel.Text = _localizer["ServerDialogLabelPassphrase"];
// The label is now driven by the ViewModel binding to SshPasswordLabel
```

Bind `DlgSrv_BasicPassphraseLabel` to `SshPasswordLabel` in XAML (or update in `OnSshKeyPathChanged` in the View's code-behind).

---

### 🟠 UX-FB-01 — Plink fallback status feedback (Important)

**File:** `src/Heimdall.App/Services/ConnectionService.Ssh.cs`

After line 111 (`FileLogger.Info("SSH.NET auth failed... falling back to Plink")`), update the connection state before the fallback:

```csharp
_connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.LaunchingSsh); // or a new "Retrying" state
// or emit a status message via the localizer
```

Consider a new `ConnectionState.Retrying` value, or surface the retry via `StatusText` binding before the await.

---

### 🟡 UX-HINT-01 — Dynamic SshAuthHint (Minor)

**File:** `src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs`

```csharp
public string SshAuthHint => !string.IsNullOrWhiteSpace(SshKeyPath)
    ? L("ServerDialogSshAuthHintKey")       // "Passphrase unlocks your private key. Leave blank if the key has no passphrase."
    : L("ServerDialogSshAuthHintPassword"); // "Password for SSH authentication. Leave blank to use Pageant or key-only auth."
```

Update `partial void OnSshKeyPathChanged` to also `OnPropertyChanged(nameof(SshAuthHint))`.

---

## What Was NOT Flagged (and why)

- **Plink silent fallback as a design choice** — flagged only as a feedback issue, not the fallback itself. The fallback strategy (SSH.NET → Plink) is architecturally sound and was the main fix from the previous session.
- **"External" client choice (PuTTY vs ssh.exe vs Windows Terminal)** — not a UX finding. The implementation decision is out of scope; the finding is that External mode doesn't work at all.
- **Tab order between key path and password** — `TabIndex 11 → 12` is correct. Not flagged.
- **Gateway diagram aesthetics** — not flagged. Arrow caption readability is not measurable without user data.
- **Missing onboarding wizard** — feature request, not a UX defect.
- **Compression / X11 / Agent Forwarding discoverability** — these are expert options correctly placed in the Advanced section. Not flagged.
