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
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// TCP ping tool that measures TCP connection latency to a host:port
/// by repeatedly connecting with <see cref="TcpClient"/> and timing
/// the handshake with a <see cref="Stopwatch"/>.
/// </summary>
public partial class TcpPingView : UserControl, IToolView
{
    private const int ConnectTimeoutMs = 5000;
    private const int DefaultPort = 443;
    private const int DefaultCount = 10;
    private const int DelayBetweenPingsMs = 1000;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private bool _disposed;
    private Action<bool>? _setBusy;

    public TcpPingView()
    {
        InitializeComponent();
        TxtHost.KeyDown += OnInputKeyDown;
        TxtPort.KeyDown += OnInputKeyDown;
        TxtCount.KeyDown += OnInputKeyDown;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        TxtHost.Clear();
        TxtPort.Text = DefaultPort.ToString(CultureInfo.InvariantCulture);
        TxtCount.Text = DefaultCount.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHost.Text = context.TargetHost.Trim();
        }

        if (context?.TargetPort is > 0)
        {
            TxtPort.Text = context.TargetPort.Value.ToString(CultureInfo.InvariantCulture);
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHost.Focus();
            if (!string.IsNullOrEmpty(TxtHost.Text))
            {
                TxtHost.SelectAll();
            }
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolTcpPingTitle");
        BtnStart.Content = L("ToolTcpPingBtnStart");
        BtnStop.Content = L("ToolTcpPingBtnStop");
        BtnCopy.Content = L("ToolBtnCopyToClipboard");
        LblHost.Text = L("ToolTcpPingLblHost");
        LblPort.Text = L("ToolTcpPingLblPort");
        LblCount.Text = L("ToolTcpPingLblCount");

        System.Windows.Automation.AutomationProperties.SetName(BtnStart, L("ToolTcpPingBtnStart"));
        System.Windows.Automation.AutomationProperties.SetName(BtnStop, L("ToolTcpPingBtnStop"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolBtnCopyToClipboard"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHost, L("ToolTcpPingLblHost"));
        System.Windows.Automation.AutomationProperties.SetName(TxtPort, L("ToolTcpPingLblPort"));
        System.Windows.Automation.AutomationProperties.SetName(TxtCount, L("ToolTcpPingLblCount"));
        System.Windows.Automation.AutomationProperties.SetName(TxtResults, L("ToolTcpPingTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        TxtHost.Tag = L("ToolWatermarkHostnameOrIp");
        TxtStatus.Text = string.Empty;
        TxtSummary.Text = string.Empty;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (!_isRunning)
            {
                _ = StartPingAsync();
            }
            e.Handled = true;
        }
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        _ = StartPingAsync();
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        StopPing();
    }

    private async Task StartPingAsync()
    {
        if (_disposed || _isRunning)
        {
            return;
        }

        var host = TxtHost.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            TxtError.Text = L("ToolValidationInvalidHost");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!int.TryParse(TxtPort.Text.Trim(), CultureInfo.InvariantCulture, out var port) ||
            port < 1 || port > 65535)
        {
            TxtError.Text = L("ToolValidationPortRangeRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!int.TryParse(TxtCount.Text.Trim(), CultureInfo.InvariantCulture, out var count) ||
            count < 1 || count > 10000)
        {
            TxtError.Text = L("ToolTcpPingErrorCount");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _cts = new CancellationTokenSource();
        _isRunning = true;
        try
        {
            _setBusy?.Invoke(true);
            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
            SetInputsEnabled(false);
            TxtResults.Clear();
            TxtSummary.Text = string.Empty;
        }
        catch
        {
            _isRunning = false;
            throw;
        }

        var latencies = new List<double>();
        var lostCount = 0;
        var ct = _cts.Token;
        var sb = new StringBuilder();

        try
        {
            for (var i = 1; i <= count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var sw = Stopwatch.StartNew();
                string line;

                try
                {
                    using var client = new TcpClient();
                    using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                    await client.ConnectAsync(host, port, linked.Token);
                    sw.Stop();

                    var ms = sw.Elapsed.TotalMilliseconds;
                    latencies.Add(ms);
                    line = $"[{i}/{count}] {host}:{port} \u2014 {ms:F1} ms";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    lostCount++;
                    var reason = ex.InnerException?.Message ?? ex.Message;
                    line = $"[{i}/{count}] {host}:{port} \u2014 FAILED: {reason}";
                }

                sb.AppendLine(line);
                TxtResults.Text = sb.ToString();
                LogScrollViewer.ScrollToEnd();

                // Wait between pings (except after the last one)
                if (i < count)
                {
                    try
                    {
                        await Task.Delay(DelayBetweenPingsMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by user
        }

        // Show summary
        if (latencies.Count > 0)
        {
            var min = latencies.Min();
            var avg = latencies.Average();
            var max = latencies.Max();
            var total = latencies.Count + lostCount;

            TxtSummary.Text = string.Format(
                CultureInfo.InvariantCulture,
                L("ToolTcpPingSummary"),
                $"{min:F0}",
                $"{avg:F0}",
                $"{max:F0}",
                lostCount,
                total);
        }
        else if (lostCount > 0)
        {
            TxtSummary.Text = string.Format(
                CultureInfo.InvariantCulture,
                L("ToolTcpPingSummary"),
                "\u2014",
                "\u2014",
                "\u2014",
                lostCount,
                lostCount);
        }

        TxtStatus.Text = L("ToolTcpPingStatus");
        StopPing();
    }

    private void StopPing()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isRunning = false;
        _setBusy?.Invoke(false);
        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled = false;
        SetInputsEnabled(true);
    }

    private void SetInputsEnabled(bool enabled)
    {
        TxtHost.IsReadOnly = !enabled;
        TxtPort.IsReadOnly = !enabled;
        TxtCount.IsReadOnly = !enabled;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var text = TxtResults.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.Append(text);
            if (!string.IsNullOrWhiteSpace(TxtSummary.Text))
            {
                sb.AppendLine();
                sb.AppendLine(TxtSummary.Text);
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"TcpPing clipboard copy failed: {ex.Message}");
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpTCPPING").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isRunning;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopPing();
        GC.SuppressFinalize(this);
    }
}
