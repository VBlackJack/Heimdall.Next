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
18. [Pageant — AGENT_COPYDATA_ID Wrong Value](#pageant-agent_copydata_id-wrong-value)
19. [Pageant — RSA-SHA2 Algorithm Registration](#pageant-rsa-sha2-algorithm-registration)
20. [Pageant — Sign() Returns Raw Bytes Instead of Blob](#pageant-sign-returns-raw-bytes-instead-of-blob)
21. [SFTP — CheckBox Fires During XAML Parse](#sftp-checkbox-fires-during-xaml-parse)
22. [SFTP — Context Menu Intercepted by MainWindow](#sftp-context-menu-intercepted-by-mainwindow)
23. [Citrix — storebrowse.exe Not Found](#citrix-storebrowseexe-not-found)
24. [Theme Switching — Light Theme Looks Wrong](#theme-switching-light-theme-looks-wrong)
25. [Multi-Exec — Broadcast Sends to Wrong Terminals](#multi-exec-broadcast-sends-to-wrong-terminals)
26. [RDP Embedded — Resize Flicker](#rdp-embedded-resize-flicker)
27. [VNC — noVNC Library Unavailable](#vnc-novnc-unavailable)
28. [VNC — WebSocket Proxy Port Conflict](#vnc-websocket-port-conflict)
29. [X11 Forwarding — No Display](#x11-no-display)
30. [Telnet — Connection Hangs](#telnet-connection-hangs)
31. [FTP — Passive Mode Failures](#ftp-passive-mode)
32. [Tab Detach — WebView2 Session Lost](#tab-detach-webview2)
33. [Ephemeral Server — Port 69 Access Denied](#tftp-port-access-denied)
26. [Quick Connect — Ad-Hoc SSH Fails](#quick-connect-ad-hoc-ssh-fails)
27. [RDP Resize — Still Reconnecting (Delta/Debounce Tuning)](#rdp-resize-still-reconnecting-deltadebounce-tuning)

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

---

## Pageant — AGENT_COPYDATA_ID Wrong Value

**Symptom**: `PageantClient` sends a request to Pageant but receives no response. Pageant appears running with keys loaded, but SSH.NET authentication fails with "no suitable method found".

**Root Cause**: The `COPYDATASTRUCT.dwData` field must be set to the exact value `0x804e50ba` (`AGENT_COPYDATA_ID`). Any other value causes Pageant to silently ignore the `WM_COPYDATA` message.

**Solution**: Ensure the constant is correct:
```csharp
private const uint AGENT_COPYDATA_ID = 0x804e50ba;
```

**Files**: `Pageant/PageantClient.cs`

---

## Pageant — RSA-SHA2 Algorithm Registration

**Symptom**: Pageant keys are loaded into SSH.NET but the server rejects authentication. Server logs show "no matching host key type found" or similar.

**Root Cause**: Modern SSH servers disable legacy `ssh-rsa` (SHA-1) and require `rsa-sha2-256` or `rsa-sha2-512`. SSH.NET does not automatically register these algorithms when using custom `IPrivateKeySource` implementations.

**Solution**: Register RSA-SHA2 algorithms on the `ConnectionInfo` before connecting:
```csharp
connectionInfo.HostKeyAlgorithms["rsa-sha2-256"] = ...;
connectionInfo.HostKeyAlgorithms["rsa-sha2-512"] = ...;
```

**Files**: `SshConnectionFactory.cs`, `Pageant/PageantHostAlgorithm.cs`

---

## Pageant — Sign() Returns Raw Bytes Instead of Blob

**Symptom**: SSH authentication starts (key is offered) but fails during the signature exchange. Server rejects the signature.

**Root Cause**: `PageantHostAlgorithm.Sign()` was returning just the raw signature bytes from Pageant. SSH.NET expects the full SSH wire-format signature blob: `[4 bytes: algorithm name length][algorithm name][4 bytes: signature length][signature bytes]`.

**Solution**: Wrap the raw signature in the SSH blob format:
```csharp
// Build SSH signature blob: algo_len + algo + sig_len + sig
byte[] algoBytes = Encoding.ASCII.GetBytes(algorithmName);
// ... assemble full blob
```

**Files**: `Pageant/PageantHostAlgorithm.cs`

---

## SFTP — CheckBox Fires During XAML Parse

**Symptom**: `NullReferenceException` on startup when `EmbeddedSftpView` is loaded, originating from a `CheckBox.Checked` event handler.

**Root Cause**: A XAML `CheckBox` with `IsChecked="True"` fires the `Checked` event during `InitializeComponent()`, before class fields and other controls are initialized.

**Solution**: Guard event handlers with null checks on fields that may not be initialized yet:
```csharp
private void OnShowHiddenChecked(object sender, RoutedEventArgs e)
{
    if (_sftpClient == null) return; // Not yet initialized
    RefreshDirectory();
}
```

**Files**: `EmbeddedSftpView.xaml.cs`

---

## SFTP — Context Menu Intercepted by MainWindow

**Symptom**: Right-clicking an SFTP session tab opens the generic session context menu (disconnect/close) instead of the SFTP-specific context menu, or causes unexpected behavior.

**Root Cause**: `MainWindow.OnSessionTabRightClick()` intercepts right-click on all session tabs and builds a generic context menu. SFTP tabs have their own context menu (with file operations), but the MainWindow handler fires first and overrides it.

**Solution**: In `OnSessionTabRightClick()`, check the session type and skip context menu creation for SFTP tabs:
```csharp
if (sessionTab.ConnectionType == ConnectionType.Sftp)
    return; // SFTP view handles its own context menu
```

**Files**: `MainWindow.xaml.cs` — `OnSessionTabRightClick()`

---

## Citrix — storebrowse.exe Not Found

**Symptom**: Citrix connection fails immediately with an error indicating `storebrowse.exe` cannot be located.

**Root Cause**: Citrix Workspace App is either not installed or installed in a non-standard location. The default detection path is `%ProgramFiles(x86)%\Citrix\ICA Client\SelfServicePlugin\storebrowse.exe`.

**Solution**:
1. Verify Citrix Workspace App is installed (download from Citrix)
2. If installed in a custom location, set the `CitrixStoreBrowsePath` in Settings > Paths to the full path of `storebrowse.exe`
3. Ensure the Citrix Workspace App version is current — older versions may lack the `storebrowse.exe` CLI

**Files**: `ConnectionService.Citrix.cs`

---

## Theme Switching — Light Theme Looks Wrong

**Symptom**: After switching to the Light theme at runtime, some controls appear with incorrect colors, missing borders, or unstyled backgrounds.

**Root Cause**: WPF `ResourceDictionary` merge order matters. If a custom control style uses `BasedOn="{StaticResource ...}"` referencing a key from `CommonControls.xaml`, and the theme dictionary is swapped AFTER the control is already loaded, the `StaticResource` reference is not re-evaluated (only `DynamicResource` responds to runtime changes).

**Solution**:
1. Ensure all theme-dependent styles in `CommonControls.xaml` use `DynamicResource` for color brushes
2. `BasedOn` must always use `StaticResource` (WPF limitation), but the referenced style itself should use `DynamicResource` for its brush properties
3. If a specific control still renders incorrectly after theme switch, force a visual tree refresh by toggling its `Visibility`

**Files**: `Themes/CommonControls.xaml`, `Themes/DarkTheme.xaml`, `Themes/LightTheme.xaml`, `Theming/WindowThemeHelper.cs`

---

## Multi-Exec — Broadcast Sends to Wrong Terminals

**Symptom**: Broadcast keystrokes appear in terminals that should not be receiving them, or do not appear in terminals that are opted in.

**Root Cause**: The broadcast subscription list is keyed by session ID. If a session tab is closed and a new one is opened, the old session ID may linger in the broadcast list, or the new session may not be registered.

**Solution**:
1. Verify each terminal's broadcast toggle state in the session toolbar (broadcast icon should be highlighted when active)
2. On session close, the `EmbeddedSessionManager` must remove the session from the broadcast subscriber list
3. On session open, broadcast opt-in defaults to OFF — the user must explicitly enable it
4. Check that `PostWebMessageAsString` targets the correct `WebView2` instance by session ID, not by tab index

**Files**: `EmbeddedSessionManager.cs`, `EmbeddedSshView.xaml.cs`

---

## Quick Connect — Ad-Hoc SSH Fails

**Symptom**: Quick Connect (Ctrl+K) opens the overlay and parses the connection string, but the SSH connection fails with authentication errors or "no suitable method found".

**Root Cause**: Ad-hoc connections via Quick Connect create a transient `ServerProfileDto` without saved credentials. If the target server requires key-based authentication and no default key is configured, the connection has no viable auth method.

**Solution**:
1. Ensure Pageant is running with the appropriate keys loaded (Quick Connect uses Pageant as the default auth method for SSH)
2. Alternatively, specify credentials in the connection string or configure a default SSH key in Settings > Authentication
3. For password-based auth, the Quick Connect parser supports `user:password@host` format (credentials are not persisted)
4. Check that `AuthPreflightChecker` has valid auth sources before the connection attempt

**Files**: `ConnectionService.Ssh.cs`, `QuickConnectOverlay.xaml.cs`

---

## RDP Resize — Still Reconnecting (Delta/Debounce Tuning)

**Symptom**: Resizing the application window causes the embedded RDP session to briefly disconnect and reconnect, producing a visible flicker or momentary black screen.

**Root Cause**: The `UpdateResolution()` call triggers an RDP reconnect when the resolution delta exceeds the threshold. If the debounce timer is too short or the delta threshold is too low, rapid window resizing causes repeated reconnects.

**Solution**:
1. Increase the resize debounce interval (default: 500ms). A value of 800-1000ms reduces reconnects during active resizing
2. Increase the minimum resolution delta threshold — small changes (under 50px in either dimension) should be skipped entirely
3. Ensure `_allowResolutionUpdates` is still gated by the 5-second post-connect stabilization guard
4. If the issue persists, check that `OnResizeTimerTick` compares against the LAST APPLIED resolution, not the last requested one

**Files**: `EmbeddedRdpView.xaml.cs` — `OnResizeTimerTick()`, `UpdateResolution()`

---

## 27. VNC — noVNC Library Unavailable {#vnc-novnc-unavailable}

**Symptom**: VNC tab shows "noVNC Library Unavailable" error instead of the remote desktop.

**Root Cause**: The noVNC JavaScript library is loaded from CDN (`cdn.jsdelivr.net`). In offline or network-restricted environments, the import fails.

**Solution**:
1. Ensure the machine has internet connectivity
2. For air-gapped environments, download noVNC from `https://github.com/novnc/noVNC/releases` and place files in `Assets/vnc/`. Update `vnc.html` to import from the local path instead of CDN
3. The app shows a clear error message with instructions when the CDN is unreachable

**Files**: `Assets/vnc.html`, `Views/EmbeddedVncView.xaml.cs`

---

## 28. VNC — WebSocket Proxy Port Conflict {#vnc-websocket-port-conflict}

**Symptom**: VNC connection fails with a port binding error.

**Root Cause**: The `WebSocketVncProxy` binds to a random local port. In rare cases, the port may already be in use.

**Solution**: Retry the connection — a new random port will be selected. If the issue persists, check for processes hogging ephemeral ports.

**Files**: `Services/WebSocketVncProxy.cs`

---

## 29. X11 Forwarding — No Display {#x11-no-display}

**Symptom**: X11 forwarded applications fail with "Cannot open display" or similar errors.

**Root Cause**: No X11 server (VcXsrv, Xming, X410) is installed or running on the Windows host.

**Solution**:
1. Install VcXsrv from `https://sourceforge.net/projects/vcxsrv/` or Xming
2. Heimdall auto-detects and auto-starts the X server when X11 forwarding is enabled
3. If auto-start fails, set the X server path manually in Settings > X11 Server Path
4. Verify the `DISPLAY` environment variable is set (Heimdall sets `localhost:0.0` automatically)

**Files**: `Services/X11ServerManager.cs`, `Services/ConnectionService.Ssh.cs`

---

## 30. Telnet — Connection Hangs {#telnet-connection-hangs}

**Symptom**: Telnet connection appears to connect but no prompt appears.

**Root Cause**: Some Telnet servers require specific IAC negotiation responses. Heimdall's Telnet implementation handles basic DO/WILL/DONT/WONT negotiation but may not satisfy all server requirements.

**Solution**:
1. Verify the target port is correct (default: 23)
2. Test with a standard Telnet client to confirm the server works
3. Some legacy devices may require specific terminal type negotiation not yet implemented

**Files**: `Terminal/TelnetSession.cs`

---

## 31. FTP — Passive Mode Failures {#ftp-passive-mode}

**Symptom**: FTP directory listing works but file transfers fail or time out.

**Root Cause**: FTP uses separate data connections. Passive mode (default) requires the server to open a port that the client connects to. Firewalls may block these ports.

**Solution**:
1. Ensure the FTP server's passive port range is accessible
2. The built-in .NET `FtpWebRequest` uses passive mode by default
3. For active mode requirements, this is a known limitation of the current implementation

**Files**: `Sftp/FtpBrowser.cs`

---

## 32. Tab Detach — WebView2 Session Lost {#tab-detach-webview2}

**Symptom**: After detaching an SSH tab to a floating window and re-docking it, the terminal may lose its WebView2 state.

**Root Cause**: WebView2 controls can behave unpredictably when re-parented between WPF visual trees. The control maintains its internal state but the rendering context may need re-initialization.

**Solution**:
1. If the terminal appears blank after re-docking, the session is still alive — try clicking in the terminal area
2. For RDP sessions, detach is one-way (ActiveX controls cannot be safely re-parented)
3. Split sessions cannot be detached (by design)

**Files**: `Views/FloatingSessionWindow.xaml.cs`, `MainWindow.xaml.cs`

---

## 33. Ephemeral Server — Port 69 Access Denied {#tftp-port-access-denied}

**Symptom**: TFTP server fails to start with "access denied" on port 69.

**Root Cause**: Ports below 1024 require elevated privileges on Windows.

**Solution**:
1. Run Heimdall as Administrator if TFTP is needed
2. The HTTP server (port 8080) works without elevation
3. For non-elevated usage, TFTP is not available (this is a Windows security restriction)

**Files**: `Services/EphemeralFileServer.cs`
