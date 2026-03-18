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

namespace Heimdall.App.Views;

/// <summary>
/// Control panel for managing an external Citrix Workspace session.
/// Provides lifecycle controls (Bring to Front, Terminate) and health monitoring.
/// </summary>
public partial class EmbeddedCitrixView : UserControl, IDisposable
{
    private const int HealthCheckIntervalMs = 3000;
    private const int SwShowNormal = 1;
    private const int SwRestore = 9;

    private CitrixSessionResult? _session;
    private SessionTabViewModel? _sessionTab;
    private LocalizationManager? _localizer;
    private DispatcherTimer? _healthTimer;
    private bool _disposed;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    public EmbeddedCitrixView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the view with a Citrix session result and starts health monitoring.
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

        // Localize button labels
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

    private void OnBringToFrontClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Process is null || _session.Process.HasExited)
        {
            return;
        }

        try
        {
            var hWnd = _session.Process.MainWindowHandle;
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SwRestore);
            }

            SetForegroundWindow(hWnd);
        }
        catch (InvalidOperationException)
        {
            // Process has exited between the check and the call.
        }
    }

    private void OnTerminateClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Process is null || _session.Process.HasExited)
        {
            return;
        }

        try
        {
            _session.Process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }

        UpdateStatus(false);
    }

    private void OnHealthTimerTick(object? sender, EventArgs e)
    {
        bool alive = _session?.Process is not null
                     && !_session.Process.HasExited;
        UpdateStatus(alive);
    }

    private void UpdateStatus(bool isRunning)
    {
        if (isRunning)
        {
            HealthDot.Fill = Application.Current.TryFindResource("SuccessBrush") as Brush
                             ?? Brushes.LimeGreen;
            StatusTextBlock.Text = _localizer?["CitrixStatusConnected"] ?? "Connected";
            SessionInfoText.Text = _session?.Process?.Id > 0
                ? $"PID: {_session.Process.Id}"
                : string.Empty;
            BringToFrontButton.IsEnabled = true;
            TerminateButton.IsEnabled = true;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_healthTimer is not null)
        {
            _healthTimer.Stop();
            _healthTimer.Tick -= OnHealthTimerTick;
            _healthTimer = null;
        }

        if (_session?.Process is not null && !_session.Process.HasExited)
        {
            try
            {
                _session.Process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }
        }

        _session?.Process?.Dispose();
    }
}
