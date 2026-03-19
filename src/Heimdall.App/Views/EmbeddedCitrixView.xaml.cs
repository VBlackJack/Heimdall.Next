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

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Localization;
using WinForms = System.Windows.Forms;

namespace Heimdall.App.Views;

/// <summary>
/// Manages a Citrix Workspace session with two modes:
/// 1. Embedded mode: captures the wfica32.exe window via SetParent and hosts it inline.
/// 2. External mode (fallback): monitors the process and provides Bring to Front / Terminate controls.
/// </summary>
public partial class EmbeddedCitrixView : UserControl, IDisposable
{
    private const int HealthCheckIntervalMs = 3000;
    private const int WindowCaptureMaxAttempts = 60;
    private const int WindowCapturePollIntervalMs = 500;

    // Win32 window style flags
    private const int GwlStyle = -16;
    private const uint WsChild = 0x40000000;
    private const uint WsPopup = 0x80000000;
    private const uint WsCaption = 0x00C00000;
    private const uint WsThickframe = 0x00040000;
    private const int SwShowNormal = 1;
    private const int SwRestore = 9;
    private const int SwMaximize = 3;

    private CitrixSessionResult? _session;
    private SessionTabViewModel? _sessionTab;
    private LocalizationManager? _localizer;
    private DispatcherTimer? _healthTimer;
    private WinForms.Panel? _hostPanel;
    private IntPtr _capturedHwnd;
    private bool _embedded;
    private bool _captureInProgress;
    private bool _disposed;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public EmbeddedCitrixView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the view with a Citrix session result and attempts to embed the window.
    /// Falls back to external mode if window capture fails.
    /// </summary>
    public void InitializeSession(
        CitrixSessionResult session,
        SessionTabViewModel sessionTab,
        string displayName,
        LocalizationManager? localizer = null)
    {
        _session = session;
        _sessionTab = sessionTab;
        _localizer = localizer;

        TerminateButton.Content = localizer?["BtnTerminateSession"] ?? "Terminate";
        BringToFrontButton.Content = localizer?["BtnBringToFront"] ?? "Bring to Front";

        SessionTitleText.Text = displayName;
        TitleText.Text = displayName;
        UpdateStatus(true);

        _healthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(HealthCheckIntervalMs)
        };
        _healthTimer.Tick += OnHealthTimerTick;
        _healthTimer.Start();

