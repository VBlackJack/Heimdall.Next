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

using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Continuous ping monitor with live latency graph and running statistics.
/// </summary>
public partial class PingToolView : UserControl, IToolView
{
    private const int MaxDataPoints = 60;
    private const int DefaultPingTimeoutMs = 2000;

    private LocalizationManager? _localizer;
    private DispatcherTimer? _pingTimer;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private bool _disposed;
    private Action<bool>? _setBusy;

    private readonly List<PingDataPoint> _dataPoints = new(MaxDataPoints + 1);
    private readonly StringBuilder _logBuilder = new();
    private int _sequenceNumber;
    private int _sentCount;
    private int _lostCount;
    private long _minLatency = long.MaxValue;
    private long _maxLatency;
    private long _totalLatency;
    private int _successCount;
    private double _sumSquaredLatency;

    private readonly List<PingCsvEntry> _csvEntries = [];

    private Polyline? _graphLine;
    private readonly List<Ellipse> _timeoutMarkers = [];

    public PingToolView()
    {
        InitializeComponent();
        TxtHost.KeyDown += OnHostKeyDown;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        // Pre-fill with a sensible default; context overrides if provided
        TxtHost.Text = "8.8.8.8";

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHost.Text = context.TargetHost;
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHost.Focus();
            TxtHost.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolPingTitle");
        BtnToggle.Content = L("ToolPingBtnStart");
        LblMin.Text = L("ToolPingMin");
        LblAvg.Text = L("ToolPingAvg");
        LblMax.Text = L("ToolPingMax");
        LblLoss.Text = L("ToolPingLoss");
        TxtPingCount.Text = string.Empty;

        LblJitter.Text = L("ToolPingJitter");
        BtnClear.Content = L("ToolPingBtnClear");
        BtnCopyLog.Content = L("ToolPingBtnCopyLog");
        BtnExportCsv.Content = L("ToolPingBtnExportCsv");
        LblInterval.Text = L("ToolPingIntervalLabel");
        Ping500ms.Content = L("PingInterval500ms");
        Ping1s.Content = L("PingInterval1s");
        Ping2s.Content = L("PingInterval2s");
        Ping5s.Content = L("PingInterval5s");
        Ping10s.Content = L("PingInterval10s");
        LblTimeout.Text = L("ToolPingTimeoutLabel");
        LblCount.Text = L("ToolPingCountLabel");

        System.Windows.Automation.AutomationProperties.SetName(BtnToggle, L("ToolPingBtnStart"));
        System.Windows.Automation.AutomationProperties.SetName(BtnClear, L("ToolPingBtnClear"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyLog, L("ToolPingBtnCopyLog"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportCsv, L("ToolPingBtnExportCsv"));
        BtnCopyLog.ToolTip = L("ToolBtnCopyToClipboard");
        System.Windows.Automation.AutomationProperties.SetName(TxtHost, L("ToolPingHostLabel"));
        System.Windows.Automation.AutomationProperties.SetName(CmbInterval, L("ToolPingIntervalLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtTimeout, L("ToolPingTimeoutLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtCount, L("ToolPingCountLabel"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtHost.Tag = L("ToolWatermarkHostnameOrIp");
    }

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TogglePing();
            e.Handled = true;
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        TogglePing();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _dataPoints.Clear();
        _logBuilder.Clear();
        _csvEntries.Clear();
        TxtLog.Text = string.Empty;
        TxtLog.Inlines.Clear();
        _sequenceNumber = 0;
        _sentCount = 0;
        _lostCount = 0;
        _minLatency = long.MaxValue;
        _maxLatency = 0;
        _totalLatency = 0;
        _successCount = 0;
        _sumSquaredLatency = 0;
        ResetStats();
        TxtPingCount.Text = string.Empty;
        RedrawGraph();
    }

    private void OnCopyLogClick(object sender, RoutedEventArgs e)
    {
        var logText = new System.Text.StringBuilder();
        foreach (var inline in TxtLog.Inlines)
        {
            if (inline is System.Windows.Documents.Run run)
            {
                logText.Append(run.Text);
            }
        }

        var text = logText.ToString().Trim();
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                Clipboard.SetText(text);
                CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"PingTool clipboard copy failed: {ex.Message}");
            }
        }
    }

    private void TogglePing()
    {
        if (_isRunning)
        {
            StopPing();
        }
        else
        {
            StartPing();
        }
    }

    private void StartPing()
    {
        var host = TxtHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            TxtLog.Inlines.Clear();
            AppendLogLine(L("ToolValidationHostRequired"), true);
            return;
        }

        if (!int.TryParse(TxtTimeout.Text.Trim(), out var timeout) || timeout < 100 || timeout > 30000)
        {
            TxtLog.Inlines.Clear();
            AppendLogLine(L("ToolPingValidationTimeout"), true);
            return;
        }

        var countText = TxtCount.Text.Trim();
        if (!string.IsNullOrEmpty(countText) && (!int.TryParse(countText, out var count) || count < 0 || count > 100000))
        {
            TxtLog.Inlines.Clear();
            AppendLogLine(L("ToolPingValidationCount"), true);
            return;
        }

        // Reset state
        _dataPoints.Clear();
        _logBuilder.Clear();
        _csvEntries.Clear();
        TxtLog.Text = string.Empty;
        TxtLog.Inlines.Clear();
        _sequenceNumber = 0;
        _sentCount = 0;
        _lostCount = 0;
        _minLatency = long.MaxValue;
        _maxLatency = 0;
        _totalLatency = 0;
        _successCount = 0;
        _sumSquaredLatency = 0;
        ResetStats();

        _cts = new CancellationTokenSource();
        _isRunning = true;
        _setBusy?.Invoke(true);
        BtnToggle.Content = L("ToolPingBtnStop");
        BtnToggle.Foreground = (Brush)FindResource("ErrorBrush");
        BtnToggle.Style = (Style)FindResource("SecondaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnToggle, L("ToolPingBtnStop"));
        TxtHost.IsReadOnly = true;
        CmbInterval.IsEnabled = false;
        TxtTimeout.IsReadOnly = true;
        TxtCount.IsReadOnly = true;

        var intervalMs = GetSelectedIntervalMs();

        _pingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _pingTimer.Tick += OnPingTimerTick;
        _pingTimer.Start();

        // Fire first ping immediately
        _ = SendPingAsync();
    }

    private void StopPing()
    {
        _pingTimer?.Stop();
        _pingTimer = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isRunning = false;
        _setBusy?.Invoke(false);
        BtnToggle.Content = L("ToolPingBtnStart");
        BtnToggle.Foreground = (Brush)FindResource("TextPrimaryBrush");
        BtnToggle.Style = (Style)FindResource("PrimaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnToggle, L("ToolPingBtnStart"));
        TxtHost.IsReadOnly = false;
        CmbInterval.IsEnabled = true;
        TxtTimeout.IsReadOnly = false;
        TxtCount.IsReadOnly = false;
    }

    private async void OnPingTimerTick(object? sender, EventArgs e)
    {
        await SendPingAsync();
    }

    private async Task SendPingAsync()
    {
        if (_cts is null || _cts.IsCancellationRequested)
        {
            return;
        }

        var host = TxtHost.Text.Trim();
        _sequenceNumber++;
        _sentCount++;
        var seq = _sequenceNumber;

        var timeoutMs = GetConfiguredTimeoutMs();

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);

            if (_cts is null || _cts.IsCancellationRequested)
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");

            if (reply.Status == IPStatus.Success)
            {
                var latency = reply.RoundtripTime;
                var ttl = reply.Options?.Ttl ?? 0;

                _dataPoints.Add(new PingDataPoint(latency, false));
                if (_dataPoints.Count > MaxDataPoints)
                {
                    _dataPoints.RemoveAt(0);
                }

                _successCount++;
                _totalLatency += latency;
                _sumSquaredLatency += latency * (double)latency;
                if (latency < _minLatency) _minLatency = latency;
                if (latency > _maxLatency) _maxLatency = latency;

                _csvEntries.Add(new PingCsvEntry(seq, timestamp, latency, "OK"));

                AppendLogLine(
                    string.Format(L("ToolPingReplyFormat"), timestamp, seq, reply.Address, latency, ttl),
                    false);
            }
            else
            {
                _dataPoints.Add(new PingDataPoint(-1, true));
                if (_dataPoints.Count > MaxDataPoints)
                {
                    _dataPoints.RemoveAt(0);
                }

                _lostCount++;
                _csvEntries.Add(new PingCsvEntry(seq, timestamp, -1, reply.Status.ToString()));

                AppendLogLine(
                    string.Format(L("ToolPingTimeoutFormat"), timestamp, seq, reply.Status),
                    true);
            }
        }
        catch (PingException ex)
        {
            if (_cts is null || _cts.IsCancellationRequested)
            {
                return;
            }

            _dataPoints.Add(new PingDataPoint(-1, true));
            if (_dataPoints.Count > MaxDataPoints)
            {
                _dataPoints.RemoveAt(0);
            }

            _lostCount++;

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var message = ex.InnerException?.Message ?? ex.Message;
            _csvEntries.Add(new PingCsvEntry(seq, timestamp, -1, $"Error: {message}"));
            AppendLogLine(
                string.Format(L("ToolPingErrorFormat"), timestamp, seq, message),
                true);
        }

        UpdateStats();
        UpdatePingCount();
        RedrawGraph();
        CheckPingCountLimit();
    }

