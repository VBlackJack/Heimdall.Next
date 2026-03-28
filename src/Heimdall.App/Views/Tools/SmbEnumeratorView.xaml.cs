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
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.Core.Configuration;
using Heimdall.Core.Discovery;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// SMB Enumerator tool that probes SMB/NTLM on a target host to extract
/// computer identity, domain information, and OS fingerprint without credentials.
/// Uses NTLMSSP Type 1/Type 2 exchange on port 445 and NetBIOS NBSTAT on UDP 137.
/// </summary>
public partial class SmbEnumeratorView : UserControl, IToolView
{
    private const int NtlmTimeoutMs = 5000;
    private const int NetBiosTimeoutMs = 3000;
    private const int TunnelCommandTimeoutSeconds = 10;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isEnumerating;
    private Action<bool>? _setBusy;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;

    /// <summary>Stores the last enumeration report for clipboard copy.</summary>
    private string _lastReport = string.Empty;

    public SmbEnumeratorView()
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
        HeaderTitle.Text = L("ToolSmbTitle");
        LblHost.Text = L("ToolSmbHostLabel");
        BtnEnumerate.Content = L("ToolSmbBtnEnum");
        BtnCopy.Content = L("ToolSmbBtnCopy");
        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        TxtStatus.Text = string.Empty;

        TxtCardIdentity.Text = L("ToolSmbCardIdentity");
        TxtCardProtocol.Text = L("ToolSmbCardProtocol");
        TxtCardFindings.Text = L("ToolSmbFindings");

        LblComputerName.Text = L("ToolSmbComputerName");
        LblDomain.Text = L("ToolSmbDomain");
        LblDnsName.Text = L("ToolSmbDnsName");
        LblDnsDomain.Text = L("ToolSmbDnsDomain");
        LblForest.Text = L("ToolSmbForest");
        LblOsBuild.Text = L("ToolSmbOsBuild");
        LblMac.Text = L("ToolSmbMac");

        LblDialect.Text = L("ToolSmbDialect");
        LblSigning.Text = L("ToolSmbSigning");
        LblServerGuid.Text = L("ToolSmbServerGuid");
        LblSystemTime.Text = L("ToolSmbSystemTime");
        LblBootTime.Text = L("ToolSmbBootTime");

        LblRouteVia.Text = L("ToolTunnelRouteVia");
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        System.Windows.Automation.AutomationProperties.SetName(TxtHost, L("ToolSmbHostLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnEnumerate, L("ToolSmbBtnEnum"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolSmbBtnCopy"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(LoadingBar, L("ToolSmbA11yLoading"));
        System.Windows.Automation.AutomationProperties.SetName(ValComputerName, L("ToolSmbComputerName"));
        System.Windows.Automation.AutomationProperties.SetName(ValDomain, L("ToolSmbDomain"));
        System.Windows.Automation.AutomationProperties.SetName(ValDnsName, L("ToolSmbDnsName"));
        System.Windows.Automation.AutomationProperties.SetName(ValDnsDomain, L("ToolSmbDnsDomain"));
        System.Windows.Automation.AutomationProperties.SetName(ValForest, L("ToolSmbForest"));
        System.Windows.Automation.AutomationProperties.SetName(ValOsBuild, L("ToolSmbOsBuild"));
        System.Windows.Automation.AutomationProperties.SetName(ValMac, L("ToolSmbMac"));
        System.Windows.Automation.AutomationProperties.SetName(ValDialect, L("ToolSmbDialect"));
        System.Windows.Automation.AutomationProperties.SetName(ValSigning, L("ToolSmbSigning"));
        System.Windows.Automation.AutomationProperties.SetName(ValServerGuid, L("ToolSmbServerGuid"));
        System.Windows.Automation.AutomationProperties.SetName(ValSystemTime, L("ToolSmbSystemTime"));
        System.Windows.Automation.AutomationProperties.SetName(ValBootTime, L("ToolSmbBootTime"));

        TxtHost.Tag = L("ToolWatermarkHostnameOrIp");
        TxtEmptyState.Text = L("ToolSmbEmptyState");
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpSMBENUM");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = PerformEnumerationAsync();
            e.Handled = true;
        }
    }

