<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->
# Heimdall.Next — Troubleshooting Guide

Index of all issues encountered during development and their solutions.

## Table of Contents

1. [RDP Embedded — White Screen](#rdp-embedded-white-screen)
2. [RDP Embedded — Disconnect Code 4360](#rdp-embedded-disconnect-code-4360)
3. [RDP Embedded — COM RCW Crash on Tab Close](#rdp-embedded-com-rcw-crash-on-tab-close)
4. [RDP Embedded — Resize HRESULT Error](#rdp-embedded-resize-hresult-error)
5. [RDP CredUI Autofill — Dialog Not Detected](#rdp-credui-autofill-dialog-not-detected)
6. [SSH — Pageant Keys Not Recognized by SSH.NET](#ssh-pageant-keys-not-recognized)
7. [SSH Terminal — Arrow Keys Not Working](#ssh-terminal-arrow-keys-not-working)
8. [SSH Terminal — No Colors, Strange Characters](#ssh-terminal-no-colors-strange-characters)
9. [SSH Terminal — Cursor Blinks Too Fast](#ssh-terminal-cursor-blinks-too-fast)
10. [WebView2 — DLL Not Found](#webview2-dll-not-found)
11. [Tab Navigation — Tabs Blocked by Active Sessions](#tab-navigation-blocked-by-sessions)
12. [WPF — DynamicResource in BasedOn](#wpf-dynamicresource-in-basedon)
13. [WPF — XAML Click Handler in Style Setter](#wpf-xaml-click-handler-in-style-setter)
14. [Build — Version Number Overflow](#build-version-number-overflow)
15. [Build — Ambiguous Type References](#build-ambiguous-type-references)
16. [TOFU — HostKeyFingerprint on PSCustomObject](#tofu-hostkeyfingerprint-on-pscustomobject)
17. [Passwords — Not Saved After Edit](#passwords-not-saved-after-edit)

---

## RDP Embedded — White Screen

**Symptom**: RDP ActiveX control connects (OnConnected fires) but the display area stays white.

**Root Cause**: WPF `WindowsFormsHost` airspace problem. The ActiveX control's rendering surface is not properly bound to the visible HWND because WPF hasn't flushed its layout pipeline before `Connect()` is called.

**Solution**: Apply the proven layout flush pattern before AND after `Connect()`:
```csharp
// Before Connect()
FormsHost.UpdateLayout();
SurfaceContainer.UpdateLayout();
WinForms.Application.DoEvents();
Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));

// EnsureHostHandle — force handle creation
if (!_rdpHost.IsHandleCreated) { _ = _rdpHost.Handle; }

// After Connect()
FormsHost.UpdateLayout();
WinForms.Application.DoEvents();
```

**Key Insight**: The `FormsHost` MUST be in the visible visual tree with a valid size BEFORE `Connect()`. Use retry loop if surface is not ready (up to 10 attempts, 120ms intervals).

**Files**: `EmbeddedRdpView.xaml.cs` — `FlushLayoutPipeline()`, `BeginConnect()`

---

## RDP Embedded — Disconnect Code 4360

**Symptom**: RDP connects then disconnects after a few seconds with reason code 4360.

**Root Cause**: Code 4360 means "session disconnected" — can be caused by:
1. `UpdateResolution()` called too early after `Connect()` crashes the COM object
2. Server-side licensing or policy issues
3. Dynamic resolution resize during the initial connection handshake

**Solution**: Block `UpdateResolution` calls for 5 seconds after `OnConnected` fires. Skip identical-size updates.

```csharp
_allowResolutionUpdates = false;
// In OnConnected:
_connectedAtUtc = DateTime.UtcNow;
// Enable after 5 second delay:
await Task.Delay(TimeSpan.FromSeconds(5));
_allowResolutionUpdates = true;
```

**Files**: `EmbeddedRdpView.xaml.cs` — `EnableResolutionUpdatesAsync()`

---

## RDP Embedded — COM RCW Crash on Tab Close

**Symptom**: `COM object that has been separated from its underlying RCW cannot be used` when closing a session tab.

**Root Cause**: WPF's `ArrangeOverride` tries to resize the ActiveX control AFTER it's been disposed. The `WindowsFormsHost` still references the COM object during layout.

**Solution**: Hide the `FormsHost` and remove its child BEFORE disposing the COM object:
```csharp
// CRITICAL ORDER:
FormsHost.Visibility = Visibility.Collapsed;  // Stop layout
FormsHost.Child = null;                       // Remove COM from tree
_rdpHost.Disconnect();                        // Then disconnect
_rdpHost.DetachEventSink();                   // Remove event sink
_rdpHost.Dispose();                           // Finally dispose
```

**Files**: `EmbeddedRdpView.xaml.cs` — `Dispose()`

---

## RDP Embedded — Resize HRESULT Error

**Symptom**: `Unexpected HRESULT has been returned from a call to a COM component` during resize.

**Root Cause**: `SetDisplay()` or `UpdateResolution()` called while the RDP session is in a connecting state (not yet fully connected).

**Solution**: Only call `UpdateResolution` when `IsConnected == true` AND after the stabilization delay.

**Files**: `EmbeddedRdpView.xaml.cs` — `OnResizeTimerTick()`

---

## RDP CredUI Autofill — Dialog Not Detected

**Symptom**: CredUI autofill scans find only 8 top-level windows, never detecting the "Windows Security" credential dialog.

**Root Cause**: The CredUI dialog from an embedded ActiveX control is NOT a top-level window — it's a child/owned window spawned by the RDP control's thread. `EnumWindows` only finds top-level windows.

**Solution**: In addition to `EnumWindows`, also scan all threads of the current process with `EnumThreadWindows`:
```csharp
foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
{
    EnumThreadWindows((uint)thread.Id, callback, IntPtr.Zero);
}
```

Also use UI Automation (`System.Windows.Automation`) for modern XAML-based CredUI dialogs, with Win32 `SendMessage`/`BM_CLICK` as fallback for classic dialogs.

**Files**: `CredentialAutofill.cs` — `GetVisibleWindows()`, `InjectPassword()`

---

## SSH — Pageant Keys Not Recognized

**Symptom**: `Server rejected the SSH key` or `No suitable authentication method found` even with Pageant running and keys loaded.

**Root Cause**: SSH.NET 2025.1.0 does not have built-in Pageant agent support. The `NoneAuthenticationMethod` fallback doesn't trigger Pageant negotiation.

**Solution**: Two-pronged approach:
1. **Pageant IPC client**: Custom `PageantClient` communicates with Pageant via Win32 shared memory (`CreateFileMapping` + `WM_COPYDATA`). Wraps keys as `IPrivateKeySource` for SSH.NET via `PageantKeyWrapper` + `PageantHostAlgorithm`.
2. **Plink fallback**: When `RequiresPageantFallback()` detects Pageant-only auth, use `PlinkTunnelRunner` for tunnels and `PipeModeSession` for interactive SSH. Plink communicates with Pageant natively.

**Files**: `Pageant/PageantClient.cs`, `SshConnectionFactory.cs`, `ConnectionService.cs`

---

## SSH Terminal — Arrow Keys Not Working

**Symptom**: Arrow keys don't navigate command history in bash. Pressing Up shows `^[[A` instead.

**Root Cause**: ConPTY (`CreatePseudoConsole`) converts VT input sequences to Windows console key events, then reconverts back. This double-conversion breaks arrow key escape sequences.

**Solution**: Use **pipe mode** (NOT ConPTY) for SSH terminals. `PipeModeSession` redirects stdin/stdout directly without a pseudo-console. Combined with plink's `-t` flag (forces remote PTY allocation), VT sequences pass through raw.

```
xterm.js → ESC[A → stdin pipe → plink -t → remote PTY → bash
bash → ESC[A response → stdout pipe → xterm.js
```

**Key Rule**: NEVER use ConPTY for SSH terminals that go through plink. ConPTY is for local shells only.

**Files**: `PipeModeSession.cs`, `ConnectionService.cs` — `ConnectSshViaPlinkAsync()`

---

## SSH Terminal — No Colors, Strange Characters

**Symptom**: Terminal shows raw ANSI escape codes like `[?2004h`, `[0;32m` instead of colors. No cursor.

**Root Cause**: Initial implementation used a WPF `TextBlock` with ANSI stripping. A TextBlock cannot render terminal escape sequences.

**Solution**: Replace with **WebView2 + xterm.js** — the industry standard terminal renderer:
- xterm.js handles ALL VT100/xterm rendering (colors, cursor, scrollback, mouse)
- Binary-safe base64 data transfer between process and xterm.js
- `PostWebMessageAsString` for C# → JS, `WebMessageReceived` for JS → C#

**Files**: `EmbeddedSshView.xaml`, `EmbeddedSshView.xaml.cs`, `Assets/terminal.html`

---

## SSH Terminal — Cursor Blinks Too Fast

**Symptom**: The xterm.js cursor blinks extremely rapidly, much faster than normal.

**Root Cause**: WPF and WebView2 fighting over focus. `GotFocus` and `PreviewMouseDown` handlers on the UserControl call `FocusTerminal()` which sets focus to WebView2, which triggers WPF `LostFocus`, which triggers `GotFocus` again → infinite focus loop.

**Solution**:
1. Remove `GotFocus` and `PreviewMouseDown` handlers
2. Apply focus only ONCE after xterm.js sends the `ready:` message
3. Slow cursor blink via CSS: `animation-duration: 1.2s`

**Files**: `EmbeddedSshView.xaml.cs`, `Assets/terminal.html`

---

## WebView2 — DLL Not Found

**Symptom**: `Unable to load DLL 'WebView2Loader.dll'` at runtime.

**Root Cause**: `WebView2Loader.dll` is a native (non-managed) DLL that `dotnet publish` doesn't copy to the output directory. It gets placed in `lib/webview2/` subfolder instead of alongside the exe.

**Solution**: Copy it explicitly in `Build.ps1` after publish:
```powershell
Copy-Item "src\Heimdall.App\lib\webview2\WebView2Loader.dll" $outputDir -Force
```

**Files**: `Build.ps1`

---

## Tab Navigation — Tabs Blocked by Active Sessions

**Symptom**: When an SSH or RDP session is open, clicking Tunnels/Scheduled/Settings tabs does nothing.

**Root Cause**: Multiple layout architectures were tried:
1. **Sessions as global overlay** (`Panel.ZIndex=10`): Blocks all tabs underneath
2. **Sessions in separate Grid**: Sessions hidden when switching tabs but not restored

**Solution** (from Gemini architecture audit): Sessions live INSIDE the Servers Grid Column 2. When switching tabs, the entire Servers Grid is hidden (`Visibility=Collapsed`) — sessions are NOT destroyed, just visually suspended. Returning to Servers restores them.

Additional fixes:
- Toolbar `Panel.ZIndex=100` ensures clicks reach RadioButtons above WebView2
- `ClipToBounds=True` on content Grid prevents WebView2 overflow
- Focus management: no focus cycling between WPF and WebView2

**Key Rule**: Sessions are children of the Servers tab, never a global overlay.

**Files**: `MainWindow.xaml`, `MainWindow.xaml.cs`

---

## WPF — DynamicResource in BasedOn

**Symptom**: `A 'DynamicResourceExtension' cannot be set on the 'BasedOn' property of type 'Style'`

**Root Cause**: WPF limitation — `BasedOn` only accepts `StaticResource`, not `DynamicResource`.

**Solution**: Replace `BasedOn="{DynamicResource ...}"` with `BasedOn="{StaticResource ...}"`.

---

## WPF — XAML Click Handler in Style Setter

**Symptom**: `Set connectionId threw an exception` when loading a window with a ContextMenu defined inside a Style Setter that uses `Click` event handlers.

**Root Cause**: WPF cannot resolve event handlers in XAML when the ContextMenu is defined inside a `<Setter.Value>` — the handler method is not in scope.

**Solution**: Build the ContextMenu programmatically in code-behind instead of XAML.

**Files**: `MainWindow.xaml.cs` — `OnSessionTabRightClick()`

---

## Build — Version Number Overflow

**Symptom**: `Arithmetic operation resulted in an overflow` during Win32 resource generation.

**Root Cause**: `<Version>2026.031614</Version>` — the segment `031614` exceeds the Win32 version field limit of 65535.

**Solution**: Use separate version properties:
- `<Version>1.0.MMDD.xx</Version>` for Win32 compatibility (AssemblyVersion)
- `<InformationalVersion>YYYY.MMDDxx</InformationalVersion>` for display

**Files**: `Build.ps1`, `Heimdall.App.csproj`

---

## Build — Ambiguous Type References

**Symptom**: `'Point' is an ambiguous reference between 'System.Drawing.Point' and 'System.Windows.Point'`

**Root Cause**: `UseWindowsForms=true` in the csproj imports System.Drawing types alongside System.Windows types.

**Solution**: Fully qualify ambiguous types: `System.Windows.Point`, `System.Windows.DataObject`.

---

## TOFU — HostKeyFingerprint on PSCustomObject

**Symptom**: `The property 'HostKeyFingerprint' cannot be found on this object` (Heimdall v1 legacy issue).

**Root Cause**: Gateway/server objects from JSON deserialization are `PSCustomObject`, not C# class instances. New properties added to the C# model don't exist on deserialized objects.

**Solution**: Add `Add-Member` guard before setting:
```powershell
if (-not $gateway.PSObject.Properties['HostKeyFingerprint']) {
    $gateway | Add-Member -MemberType NoteProperty -Name 'HostKeyFingerprint' -Value $null
}
$gateway.HostKeyFingerprint = $fingerprint
```

**Files**: `ConnectionManager.psm1`, `EmbeddedSsh.psm1`, `HeimdallSftpPanel.ps1`

---

## Passwords — Not Saved After Edit

**Symptom**: Password field appears empty when re-opening server edit dialog after saving.

**Root Cause**: `ServerDialogViewModel.ToDto()` didn't map `RdpPassword`/`SshPassword` to `RdpPasswordEncrypted`/`SshPasswordEncrypted` via DPAPI.

**Solution**:
1. Encrypt new passwords in `ToDto()`: `DpapiProvider.Protect(password)`
2. Preserve existing encrypted passwords on edit (if user didn't change the field)
3. Store `ExistingRdpPasswordEncrypted`/`ExistingSshPasswordEncrypted` in ViewModel

**Files**: `ServerDialogViewModel.cs`