    private void AppendLogLine(string text, bool isError)
    {
        var run = new System.Windows.Documents.Run(text + Environment.NewLine);
        if (isError)
        {
            run.Foreground = (Brush)FindResource("ErrorBrush");
        }
        else
        {
            run.Foreground = (Brush)FindResource("TextPrimaryBrush");
        }

        TxtLog.Inlines.Add(run);
        LogScrollViewer.ScrollToEnd();
    }

    private void UpdateStats()
    {
        if (_successCount > 0)
        {
            TxtMin.Text = $"{_minLatency} ms";
            TxtAvg.Text = $"{_totalLatency / _successCount} ms";
            TxtMax.Text = $"{_maxLatency} ms";

            // Jitter = standard deviation of latencies
            double mean = (double)_totalLatency / _successCount;
            double variance = (_sumSquaredLatency / _successCount) - (mean * mean);
            double jitter = Math.Sqrt(Math.Max(0, variance));
            TxtJitter.Text = $"{jitter:F1} ms";
        }

        var lossPercent = _sentCount > 0 ? (_lostCount * 100.0 / _sentCount) : 0;
        TxtLoss.Text = $"{lossPercent:F1}%";
    }

    private void UpdatePingCount()
    {
        TxtPingCount.Text = string.Format(L("ToolPingCountFormat"), _sentCount);
    }

