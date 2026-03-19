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
    private const int WindowCaptureMaxAttempts = 30;
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

    /// <summary>
    /// Polls for a new wfica32.exe window and reparents it into the embedded panel.
    /// Falls back to external mode after timeout.
    /// </summary>
    private async Task TryCaptureWindowAsync()
    {
        Core.Logging.FileLogger.Info("Citrix: attempting to capture wfica32.exe window...");

        // Record existing wfica32 PIDs before our launch
        var existingPids = new HashSet<int>(
            Process.GetProcessesByName("wfica32").Select(p => p.Id));

        for (int attempt = 0; attempt < WindowCaptureMaxAttempts; attempt++)
        {
            if (_disposed) return;

            await Task.Delay(WindowCapturePollIntervalMs);

            // Find new wfica32 processes
            var candidates = Process.GetProcessesByName("wfica32")
                .Where(p => !existingPids.Contains(p.Id))
                .ToList();

            foreach (var proc in candidates)
            {
                try
                {
                    if (proc.HasExited) continue;

                    var hwnd = proc.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;

                    // Found a new wfica32 window — attempt to embed
                    Core.Logging.FileLogger.Info(
                        $"Citrix: found wfica32 window (PID={proc.Id}, hwnd=0x{hwnd.ToInt64():X}), embedding...");

                    await Dispatcher.InvokeAsync(() => EmbedWindow(hwnd));
                    return;
                }
                catch (Exception ex)
                {
                    Core.Logging.FileLogger.Warn($"Citrix: window capture probe failed: {ex.Message}");
                }
            }
        }

        // Timeout — fall back to external mode
        Core.Logging.FileLogger.Info("Citrix: window capture timed out, using external mode");
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
        bool alive;

        if (_embedded && _capturedHwnd != IntPtr.Zero)
        {
            // In embedded mode, check if the captured window still exists
            alive = IsWindow(_capturedHwnd);
            if (!alive)
            {
                _embedded = false;
                _capturedHwnd = IntPtr.Zero;
                EmbeddedContainer.Visibility = Visibility.Collapsed;
                InfoPanel.Visibility = Visibility.Visible;
            }
        }
        else
        {
            alive = _session?.Process is not null && !_session.Process.HasExited;
        }

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
