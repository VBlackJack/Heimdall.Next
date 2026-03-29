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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Ssh;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// DNS record lookup tool supporting A, AAAA, MX, CNAME, TXT, NS, PTR, SOA, and ANY record types.
/// </summary>
public partial class DnsLookupView : UserControl, IToolView
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] RecordTypes =
        ["A", "AAAA", "MX", "CNAME", "TXT", "NS", "PTR", "SOA", "ANY"];

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isQuerying;
    private Action<bool>? _setBusy;
    private readonly ToolAsyncStateController _viewState;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;

    public DnsLookupView()
    {
        InitializeComponent();
        _viewState = new ToolAsyncStateController(
            isBusy => _setBusy?.Invoke(isBusy),
            LoadingBar,
            TxtError,
            EmptyStatePanel,
            ResultsPanel,
            TxtStatus,
            BtnLookup,
            TxtHostname,
            CmbRecordType,
            CmbDnsServer,
            CmbRouteVia);

        foreach (var type in RecordTypes)
        {
            CmbRecordType.Items.Add(new ComboBoxItem { Content = type });
        }
        CmbRecordType.SelectedIndex = 0;

        foreach (var preset in NetworkToolPresets.DnsServers)
        {
            CmbDnsServer.Items.Add(new ComboBoxItem { Content = preset.Label });
        }
        CmbDnsServer.SelectedIndex = 0;

        TxtHostname.KeyDown += OnHostnameKeyDown;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();
        TxtHostname.Clear();

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHostname.Text = context.TargetHost;
        }

        // Populate SSH gateway selector for tunnel-based DNS lookups
        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHostname.Focus();
            TxtHostname.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolDnsTitle");
        LblHostname.Text = L("ToolDnsHostnameLabel");
        BtnLookup.Content = L("ToolDnsBtnLookup");
        TxtStatus.Text = string.Empty;

        BtnCopyResults.Content = L("ToolDnsBtnCopyResults");
        BtnCopyResults.ToolTip = L("ToolBtnCopyToClipboard");

        System.Windows.Automation.AutomationProperties.SetName(BtnLookup, L("ToolDnsBtnLookup"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHostname, L("ToolDnsHostnameLabel"));
        System.Windows.Automation.AutomationProperties.SetName(CmbRecordType, L("ToolDnsRecordTypeLabel"));
        System.Windows.Automation.AutomationProperties.SetName(CmbDnsServer, L("ToolDnsServerLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyResults, L("ToolDnsBtnCopyResults"));

        LblRouteVia.Text = L("ToolTunnelRouteVia");
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(LoadingBar, L("ToolDnsA11yLoading"));

        TxtHostname.Tag = L("ToolWatermarkExampleDomain");
        TxtEmptyState.Text = L("ToolEmptyStateDns");
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpDNS").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private void OnHostnameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = PerformLookupAsync();
            e.Handled = true;
        }
    }

    private void OnLookupClick(object sender, RoutedEventArgs e)
    {
        _ = PerformLookupAsync();
    }

    private async Task PerformLookupAsync()
    {
        if (_isQuerying)
        {
            return;
        }

        var hostname = TxtHostname.Text.Trim();
        _viewState.Reset();

        if (string.IsNullOrWhiteSpace(hostname))
        {
            _viewState.ShowError(L("ToolValidationHostRequired"), string.Empty);
            return;
        }

        if (!InputValidator.ValidateDomain(hostname))
        {
            _viewState.ShowError(L("ToolValidationInvalidHost"), string.Empty);
            return;
        }

        var recordType = (CmbRecordType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A";
        var serverIndex = CmbDnsServer.SelectedIndex;
        string? dnsServer = serverIndex >= 0 && serverIndex < NetworkToolPresets.DnsServers.Length
            ? NetworkToolPresets.DnsServers[serverIndex].Address
            : null;

        // Cancel any previous lookup
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(LookupTimeout);

        _isQuerying = true;
        _viewState.Begin(L("ToolDnsStatusQuerying"));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            string results;

            if (_selectedGateway is not null)
            {
                results = await LookupViaTunnelAsync(hostname, recordType, dnsServer, _cts.Token);
            }
            else if (recordType is "A" or "AAAA" && dnsServer is null)
            {
                results = await LookupHostEntryAsync(hostname, recordType, _cts.Token);
            }
            else
            {
                results = await LookupViaNslookupAsync(hostname, recordType, dnsServer, _cts.Token);
            }

            stopwatch.Stop();

            if (_cts.IsCancellationRequested)
            {
                return;
            }

            TxtResultHeader.Text = string.Format(L("ToolDnsResultHeader"), recordType, hostname);
            TxtResults.Text = results;
            _viewState.ShowResults(string.Format(L("ToolDnsStatusComplete"), stopwatch.ElapsedMilliseconds));
        }
        catch (OperationCanceledException)
        {
            _viewState.ShowError(L("ToolDnsErrorTimeout"), string.Empty);
        }
        catch (SocketException ex)
        {
            _viewState.ShowError(string.Format(L("ToolDnsErrorLookupFailed"), ex.Message), string.Empty);
        }
        catch (Exception ex)
        {
            _viewState.ShowError(string.Format(L("ToolDnsErrorLookupFailed"), ex.Message), string.Empty);
        }
        finally
        {
            _isQuerying = false;
            _viewState.End();
        }
    }

    public bool CanClose() => !_isQuerying;

    private async Task<string> LookupHostEntryAsync(
        string hostname, string recordType, CancellationToken ct)
    {
        var entry = await Dns.GetHostEntryAsync(hostname, ct);
        var sb = new StringBuilder();

        foreach (var address in entry.AddressList)
        {
            var isIpv4 = address.AddressFamily == AddressFamily.InterNetwork;
            var isIpv6 = address.AddressFamily == AddressFamily.InterNetworkV6;

            if (recordType == "A" && isIpv4)
            {
                sb.AppendLine(address.ToString());
            }
            else if (recordType == "AAAA" && isIpv6)
            {
                sb.AppendLine(address.ToString());
            }
        }

        if (sb.Length == 0)
        {
            sb.AppendLine(recordType == "A"
                ? L("ToolDnsNoIpv4")
                : L("ToolDnsNoIpv6"));
        }

        if (entry.Aliases.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(L("ToolDnsAliases"));
            foreach (var alias in entry.Aliases)
            {
                sb.AppendLine($"  {alias}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task<string> LookupViaNslookupAsync(
        string hostname, string recordType, string? dnsServer, CancellationToken ct)
    {
        var arguments = dnsServer is not null
            ? $"-type={recordType} {hostname} {dnsServer}"
            : $"-type={recordType} {hostname}";

        var psi = new ProcessStartInfo
        {
            FileName = "nslookup",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var output = await outputTask;
        var error = await errorTask;

        var parsed = ParseNslookupOutput(output, recordType);

        if (string.IsNullOrWhiteSpace(parsed) && !string.IsNullOrWhiteSpace(error))
        {
            return error.Trim();
        }

        return string.IsNullOrWhiteSpace(parsed)
            ? output.Trim()
            : parsed;
    }

    /// <summary>
    /// Parses nslookup output into structured results, skipping the server information header.
    /// </summary>
    private static string ParseNslookupOutput(string output, string recordType)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        var lines = output.Split('\n');
        var sb = new StringBuilder();
        var pastHeader = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip the initial "Server:" and "Address:" header block
            if (!pastHeader)
            {
                if (line.Length == 0 && sb.Length == 0)
                {
                    continue;
                }

                // The header ends after the DNS server's address line followed by a blank line
                if (line.Length == 0)
                {
                    pastHeader = true;
                    continue;
                }

                if (line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Address:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // If we hit a non-header line before a blank, we are past the header
                pastHeader = true;
            }

            if (pastHeader && line.Length > 0)
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString().TrimEnd();
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

    private static Renci.SshNet.SshClient ConnectToGateway(SshGatewayDto gateway)
        => ToolGatewayConnector.Connect(gateway);

    /// <summary>
    /// Performs a DNS lookup remotely via an SSH gateway using dig or nslookup.
    /// </summary>
    private async Task<string> LookupViaTunnelAsync(
        string hostname, string recordType, string? dnsServer, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var client = ConnectToGateway(_selectedGateway!);
            try
            {
                // Escape all user-controlled values for safe shell interpolation
                var safeHostname = InputValidator.EscapeShellArg(hostname);
                var safeRecordType = InputValidator.EscapeShellArg(recordType);
                var safeDnsServer = dnsServer is not null ? InputValidator.EscapeShellArg(dnsServer) : null;

                // Try dig first (more commonly available on Linux servers)
                var serverArg = safeDnsServer is not null ? $"@{safeDnsServer} " : "";
                var digCommand = $"dig {serverArg}{safeHostname} {safeRecordType} +noall +answer 2>/dev/null";

                using var digCmd = client.CreateCommand(digCommand);
                digCmd.CommandTimeout = TimeSpan.FromSeconds(8);
                var digResult = digCmd.Execute()?.Trim();

                if (!string.IsNullOrWhiteSpace(digResult))
                {
                    return digResult;
                }

                // Fall back to nslookup
                var nslookupArgs = safeDnsServer is not null
                    ? $"-type={safeRecordType} {safeHostname} {safeDnsServer}"
                    : $"-type={safeRecordType} {safeHostname}";
                using var nsCmd = client.CreateCommand($"nslookup {nslookupArgs} 2>&1");
                nsCmd.CommandTimeout = TimeSpan.FromSeconds(8);
                var nsResult = nsCmd.Execute()?.Trim();

                if (!string.IsNullOrWhiteSpace(nsResult))
                {
                    return ParseNslookupOutput(nsResult, recordType);
                }

                // Fall back to host command
                var hostDnsArg = safeDnsServer ?? "";
                using var hostCmd = client.CreateCommand($"host -t {safeRecordType} {safeHostname} {hostDnsArg} 2>&1");
                hostCmd.CommandTimeout = TimeSpan.FromSeconds(8);
                return hostCmd.Execute()?.Trim() ?? L("ToolDnsNoResults");
            }
            finally
            {
                try { client.Disconnect(); } catch { /* best effort */ }
            }
        }, ct).ConfigureAwait(false);
    }

    private void OnCopyResultsClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtResults.Text))
        {
            try { Clipboard.SetText(TxtResults.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _setBusy?.Invoke(false);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}