        // Attempt to capture the Citrix window asynchronously
        _ = TryCaptureWindowAsync();
    }

    /// <summary>
    /// Populates the info panel with StoreFront URL and application name.
    /// </summary>
    public void SetConnectionInfo(string? storeFrontUrl, string? appName)
    {
        StoreFrontText.Text = !string.IsNullOrWhiteSpace(storeFrontUrl)
            ? $"StoreFront: {storeFrontUrl}"
            : string.Empty;

        AppNameText.Text = !string.IsNullOrWhiteSpace(appName)
            ? $"Application: {appName}"
            : string.Empty;
    }

    // Citrix process names to scan (covers different Workspace versions)
    private static readonly string[] CitrixProcessNames =
        ["wfica32", "wfcrun32", "CDViewer", "Receiver", "SelfService"];

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Polls for new Citrix windows and reparents the first viable one.
    /// Searches multiple process names and uses EnumWindows for robust detection.
    /// Falls back to external mode after timeout.
    /// </summary>
    private async Task TryCaptureWindowAsync()
    {
        _captureInProgress = true;
        Core.Logging.FileLogger.Info("Citrix: attempting to capture Citrix session window...");

        // Record existing Citrix PIDs before our launch
        var existingPids = new HashSet<int>();
        foreach (var name in CitrixProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                existingPids.Add(p.Id);
            }
        }

        Core.Logging.FileLogger.Info($"Citrix: {existingPids.Count} existing Citrix process(es) before launch");

        for (int attempt = 1; attempt <= WindowCaptureMaxAttempts; attempt++)
        {
            if (_disposed) return;

            await Task.Delay(WindowCapturePollIntervalMs);

            // Scan for new Citrix processes
            var newPids = new HashSet<int>();
            foreach (var name in CitrixProcessNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    if (!existingPids.Contains(p.Id) && !p.HasExited)
                    {
                        newPids.Add(p.Id);
                    }
                }
            }

            if (newPids.Count == 0) continue;

            if (attempt % 5 == 0)
            {
                Core.Logging.FileLogger.Info(
                    $"Citrix: scan {attempt}/{WindowCaptureMaxAttempts}, {newPids.Count} new Citrix process(es)");
            }

            // Use EnumWindows to find visible windows belonging to new Citrix processes
            IntPtr bestHwnd = IntPtr.Zero;
            int bestArea = 0;

            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;

                GetWindowThreadProcessId(hwnd, out var pid);
                if (!newPids.Contains((int)pid)) return true;

                var className = new System.Text.StringBuilder(256);
                GetClassName(hwnd, className, 256);
                var cName = className.ToString();

                // Accept Citrix session windows by class name (seamless windows have no title)
                // Also accept any window with a title as fallback
                bool isCitrixClass = cName.StartsWith("Transparent Windows Client", StringComparison.OrdinalIgnoreCase)
                    || cName.StartsWith("CtxSeamless", StringComparison.OrdinalIgnoreCase)
                    || cName.Contains("CDViewer", StringComparison.OrdinalIgnoreCase)
                    || cName.StartsWith("TUIWindowClass", StringComparison.OrdinalIgnoreCase)
                    || cName.StartsWith("IHWindow", StringComparison.OrdinalIgnoreCase);

                if (!isCitrixClass && GetWindowTextLength(hwnd) == 0)
                    return true; // Skip unknown windows without titles

                GetWindowRect(hwnd, out var rect);
                int area = (rect.Right - rect.Left) * (rect.Bottom - rect.Top);

                // Skip tiny windows (toolbars, tray icons, etc.)
                if (area < 10000) return true;

                if (area > bestArea)
                {
                    bestArea = area;
                    bestHwnd = hwnd;
                    Core.Logging.FileLogger.Info(
                        $"Citrix: candidate hwnd=0x{hwnd.ToInt64():X} class='{cName}' pid={pid} size={rect.Right - rect.Left}x{rect.Bottom - rect.Top}");
                }

                return true;
            }, IntPtr.Zero);

            if (bestHwnd != IntPtr.Zero)
            {
                _captureInProgress = false;
                Core.Logging.FileLogger.Info(
                    $"Citrix: capturing hwnd=0x{bestHwnd.ToInt64():X} (area={bestArea}px)");
                await Dispatcher.InvokeAsync(() => EmbedWindow(bestHwnd));
                return;
            }
        }

        // Timeout — fall back to external mode
        _captureInProgress = false;
        Core.Logging.FileLogger.Info("Citrix: window capture timed out after 30s, using external mode");
        await Dispatcher.InvokeAsync(() =>
        {
            BringToFrontButton.Visibility = Visibility.Visible;
            StatusTextBlock.Text = _localizer?["CitrixStatusExternal"] ?? "External window";
        });
    }

    /// <summary>
    /// Reparents the given window handle into the embedded WinForms panel.
    /// </summary>
    private void EmbedWindow(IntPtr hwnd)
    {
        try
        {
            // Create a host panel for the captured window
            _hostPanel = new WinForms.Panel { Dock = WinForms.DockStyle.Fill };
            _hostPanel.Resize += (_, _) => ResizeCapturedWindow();
            FormsHost.Child = _hostPanel;

            // Strip popup/caption styles and make it a child window
            var style = GetWindowLong(hwnd, GwlStyle);
            style &= ~(WsPopup | WsCaption | WsThickframe);
            style |= WsChild;
            SetWindowLong(hwnd, GwlStyle, style);

            // Reparent into our panel
            SetParent(hwnd, _hostPanel.Handle);
            _capturedHwnd = hwnd;
            _embedded = true;

            // Show embedded container, hide info panel
            EmbeddedContainer.Visibility = Visibility.Visible;
            InfoPanel.Visibility = Visibility.Collapsed;
            BringToFrontButton.Visibility = Visibility.Collapsed;

            // Resize to fill
            ResizeCapturedWindow();
            ShowWindow(hwnd, SwMaximize);

            Core.Logging.FileLogger.Info($"Citrix: window embedded successfully (hwnd=0x{hwnd.ToInt64():X})");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Citrix: SetParent failed: {ex.Message}");
            // Fall back to external mode
            BringToFrontButton.Visibility = Visibility.Visible;
            _embedded = false;
        }
    }

    /// <summary>
    /// Resizes the captured window to fill the host panel.
    /// </summary>
    private void ResizeCapturedWindow()
    {
        if (_capturedHwnd == IntPtr.Zero || _hostPanel is null) return;

        try
        {
            MoveWindow(_capturedHwnd, 0, 0,
                _hostPanel.ClientSize.Width,
                _hostPanel.ClientSize.Height,
                true);
        }
        catch { /* Best-effort resize */ }
    }

    private void OnBringToFrontClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Process is null || _session.Process.HasExited) return;

        try
        {
            var hWnd = _session.Process.MainWindowHandle;
            if (hWnd == IntPtr.Zero) return;

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SwRestore);

            SetForegroundWindow(hWnd);
        }
        catch (InvalidOperationException) { }
    }

    private void OnTerminateClick(object sender, RoutedEventArgs e)
    {
        ReleaseEmbeddedWindow();

        if (_session?.Process is not null && !_session.Process.HasExited)
        {
            try { _session.Process.Kill(); }
            catch (InvalidOperationException) { }
        }

        UpdateStatus(false);
    }

    private void OnHealthTimerTick(object? sender, EventArgs e)
    {
        if (_embedded && _capturedHwnd != IntPtr.Zero)
        {
            // In embedded mode — only check if the captured window still exists
            if (!IsWindow(_capturedHwnd))
            {
                _embedded = false;
                _capturedHwnd = IntPtr.Zero;
                EmbeddedContainer.Visibility = Visibility.Collapsed;
                InfoPanel.Visibility = Visibility.Visible;
                UpdateStatus(false);
            }

            return;
        }

        if (_captureInProgress)
        {
            // Still searching for a window to capture — don't declare dead.
            // SelfService.exe exits immediately; that's expected.
            return;
        }

        // External mode (capture timed out) — monitor the last known process
        bool alive = _session?.Process is not null && !_session.Process.HasExited;
        UpdateStatus(alive);
    }

    private void UpdateStatus(bool isRunning)
    {
        if (isRunning)
        {
            HealthDot.Fill = Application.Current.TryFindResource("SuccessBrush") as Brush
                             ?? Brushes.LimeGreen;
            StatusTextBlock.Text = _embedded
                ? (_localizer?["CitrixStatusEmbedded"] ?? "Embedded")
                : (_localizer?["CitrixStatusConnected"] ?? "Connected");
            SessionInfoText.Text = _session?.Process?.Id > 0
                ? $"PID: {_session.Process.Id}"
                : string.Empty;
            TerminateButton.IsEnabled = true;
            BringToFrontButton.IsEnabled = !_embedded;
        }
        else
        {
            HealthDot.Fill = Application.Current.TryFindResource("ErrorBrush") as Brush
                             ?? Brushes.Red;
            StatusTextBlock.Text = _localizer?["CitrixStatusDisconnected"] ?? "Disconnected";
            SessionInfoText.Text = string.Empty;
            BringToFrontButton.IsEnabled = false;
            TerminateButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// Releases the captured window back to the desktop before disposing.
    /// </summary>
    private void ReleaseEmbeddedWindow()
    {
        if (_capturedHwnd != IntPtr.Zero && IsWindow(_capturedHwnd))
        {
            try
            {
                // Restore original window style
                var style = GetWindowLong(_capturedHwnd, GwlStyle);
                style &= ~WsChild;
                style |= WsPopup | WsCaption | WsThickframe;
                SetWindowLong(_capturedHwnd, GwlStyle, style);

                // Reparent back to desktop
                SetParent(_capturedHwnd, IntPtr.Zero);
                ShowWindow(_capturedHwnd, SwShowNormal);
            }
            catch { /* Best-effort release */ }
        }

        _capturedHwnd = IntPtr.Zero;
        _embedded = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_healthTimer is not null)
        {
            _healthTimer.Stop();
            _healthTimer.Tick -= OnHealthTimerTick;
            _healthTimer = null;
        }

        ReleaseEmbeddedWindow();

        try { FormsHost.Child = null; }
        catch { }
        _hostPanel?.Dispose();

        if (_session?.Process is not null && !_session.Process.HasExited)
        {
            try { _session.Process.Kill(); }
            catch (InvalidOperationException) { }
        }

        _session?.Process?.Dispose();
    }
}
