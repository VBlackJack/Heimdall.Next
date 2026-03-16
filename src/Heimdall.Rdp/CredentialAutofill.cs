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
using System.Text;
using System.Text.RegularExpressions;

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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageString(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint BM_CLICK = 0x00F5;
    private const int VK_TAB = 0x09;

    #endregion

    #region Configuration

    /// <summary>Default interval between window scans.</summary>
    private static readonly TimeSpan DefaultScanInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Default delay after bringing the credential dialog to the foreground.</summary>
    private static readonly TimeSpan DefaultFocusDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Known credential broker process names spawned by Windows for CredUI.</summary>
    private static readonly string[] CredentialBrokerProcessNames =
        ["CredentialUIBroker", "LogonUI", "consent"];

    /// <summary>
    /// Regex pattern matching credential dialog titles across supported locales.
    /// Covers: "Windows Security" (EN), "Securite Windows" (FR), and mstsc prompts.
    /// </summary>
    private static readonly Regex TitlePattern = new(
        @"Windows Security|S[e\u00e9]curit[e\u00e9](\s+de)?\s+Windows|Credential|mstsc",
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

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = FindCredentialDialog(mstscProcessId, hostHintPattern);
            if (target is not null)
            {
                // Allow the dialog to fully render
                await Task.Delay(DefaultFocusDelay, cancellationToken);

                return InjectPassword(target.Value, password);
            }

            await Task.Delay(DefaultScanInterval, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Scans all visible top-level windows via EnumWindows to find a CredUI dialog
    /// matching the title pattern and owned by the target process or a known credential broker.
    /// </summary>
    private static IntPtr? FindCredentialDialog(int mstscProcessId, Regex? hostHintPattern)
    {
        var windows = GetVisibleWindows();

        // Filter by title pattern
        var titleMatches = windows.Where(w => TitlePattern.IsMatch(w.Title)).ToList();
        if (titleMatches.Count == 0)
        {
            return null;
        }

        // Prefer windows owned by the target mstsc process
        var ownedByTarget = titleMatches.Where(w => w.ProcessId == mstscProcessId).ToList();
        if (ownedByTarget.Count > 0)
        {
            return SelectBestMatch(ownedByTarget, hostHintPattern);
        }

        // Fall back to known credential broker processes
        var brokerMatches = titleMatches.Where(w => IsCredentialBroker(w.ProcessId)).ToList();
        if (brokerMatches.Count > 0)
        {
            return SelectBestMatch(brokerMatches, hostHintPattern);
        }

        return null;
    }

    /// <summary>
    /// Selects the best matching window handle, preferring one whose title contains
    /// the host hint if available.
    /// </summary>
    private static IntPtr SelectBestMatch(List<WindowInfo> candidates, Regex? hostHintPattern)
    {
        if (hostHintPattern is not null)
        {
            var hostMatch = candidates.FirstOrDefault(w => hostHintPattern.IsMatch(w.Title));
            if (hostMatch.Handle != IntPtr.Zero)
            {
                return hostMatch.Handle;
            }
        }

        return candidates[0].Handle;
    }

    /// <summary>
    /// Checks whether the given process ID belongs to a known credential broker.
    /// </summary>
    private static bool IsCredentialBroker(int processId)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(processId);
            return CredentialBrokerProcessNames.Contains(proc.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Injects the password into the credential dialog by:
    /// 1. Bringing the dialog to the foreground
    /// 2. Finding the password Edit control
    /// 3. Sending WM_SETTEXT with the password
    /// </summary>
    private static bool InjectPassword(IntPtr dialogHandle, string password)
    {
        // Bring the dialog to the foreground
        SetForegroundWindow(dialogHandle);

        // Find the password edit control — typically the second Edit child
        // First Edit is usually the username field
        var firstEdit = FindWindowExW(dialogHandle, IntPtr.Zero, "Edit", null);
        if (firstEdit == IntPtr.Zero)
        {
            // Some CredUI dialogs use DirectUIHWND with nested edits
            return false;
        }

        // The password field is the next Edit control after the username
        var passwordEdit = FindWindowExW(dialogHandle, firstEdit, "Edit", null);
        if (passwordEdit == IntPtr.Zero)
        {
            // Only one Edit found — it may be the password field itself (username pre-filled)
            passwordEdit = firstEdit;
        }

        // Inject the password via WM_SETTEXT
        SendMessageString(passwordEdit, WM_SETTEXT, IntPtr.Zero, password);

        // Press Tab to move focus and then click the OK/Submit button
        var submitButton = FindWindowExW(dialogHandle, IntPtr.Zero, "Button", null);
        if (submitButton != IntPtr.Zero)
        {
            // Find the second button (first is usually "Use a different account" or similar)
            var nextButton = FindWindowExW(dialogHandle, submitButton, "Button", null);
            var targetButton = nextButton != IntPtr.Zero ? nextButton : submitButton;
            SendMessage(targetButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        return true;
    }

    #region Window Enumeration

    private readonly record struct WindowInfo(IntPtr Handle, string Title, int ProcessId);

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

            int len = GetWindowTextLengthW(hWnd);
            if (len <= 0)
            {
                return true;
            }

            var buffer = new char[len + 1];
            GetWindowTextW(hWnd, buffer, buffer.Length);
            var title = new string(buffer, 0, len);

            GetWindowThreadProcessId(hWnd, out var pid);
            result.Add(new WindowInfo(hWnd, title, pid));

            return true;
        }, IntPtr.Zero);

        return result;
    }

    #endregion
}
