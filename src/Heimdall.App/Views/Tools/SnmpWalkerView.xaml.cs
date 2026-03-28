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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Configuration;
using Heimdall.Core.Discovery;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Ssh;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// SNMP Walker tool that queries SNMP agents to enumerate device information
/// by iterating GET-NEXT requests over a subtree.
/// </summary>
public partial class SnmpWalkerView : UserControl, IToolView
{
    private const int SnmpPort = 161;
    private const int DefaultTimeoutMs = 3000;
    private const int MaxWalkResults = 10000;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isWalking;
    private bool _disposed;
    private Action<bool>? _setBusy;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;

    private readonly ObservableCollection<SnmpEntry> _results = [];
    private readonly ObservableCollection<CommunityResult> _communityResults = [];

    /// <summary>
    /// Common community strings used for bruteforce testing.
    /// </summary>
    private static readonly string[] CommonCommunities =
    [
        "public", "private", "community", "default", "snmp", "monitor",
        "admin", "manager", "test", "cisco", "secret", "write"
    ];

    /// <summary>
    /// Well-known OID-to-name mappings for common MIB-2 objects.
    /// </summary>
    private static readonly Dictionary<string, string> WellKnownOids = new(StringComparer.Ordinal)
    {
        // system group
        ["1.3.6.1.2.1.1.1"] = "sysDescr",
        ["1.3.6.1.2.1.1.1.0"] = "sysDescr.0",
        ["1.3.6.1.2.1.1.2"] = "sysObjectID",
        ["1.3.6.1.2.1.1.2.0"] = "sysObjectID.0",
        ["1.3.6.1.2.1.1.3"] = "sysUpTime",
        ["1.3.6.1.2.1.1.3.0"] = "sysUpTime.0",
        ["1.3.6.1.2.1.1.4"] = "sysContact",
        ["1.3.6.1.2.1.1.4.0"] = "sysContact.0",
        ["1.3.6.1.2.1.1.5"] = "sysName",
        ["1.3.6.1.2.1.1.5.0"] = "sysName.0",
        ["1.3.6.1.2.1.1.6"] = "sysLocation",
        ["1.3.6.1.2.1.1.6.0"] = "sysLocation.0",
        ["1.3.6.1.2.1.1.7"] = "sysServices",
        ["1.3.6.1.2.1.1.7.0"] = "sysServices.0",

        // interfaces group
        ["1.3.6.1.2.1.2.1"] = "ifNumber",
        ["1.3.6.1.2.1.2.1.0"] = "ifNumber.0",
        ["1.3.6.1.2.1.2.2"] = "ifTable",
        ["1.3.6.1.2.1.2.2.1.1"] = "ifIndex",
        ["1.3.6.1.2.1.2.2.1.2"] = "ifDescr",
        ["1.3.6.1.2.1.2.2.1.3"] = "ifType",
        ["1.3.6.1.2.1.2.2.1.4"] = "ifMtu",
        ["1.3.6.1.2.1.2.2.1.5"] = "ifSpeed",
        ["1.3.6.1.2.1.2.2.1.6"] = "ifPhysAddress",
        ["1.3.6.1.2.1.2.2.1.7"] = "ifAdminStatus",
        ["1.3.6.1.2.1.2.2.1.8"] = "ifOperStatus",
        ["1.3.6.1.2.1.2.2.1.10"] = "ifInOctets",
        ["1.3.6.1.2.1.2.2.1.16"] = "ifOutOctets",

        // IP group
        ["1.3.6.1.2.1.4.1"] = "ipForwarding",
        ["1.3.6.1.2.1.4.2"] = "ipDefaultTTL",
        ["1.3.6.1.2.1.4.20"] = "ipAddrTable",
        ["1.3.6.1.2.1.4.20.1.1"] = "ipAdEntAddr",
        ["1.3.6.1.2.1.4.20.1.2"] = "ipAdEntIfIndex",
        ["1.3.6.1.2.1.4.20.1.3"] = "ipAdEntNetMask",
        ["1.3.6.1.2.1.4.21"] = "ipRouteTable",

        // TCP group
        ["1.3.6.1.2.1.6.1"] = "tcpRtoAlgorithm",
        ["1.3.6.1.2.1.6.5"] = "tcpActiveOpens",
        ["1.3.6.1.2.1.6.6"] = "tcpPassiveOpens",
        ["1.3.6.1.2.1.6.9"] = "tcpCurrEstab",
        ["1.3.6.1.2.1.6.10"] = "tcpInSegs",
        ["1.3.6.1.2.1.6.11"] = "tcpOutSegs",
        ["1.3.6.1.2.1.6.13"] = "tcpConnTable",

        // UDP group
        ["1.3.6.1.2.1.7.1"] = "udpInDatagrams",
        ["1.3.6.1.2.1.7.2"] = "udpNoPorts",
        ["1.3.6.1.2.1.7.3"] = "udpInErrors",
        ["1.3.6.1.2.1.7.4"] = "udpOutDatagrams",
    };

