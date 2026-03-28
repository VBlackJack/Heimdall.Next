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

using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Traces the network path to a destination host using ICMP packets
/// with incrementing TTL values. Displays each hop with reverse DNS
/// and latency statistics.
/// </summary>
public partial class TcpTracerouteView : UserControl, IToolView
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);
    private const int DefaultMaxHops = 30;
    private const int ProbesPerHop = 3;
    private const int MinMaxHops = 1;
    private const int MaxMaxHops = 128;
    private const int PingBufferSize = 32;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isTracing;
    private bool _disposed;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private Action<bool>? _setBusy;

    private readonly ObservableCollection<TraceHop> _results = [];

    public TcpTracerouteView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
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

        TxtHost.Text = "8.8.8.8";

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHost.Text = context.TargetHost;
        }

        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHost.Focus();
            TxtHost.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolTraceTitle");
        BtnTrace.Content = L("ToolTraceBtnTrace");
        BtnCopy.Content = L("ToolTraceBtnCopy");
        LblMaxHops.Text = L("ToolTraceMaxHops");

        ColHop.Header = L("ToolTraceColHop");
        ColAddress.Header = L("ToolTraceColAddress");
        ColHostname.Header = L("ToolTraceColHostname");
        ColLatency.Header = L("ToolTraceColLatency");
        ColStatus.Header = L("ToolTraceColStatus");

        System.Windows.Automation.AutomationProperties.SetName(BtnTrace, L("ToolTraceBtnTrace"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolTraceBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHost, L("ToolTraceHostLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtMaxHops, L("ToolTraceMaxHops"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        LblRouteVia.Text = L("ToolTunnelRouteVia");
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(TraceProgress, L("ToolTraceA11yProgress"));
        System.Windows.Automation.AutomationProperties.SetName(ResultsGrid, L("ToolTraceTitle"));

        TxtEmptyState.Text = L("ToolTraceEmptyState");
        TxtHost.Tag = L("ToolWatermarkHostnameOrIp");
    }

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ToggleTrace();
            e.Handled = true;
        }
    }

    private void OnTraceClick(object sender, RoutedEventArgs e)
    {
        ToggleTrace();
    }

    private void ToggleTrace()
    {
        if (_isTracing)
        {
            StopTrace();
        }
        else
        {
            _ = StartTraceAsync();
        }
    }

    private async Task StartTraceAsync()
    {
        var host = TxtHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            TxtStatus.Text = L("ToolValidationHostRequired");
            TxtStatus.Foreground = (Brush)FindResource("ErrorTextBrush");
            return;
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            TxtStatus.Text = L("ErrorInvalidHost");
            TxtStatus.Foreground = (Brush)FindResource("ErrorTextBrush");
            return;
        }

        if (!int.TryParse(TxtMaxHops.Text.Trim(), out var maxHops) ||
            maxHops < MinMaxHops || maxHops > MaxMaxHops)
        {
            maxHops = DefaultMaxHops;
            TxtMaxHops.Text = DefaultMaxHops.ToString(CultureInfo.InvariantCulture);
        }

        _results.Clear();
        _cts = new CancellationTokenSource();
        _isTracing = true;

        try
        {
            _setBusy?.Invoke(true);
            BtnTrace.Content = L("ToolTraceBtnStop");
            BtnTrace.Foreground = (Brush)FindResource("ErrorBrush");
            BtnTrace.Style = (Style)FindResource("SecondaryButtonStyle");
            System.Windows.Automation.AutomationProperties.SetName(BtnTrace, L("ToolTraceBtnStop"));
            TxtHost.IsReadOnly = true;
            TxtMaxHops.IsReadOnly = true;
            CmbRouteVia.IsEnabled = false;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            TraceProgress.Maximum = maxHops;
            TraceProgress.Value = 0;
            TraceProgress.IsIndeterminate = false;
            TxtProgressStatus.Text = L("ToolTraceResolving");
            ProgressPanel.Visibility = Visibility.Visible;
            TxtStatus.Text = string.Empty;
            TxtStatus.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }
        catch
        {
            _isTracing = false;
            throw;
        }

        var ct = _cts.Token;

        try
        {
            if (_selectedGateway is not null)
            {
                await TraceRouteViaTunnelAsync(host, maxHops, ct);
            }
            else
            {
                await TraceRouteAsync(host, maxHops, ct);
            }

            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    TxtStatus.Text = string.Format(
                        CultureInfo.InvariantCulture, L("ToolTraceComplete"), _results.Count);
                    TxtStatus.Foreground = (Brush)FindResource("TextSecondaryBrush");
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Trace was cancelled by user
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Traceroute failed: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = string.Format(
                    CultureInfo.InvariantCulture, L("ToolTraceErrorResolve"), ex.Message);
                TxtStatus.Foreground = (Brush)FindResource("ErrorTextBrush");
            });
        }

        StopTrace();
    }

    /// <summary>
    /// Performs a local ICMP traceroute by sending pings with incrementing TTL.
    /// </summary>
    private async Task TraceRouteAsync(string host, int maxHops, CancellationToken ct)
    {
        IPAddress targetIp;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            if (addresses.Length == 0)
            {
                throw new InvalidOperationException(host);
            }
            targetIp = addresses[0];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = string.Format(
                    CultureInfo.InvariantCulture, L("ToolTraceErrorResolve"), ex.Message);
                TxtStatus.Foreground = (Brush)FindResource("ErrorTextBrush");
            });
            return;
        }

        var buffer = new byte[PingBufferSize];

        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            ct.ThrowIfCancellationRequested();

            var currentTtl = ttl;
            await Dispatcher.InvokeAsync(() =>
            {
                TxtProgressStatus.Text = string.Format(
                    CultureInfo.InvariantCulture, L("ToolTraceHopProgress"), currentTtl, maxHops);
                TraceProgress.Value = currentTtl;
            });

            var hopResults = new List<(long Ms, IPAddress? Addr)>(ProbesPerHop);

            for (int probe = 0; probe < ProbesPerHop; probe++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var ping = new Ping();
                    var options = new PingOptions(ttl, true);
                    var reply = await ping.SendPingAsync(
                        targetIp, (int)PingTimeout.TotalMilliseconds, buffer, options);

                    if (reply.Status is IPStatus.TtlExpired or IPStatus.Success)
                    {
                        hopResults.Add((reply.RoundtripTime, reply.Address));
                    }
                    else
                    {
                        hopResults.Add((-1, null));
                    }
                }
                catch (PingException)
                {
                    hopResults.Add((-1, null));
                }
            }

            var hopAddress = hopResults.FirstOrDefault(r => r.Addr is not null).Addr;
            var successfulProbes = hopResults.Where(r => r.Ms >= 0).Select(r => r.Ms).ToList();
            bool isDestination = hopAddress is not null && hopAddress.Equals(targetIp);

            string latencyText;
            if (successfulProbes.Count == 0)
            {
                latencyText = "*";
            }
            else if (successfulProbes.Count == 1)
            {
                latencyText = $"{successfulProbes[0]} ms";
            }
            else
            {
                var min = successfulProbes.Min();
                var max = successfulProbes.Max();
                var avg = successfulProbes.Average();
                latencyText = $"{min}/{avg:F0}/{max} ms";
            }

            string statusText;
            Brush statusBrush;
            if (successfulProbes.Count == 0)
            {
                statusText = L("ToolTraceStatusTimeout");
                statusBrush = (Brush)FindResource("WarningTextBrush");
            }
            else if (isDestination)
            {
                statusText = L("ToolTraceStatusDestination");
                statusBrush = (Brush)FindResource("SuccessTextBrush");
            }
            else
            {
                statusText = L("ToolTraceStatusReply");
                statusBrush = (Brush)FindResource("TextSecondaryBrush");
            }

            var addressText = hopAddress?.ToString() ?? "*";
            var hop = new TraceHop
            {
                Hop = ttl,
                Address = addressText,
                Hostname = "",
                Latency = latencyText,
                Status = statusText,
                StatusBrush = statusBrush
            };

            await Dispatcher.InvokeAsync(() => _results.Add(hop));

            // Kick off reverse DNS in background (non-blocking)
            if (hopAddress is not null)
            {
                var capturedAddr = hopAddress;
                var capturedIndex = _results.Count - 1;
                _ = ResolveHostnameAsync(capturedAddr, capturedIndex);
            }

            if (isDestination)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Resolves the hostname for a hop address via reverse DNS and updates the results grid.
    /// </summary>
    private async Task ResolveHostnameAsync(IPAddress address, int resultIndex)
    {
        var capturedAddr = address.ToString();
        var hostname = await ReverseDnsAsync(address);
        if (!string.IsNullOrEmpty(hostname))
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (resultIndex >= 0 && resultIndex < _results.Count &&
                    _results[resultIndex].Address == capturedAddr)
                {
                    var existing = _results[resultIndex];
                    _results[resultIndex] = existing with { Hostname = hostname };
                }
            });
        }
    }

    /// <summary>
    /// Performs reverse DNS lookup for an IP address.
    /// </summary>
    private static async Task<string> ReverseDnsAsync(IPAddress ip)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            // Only return hostname if it differs from the IP string representation
            return entry.HostName != ip.ToString() ? entry.HostName : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Runs traceroute remotely via an SSH gateway using the traceroute or tracert command.
    /// Parses incremental output line by line.
    /// </summary>
    private async Task TraceRouteViaTunnelAsync(string host, int maxHops, CancellationToken ct)
    {
        Renci.SshNet.SshClient? client = null;
        try
        {
            client = ToolGatewayConnector.Connect(_selectedGateway!);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Traceroute gateway connection failed: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = string.Format(
                    CultureInfo.InvariantCulture, L("ToolTunnelFailed"), ex.Message);
                TxtStatus.Foreground = (Brush)FindResource("ErrorTextBrush");
            });
            return;
        }

        try
        {
            // Try Linux traceroute first, fall back to Windows tracert
            var escapedHost = InputValidator.EscapeShellArg(host);
            var command = $"traceroute -n -m {maxHops} {escapedHost} 2>/dev/null || tracert -d -h {maxHops} {escapedHost} 2>/dev/null";

            await Dispatcher.InvokeAsync(() =>
            {
                TraceProgress.IsIndeterminate = true;
                TxtProgressStatus.Text = L("ToolTraceResolving");
            });

            var result = await Task.Run(() =>
            {
                using var cmd = client.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(maxHops * 5);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int hopNumber = 0;

            foreach (var rawLine in lines)
            {
                ct.ThrowIfCancellationRequested();

                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var parsed = ParseTracerouteLine(line, ref hopNumber);
                if (parsed is null)
                {
                    continue;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _results.Add(parsed);
                    TraceProgress.Value = hopNumber;
                    TxtProgressStatus.Text = string.Format(
                        CultureInfo.InvariantCulture, L("ToolTraceHopProgress"), hopNumber, maxHops);
                });
            }
        }
        finally
        {
            try { client.Disconnect(); } catch { /* best effort */ }
            client.Dispose();
        }
    }

    /// <summary>
    /// Parses a single line from traceroute/tracert output.
    /// Handles both Linux format: "1  10.0.0.1  1.234 ms  1.123 ms  0.987 ms"
    /// and Windows format: "  1    &lt;1 ms    &lt;1 ms    &lt;1 ms  10.0.0.1"
    /// </summary>
    private TraceHop? ParseTracerouteLine(string line, ref int hopNumber)
    {
        // Skip header lines (e.g., "traceroute to..." or "Tracing route to...")
        if (line.StartsWith("traceroute", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Tracing", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("over a maximum", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Trace complete", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Match Linux traceroute format: "hop_number  ip  latency1 ms  latency2 ms  latency3 ms"
        // Also handles timeout with "*"
        var linuxPattern = LinuxTracerouteRegex();
        var linuxMatch = linuxPattern.Match(line);
        if (linuxMatch.Success)
        {
            hopNumber = int.Parse(linuxMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var remainder = linuxMatch.Groups[2].Value.Trim();

            return ParseLinuxHopData(hopNumber, remainder);
        }

        // Match Windows tracert format: "hop_number  latency1  latency2  latency3  ip"
        var windowsPattern = WindowsTracertRegex();
        var windowsMatch = windowsPattern.Match(line);
        if (windowsMatch.Success)
        {
            hopNumber = int.Parse(windowsMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var l1 = windowsMatch.Groups[2].Value.Trim();
            var l2 = windowsMatch.Groups[3].Value.Trim();
            var l3 = windowsMatch.Groups[4].Value.Trim();
            var addr = windowsMatch.Groups[5].Value.Trim();

            return ParseWindowsHopData(hopNumber, addr, l1, l2, l3);
        }

        return null;
    }

    private TraceHop ParseLinuxHopData(int hop, string remainder)
    {
        // Extract IP and latency values from remainder
        // Format examples: "10.0.0.1  1.234 ms  1.123 ms  0.987 ms"
        //                  "* * *"
        //                  "10.0.0.1  1.234 ms  *  0.987 ms"
        var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string address = "*";
        var latencies = new List<double>();
        bool allTimeout = true;

        int i = 0;
        while (i < parts.Length)
        {
            if (parts[i] == "*")
            {
                i++;
                continue;
            }

            // Try to parse as IP address
            if (IPAddress.TryParse(parts[i], out _))
            {
                address = parts[i];
                i++;
                continue;
            }

            // Try to parse as latency value (number followed by "ms")
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            {
                latencies.Add(ms);
                allTimeout = false;
                i++;
                // Skip the "ms" token if present
                if (i < parts.Length && parts[i].Equals("ms", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                }
                continue;
            }

            i++;
        }

        return BuildTraceHop(hop, address, latencies, allTimeout);
    }

    private TraceHop ParseWindowsHopData(int hop, string address, params string[] latencyStrings)
    {
        var latencies = new List<double>();
        bool allTimeout = true;

        foreach (var ls in latencyStrings)
        {
            if (ls == "*")
            {
                continue;
            }

            // Parse "<1 ms" or "5 ms"
            var cleaned = ls.Replace("ms", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("<", "", StringComparison.Ordinal)
                            .Trim();
            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            {
                latencies.Add(ms);
                allTimeout = false;
            }
        }

        if (string.IsNullOrWhiteSpace(address) || address == "*")
        {
            address = "*";
        }

        return BuildTraceHop(hop, address, latencies, allTimeout);
    }

    private TraceHop BuildTraceHop(int hop, string address, List<double> latencies, bool allTimeout)
    {
        string latencyText;
        if (latencies.Count == 0)
        {
            latencyText = "*";
        }
        else if (latencies.Count == 1)
        {
            latencyText = $"{latencies[0]:F0} ms";
        }
        else
        {
            var min = latencies.Min();
            var max = latencies.Max();
            var avg = latencies.Average();
            latencyText = $"{min:F0}/{avg:F0}/{max:F0} ms";
        }

        string statusText;
        Brush statusBrush;
        if (allTimeout && address == "*")
        {
            statusText = L("ToolTraceStatusTimeout");
            statusBrush = (Brush)FindResource("WarningTextBrush");
        }
        else
        {
            statusText = L("ToolTraceStatusReply");
            statusBrush = (Brush)FindResource("TextSecondaryBrush");
        }

        return new TraceHop
        {
            Hop = hop,
            Address = address,
            Hostname = "",
            Latency = latencyText,
            Status = statusText,
            StatusBrush = statusBrush
        };
    }

    private void StopTrace()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isTracing = false;
        _setBusy?.Invoke(false);
        BtnTrace.Content = L("ToolTraceBtnTrace");
        BtnTrace.Foreground = (Brush)FindResource("TextPrimaryBrush");
        BtnTrace.Style = (Style)FindResource("PrimaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnTrace, L("ToolTraceBtnTrace"));
        TxtHost.IsReadOnly = false;
        TxtMaxHops.IsReadOnly = false;
        CmbRouteVia.IsEnabled = true;
        ProgressPanel.Visibility = Visibility.Collapsed;
    }

    private void PopulateRouteSelector()
    {
        CmbRouteVia.Items.Clear();
        CmbRouteVia.Items.Add(new ComboBoxItem { Content = L("ToolTunnelDirect") });

        if (_gateways is not null)
        {
            foreach (var gw in _gateways)
            {
                var label = $"{gw.Name} ({gw.Host}:{gw.Port})";
                CmbRouteVia.Items.Add(new ComboBoxItem { Content = label, Tag = gw });
            }
        }

        CmbRouteVia.SelectedIndex = 0;
    }

    private void OnRouteViaChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbRouteVia.SelectedItem is ComboBoxItem item && item.Tag is SshGatewayDto gw)
        {
            _selectedGateway = gw;
        }
        else
        {
            _selectedGateway = null;
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{L("ToolTraceColHop"),-5}{L("ToolTraceColAddress"),-18}{L("ToolTraceColHostname"),-30}{L("ToolTraceColLatency"),-20}{L("ToolTraceColStatus")}");
            sb.AppendLine(new string('-', 85));

            foreach (var r in _results)
            {
                sb.AppendLine($"{r.Hop,-5}{r.Address,-18}{r.Hostname,-30}{r.Latency,-20}{r.Status}");
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Traceroute clipboard copy failed: {ex.Message}");
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpTCPTRACE");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isTracing;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTrace();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents a single hop result in the traceroute output.
    /// </summary>
    public sealed record TraceHop
    {
        public int Hop { get; init; }
        public string Address { get; init; } = "";
        public string Hostname { get; init; } = "";
        public string Latency { get; init; } = "";
        public string Status { get; init; } = "";
        public Brush StatusBrush { get; init; } = Brushes.Transparent;
    }

    // Linux traceroute: line starts with hop number followed by data
    [GeneratedRegex(@"^\s*(\d+)\s+(.+)$")]
    private static partial Regex LinuxTracerouteRegex();

    // Windows tracert: "  1    <1 ms    <1 ms    <1 ms  10.0.0.1"
    [GeneratedRegex(@"^\s*(\d+)\s+([\d<*]+\s*ms|\*)\s+([\d<*]+\s*ms|\*)\s+([\d<*]+\s*ms|\*)\s+(\S+)\s*$")]
    private static partial Regex WindowsTracertRegex();
}