    private void ResetStats()
    {
        TxtMin.Text = "\u2014";
        TxtAvg.Text = "\u2014";
        TxtMax.Text = "\u2014";
        TxtJitter.Text = "\u2014";
        TxtLoss.Text = "\u2014";
    }

    private void OnGraphCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawGraph();
    }

    private void RedrawGraph()
    {
        GraphCanvas.Children.Clear();
        _timeoutMarkers.Clear();

        if (_dataPoints.Count < 2)
        {
            return;
        }

        var width = GraphCanvas.ActualWidth;
        var height = GraphCanvas.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Find max latency for Y-axis scaling (only successful pings)
        long maxY = 1;
        foreach (var dp in _dataPoints)
        {
            if (!dp.IsTimeout && dp.Latency > maxY)
            {
                maxY = dp.Latency;
            }
        }

        // Add 20% headroom
        maxY = (long)(maxY * 1.2);
        if (maxY < 10) maxY = 10;

        // Update Y-axis labels
        TxtYMax.Text = $"{maxY} ms";
        TxtYMid.Text = $"{maxY / 2} ms";

        const double marginLeft = 50;
        const double marginRight = 10;
        const double marginTop = 10;
        const double marginBottom = 10;

        var graphWidth = width - marginLeft - marginRight;
        var graphHeight = height - marginTop - marginBottom;

        // Draw grid lines
        for (int i = 0; i <= 4; i++)
        {
            var y = marginTop + (graphHeight * i / 4.0);
            var gridLine = new Line
            {
                X1 = marginLeft,
                Y1 = y,
                X2 = width - marginRight,
                Y2 = y,
                Stroke = (Brush)FindResource("BorderBrush"),
                StrokeThickness = 0.5,
                Opacity = 0.5
            };
            GraphCanvas.Children.Add(gridLine);
        }

        // Build polyline for successful pings
        var accentBrush = (Brush)FindResource("AccentBrush");
        var errorBrush = (Brush)FindResource("ErrorBrush");

        var polyline = new Polyline
        {
            Stroke = accentBrush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };

        var stepX = _dataPoints.Count > 1 ? graphWidth / (_dataPoints.Count - 1) : 0;

        for (int i = 0; i < _dataPoints.Count; i++)
        {
            var dp = _dataPoints[i];
            var x = marginLeft + (i * stepX);

            if (dp.IsTimeout)
            {
                // Draw timeout marker
                var marker = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = errorBrush
                };
                Canvas.SetLeft(marker, x - 3);
                Canvas.SetTop(marker, marginTop + graphHeight - 3);
                GraphCanvas.Children.Add(marker);
                _timeoutMarkers.Add(marker);
            }
            else
            {
                var y = marginTop + graphHeight - (dp.Latency * graphHeight / maxY);
                polyline.Points.Add(new System.Windows.Point(x, y));
            }
        }

        if (polyline.Points.Count > 1)
        {
            GraphCanvas.Children.Add(polyline);
        }

        _graphLine = polyline;
    }

    /// <summary>
    /// Returns the selected ping interval in milliseconds from the ComboBox.
    /// </summary>
    private int GetSelectedIntervalMs()
    {
        if (CmbInterval.SelectedItem is ComboBoxItem item && item.Tag is string tagStr &&
            int.TryParse(tagStr, out var ms))
        {
            return ms;
        }

        return 1000;
    }

    /// <summary>
    /// Returns the configured timeout in milliseconds from the TextBox.
    /// </summary>
    private int GetConfiguredTimeoutMs()
    {
        if (int.TryParse(TxtTimeout.Text.Trim(), out var ms) && ms > 0)
        {
            return ms;
        }

        return DefaultPingTimeoutMs;
    }

    /// <summary>
    /// Returns the configured ping count limit (0 = unlimited).
    /// </summary>
    private int GetConfiguredCount()
    {
        if (int.TryParse(TxtCount.Text.Trim(), out var count) && count >= 0)
        {
            return count;
        }

        return 0;
    }

    /// <summary>
    /// Checks whether the configured ping count has been reached and auto-stops if so.
    /// </summary>
    private void CheckPingCountLimit()
    {
        var limit = GetConfiguredCount();
        if (limit > 0 && _sentCount >= limit)
        {
            StopPing();

            var lossPercent = _sentCount > 0 ? (_lostCount * 100.0 / _sentCount) : 0;
            AppendLogLine(
                string.Format(L("ToolPingCompleteFormat"), _sentCount, _successCount, $"{lossPercent:F1}"),
                false);
        }
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_csvEntries.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"ping_{TxtHost.Text.Trim()}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Seq,Timestamp,Latency,Status");

            foreach (var entry in _csvEntries)
            {
                var latencyStr = entry.Latency >= 0 ? entry.Latency.ToString() : "";
                sb.AppendLine($"{entry.Seq},{entry.Timestamp},{latencyStr},{entry.Status}");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"PingTool CSV export failed: {ex.Message}");
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpPING");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

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

    private readonly record struct PingDataPoint(long Latency, bool IsTimeout);
    private readonly record struct PingCsvEntry(int Seq, string Timestamp, long Latency, string Status);
}
