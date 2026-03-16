/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Heimdall.Core.Logging;

namespace Heimdall.Rdp;

/// <summary>
/// Detects RDP credential dialog (CredUI) windows and auto-fills the password.
/// Uses EnumWindows to find secondary CredUI dialogs that are invisible to
/// Process.MainWindowTitle (which returns only one window per process).
/// </summary>
public static partial class CredentialAutofill
{
    #region Win32 P/Invoke

    private delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);
    private delegate bool EnumChildWindowsDelegate(IntPtr hWnd, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumChildWindows(IntPtr hWndParent, EnumChildWindowsDelegate lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(IntPtr hWnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassNameW(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageString(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    private const uint WM_SETTEXT = 0x000C;
    private const uint BM_CLICK = 0x00F5;

    #endregion

    #region Configuration

    /// <summary>Default interval between window scans.</summary>
    private static readonly TimeSpan DefaultScanInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Default delay after bringing the credential dialog to the foreground.</summary>
    private static readonly TimeSpan DefaultFocusDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Known credential broker process names spawned by Windows for CredUI.</summary>
    private static readonly string[] CredentialBrokerProcessNames =
        ["CredentialUIBroker", "LogonUI", "consent"];

    /// <summary>Known top-level classes used by classic and modern CredUI hosts.</summary>
    private static readonly string[] CredentialDialogClassNames =
        ["Credential Dialog Xaml Host", "Windows Security", "#32770"];

    /// <summary>
    /// Regex pattern matching credential dialog titles across supported locales.
    /// Covers: "Windows Security" (EN), "Securite Windows" (FR), and mstsc prompts.
    /// </summary>
    private static readonly Regex TitlePattern = new(
        @"Windows Security|S[e\u00e9]curit[e\u00e9](\s+de)?\s+Windows|Credential|mstsc",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Regex pattern matching password fields in UI Automation trees.</summary>
    private static readonly Regex PasswordFieldPattern = new(
        @"Password|Mot de passe",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Regex pattern matching confirmation buttons in credential dialogs.</summary>
    private static readonly Regex OkButtonPattern = new(
        @"^(OK|&OK|Sign in|Connect|Se connecter)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    #endregion

    /// <summary>
    /// Polls for a CredUI credential dialog associated with the given mstsc process
    /// and fills in the password when found. The password is sent via WM_SETTEXT
    /// to the password edit control, then a Tab + Enter sequence submits the dialog.
    /// </summary>
    /// <param name="mstscProcessId">Process ID of the mstsc.exe instance.</param>
    /// <param name="targetHost">Hostname hint used to disambiguate multiple credential dialogs.</param>
    /// <param name="password">Cleartext password to inject.</param>
    /// <param name="timeout">Maximum time to wait for the credential dialog to appear.</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>True if the password was successfully injected, false if timed out or cancelled.</returns>
    public static async Task<bool> WaitAndFillAsync(
        int mstscProcessId,
        string targetHost,
        string password,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var hostHintPattern = string.IsNullOrWhiteSpace(targetHost)
            ? null
            : new Regex(Regex.Escape(targetHost), RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var passwordBuffer = password.ToCharArray();

        var deadline = DateTime.UtcNow + timeout;
        var scan = 0;

        FileLogger.Info(
            $"CredentialAutofill started: pid={mstscProcessId} hostHint={targetHost} timeout={timeout.TotalSeconds:0}s interval={DefaultScanInterval.TotalMilliseconds:0}ms");

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scan++;

                var target = FindCredentialDialog(mstscProcessId, hostHintPattern, scan);
                if (target is not null)
                {
                    FileLogger.Info(
                        $"CredentialAutofill dialog candidate found on scan {scan}: handle=0x{target.Value.Handle.ToInt64():X} class={target.Value.ClassName} title='{target.Value.Title}' pid={target.Value.ProcessId} process={target.Value.ProcessName}");

                    // Allow the dialog to fully render.
                    await Task.Delay(DefaultFocusDelay, cancellationToken).ConfigureAwait(false);

                    if (InjectPassword(target.Value, passwordBuffer))
                    {
                        FileLogger.Info($"CredentialAutofill succeeded on scan {scan}");
                        return true;
                    }

                    FileLogger.Warn(
                        $"CredentialAutofill injection attempt failed on scan {scan}; continuing to watch for CredUI.");
                }

                await Task.Delay(DefaultScanInterval, cancellationToken).ConfigureAwait(false);
            }

            FileLogger.Warn($"CredentialAutofill timed out after {scan} scans.");
            return false;
        }
        finally
        {
            Array.Clear(passwordBuffer, 0, passwordBuffer.Length);
        }
    }

    /// <summary>
    /// Scans all visible top-level windows via EnumWindows to find a CredUI dialog
    /// matching the title pattern and owned by the target process or a known credential broker.
    /// </summary>
    private static WindowInfo? FindCredentialDialog(int mstscProcessId, Regex? hostHintPattern, int scan)
    {
        var windows = GetVisibleWindows();
        FileLogger.Info($"CredentialAutofill scan {scan}: visibleWindows={windows.Count}");

        var candidates = windows.Where(IsCredentialDialogCandidate).ToList();
        if (candidates.Count == 0)
        {
            FileLogger.Info($"CredentialAutofill scan {scan}: no dialog candidates.");
            return null;
        }

        foreach (var candidate in candidates.Take(6))
        {
            FileLogger.Info(
                $"CredentialAutofill scan {scan}: candidate handle=0x{candidate.Handle.ToInt64():X} class={candidate.ClassName} title='{candidate.Title}' pid={candidate.ProcessId} process={candidate.ProcessName}");
        }

        // Prefer windows owned by the target mstsc process.
        var ownedByTarget = candidates.Where(w => w.ProcessId == mstscProcessId).ToList();
        if (ownedByTarget.Count > 0)
        {
            return SelectBestMatch(ownedByTarget, hostHintPattern);
        }

        // Fall back to known credential broker processes.
        var brokerMatches = candidates.Where(w => IsCredentialBroker(w.ProcessId, w.ProcessName)).ToList();
        if (brokerMatches.Count > 0)
        {
            return SelectBestMatch(brokerMatches, hostHintPattern);
        }

        FileLogger.Warn(
            $"CredentialAutofill scan {scan}: candidates found but none matched target pid {mstscProcessId} or known brokers.");
        return null;
    }

    /// <summary>
    /// Selects the best matching window handle, preferring one whose title contains
    /// the host hint if available.
    /// </summary>
    private static WindowInfo SelectBestMatch(List<WindowInfo> candidates, Regex? hostHintPattern)
    {
        if (hostHintPattern is not null)
        {
            var hostMatch = candidates.FirstOrDefault(w => hostHintPattern.IsMatch(w.Title));
            if (hostMatch.Handle != IntPtr.Zero)
            {
                return hostMatch;
            }
        }

        var foregroundHandle = GetForegroundWindow();
        var foregroundMatch = candidates.FirstOrDefault(w => w.Handle == foregroundHandle);
        if (foregroundMatch.Handle != IntPtr.Zero)
        {
            return foregroundMatch;
        }

        var xamlHost = candidates.FirstOrDefault(w =>
            w.ClassName.Contains("Credential Dialog Xaml Host", StringComparison.OrdinalIgnoreCase));
        if (xamlHost.Handle != IntPtr.Zero)
        {
            return xamlHost;
        }

        return candidates[0];
    }

    /// <summary>
    /// Checks whether the given process ID belongs to a known credential broker.
    /// </summary>
    private static bool IsCredentialBroker(int processId, string? processName = null)
    {
        try
        {
            var name = processName;
            if (string.IsNullOrWhiteSpace(name))
            {
                using var proc = System.Diagnostics.Process.GetProcessById(processId);
                name = proc.ProcessName;
            }

            return CredentialBrokerProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether a top-level window matches known CredUI classes or titles.
    /// </summary>
    private static bool IsCredentialDialogCandidate(WindowInfo window)
    {
        if (TitlePattern.IsMatch(window.Title))
        {
            return true;
        }

        return CredentialDialogClassNames.Any(className =>
            window.ClassName.Contains(className, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Injects the password into the credential dialog by:
    /// 1. Bringing the dialog to the foreground
    /// 2. Finding the password Edit control
    /// 3. Sending WM_SETTEXT with the password
    /// </summary>
    private static bool InjectPassword(WindowInfo dialog, char[] passwordBuffer)
    {
        FileLogger.Info(
            $"CredentialAutofill injecting: handle=0x{dialog.Handle.ToInt64():X} class={dialog.ClassName} title='{dialog.Title}'");

        SetForegroundWindow(dialog.Handle);
        var foregroundHandle = GetForegroundWindow();
        FileLogger.Info(
            $"CredentialAutofill foreground after SetForegroundWindow: requested=0x{dialog.Handle.ToInt64():X} actual=0x{foregroundHandle.ToInt64():X}");

        if (TryInjectPasswordViaAutomation(dialog, passwordBuffer))
        {
            return true;
        }

        FileLogger.Warn(
            $"CredentialAutofill UI Automation path failed for handle=0x{dialog.Handle.ToInt64():X}; trying Win32 fallback.");
        return TryInjectPasswordViaWin32(dialog, passwordBuffer);
    }

    /// <summary>
    /// Uses UI Automation for modern XAML-hosted CredUI dialogs and as a first choice
    /// for classic dialogs when the automation tree is available.
    /// </summary>
    private static bool TryInjectPasswordViaAutomation(WindowInfo dialog, char[] passwordBuffer)
    {
        try
        {
            var root = AutomationElement.FromHandle(dialog.Handle);
            if (root is null)
            {
                FileLogger.Warn("CredentialAutofill UIA root was null.");
                return false;
            }

            var passwordElement = FindPasswordElement(root);
            if (passwordElement is null)
            {
                FileLogger.Warn("CredentialAutofill UIA could not locate a password field.");
                return false;
            }

            var passwordText = new string(passwordBuffer);
            if (!TrySetAutomationValue(passwordElement, passwordText))
            {
                FileLogger.Warn("CredentialAutofill UIA could not set the password field value.");
                return false;
            }

            var okButton = FindOkButtonElement(root);
            if (okButton is null)
            {
                FileLogger.Warn("CredentialAutofill UIA could not locate the confirmation button.");
                return false;
            }

            if (!TryInvokeAutomationButton(okButton))
            {
                FileLogger.Warn("CredentialAutofill UIA could not invoke the confirmation button.");
                return false;
            }

            FileLogger.Info("CredentialAutofill UIA injection succeeded.");
            return true;
        }
        catch (ElementNotAvailableException ex)
        {
            FileLogger.Warn($"CredentialAutofill UIA element disappeared: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"CredentialAutofill UIA failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Falls back to recursive Win32 child enumeration for classic dialogs where direct
    /// descendant Edit/Button handles are exposed.
    /// </summary>
    private static bool TryInjectPasswordViaWin32(WindowInfo dialog, char[] passwordBuffer)
    {
        var editControls = GetDescendantWindows(dialog.Handle)
            .Where(w => string.Equals(w.ClassName, "Edit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        FileLogger.Info($"CredentialAutofill Win32 found {editControls.Count} descendant Edit controls.");

        if (editControls.Count == 0)
        {
            return false;
        }

        var passwordEdit = editControls.Count >= 2 ? editControls[1].Handle : editControls[0].Handle;
        var passwordText = new string(passwordBuffer);
        SendMessageString(passwordEdit, WM_SETTEXT, IntPtr.Zero, passwordText);
        FileLogger.Info($"CredentialAutofill Win32 password text sent to handle=0x{passwordEdit.ToInt64():X}");

        var buttons = GetDescendantWindows(dialog.Handle)
            .Where(w => string.Equals(w.ClassName, "Button", StringComparison.OrdinalIgnoreCase))
            .ToList();

        FileLogger.Info($"CredentialAutofill Win32 found {buttons.Count} descendant Button controls.");

        if (buttons.Count == 0)
        {
            return true;
        }

        var targetButton = buttons.FirstOrDefault(w => OkButtonPattern.IsMatch(w.Title));
        if (targetButton.Handle == IntPtr.Zero)
        {
            targetButton = buttons.Count >= 2 ? buttons[1] : buttons[0];
        }

        SendMessage(targetButton.Handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        FileLogger.Info(
            $"CredentialAutofill Win32 clicked button handle=0x{targetButton.Handle.ToInt64():X} title='{targetButton.Title}'");
        return true;
    }

    private static AutomationElement? FindPasswordElement(AutomationElement root)
    {
        var edits = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

        FileLogger.Info($"CredentialAutofill UIA discovered {edits.Count} Edit elements.");

        AutomationElement? namedPassword = null;
        AutomationElement? passwordFlagMatch = null;
        AutomationElement? firstEnabled = null;

        for (var index = 0; index < edits.Count; index++)
        {
            var element = edits[index];
            var name = SafeAutomationName(element);
            var automationId = SafeAutomationId(element);
            var isEnabled = SafeAutomationBool(element, AutomationElement.IsEnabledProperty);
            var isPassword = SafeAutomationBool(element, AutomationElement.IsPasswordProperty);

            FileLogger.Info(
                $"CredentialAutofill UIA edit[{index}]: name='{name}' automationId='{automationId}' enabled={isEnabled} isPassword={isPassword}");

            if (!isEnabled)
            {
                continue;
            }

            if (firstEnabled is null)
            {
                firstEnabled = element;
            }

            if (isPassword && passwordFlagMatch is null)
            {
                passwordFlagMatch = element;
            }

            if (namedPassword is null && PasswordFieldPattern.IsMatch(name))
            {
                namedPassword = element;
            }
        }

        return passwordFlagMatch ?? namedPassword ?? firstEnabled;
    }

    private static AutomationElement? FindOkButtonElement(AutomationElement root)
    {
        var buttons = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

        FileLogger.Info($"CredentialAutofill UIA discovered {buttons.Count} Button elements.");

        AutomationElement? firstEnabled = null;

        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            var name = SafeAutomationName(button);
            var automationId = SafeAutomationId(button);
            var isEnabled = SafeAutomationBool(button, AutomationElement.IsEnabledProperty);

            FileLogger.Info(
                $"CredentialAutofill UIA button[{index}]: name='{name}' automationId='{automationId}' enabled={isEnabled}");

            if (!isEnabled)
            {
                continue;
            }

            if (firstEnabled is null)
            {
                firstEnabled = button;
            }

            if (OkButtonPattern.IsMatch(name))
            {
                return button;
            }
        }

        return firstEnabled;
    }

    private static bool TrySetAutomationValue(AutomationElement element, string value)
    {
        try
        {
            element.SetFocus();
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"CredentialAutofill UIA SetFocus failed: {ex.Message}");
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject)
            && valuePatternObject is ValuePattern valuePattern)
        {
            valuePattern.SetValue(value);
            FileLogger.Info("CredentialAutofill UIA password set via ValuePattern.");
            return true;
        }

        var nativeHandle = element.Current.NativeWindowHandle;
        if (nativeHandle != 0)
        {
            SendMessageString(new IntPtr(nativeHandle), WM_SETTEXT, IntPtr.Zero, value);
            FileLogger.Info($"CredentialAutofill UIA password set via native handle 0x{nativeHandle:X}.");
            return true;
        }

        return false;
    }

    private static bool TryInvokeAutomationButton(AutomationElement button)
    {
        try
        {
            button.SetFocus();
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"CredentialAutofill UIA button SetFocus failed: {ex.Message}");
        }

        if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject)
            && invokeObject is InvokePattern invokePattern)
        {
            invokePattern.Invoke();
            FileLogger.Info("CredentialAutofill UIA confirmation invoked via InvokePattern.");
            return true;
        }

        var nativeHandle = button.Current.NativeWindowHandle;
        if (nativeHandle != 0)
        {
            SendMessage(new IntPtr(nativeHandle), BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            FileLogger.Info($"CredentialAutofill UIA confirmation invoked via native handle 0x{nativeHandle:X}.");
            return true;
        }

        return false;
    }

    #region Window Enumeration

    private readonly record struct WindowInfo(
        IntPtr Handle,
        string Title,
        string ClassName,
        int ProcessId,
        string ProcessName);

    /// <summary>
    /// Enumerates ALL visible top-level windows with non-empty titles via EnumWindows.
    /// Unlike Process.MainWindowTitle which returns only one window per process,
    /// this finds secondary windows such as credential dialogs spawned by ActiveX controls.
    /// </summary>
    private static List<WindowInfo> GetVisibleWindows()
    {
        var result = new List<WindowInfo>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out var pid);
            var title = GetWindowText(hWnd);
            var className = GetClassName(hWnd);
            var processName = GetProcessName(pid);

            if (string.IsNullOrWhiteSpace(title)
                && !CredentialDialogClassNames.Any(classNameHint =>
                    className.Contains(classNameHint, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            result.Add(new WindowInfo(hWnd, title, className, pid, processName));

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static List<WindowInfo> GetDescendantWindows(IntPtr parentHandle)
    {
        var result = new List<WindowInfo>();

        EnumChildWindows(parentHandle, (hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            result.Add(new WindowInfo(
                hWnd,
                GetWindowText(hWnd),
                GetClassName(hWnd),
                pid,
                GetProcessName(pid)));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static string GetWindowText(IntPtr hWnd)
    {
        var len = GetWindowTextLengthW(hWnd);
        if (len <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[len + 1];
        GetWindowTextW(hWnd, buffer, buffer.Length);
        return new string(buffer, 0, len);
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var buffer = new char[256];
        var len = GetClassNameW(hWnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : string.Empty;
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeAutomationName(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeAutomationId(AutomationElement element)
    {
        try
        {
            return element.Current.AutomationId ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool SafeAutomationBool(AutomationElement element, AutomationProperty property)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(property, true);
            return value is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