    public SnmpWalkerView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        CommunityResults.ItemsSource = _communityResults;
        TxtHost.KeyDown += OnHostKeyDown;
        TxtOid.KeyDown += OnHostKeyDown;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        TxtHost.Text = "localhost";

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
        HeaderTitle.Text = L("ToolSnmpTitle");
        BtnWalk.Content = L("ToolSnmpBtnWalk");
        BtnCopy.Content = L("ToolSnmpBtnCopy");
        BtnExportCsv.Content = L("ToolSnmpBtnExport");
        BtnTestCommunities.Content = L("ToolSnmpBtnTestCommunities");

        ColOid.Header = L("ToolSnmpColOid");
        ColName.Header = L("ToolSnmpColName");
        ColType.Header = L("ToolSnmpColType");
        ColValue.Header = L("ToolSnmpColValue");

        BtnPresetSystem.Content = L("ToolSnmpPresetSystem");
        BtnPresetInterfaces.Content = L("ToolSnmpPresetInterfaces");
        BtnPresetIp.Content = L("ToolSnmpPresetIp");
        BtnPresetTcp.Content = L("ToolSnmpPresetTcp");
        BtnPresetUdp.Content = L("ToolSnmpPresetUdp");

        System.Windows.Automation.AutomationProperties.SetName(BtnWalk, L("ToolSnmpBtnWalk"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolSnmpBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportCsv, L("ToolSnmpBtnExport"));
        System.Windows.Automation.AutomationProperties.SetName(BtnTestCommunities, L("ToolSnmpBtnTestCommunities"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHost, L("ToolSnmpHostLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtCommunity, L("ToolSnmpCommunity"));
        System.Windows.Automation.AutomationProperties.SetName(TxtOid, L("ToolSnmpOid"));
        System.Windows.Automation.AutomationProperties.SetName(ResultsGrid, L("ToolSnmpTitle"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetSystem, L("ToolSnmpPresetSystem"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetInterfaces, L("ToolSnmpPresetInterfaces"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetIp, L("ToolSnmpPresetIp"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetTcp, L("ToolSnmpPresetTcp"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetUdp, L("ToolSnmpPresetUdp"));

        LblHost.Text = L("ToolSnmpHostLabel");
        LblCommunity.Text = L("ToolSnmpCommunity");
        LblOid.Text = L("ToolSnmpOid");

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
        System.Windows.Automation.AutomationProperties.SetName(LoadingBar, L("ToolSnmpA11yLoading"));

        LblRouteVia.Text = L("ToolTunnelRouteVia");
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        TxtHost.Tag = L("ToolWatermarkHostnameOrIp");
        TxtCommunity.Tag = L("ToolSnmpCommunity");
        TxtOid.Tag = L("ToolSnmpOid");
        TxtEmptyState.Text = L("ToolSnmpEmptyState");
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpSNMPWALK").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ToggleWalk();
            e.Handled = true;
        }
    }

    private void OnWalkClick(object sender, RoutedEventArgs e)
    {
        ToggleWalk();
    }

    private void ToggleWalk()
    {
        if (_isWalking)
        {
            StopWalk();
        }
        else
        {
            _ = StartWalkAsync();
        }
    }

    private void StopWalk()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isWalking = false;
        _setBusy?.Invoke(false);
        BtnWalk.Content = L("ToolSnmpBtnWalk");
        LoadingBar.Visibility = Visibility.Collapsed;
    }

    private async Task StartWalkAsync()
    {
        var host = TxtHost.Text.Trim();
        var community = TxtCommunity.Text.Trim();
        var oid = TxtOid.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(community))
        {
            community = "public";
        }

        if (string.IsNullOrWhiteSpace(oid))
        {
            oid = "1.3.6.1.2.1.1";
        }

        _results.Clear();
        _cts = new CancellationTokenSource();
        _isWalking = true;
        _setBusy?.Invoke(true);
        BtnWalk.Content = L("ToolSnmpBtnStop");
        LoadingBar.Visibility = Visibility.Visible;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultsBorder.Visibility = Visibility.Visible;
        TxtStatus.Text = string.Empty;

        try
        {
            if (_selectedGateway is not null)
            {
                await WalkViaTunnelAsync(host, community, oid, _cts.Token);
            }
            else
            {
                await WalkAsync(host, community, oid, _cts.Token);
            }

            TxtStatus.Text = string.Format(L("ToolSnmpProgress"), _results.Count);
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = string.Format(L("ToolSnmpProgress"), _results.Count);
        }
        catch (Exception ex)
        {
            if (_results.Count == 0)
            {
                TxtError.Text = string.Format(L("ToolSnmpErrorConnection"), ex.Message);
                TxtError.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Visible;
                ResultsBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtStatus.Text = string.Format(L("ToolSnmpProgress"), _results.Count);
            }
        }
        finally
        {
            _isWalking = false;
            _setBusy?.Invoke(false);
            BtnWalk.Content = L("ToolSnmpBtnWalk");
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    // ── SNMP Walk (direct UDP) ──────────────────────────────────────

    /// <summary>
    /// Walks an SNMP subtree by iterating GET-NEXT requests until the
    /// returned OID leaves the target subtree.
    /// </summary>
    private async Task WalkAsync(string host, string community, string startOid, CancellationToken ct)
    {
        var currentOid = startOid;
        var prefix = startOid + ".";

        while (!ct.IsCancellationRequested && _results.Count < MaxWalkResults)
        {
            var response = await GetNextAsync(host, community, currentOid, DefaultTimeoutMs, ct);

            if (response.Oid is null)
            {
                break;
            }

            // Stop if the returned OID has left the subtree
            if (!response.Oid.StartsWith(prefix, StringComparison.Ordinal) &&
                !string.Equals(response.Oid, startOid, StringComparison.Ordinal))
            {
                break;
            }

            // Detect endOfMibView or noSuchObject
            if (string.Equals(response.Type, "endOfMibView", StringComparison.Ordinal) ||
                string.Equals(response.Type, "noSuchObject", StringComparison.Ordinal) ||
                string.Equals(response.Type, "noSuchInstance", StringComparison.Ordinal))
            {
                break;
            }

            var entry = new SnmpEntry
            {
                Oid = response.Oid,
                Name = ResolveOidName(response.Oid),
                Type = response.Type,
                Value = response.Value
            };

            await Dispatcher.InvokeAsync(() =>
            {
                _results.Add(entry);
                TxtStatus.Text = string.Format(L("ToolSnmpProgress"), _results.Count);
            });

            currentOid = response.Oid;
        }
    }

    // ── SNMP Walk via SSH tunnel ────────────────────────────────────

    /// <summary>
    /// Performs an SNMP walk remotely via an SSH gateway using the snmpwalk command.
    /// </summary>
    private async Task WalkViaTunnelAsync(string host, string community, string oid, CancellationToken ct)
    {
        await Task.Run(async () =>
        {
            using var client = ToolGatewayConnector.Connect(_selectedGateway!);
            try
            {
                var command = $"snmpwalk -v2c -c {InputValidator.EscapeShellArg(community)} {InputValidator.EscapeShellArg(host)} {InputValidator.EscapeShellArg(oid)} 2>&1";
                using var cmd = client.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(30);
                var output = cmd.Execute()?.Trim();

                if (string.IsNullOrWhiteSpace(output))
                {
                    return;
                }

                foreach (var line in output.Split('\n'))
                {
                    ct.ThrowIfCancellationRequested();
                    var parsed = ParseSnmpWalkLine(line.Trim());
                    if (parsed is not null)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _results.Add(parsed);
                            TxtStatus.Text = string.Format(L("ToolSnmpProgress"), _results.Count);
                        });
                    }
                }
            }
            finally
            {
                try { client.Disconnect(); } catch { /* best effort */ }
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses a single line of snmpwalk output.
    /// Typical format: "iso.3.6.1.2.1.1.1.0 = STRING: Linux server 5.4.0"
    /// or: "SNMPv2-MIB::sysDescr.0 = STRING: ..."
    /// </summary>
    private static SnmpEntry? ParseSnmpWalkLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        // Split on " = " to separate OID from type:value
        var eqIdx = line.IndexOf(" = ", StringComparison.Ordinal);
        if (eqIdx < 0)
        {
            return null;
        }

        var oidPart = line[..eqIdx].Trim();
        var valuePart = line[(eqIdx + 3)..].Trim();

        // Normalize OID: replace "iso" prefix with "1"
        oidPart = oidPart.Replace("iso.", "1.", StringComparison.OrdinalIgnoreCase);
        if (oidPart.StartsWith("iso", StringComparison.OrdinalIgnoreCase))
        {
            oidPart = "1" + oidPart[3..];
        }

        // Remove MIB prefix if present (e.g., "SNMPv2-MIB::sysDescr.0")
        var colonIdx = oidPart.IndexOf("::", StringComparison.Ordinal);
        string displayName;
        if (colonIdx >= 0)
        {
            displayName = oidPart[(colonIdx + 2)..];
            // Keep the original OID part for display, but resolve name from after ::
        }
        else
        {
            displayName = ResolveOidName(oidPart);
        }

        // Parse type and value (e.g., "STRING: Linux server" or "INTEGER: 42")
        var colonValIdx = valuePart.IndexOf(": ", StringComparison.Ordinal);
        string type;
        string value;
        if (colonValIdx >= 0)
        {
            type = valuePart[..colonValIdx].Trim();
            value = valuePart[(colonValIdx + 2)..].Trim();
        }
        else
        {
            type = valuePart;
            value = string.Empty;
        }

        return new SnmpEntry
        {
            Oid = oidPart,
            Name = displayName,
            Type = type,
            Value = value
        };
    }

    // ── Community bruteforce ────────────────────────────────────────

    private void OnTestCommunitiesClick(object sender, RoutedEventArgs e)
    {
        if (_isWalking)
        {
            return;
        }

        _ = TestCommunitiesAsync();
    }

    private async Task TestCommunitiesAsync()
    {
        var host = TxtHost.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _communityResults.Clear();
        _isWalking = true;
        _setBusy?.Invoke(true);
        BtnTestCommunities.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        _cts = new CancellationTokenSource();

        try
        {
            foreach (var community in CommonCommunities)
            {
                _cts.Token.ThrowIfCancellationRequested();

                try
                {
                    var info = await UdpProbeEngine.QuerySnmpAsync(host, community, 2000, _cts.Token);
                    if (info is not null)
                    {
                        var result = new CommunityResult
                        {
                            Community = community,
                            Status = L("ToolSnmpCommunityAccepted"),
                            SysName = info.SysName ?? string.Empty
                        };
                        _communityResults.Add(result);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Timeout or error means community was rejected
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Walk was cancelled
        }
        finally
        {
            _isWalking = false;
            _setBusy?.Invoke(false);
            BtnTestCommunities.IsEnabled = true;
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    // ── Preset buttons ──────────────────────────────────────────────

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string oid)
        {
            TxtOid.Text = oid;
        }
    }

    // ── Route via gateway ───────────────────────────────────────────

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

    // ── Copy / Export ───────────────────────────────────────────────

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{L("ToolSnmpColOid"),-40}{L("ToolSnmpColName"),-24}{L("ToolSnmpColType"),-16}{L("ToolSnmpColValue")}");
            sb.AppendLine(new string('-', 90));

            foreach (var r in _results)
            {
                sb.AppendLine($"{r.Oid,-40}{r.Name,-24}{r.Type,-16}{r.Value}");
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"SNMP Walker clipboard copy failed: {ex.Message}");
        }
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = L("FileDialogCsvFilter"),
            FileName = $"snmpwalk_{TxtHost.Text.Trim()}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{L("ToolSnmpColOid")},{L("ToolSnmpColName")},{L("ToolSnmpColType")},{L("ToolSnmpColValue")}");

            foreach (var r in _results)
            {
                var oid = InputValidator.SanitizeCsvCell(r.Oid);
                var name = InputValidator.SanitizeCsvCell(r.Name);
                var type = InputValidator.SanitizeCsvCell(r.Type);
                var value = InputValidator.SanitizeCsvCell(r.Value).Replace("\"", "\"\"");
                sb.AppendLine($"{oid},{name},{type},\"{value}\"");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"SNMP Walker CSV export failed: {ex.Message}");
        }
    }

    // ── OID name resolution ─────────────────────────────────────────

    /// <summary>
    /// Resolves an OID to a human-readable name by checking well-known OIDs,
    /// then trying progressively shorter prefixes for table indices.
    /// </summary>
    private static string ResolveOidName(string oid)
    {
        if (WellKnownOids.TryGetValue(oid, out var name))
        {
            return name;
        }

        // Try stripping trailing instance indices to find the column OID
        var dotIdx = oid.LastIndexOf('.');
        while (dotIdx > 0)
        {
            var prefix = oid[..dotIdx];
            if (WellKnownOids.TryGetValue(prefix, out var baseName))
            {
                var suffix = oid[(dotIdx + 1)..];
                return $"{baseName}.{suffix}";
            }
            dotIdx = prefix.LastIndexOf('.');
        }

        return string.Empty;
    }

    // ── ASN.1/BER encoding (SNMPv2c packet construction) ────────────

    /// <summary>
    /// Sends an SNMPv2c GET-NEXT request and parses the response.
    /// Uses the same ASN.1/BER encoding pattern as UdpProbeEngine.
    /// </summary>
    private static async Task<(string? Oid, string Type, string Value)> GetNextAsync(
        string host, string community, string oid, int timeoutMs, CancellationToken ct)
    {
        var oidComponents = ParseOidString(oid);
        if (oidComponents is null)
        {
            return (null, string.Empty, string.Empty);
        }

        using var udp = new UdpClient();

        var packet = BuildSnmpGetNextRequest(community, oidComponents);
        var endpoint = new IPEndPoint(
            (await Dns.GetHostAddressesAsync(host, ct))[0],
            SnmpPort);
        await udp.SendAsync(packet, packet.Length, endpoint).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
        return ParseSnmpGetNextResponse(result.Buffer);
    }

    /// <summary>
    /// Parses a dotted-decimal OID string into component integers.
    /// </summary>
    private static int[]? ParseOidString(string oid)
    {
        var parts = oid.Split('.');
        var components = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out components[i]))
            {
                return null;
            }
        }
        return components;
    }

    /// <summary>
    /// Builds an SNMPv2c GET-NEXT request packet (PDU type 0xA1).
    /// </summary>
    private static byte[] BuildSnmpGetNextRequest(string community, int[] oid)
    {
        var requestId = Random.Shared.Next(1, 0x7FFFFFFF);

        // Varbind: OID + NULL value
        var oidBytes = Asn1EncodeOid(oid);
        var nullVal = new byte[] { 0x05, 0x00 };
        var varbind = Asn1Wrap(0x30, [.. oidBytes, .. nullVal]);
        var varbindList = Asn1Wrap(0x30, varbind);

        // PDU: GET-NEXT (0xA1)
        var reqIdBytes = Asn1EncodeInteger(requestId);
        var errorStatus = Asn1EncodeInteger(0);
        var errorIndex = Asn1EncodeInteger(0);
        var pdu = Asn1Wrap(0xA1, [.. reqIdBytes, .. errorStatus, .. errorIndex, .. varbindList]);

        // Message: version(1=SNMPv2c) + community + PDU
        var version = Asn1EncodeInteger(1);
        var communityBytes = Asn1EncodeOctetString(community);
        return Asn1Wrap(0x30, [.. version, .. communityBytes, .. pdu]);
    }

    /// <summary>
    /// Parses an SNMPv2c GET-NEXT response and extracts the returned OID, type, and value.
    /// </summary>
    private static (string? Oid, string Type, string Value) ParseSnmpGetNextResponse(byte[] data)
    {
        try
        {
            var (_, messageContent) = Asn1ReadTlv(data, 0);
            if (messageContent is null)
            {
                return (null, string.Empty, string.Empty);
            }

            var offset = 0;

            // Skip version
            var (vLen, _) = Asn1ReadTlv(messageContent, offset);
            offset += vLen;

            // Skip community
            var (cLen, _2) = Asn1ReadTlv(messageContent, offset);
            offset += cLen;

            // PDU (GetResponse = 0xA2)
            var (_, pduContent) = Asn1ReadTlv(messageContent, offset);
            if (pduContent is null)
            {
                return (null, string.Empty, string.Empty);
            }

            // Skip requestId, errorStatus, errorIndex
            var pduOffset = 0;
            for (var i = 0; i < 3; i++)
            {
                var (skip, _3) = Asn1ReadTlv(pduContent, pduOffset);
                pduOffset += skip;
            }

            // Varbind list
            var (_, varbindListContent) = Asn1ReadTlv(pduContent, pduOffset);
            if (varbindListContent is null)
            {
                return (null, string.Empty, string.Empty);
            }

            // First varbind
            var (_, vbContent) = Asn1ReadTlv(varbindListContent, 0);
            if (vbContent is null)
            {
                return (null, string.Empty, string.Empty);
            }

            // Read OID
            var (oidLen, oidContent) = Asn1ReadTlv(vbContent, 0);
            if (oidContent is null || vbContent[0] != 0x06)
            {
                return (null, string.Empty, string.Empty);
            }
            var returnedOid = Asn1DecodeOid(oidContent);

            // Read value
            if (oidLen >= vbContent.Length)
            {
                return (returnedOid, string.Empty, string.Empty);
            }

            var valueTag = vbContent[oidLen];
            var (_, valContent) = Asn1ReadTlv(vbContent, oidLen);

            var (type, value) = DecodeSnmpValue(valueTag, valContent);
            return (returnedOid, type, value);
        }
        catch
        {
            return (null, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Decodes an SNMP value based on its ASN.1 tag.
    /// </summary>
    private static (string Type, string Value) DecodeSnmpValue(byte tag, byte[]? content)
    {
        if (content is null || content.Length == 0)
        {
            return (GetTagName(tag), string.Empty);
        }

        return tag switch
        {
            0x02 => ("INTEGER", Asn1DecodeSignedInt(content).ToString()),
            0x04 => ("STRING", DecodeOctetString(content)),
            0x05 => ("NULL", string.Empty),
            0x06 => ("OID", Asn1DecodeOid(content) ?? string.Empty),
            0x40 => ("IpAddress", DecodeIpAddress(content)),
            0x41 => ("Counter32", Asn1DecodeUnsignedInt(content).ToString()),
            0x42 => ("Gauge32", Asn1DecodeUnsignedInt(content).ToString()),
            0x43 => ("TimeTicks", FormatTimeTicks(Asn1DecodeUnsignedInt(content))),
            0x44 => ("Opaque", Convert.ToHexString(content)),
            0x46 => ("Counter64", Asn1DecodeUnsignedInt(content).ToString()),
            0x80 => ("noSuchObject", string.Empty),
            0x81 => ("noSuchInstance", string.Empty),
            0x82 => ("endOfMibView", string.Empty),
            _ => (GetTagName(tag), Convert.ToHexString(content))
        };
    }

    private static string GetTagName(byte tag)
    {
        return tag switch
        {
            0x02 => "INTEGER",
            0x04 => "STRING",
            0x05 => "NULL",
            0x06 => "OID",
            0x40 => "IpAddress",
            0x41 => "Counter32",
            0x42 => "Gauge32",
            0x43 => "TimeTicks",
            0x44 => "Opaque",
            0x46 => "Counter64",
            0x80 => "noSuchObject",
            0x81 => "noSuchInstance",
            0x82 => "endOfMibView",
            _ => $"0x{tag:X2}"
        };
    }

    private static string DecodeOctetString(byte[] content)
    {
        // Check if the content is printable ASCII/UTF-8
        var isPrintable = true;
        foreach (var b in content)
        {
            if (b < 0x20 && b != 0x0A && b != 0x0D && b != 0x09)
            {
                isPrintable = false;
                break;
            }
        }

        return isPrintable
            ? Encoding.UTF8.GetString(content).Trim()
            : string.Join("-", content.Select(b => b.ToString("X2")));
    }

    private static string DecodeIpAddress(byte[] content)
    {
        if (content.Length == 4)
        {
            return $"{content[0]}.{content[1]}.{content[2]}.{content[3]}";
        }
        return Convert.ToHexString(content);
    }

    private static string FormatTimeTicks(long centiseconds)
    {
        var ts = TimeSpan.FromMilliseconds(centiseconds * 10);
        return $"{(int)ts.TotalDays}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2} ({centiseconds / 100}s)";
    }

    // ── ASN.1/BER primitives ────────────────────────────────────────
    // These replicate the encoding logic from UdpProbeEngine since those
    // methods are not publicly accessible from the App layer.

    private static byte[] Asn1Wrap(byte tag, byte[] content)
    {
        var length = Asn1EncodeLength(content.Length);
        var result = new byte[1 + length.Length + content.Length];
        result[0] = tag;
        Array.Copy(length, 0, result, 1, length.Length);
        Array.Copy(content, 0, result, 1 + length.Length, content.Length);
        return result;
    }

    private static byte[] Asn1EncodeLength(int length)
    {
        if (length < 0x80)
            return [(byte)length];
        if (length <= 0xFF)
            return [0x81, (byte)length];
        return [0x82, (byte)(length >> 8), (byte)(length & 0xFF)];
    }

    private static byte[] Asn1EncodeOid(int[] oid)
    {
        var bytes = new List<byte>();
        if (oid.Length >= 2)
            bytes.Add((byte)(40 * oid[0] + oid[1]));

        for (var i = 2; i < oid.Length; i++)
        {
            var val = oid[i];
            if (val < 0x80)
            {
                bytes.Add((byte)val);
            }
            else
            {
                var encoded = new Stack<byte>();
                encoded.Push((byte)(val & 0x7F));
                val >>= 7;
                while (val > 0)
                {
                    encoded.Push((byte)(0x80 | (val & 0x7F)));
                    val >>= 7;
                }
                while (encoded.Count > 0) bytes.Add(encoded.Pop());
            }
        }

        return Asn1Wrap(0x06, [.. bytes]);
    }

    private static byte[] Asn1EncodeInteger(int value)
    {
        var bytes = new List<byte>();
        if (value == 0)
        {
            bytes.Add(0);
        }
        else
        {
            var temp = value;
            var parts = new Stack<byte>();
            while (temp > 0)
            {
                parts.Push((byte)(temp & 0xFF));
                temp >>= 8;
            }
            if ((parts.Peek() & 0x80) != 0)
                parts.Push(0);
            while (parts.Count > 0) bytes.Add(parts.Pop());
        }

        return Asn1Wrap(0x02, [.. bytes]);
    }

    private static byte[] Asn1EncodeOctetString(string value)
    {
        return Asn1Wrap(0x04, Encoding.ASCII.GetBytes(value));
    }

    /// <summary>
    /// Reads a TLV (Tag-Length-Value) structure from an ASN.1 buffer.
    /// </summary>
    private static (int TotalLength, byte[]? Value) Asn1ReadTlv(byte[] data, int offset)
    {
        if (offset >= data.Length) return (0, null);

        var start = offset;
        offset++; // Skip tag

        if (offset >= data.Length) return (offset - start, null);

        int length;
        if ((data[offset] & 0x80) == 0)
        {
            length = data[offset];
            offset++;
        }
        else
        {
            var numBytes = data[offset] & 0x7F;
            offset++;
            length = 0;
            for (var i = 0; i < numBytes && offset < data.Length; i++)
            {
                length = (length << 8) | data[offset];
                offset++;
            }
        }

        if (offset + length > data.Length)
            length = data.Length - offset;

        var value = new byte[length];
        Array.Copy(data, offset, value, 0, length);
        return (offset - start + length, value);
    }

    /// <summary>
    /// Decodes a BER-encoded OID value (content bytes only).
    /// </summary>
    private static string? Asn1DecodeOid(byte[] content)
    {
        if (content.Length < 1) return null;
        var components = new List<int>
        {
            content[0] / 40,
            content[0] % 40
        };

        var val = 0;
        for (var i = 1; i < content.Length; i++)
        {
            val = (val << 7) | (content[i] & 0x7F);
            if ((content[i] & 0x80) == 0)
            {
                components.Add(val);
                val = 0;
            }
        }
        return string.Join('.', components);
    }

    /// <summary>
    /// Decodes a BER-encoded unsigned integer (Counter, Gauge, TimeTicks).
    /// </summary>
    private static long Asn1DecodeUnsignedInt(byte[] content)
    {
        long result = 0;
        foreach (var b in content)
        {
            result = (result << 8) | b;
        }
        return result;
    }

    /// <summary>
    /// Decodes a BER-encoded signed integer (INTEGER).
    /// </summary>
    private static long Asn1DecodeSignedInt(byte[] content)
    {
        if (content.Length == 0) return 0;
        long result = (content[0] & 0x80) != 0 ? -1L : 0L;
        foreach (var b in content)
        {
            result = (result << 8) | b;
        }
        return result;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isWalking;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _setBusy?.Invoke(false);
        StopWalk();
        GC.SuppressFinalize(this);
    }

    // ── Data models ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a single OID entry returned by an SNMP walk.
    /// </summary>
    public sealed class SnmpEntry
    {
        public string Oid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }

    /// <summary>
    /// Result of testing a community string against an SNMP agent.
    /// </summary>
    public sealed class CommunityResult
    {
        public string Community { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string SysName { get; init; } = string.Empty;
    }
}