    private void OnEnumerateClick(object sender, RoutedEventArgs e)
    {
        _ = PerformEnumerationAsync();
    }

    private async Task PerformEnumerationAsync()
    {
        var host = TxtHost.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        TxtStatus.Text = string.Empty;
        _lastReport = string.Empty;

        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            TxtError.Text = L("ErrorInvalidHost");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        _isEnumerating = true;
        _setBusy?.Invoke(true);
        BtnEnumerate.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (_selectedGateway is not null)
            {
                await EnumerateViaTunnelAsync(host, _cts.Token);
            }
            else
            {
                await EnumerateDirectAsync(host, _cts.Token);
            }

            stopwatch.Stop();

            if (!_cts.IsCancellationRequested)
            {
                TxtStatus.Text = $"{stopwatch.ElapsedMilliseconds} ms";
            }
        }
        catch (OperationCanceledException)
        {
            // Enumeration was cancelled
        }
        catch (Exception ex)
        {
            TxtError.Text = string.Format(L("ToolSmbErrorConnection"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
            Core.Logging.FileLogger.Warn($"SmbEnumerator failed for {host}: {ex.Message}");
        }
        finally
        {
            _isEnumerating = false;
            _setBusy?.Invoke(false);
            BtnEnumerate.IsEnabled = true;
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Enumerates SMB/NTLM information directly via TCP 445 and UDP 137.
    /// Runs both probes in parallel for speed.
    /// </summary>
    private async Task EnumerateDirectAsync(string host, CancellationToken ct)
    {
        var ntlmTask = Task.Run(
            () => NtlmProbe.ProbeWithSmbInfoAsync(host, NtlmTimeoutMs, ct), ct);
        var nbTask = Task.Run(
            () => UdpProbeEngine.QueryNetBiosAsync(host, NetBiosTimeoutMs, ct), ct);

        // Wait for both probes; do not fail entirely if one times out
        NtlmInfo? ntlm = null;
        SmbNegotiateInfo? smb = null;
        string? nbName = null;
        string? nbDomain = null;
        string? nbMac = null;
        string? ntlmError = null;
        var netBiosFailed = false;

        try
        {
            (ntlm, smb) = await ntlmTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ntlmError = ex.Message;
        }

        try
        {
            (nbName, nbDomain, nbMac) = await nbTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            netBiosFailed = true;
            Core.Logging.FileLogger.Log("DEBUG", $"NetBIOS query failed for {host}: {ex.Message}");
        }

        if (ntlm is null && smb is null && ntlmError is not null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtError.Text = string.Format(L("ToolSmbErrorNtlm"), ntlmError);
                TxtError.Visibility = Visibility.Visible;
            });
            return;
        }

        if (ntlm is null && smb is null && nbName is null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtError.Text = string.Format(L("ToolSmbErrorConnection"), "port 445 closed or filtered");
                TxtError.Visibility = Visibility.Visible;
            });
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            DisplayResults(ntlm, smb, nbName, nbDomain, nbMac, netBiosFailed);
        });
    }

    /// <summary>
    /// Enumerates SMB information remotely via an SSH gateway using smbclient and rpcclient.
    /// </summary>
    private async Task EnumerateViaTunnelAsync(string host, CancellationToken ct)
    {
        Renci.SshNet.SshClient? tunnelClient = null;
        try
        {
            tunnelClient = ToolGatewayConnector.Connect(_selectedGateway!);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"SmbEnumerator gateway connection failed: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                TxtError.Text = string.Format(L("ToolTunnelFailed"), ex.Message);
                TxtError.Visibility = Visibility.Visible;
            });
            return;
        }

        try
        {
            // Try smbclient first for basic enumeration
            var smbclientResult = await Task.Run(() =>
            {
                using var cmd = tunnelClient.CreateCommand($"smbclient -N -L //{InputValidator.EscapeShellArg(host)} 2>&1 | head -30");
                cmd.CommandTimeout = TimeSpan.FromSeconds(TunnelCommandTimeoutSeconds);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            // Try rpcclient for server info
            var rpcResult = await Task.Run(() =>
            {
                using var cmd = tunnelClient.CreateCommand(
                    $"rpcclient -U \"\" -N {InputValidator.EscapeShellArg(host)} -c \"srvinfo\" 2>&1 | head -10");
                cmd.CommandTimeout = TimeSpan.FromSeconds(TunnelCommandTimeoutSeconds);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            // Try nmblookup for NetBIOS info
            var nbResult = await Task.Run(() =>
            {
                using var cmd = tunnelClient.CreateCommand(
                    $"nmblookup -A {InputValidator.EscapeShellArg(host)} 2>&1 | head -20");
                cmd.CommandTimeout = TimeSpan.FromSeconds(TunnelCommandTimeoutSeconds);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                DisplayTunnelResults(smbclientResult, rpcResult, nbResult);
            });
        }
        finally
        {
            try { tunnelClient.Disconnect(); } catch { /* best effort */ }
            tunnelClient.Dispose();
        }
    }

    /// <summary>
    /// Populates the result cards with NTLM probe and NetBIOS query results.
    /// </summary>
    private void DisplayResults(
        NtlmInfo? ntlm,
        SmbNegotiateInfo? smb,
        string? nbName,
        string? nbDomain,
        string? nbMac,
        bool netBiosFailed)
    {
        var na = L("ToolSmbNotAvailable");

        // Host Identity card
        ValComputerName.Text = ntlm?.NetBiosComputerName ?? nbName ?? na;
        ValDomain.Text = ntlm?.NetBiosDomainName ?? nbDomain ?? na;
        ValDnsName.Text = ntlm?.DnsComputerName ?? na;
        ValDnsDomain.Text = ntlm?.DnsDomainName ?? na;
        ValForest.Text = ntlm?.DnsForestName ?? na;
        ValOsBuild.Text = ntlm?.OsBuild ?? na;
        ValMac.Text = nbMac ?? na;

        // SMB Protocol card
        if (smb is not null)
        {
            ValDialect.Text = FormatDialect(smb.DialectRevision);
            ValSigning.Text = smb.SigningRequired ? L("ToolSmbYes") : L("ToolSmbNo");
            ValServerGuid.Text = smb.ServerGuid ?? na;
            ValSystemTime.Text = smb.SystemTime?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? na;
            ValBootTime.Text = smb.ServerStartTime?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? na;
        }
        else
        {
            ValDialect.Text = na;
            ValSigning.Text = na;
            ValServerGuid.Text = na;
            ValSystemTime.Text = na;
            ValBootTime.Text = na;
        }

        // Security findings
        BuildSecurityFindings(ntlm, smb, netBiosFailed);

        // Build clipboard report
        _lastReport = BuildReport(ntlm, smb, nbName, nbDomain, nbMac);

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Displays tunnel-based results parsed from smbclient, rpcclient, and nmblookup output.
    /// </summary>
    private void DisplayTunnelResults(string? smbclientResult, string? rpcResult, string? nbResult)
    {
        var na = L("ToolSmbNotAvailable");
        var hasData = false;

        // Parse smbclient output for domain/OS
        string? domain = null;
        string? osInfo = null;
        string? serverName = null;

        if (!string.IsNullOrWhiteSpace(smbclientResult) &&
            !smbclientResult.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) &&
            !smbclientResult.Contains("NT_STATUS_", StringComparison.OrdinalIgnoreCase))
        {
            hasData = true;
            foreach (var line in smbclientResult.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Domain=", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse: Domain=[WORKGROUP] OS=[...] Server=[...]
                    domain = ExtractBracketedValue(trimmed, "Domain");
                    osInfo = ExtractBracketedValue(trimmed, "OS");
                    serverName = ExtractBracketedValue(trimmed, "Server");
                }
            }
        }

        // Parse rpcclient srvinfo output
        string? rpcServerName = null;
        string? rpcOsVersion = null;
        if (!string.IsNullOrWhiteSpace(rpcResult) &&
            !rpcResult.Contains("NT_STATUS_", StringComparison.OrdinalIgnoreCase))
        {
            hasData = true;
            foreach (var line in rpcResult.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("server_name", StringComparison.OrdinalIgnoreCase))
                {
                    rpcServerName = trimmed.Split(':').LastOrDefault()?.Trim();
                }
                else if (trimmed.Contains("os_version", StringComparison.OrdinalIgnoreCase))
                {
                    rpcOsVersion = trimmed.Split(':').LastOrDefault()?.Trim();
                }
            }
        }

        // Parse nmblookup output for NetBIOS name and MAC
        string? nbName = null;
        string? nbDomain = null;
        string? nbMac = null;
        if (!string.IsNullOrWhiteSpace(nbResult) &&
            !nbResult.Contains("name_query failed", StringComparison.OrdinalIgnoreCase))
        {
            hasData = true;
            foreach (var line in nbResult.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("<00>") && !trimmed.StartsWith("MAC", StringComparison.OrdinalIgnoreCase))
                {
                    var namePart = trimmed.Split('<')[0].Trim();
                    if (trimmed.Contains("<GROUP>", StringComparison.OrdinalIgnoreCase))
                    {
                        nbDomain ??= namePart;
                    }
                    else
                    {
                        nbName ??= namePart;
                    }
                }
                else if (trimmed.StartsWith("MAC Address", StringComparison.OrdinalIgnoreCase))
                {
                    nbMac = trimmed.Split('=').LastOrDefault()?.Trim();
                }
            }
        }

        if (!hasData)
        {
            TxtError.Text = string.Format(L("ToolSmbErrorConnection"), "port 445 closed or filtered");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        // Populate cards with tunnel data
        ValComputerName.Text = rpcServerName ?? nbName ?? na;
        ValDomain.Text = domain ?? nbDomain ?? na;
        ValDnsName.Text = na;
        ValDnsDomain.Text = na;
        ValForest.Text = na;
        ValOsBuild.Text = rpcOsVersion ?? osInfo ?? na;
        ValMac.Text = nbMac ?? na;

        // SMB Protocol card is not available via tunnel
        ValDialect.Text = serverName ?? na;
        ValSigning.Text = na;
        ValServerGuid.Text = na;
        ValSystemTime.Text = na;
        ValBootTime.Text = na;

        // No security findings in tunnel mode (would need raw protocol data)
        FindingsPanel.Visibility = Visibility.Collapsed;

        // Build a basic report
        var sb = new StringBuilder();
        sb.AppendLine($"{L("ToolSmbComputerName"),-14}: {ValComputerName.Text}");
        sb.AppendLine($"{L("ToolSmbDomain"),-14}: {ValDomain.Text}");
        sb.AppendLine($"{L("ToolSmbOsBuild"),-14}: {ValOsBuild.Text}");
        sb.AppendLine($"{L("ToolSmbMac"),-14}: {ValMac.Text}");
        sb.AppendLine($"{L("ToolSmbDialect"),-14}: {ValDialect.Text}");
        _lastReport = sb.ToString();

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Builds the security findings panel based on SMB negotiation results.
    /// </summary>
    private void BuildSecurityFindings(NtlmInfo? ntlm, SmbNegotiateInfo? smb, bool netBiosFailed)
    {
        FindingsList.Children.Clear();

        if (smb is null && ntlm is null)
        {
            FindingsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var hasFindings = false;

        // SMB signing check
        if (smb is not null)
        {
            if (!smb.SigningRequired)
            {
                AddFinding(L("ToolSmbSigningDisabled"), "WarningBrush", "\uE7BA");
                hasFindings = true;
            }
            else
            {
                AddFinding(L("ToolSmbSigningEnabled"), "SuccessBrush", "\uE73E");
                hasFindings = true;
            }

            // SMBv1 dialect check
            if (smb.DialectRevision < 0x0202)
            {
                AddFinding(L("ToolSmbV1Detected"), "ErrorBrush", "\uE730");
                hasFindings = true;
            }
            else
            {
                var dialectStr = FormatDialect(smb.DialectRevision);
                AddFinding(
                    string.Format(L("ToolSmbModernDialect"), dialectStr),
                    "SuccessBrush",
                    "\uE73E");
                hasFindings = true;
            }
        }

        // NetBIOS timeout note
        if (netBiosFailed)
        {
            AddFinding(L("ToolSmbNetBiosFailed"), "InfoBrush", "\uE946");
            hasFindings = true;
        }

        FindingsPanel.Visibility = hasFindings ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Adds a single security finding row with icon, color, and text.
    /// </summary>
    private void AddFinding(string text, string brushKey, string icon)
    {
        var brush = (Brush)FindResource(brushKey);

        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

        panel.Children.Add(new TextBlock
        {
            Text = icon,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = (double)FindResource("FontSizeBody"),
            Foreground = brush,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = (double)FindResource("FontSizeBody"),
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        System.Windows.Automation.AutomationProperties.SetName(panel, text);

        FindingsList.Children.Add(panel);
    }

    /// <summary>
    /// Formats the SMB dialect revision code to a human-readable string.
    /// </summary>
    private static string FormatDialect(ushort dialect) => dialect switch
    {
        0x0202 => "SMB 2.0.2",
        0x0210 => "SMB 2.1",
        0x0300 => "SMB 3.0",
        0x0302 => "SMB 3.0.2",
        0x0311 => "SMB 3.1.1",
        0x00FF => "SMBv1 (legacy)",
        _ => $"0x{dialect:X4}",
    };

    /// <summary>
    /// Builds a plain-text report from the enumeration results.
    /// </summary>
    private string BuildReport(
        NtlmInfo? ntlm,
        SmbNegotiateInfo? smb,
        string? nbName,
        string? nbDomain,
        string? nbMac)
    {
        var na = L("ToolSmbNotAvailable");
        var sb = new StringBuilder();

        sb.AppendLine(L("ToolSmbReportIdentity"));
        sb.AppendLine($"{L("ToolSmbComputerName"),-14}: {ntlm?.NetBiosComputerName ?? nbName ?? na}");
        sb.AppendLine($"{L("ToolSmbDomain"),-14}: {ntlm?.NetBiosDomainName ?? nbDomain ?? na}");
        sb.AppendLine($"{L("ToolSmbDnsName"),-14}: {ntlm?.DnsComputerName ?? na}");
        sb.AppendLine($"{L("ToolSmbDnsDomain"),-14}: {ntlm?.DnsDomainName ?? na}");
        sb.AppendLine($"{L("ToolSmbForest"),-14}: {ntlm?.DnsForestName ?? na}");
        sb.AppendLine($"{L("ToolSmbOsBuild"),-14}: {ntlm?.OsBuild ?? na}");
        sb.AppendLine($"{L("ToolSmbMac"),-14}: {nbMac ?? na}");

        if (smb is not null)
        {
            sb.AppendLine();
            sb.AppendLine(L("ToolSmbReportProtocol"));
            sb.AppendLine($"{L("ToolSmbDialect"),-14}: {FormatDialect(smb.DialectRevision)}");
            sb.AppendLine($"{L("ToolSmbSigning"),-14}: {(smb.SigningRequired ? L("ToolSmbYes") : L("ToolSmbNo"))}");
            sb.AppendLine($"{L("ToolSmbServerGuid"),-14}: {smb.ServerGuid ?? na}");
            sb.AppendLine($"{L("ToolSmbSystemTime"),-14}: {smb.SystemTime?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? na}");
            sb.AppendLine($"{L("ToolSmbBootTime"),-14}: {smb.ServerStartTime?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? na}");
        }

        if (smb is not null)
        {
            sb.AppendLine();
            sb.AppendLine(L("ToolSmbReportFindings"));
            sb.AppendLine(smb.SigningRequired
                ? $"[OK]   {L("ToolSmbSigningEnabled")}"
                : $"[WARN] {L("ToolSmbSigningDisabled")}");

            sb.AppendLine(smb.DialectRevision < 0x0202
                ? $"[CRIT] {L("ToolSmbV1Detected")}"
                : $"[OK]   {string.Format(L("ToolSmbModernDialect"), FormatDialect(smb.DialectRevision))}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts a value from a bracketed key=value string (e.g., "Domain=[WORKGROUP]").
    /// </summary>
    private static string? ExtractBracketedValue(string line, string key)
    {
        var prefix = $"{key}=[";
        var idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var start = idx + prefix.Length;
        var end = line.IndexOf(']', start);
        return end > start ? line[start..end] : null;
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
        if (string.IsNullOrWhiteSpace(_lastReport))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_lastReport);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"SmbEnumerator clipboard copy failed: {ex.Message}");
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isEnumerating;

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
