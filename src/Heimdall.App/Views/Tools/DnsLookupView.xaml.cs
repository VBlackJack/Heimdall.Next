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

    private static readonly (string Label, string? Address)[] DnsServers =
    [
        ("System", null),
        ("Google (8.8.8.8)", "8.8.8.8"),
        ("Cloudflare (1.1.1.1)", "1.1.1.1"),
        ("Quad9 (9.9.9.9)", "9.9.9.9"),
    ];

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;

    public DnsLookupView()
    {
        InitializeComponent();

        foreach (var type in RecordTypes)
        {
            CmbRecordType.Items.Add(new ComboBoxItem { Content = type });
        }
        CmbRecordType.SelectedIndex = 0;

        foreach (var (label, _) in DnsServers)
        {
            CmbDnsServer.Items.Add(new ComboBoxItem { Content = label });
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
        ApplyLocalization();

        // Pre-fill with a sensible default; context overrides if provided
        TxtHostname.Text = "example.com";

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

        TxtHostname.Tag = L("ToolWatermarkExampleDomain");
        TxtEmptyState.Text = L("ToolEmptyStateDns");
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpDNS");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
        var hostname = TxtHostname.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        TxtStatus.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(hostname))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        var recordType = (CmbRecordType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A";
        var serverIndex = CmbDnsServer.SelectedIndex;
        string? dnsServer = serverIndex >= 0 && serverIndex < DnsServers.Length
            ? DnsServers[serverIndex].Address
            : null;

        // Cancel any previous lookup
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(LookupTimeout);

        BtnLookup.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        TxtStatus.Text = L("ToolDnsStatusQuerying");

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
            ResultsPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            TxtStatus.Text = string.Format(L("ToolDnsStatusComplete"), stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            TxtError.Text = L("ToolDnsErrorTimeout");
            TxtError.Visibility = Visibility.Visible;
        }
        catch (SocketException ex)
        {
            TxtError.Text = string.Format(L("ToolDnsErrorLookupFailed"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TxtError.Text = string.Format(L("ToolDnsErrorLookupFailed"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnLookup.IsEnabled = true;
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

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

    /// <summary>
    /// Creates a temporary SSH connection to the selected gateway.
    /// </summary>
    private static Renci.SshNet.SshClient ConnectToGateway(SshGatewayDto gateway)
    {
        string? password = null;
        if (!string.IsNullOrEmpty(gateway.SshPasswordEncrypted))
        {
            password = CredentialProtector.Unprotect(gateway.SshPasswordEncrypted);
        }

        var connParams = new SshConnectionParams
        {
            Host = gateway.Host,
            Port = gateway.Port,
            Username = gateway.User,
            KeyPath = gateway.KeyPath,
            Password = password,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        var connInfo = SshConnectionFactory.Create(connParams);
        var client = new Renci.SshNet.SshClient(connInfo);
        client.Connect();
        return client;
    }

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
                // Try dig first (more commonly available on Linux servers)
                var serverArg = dnsServer is not null ? $"@{dnsServer} " : "";
                var digCommand = $"dig {serverArg}{hostname} {recordType} +noall +answer 2>/dev/null";

                using var digCmd = client.CreateCommand(digCommand);
                digCmd.CommandTimeout = TimeSpan.FromSeconds(8);
                var digResult = digCmd.Execute()?.Trim();

                if (!string.IsNullOrWhiteSpace(digResult))
                {
                    return digResult;
                }

                // Fall back to nslookup
                var nslookupArgs = dnsServer is not null
                    ? $"-type={recordType} {hostname} {dnsServer}"
                    : $"-type={recordType} {hostname}";
                using var nsCmd = client.CreateCommand($"nslookup {nslookupArgs} 2>&1");
                nsCmd.CommandTimeout = TimeSpan.FromSeconds(8);
                var nsResult = nsCmd.Execute()?.Trim();

                if (!string.IsNullOrWhiteSpace(nsResult))
                {
                    return ParseNslookupOutput(nsResult, recordType);
                }

                // Fall back to host command
                using var hostCmd = client.CreateCommand($"host -t {recordType} {hostname} {dnsServer ?? ""} 2>&1");
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
            Clipboard.SetText(TxtResults.Text);
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
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}
