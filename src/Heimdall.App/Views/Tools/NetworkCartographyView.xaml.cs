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

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Discovery;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Network cartography tool that performs subnet-wide discovery with port scanning,
/// service identification, banner grabbing, and heuristic role classification.
/// </summary>
public partial class NetworkCartographyView : UserControl, IToolView
{
    private const int LargeSubnetThreshold = 512;
    private const int DefaultMaxConcurrency = 50;
    private const int DefaultProbeTimeoutMs = 2000;
    private const int MinCommandTimeoutSeconds = 10;
    private const int PortTimeoutDivisor = 2;

    private static readonly System.Text.RegularExpressions.Regex s_linuxInetRegex = new(
        @"inet\s+(\d+\.\d+\.\d+\.\d+/\d+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex s_ifconfigInetRegex = new(
        @"inet\s+(?:addr:)?(\d+\.\d+\.\d+\.\d+)\s+.*?(?:netmask|Mask:?)\s*(\d+\.\d+\.\d+\.\d+)",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _subnetDetectCts;
    private bool _isScanning;
    private bool _disposed;
    private NetworkScanSnapshot? _lastSnapshot;
    private List<(string FileName, DateTime Timestamp, string Subnet)> _historyList = [];
    private List<Heimdall.Core.Configuration.SshGatewayDto>? _gateways;
    private Heimdall.Core.Configuration.SshGatewayDto? _selectedGateway;
    private Action<string, string, ToolContext?>? _openToolAction;
    private Action<bool>? _setBusy;

    private NetworkKnowledgeBase? _knowledgeBase;

    private readonly ObservableCollection<CartographyRowViewModel> _results = [];

    public NetworkCartographyView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        TxtSubnet.KeyDown += OnSubnetKeyDown;
        ResultsGrid.PreviewMouseRightButtonDown += ToolContextMenuHelper.SelectRowOnRightClick;
        ResultsGrid.ContextMenuOpening += OnResultsContextMenuOpening;
        ResultsGrid.LoadingRow += OnResultsLoadingRow;
    }

    /// <inheritdoc />
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _openToolAction = ToolContextMenuHelper.GetOpenToolAction(context);
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtSubnet.Text = context.TargetHost;
        }
        else
        {
            var localSubnet = DetectLocalSubnet();
            if (localSubnet is not null)
            {
                TxtSubnet.Text = localSubnet;
            }
        }

        // Populate gateway selector for "Route via" tunnel support
        if (context?.SshGateways is System.Collections.IList gateways)
        {
            _gateways = gateways.Cast<Heimdall.Core.Configuration.SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();
        PopulateHistory();
        _ = LoadKbStatsAsync();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtSubnet.Focus();
            TxtSubnet.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolNetMapTitle");
        LblSubnet.Text = L("ToolNetMapSubnet");
        LblDepth.Text = L("ToolNetMapDepth");
        BtnStart.Content = L("ToolNetMapBtnStart");
        BtnStop.Content = L("ToolNetMapBtnStop");
        BtnExportCsv.Content = L("ToolNetMapBtnExport");

        ChkSkipPing.Content = L("ToolNetMapSkipPing");
        ChkReverseDns.Content = L("ToolNetMapReverseDns");
        LblRouteVia.Text = L("ToolTunnelRouteVia");

        BtnExportDrawio.Content = L("ToolNetMapBtnExportDrawio");
        BtnEditDiagram.Content = L("ToolDiagramBtnEdit");
        ChkUseKnowledgeBase.Content = L("ToolNetMapUseKb");
        BtnClearKb.Content = L("ToolNetMapBtnClearKb");

        ColIp.Header = L("ToolNetMapColIp");
        ColHostname.Header = L("ToolNetMapColHostname");
        ColPorts.Header = L("ToolNetMapColPorts");
        ColServices.Header = L("ToolNetMapColServices");
        ColTls.Header = L("ToolNetMapColTls");
        ColRole.Header = L("ToolNetMapColRole");
        ColConfidence.Header = L("ToolNetMapColConfidence");
        ColOs.Header = L("ToolNetMapColOs");
        ColVlan.Header = L("ToolNetMapColVlan");
        ColDetails.Header = L("ToolNetMapColDetails");
        ColManufacturer.Header = L("ToolNetMapColManufacturer");

        CmbDepth.Items.Clear();
        CmbDepth.Items.Add(new ComboBoxItem { Content = L("ToolNetMapDepthQuick"), Tag = ScanDepth.Quick });
        CmbDepth.Items.Add(new ComboBoxItem { Content = L("ToolNetMapDepthStandard"), Tag = ScanDepth.Standard });
        CmbDepth.Items.Add(new ComboBoxItem { Content = L("ToolNetMapDepthDeep"), Tag = ScanDepth.Deep });
        CmbDepth.SelectedIndex = 0;

        System.Windows.Automation.AutomationProperties.SetName(TxtSubnet, L("ToolNetMapSubnet"));
        System.Windows.Automation.AutomationProperties.SetName(CmbDepth, L("ToolNetMapDepth"));
        System.Windows.Automation.AutomationProperties.SetName(BtnStart, L("ToolNetMapBtnStart"));
        System.Windows.Automation.AutomationProperties.SetName(BtnStop, L("ToolNetMapBtnStop"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportCsv, L("ToolNetMapBtnExport"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportDrawio, L("ToolNetMapBtnExportDrawio"));
        System.Windows.Automation.AutomationProperties.SetName(BtnEditDiagram, L("ToolDiagramBtnEdit"));
        System.Windows.Automation.AutomationProperties.SetName(CmbHistory, L("ToolNetMapCompareWith"));
        System.Windows.Automation.AutomationProperties.SetName(ChkSkipPing, L("ToolNetMapSkipPing"));
        System.Windows.Automation.AutomationProperties.SetName(ChkReverseDns, L("ToolNetMapReverseDns"));
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));
        ChkUseKnowledgeBase.ToolTip = L("ToolNetMapKbTooltip");
        System.Windows.Automation.AutomationProperties.SetName(ChkUseKnowledgeBase, L("ToolNetMapUseKb"));
        System.Windows.Automation.AutomationProperties.SetName(BtnClearKb, L("ToolNetMapBtnClearKb"));

        TxtSubnet.Tag = L("ToolWatermarkSubnetCidr");

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtEmptyState.Text = L("ToolEmptyStateNetMap");

        System.Windows.Automation.AutomationProperties.SetName(ScanProgress, L("ToolNetMapA11yProgress"));

        System.Windows.Automation.AutomationProperties.SetName(ResultsGrid, L("ToolNetMapTitle"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpNETMAP");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnSubnetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isScanning)
        {
            _ = StartScanAsync();
            e.Handled = true;
        }
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (!_isScanning)
        {
            _ = StartScanAsync();
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        StopScan();
    }

    private async Task StartScanAsync()
    {
        var subnet = TxtSubnet.Text.Trim();
        if (string.IsNullOrWhiteSpace(subnet))
        {
            TxtStatus.Text = L("ToolNetMapErrorEmptySubnet");
            return;
        }

        // Auto-append /24 if user entered a bare IP without CIDR prefix
        if (!subnet.Contains('/'))
        {
            subnet = subnet + "/24";
            TxtSubnet.Text = subnet;
        }

        var ipList = CartographyEngine.ParseCidr(subnet);
        if (ipList.Count == 0)
        {
            TxtStatus.Text = L("ToolNetMapErrorInvalidSubnet");
            return;
        }

        if (ipList.Count > LargeSubnetThreshold)
        {
            var warning = string.Format(L("ToolNetMapWarningLargeSubnet"), ipList.Count);
            var result = MessageBox.Show(
                warning,
                L("ToolNetMapTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;
        }

        _results.Clear();
        _cts = new CancellationTokenSource();
        _isScanning = true;
        _setBusy?.Invoke(true);

        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = true;
        TxtSubnet.IsReadOnly = true;
        CmbDepth.IsEnabled = false;
        ChkSkipPing.IsEnabled = false;
        ChkReverseDns.IsEnabled = false;
        ChkUseKnowledgeBase.IsEnabled = false;
        CmbRouteVia.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ScanProgress.Value = 0;
        ScanProgress.IsIndeterminate = true;
        TxtStatus.Text = string.Format(L("ToolNetMapStatusDiscovery"), 0, ipList.Count);
        TxtStats.Text = "";
        TxtScanProgress.Visibility = Visibility.Visible;
        TxtScanProgress.Text = string.Format(L("ToolNetMapScanProgress"), 0, ipList.Count);

        var depth = CmbDepth.SelectedItem is ComboBoxItem item && item.Tag is ScanDepth d
            ? d : ScanDepth.Quick;

        var profile = new ScanProfile(
            Subnet: subnet,
            Depth: depth,
            CustomPorts: null,
            MaxConcurrency: DefaultMaxConcurrency,
            TimeoutMs: DefaultProbeTimeoutMs,
            SkipPing: ChkSkipPing.IsChecked == true,
            ReverseDns: ChkReverseDns.IsChecked == true);

        // Capture UI state before leaving the UI thread
        var useKb = ChkUseKnowledgeBase.IsChecked == true;

        // Load knowledge base (for cache if checkbox checked, for merge after scan)
        try { _knowledgeBase = await KnowledgeBaseManager.LoadAsync().ConfigureAwait(false); }
        catch { _knowledgeBase = KnowledgeBaseManager.CreateEmpty(); }

        var kb = useKb ? _knowledgeBase : null;

        var engine = new CartographyEngine();

        engine.CacheHitProgress += (host, phase) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = string.Format(L("ToolNetMapKbCacheHit"), host, phase);
            });
        };

        engine.HostDiscoveryProgress += (completed, total) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                ScanProgress.IsIndeterminate = false;
                ScanProgress.Maximum = total;
                ScanProgress.Value = completed;
                TxtStatus.Text = string.Format(L("ToolNetMapStatusDiscovery"), completed, total);
                TxtScanProgress.Text = string.Format(L("ToolNetMapScanProgress"), completed, total);
            });
        };

        engine.PortScanProgress += (host, completed, totalPorts) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = string.Format(L("ToolNetMapStatusScanning"), host, completed, totalPorts);
            });
        };

        engine.EnrichmentProgress += (host, phase) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = string.Format(L("ToolNetMapStatusEnriching"), host, phase);
            });
        };

        engine.HostCompleted += hostResult =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                _results.Add(ToRow(hostResult));
            });
        };

        try
        {
            NetworkScanSnapshot snapshot;

            if (_selectedGateway is not null)
            {
                snapshot = await ScanViaTunnelAsync(profile, _cts.Token).ConfigureAwait(false);
            }
            else
            {
                snapshot = await engine.ScanAsync(profile, kb, _cts.Token).ConfigureAwait(false);
            }

            try
            {
                await ScanHistoryManager.SaveSnapshotAsync(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"NetworkCartography history save failed: {ex.Message}");
            }

            // Merge into knowledge base (always, even if KB checkbox unchecked)
            // Reuse the instance loaded before scan if available, avoid redundant I/O
            try
            {
                _knowledgeBase ??= await KnowledgeBaseManager.LoadAsync().ConfigureAwait(false);
                _knowledgeBase = KnowledgeBaseManager.MergeSnapshot(_knowledgeBase, snapshot);
                await KnowledgeBaseManager.SaveAsync(_knowledgeBase).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"NetworkCartography KB merge failed: {ex.Message}");
            }

            await Dispatcher.InvokeAsync(() =>
            {
                _lastSnapshot = snapshot;
                var totalServices = snapshot.Hosts.Sum(h => h.Services.Count);
                var totalRoles = snapshot.Hosts.Count(h => h.PrimaryRole is not null);
                var durationText = snapshot.Duration.TotalSeconds < 60
                    ? $"{snapshot.Duration.TotalSeconds:F1}s"
                    : $"{snapshot.Duration.TotalMinutes:F1}m";

                if (snapshot.Hosts.Count == 0)
                {
                    TxtStatus.Text = L("ToolNetMapNoHostsFound");
                    EmptyStatePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtStatus.Text = string.Format(
                        L("ToolNetMapStatusComplete"),
                        snapshot.Hosts.Count, totalServices, totalRoles, durationText);
                }

                var serviceNames = snapshot.Hosts
                    .SelectMany(h => h.Services)
                    .Where(s => s.ServiceName is not null)
                    .Select(s => s.ServiceName!)
                    .Distinct()
                    .Count();
                TxtStats.Text = string.Format(
                    L("ToolNetMapStats"),
                    snapshot.Hosts.Count, totalServices, serviceNames);
                TxtStatsSeparator.Visibility = Visibility.Visible;

                BtnExportCsv.IsEnabled = _results.Count > 0;
                BtnExportDrawio.IsEnabled = true;
                BtnEditDiagram.IsEnabled = _openToolAction is not null;

                // Refresh VLAN column now that full snapshot with VLAN data is available
                if (snapshot.DetectedVlans is { Count: > 0 })
                {
                    foreach (var row in _results)
                    {
                        var vlan = snapshot.DetectedVlans
                            .FirstOrDefault(v => v.MemberIps.Contains(row.IpAddress));
                        if (vlan is not null)
                            row.VlanSegment = $"VLAN {vlan.VlanId} ({vlan.Subnet})";
                    }
                    ResultsGrid.Items.Refresh();
                }

                UpdateKbStats();
                PopulateHistory();
            });
        }
        catch (OperationCanceledException)
        {
            // Scan cancelled
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"NetworkCartography scan failed: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = string.Format(L("ToolNetMapErrorScanFailed"), ex.Message);
            });
        }

        await Dispatcher.InvokeAsync(StopScan);
    }

    /// <summary>
    /// Scans a subnet via SSH gateway using remote commands.
    /// Uses ping, /dev/tcp probes, and nslookup on the gateway host.
    /// </summary>
    private async Task<NetworkScanSnapshot> ScanViaTunnelAsync(ScanProfile profile, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var gw = _selectedGateway!;

        await Dispatcher.InvokeAsync(() =>
            TxtStatus.Text = string.Format(L("ToolTunnelConnecting"), gw.Name));

        using var sshClient = await Task.Run(
            () => ToolGatewayConnector.Connect(gw), ct).ConfigureAwait(false);

        var ipList = CartographyEngine.ParseCidr(profile.Subnet);
        var hosts = new List<HostScanResult>();
        var ports = profile.CustomPorts ?? (profile.Depth == ScanDepth.Quick
            ? CartographyEngine.QuickPorts : CartographyEngine.StandardPorts);
        var completed = 0;

        // Pre-compute validated port list once (invariant across hosts)
        var portList = string.Join(" ", ports.Where(p => p is >= 1 and <= 65535));

        // Batch scan: for each IP, test ports via /dev/tcp
        foreach (var ip in ipList)
        {
            ct.ThrowIfCancellationRequested();

            // Validate IP to prevent shell injection (CWE-78)
            if (!System.Net.IPAddress.TryParse(ip, out _)) continue;

            var openServices = new List<ServiceResult>();
            try
            {
                using var cmd = sshClient.CreateCommand(
                    $"for p in {portList}; do (echo >/dev/tcp/{ip}/$p) 2>/dev/null && echo $p; done");
                cmd.CommandTimeout = TimeSpan.FromSeconds(Math.Max(MinCommandTimeoutSeconds, ports.Length / PortTimeoutDivisor));
                var result = await Task.Run(() => cmd.Execute(), ct).ConfigureAwait(false);

                if (result is not null)
                {
                    foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(line.Trim(), out var port))
                        {
                            var svcName = RoleClassifier.GetPortServiceName(port);
                            openServices.Add(new ServiceResult(port, true,
                                svcName != $"Port-{port}" ? svcName : null,
                                null, null, 0));
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* probe failed */ }

            // Reverse DNS via gateway
            string? hostname = null;
            if (profile.ReverseDns)
            {
                try
                {
                    using var dnsCmd = sshClient.CreateCommand($"host {ip} 2>/dev/null | head -1");
                    dnsCmd.CommandTimeout = TimeSpan.FromSeconds(3);
                    var dnsResult = await Task.Run(() => dnsCmd.Execute(), ct).ConfigureAwait(false);
                    if (dnsResult?.Contains("domain name pointer") == true)
                    {
                        hostname = dnsResult.Split("domain name pointer")[1].Trim().TrimEnd('.');
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* DNS failed */ }
            }

            if (openServices.Count > 0)
            {
                var openPorts = openServices.Select(s => s.Port).ToList();
                var roles = RoleClassifier.Classify(openPorts);

                var hostResult = new HostScanResult(ip, hostname, true, 0,
                    openServices, roles.FirstOrDefault(), roles);
                hosts.Add(hostResult);

                await Dispatcher.InvokeAsync(() => _results.Add(ToRow(hostResult)));
            }

            completed++;
            await Dispatcher.InvokeAsync(() =>
            {
                ScanProgress.IsIndeterminate = false;
                ScanProgress.Maximum = ipList.Count;
                ScanProgress.Value = completed;
                TxtStatus.Text = string.Format(L("ToolNetMapStatusScanning"),
                    ip, completed, ipList.Count);
                TxtScanProgress.Text = string.Format(L("ToolNetMapScanProgress"), completed, ipList.Count);
            });
        }

        var orderedHosts = hosts.OrderBy(h => CartographyEngine.IpToLong(h.IpAddress)).ToList();
        var vlans = VlanDetector.InferFromHosts(orderedHosts, profile.Subnet);

        return new NetworkScanSnapshot(
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            profile,
            gw.Name,
            DateTime.UtcNow - startTime,
            orderedHosts,
            vlans);
    }

    private void StopScan()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isScanning = false;
        _setBusy?.Invoke(false);

        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled = false;
        TxtSubnet.IsReadOnly = false;
        CmbDepth.IsEnabled = true;
        ChkSkipPing.IsEnabled = true;
        ChkReverseDns.IsEnabled = true;
        ChkUseKnowledgeBase.IsEnabled = true;
        CmbRouteVia.IsEnabled = true;
        TxtScanProgress.Visibility = Visibility.Collapsed;

        // Keep progress panel visible if there's a status message to show
        // (0-hosts warning, error, or completion summary)
        if (string.IsNullOrEmpty(TxtStatus.Text))
            ProgressPanel.Visibility = Visibility.Collapsed;
    }

    private CartographyRowViewModel ToRow(HostScanResult host)
    {
        var openPortsList = host.Services.Where(s => s.IsOpen).Select(s => s.Port).OrderBy(p => p).ToList();
        var openPorts = openPortsList.AsEnumerable();
        var services = host.Services
            .Where(s => s.IsOpen && s.ServiceName is not null)
            .Select(s => s.ServiceName!)
            .Distinct()
            .OrderBy(s => s);

        var tlsCert = host.Services.FirstOrDefault(s => s.Certificate is not null)?.Certificate;
        var tlsStatus = "";
        if (tlsCert is not null)
        {
            if (tlsCert.IsExpired)
                tlsStatus = $"{L("ToolNetMapCertExpired")}!";
            else if (tlsCert.ExpiresSoon)
                tlsStatus = $"{tlsCert.TlsVersion} ({L("ToolNetMapCertExpiring")} {tlsCert.NotAfter:yyyy-MM-dd})";
            else
                tlsStatus = $"{tlsCert.TlsVersion} ({L("ToolNetMapCertValid")} {tlsCert.NotAfter:yyyy-MM-dd})";
        }

        // OS fingerprint
        var osGuess = host.OsFingerprint?.OsGuess ?? "\u2014";

        // Details summary: compact one-line
        var detailParts = new List<string>();
        if (!string.IsNullOrEmpty(host.NetBiosName))
            detailParts.Add($"NB:{host.NetBiosName}");
        if (host.SnmpInfo?.SysName is not null)
            detailParts.Add($"SNMP:{host.SnmpInfo.SysName}");
        if (host.MdnsServices is { Count: > 0 })
            detailParts.Add($"mDNS:{host.MdnsServices.Count}");
        if (host.SsdpInfo?.DeviceType is not null)
            detailParts.Add($"UPnP:{host.SsdpInfo.DeviceType}");
        if (host.NtlmInfo?.DnsComputerName is not null)
            detailParts.Add($"NTLM:{host.NtlmInfo.DnsComputerName}");
        if (host.SshHashFingerprint is not null)
            detailParts.Add($"HASSH:{host.SshHashFingerprint[..8]}");
        if (host.FaviconHash is not null && FaviconHasher.KnownHashes.TryGetValue(host.FaviconHash.Value, out var favName))
            detailParts.Add($"Fav:{favName}");
        if (host.HttpFingerprint?.ProductName is not null)
            detailParts.Add($"HTTP:{host.HttpFingerprint.ProductName}");
        if (host.SmbInfo is not null)
            detailParts.Add($"SMB:{host.SmbInfo.DialectRevision:X4}");
        var detailsSummary = detailParts.Count > 0 ? string.Join(" | ", detailParts) : "\u2014";

        // Full tooltip (localized labels)
        var tooltipParts = new List<string>
        {
            $"{L("ToolNetMapTipIp")}: {host.IpAddress}"
        };
        if (host.Hostname is not null) tooltipParts.Add($"{L("ToolNetMapTipHostname")}: {host.Hostname}");
        if (host.OsFingerprint is not null)
            tooltipParts.Add($"{L("ToolNetMapTipOs")}: {host.OsFingerprint.OsGuess} ({host.OsFingerprint.Source}, {host.OsFingerprint.Confidence}%)");
        if (host.PingLatencyMs > 0) tooltipParts.Add($"{L("ToolNetMapTipLatency")}: {host.PingLatencyMs}ms");
        if (host.MacAddress is not null) tooltipParts.Add($"{L("ToolNetMapTipMac")}: {host.MacAddress}");
        if (host.Manufacturer is not null) tooltipParts.Add($"{L("ToolNetMapTipManufacturer")}: {host.Manufacturer}");
        if (host.NetBiosName is not null) tooltipParts.Add($"{L("ToolNetMapTipNetBios")}: {host.NetBiosName}");
        if (host.NetBiosDomain is not null) tooltipParts.Add($"{L("ToolNetMapTipDomain")}: {host.NetBiosDomain}");
        if (host.SnmpInfo is not null)
        {
            if (host.SnmpInfo.SysDescr is not null) tooltipParts.Add($"{L("ToolNetMapTipSnmpDescr")}: {host.SnmpInfo.SysDescr}");
            if (host.SnmpInfo.SysName is not null) tooltipParts.Add($"{L("ToolNetMapTipSnmpName")}: {host.SnmpInfo.SysName}");
            if (host.SnmpInfo.SysLocation is not null) tooltipParts.Add($"{L("ToolNetMapTipSnmpLocation")}: {host.SnmpInfo.SysLocation}");
        }
        if (host.MdnsServices is { Count: > 0 })
            tooltipParts.Add($"{L("ToolNetMapTipMdns")}: {string.Join(", ", host.MdnsServices)}");
        if (host.SsdpInfo is not null)
        {
            if (host.SsdpInfo.FriendlyName is not null)
                tooltipParts.Add($"{L("ToolNetMapTipSsdpName")}: {host.SsdpInfo.FriendlyName}");
            if (host.SsdpInfo.Manufacturer is not null)
                tooltipParts.Add($"{L("ToolNetMapTipSsdpMfr")}: {host.SsdpInfo.Manufacturer}");
            if (host.SsdpInfo.ModelName is not null)
                tooltipParts.Add($"{L("ToolNetMapTipSsdpModel")}: {host.SsdpInfo.ModelName}");
            if (host.SsdpInfo.ModelNumber is not null)
                tooltipParts.Add($"{L("ToolNetMapTipSsdpModelNum")}: {host.SsdpInfo.ModelNumber}");
            if (host.SsdpInfo.SerialNumber is not null)
                tooltipParts.Add($"{L("ToolNetMapTipSsdpSerial")}: {host.SsdpInfo.SerialNumber}");
            if (host.SsdpInfo.DeviceType is not null)
                tooltipParts.Add($"{L("ToolNetMapTipSsdpDevice")}: {host.SsdpInfo.DeviceType}");
            if (host.SsdpInfo.Server is not null)
                tooltipParts.Add($"{L("ToolNetMapTipSsdpServer")}: {host.SsdpInfo.Server}");
        }
        if (host.NtlmInfo is not null)
        {
            if (host.NtlmInfo.DnsComputerName is not null)
                tooltipParts.Add($"{L("ToolNetMapTipNtlmDns")}: {host.NtlmInfo.DnsComputerName}");
            if (host.NtlmInfo.DnsDomainName is not null)
                tooltipParts.Add($"{L("ToolNetMapTipNtlmDomain")}: {host.NtlmInfo.DnsDomainName}");
            if (host.NtlmInfo.DnsForestName is not null)
                tooltipParts.Add($"{L("ToolNetMapTipNtlmForest")}: {host.NtlmInfo.DnsForestName}");
            if (host.NtlmInfo.OsBuild is not null)
                tooltipParts.Add($"{L("ToolNetMapTipNtlmBuild")}: {host.NtlmInfo.OsBuild}");
        }
        if (host.SshHashFingerprint is not null)
            tooltipParts.Add($"{L("ToolNetMapTipHashsh")}: {host.SshHashFingerprint}");
        if (host.SmbInfo is not null)
        {
            var dialectStr = host.SmbInfo.DialectRevision switch
            {
                0x0202 => "SMB 2.0.2", 0x0210 => "SMB 2.1", 0x0300 => "SMB 3.0",
                0x0302 => "SMB 3.0.2", 0x0311 => "SMB 3.1.1",
                _ => $"SMB 0x{host.SmbInfo.DialectRevision:X4}"
            };
            tooltipParts.Add($"{L("ToolNetMapTipSmb")}: {dialectStr}{(host.SmbInfo.SigningRequired ? $" ({L("ToolNetMapTipSmbSigning")})" : "")}");
            if (host.SmbInfo.ServerGuid is not null)
                tooltipParts.Add($"{L("ToolNetMapTipSmbGuid")}: {host.SmbInfo.ServerGuid}");
            if (host.SmbInfo.ServerStartTime is not null)
            {
                var uptime = DateTime.UtcNow - host.SmbInfo.ServerStartTime.Value;
                tooltipParts.Add($"{L("ToolNetMapTipUptime")}: {uptime.Days}d {uptime.Hours}h");
            }
        }
        if (host.HttpFingerprint is not null)
        {
            if (host.HttpFingerprint.Framework is not null)
                tooltipParts.Add($"{L("ToolNetMapTipHttpFramework")}: {host.HttpFingerprint.Framework}");
            if (host.HttpFingerprint.ProductName is not null)
                tooltipParts.Add($"{L("ToolNetMapTipHttpProduct")}: {host.HttpFingerprint.ProductName}");
        }
        if (host.FaviconHash is not null)
        {
            var deviceName = FaviconHasher.KnownHashes.TryGetValue(host.FaviconHash.Value, out var name)
                ? name : null;
            tooltipParts.Add(deviceName is not null
                ? $"{L("ToolNetMapTipFavicon")}: {host.FaviconHash.Value} ({deviceName})"
                : $"{L("ToolNetMapTipFavicon")}: {host.FaviconHash.Value}");
        }
        if (host.HttpHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in host.HttpHeaders)
                tooltipParts.Add($"{key}: {value}");
        }
        var detailTooltip = string.Join("\n", tooltipParts);

        var mdnsServicesList = host.MdnsServices is { Count: > 0 }
            ? string.Join("; ", host.MdnsServices) : null;

        return new CartographyRowViewModel
        {
            IpAddress = host.IpAddress,
            Hostname = host.Hostname ?? "\u2014",
            OsGuess = osGuess,
            PortSummary = string.Join(", ", openPorts),
            ServiceSummary = string.Join(", ", services),
            TlsStatus = tlsStatus,
            CertSubject = tlsCert?.Subject,
            CertExpires = tlsCert?.NotAfter.ToString("yyyy-MM-dd"),
            CertAlgorithm = tlsCert?.KeyAlgorithm,
            CertStatus = tlsCert is null ? null
                : tlsCert.IsExpired ? L("ToolNetMapCertExpired")
                : tlsCert.ExpiresSoon ? L("ToolNetMapCertExpiring")
                : L("ToolNetMapCertValid"),
            PrimaryRoleName = host.PrimaryRole?.Role
                ?? (host.Manufacturer is not null ? host.Manufacturer : "\u2014"),
            Confidence = host.PrimaryRole is not null ? $"{host.PrimaryRole.Confidence}%"
                : (host.Manufacturer is not null ? "MAC" : "\u2014"),
            DetailsSummary = detailsSummary,
            DetailTooltip = detailTooltip,
            VlanSegment = _lastSnapshot?.DetectedVlans?
                .FirstOrDefault(v => v.MemberIps.Contains(host.IpAddress))
                is { } vlan ? $"VLAN {vlan.VlanId} ({vlan.Subnet})" : "\u2014",
            Manufacturer = host.Manufacturer ?? "\u2014",
            NetBiosName = host.NetBiosName,
            NetBiosDomain = host.NetBiosDomain,
            SnmpSysName = host.SnmpInfo?.SysName,
            SnmpSysDescr = host.SnmpInfo?.SysDescr,
            SnmpSysLocation = host.SnmpInfo?.SysLocation,
            MdnsServicesList = mdnsServicesList,
            SsdpDeviceType = host.SsdpInfo is not null
                ? FormatSsdpSummary(host.SsdpInfo) : null,
            SnmpObjectId = host.SnmpInfo?.SysObjectId,
            NtlmDns = host.NtlmInfo?.DnsComputerName,
            NtlmDomain = host.NtlmInfo?.DnsDomainName,
            NtlmBuild = host.NtlmInfo?.OsBuild,
            SshHashFingerprint = host.SshHashFingerprint,
            FaviconHashValue = host.FaviconHash?.ToString(),
            OpenPorts = openPortsList
        };
    }

    private static string FormatSsdpSummary(SsdpInfo ssdp)
    {
        var parts = new List<string>();
        if (ssdp.FriendlyName is not null) parts.Add(ssdp.FriendlyName);
        else if (ssdp.ModelName is not null) parts.Add(ssdp.ModelName);
        else if (ssdp.DeviceType is not null) parts.Add(ssdp.DeviceType);
        if (ssdp.Manufacturer is not null) parts.Add(ssdp.Manufacturer);
        if (ssdp.Server is not null) parts.Add(ssdp.Server);
        return parts.Count > 0 ? string.Join(" | ", parts) : "";
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"cartography_{TxtSubnet.Text.Trim().Replace('/', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(L("ToolNetMapExportHeader"));

            foreach (var r in _results)
            {
                var hostname = InputValidator.SanitizeCsvCell(r.Hostname ?? "").Replace("\"", "\"\"");
                var os = InputValidator.SanitizeCsvCell(r.OsGuess ?? "").Replace("\"", "\"\"");
                var ports = InputValidator.SanitizeCsvCell(r.PortSummary).Replace("\"", "\"\"");
                var services = InputValidator.SanitizeCsvCell(r.ServiceSummary).Replace("\"", "\"\"");
                var tls = InputValidator.SanitizeCsvCell(r.TlsStatus ?? "").Replace("\"", "\"\"");
                var certSubject = InputValidator.SanitizeCsvCell(r.CertSubject ?? "").Replace("\"", "\"\"");
                var certExpires = InputValidator.SanitizeCsvCell(r.CertExpires ?? "");
                var certAlgorithm = InputValidator.SanitizeCsvCell(r.CertAlgorithm ?? "").Replace("\"", "\"\"");
                var certStatus = InputValidator.SanitizeCsvCell(r.CertStatus ?? "").Replace("\"", "\"\"");
                var role = InputValidator.SanitizeCsvCell(r.PrimaryRoleName ?? "").Replace("\"", "\"\"");
                var nbName = InputValidator.SanitizeCsvCell(r.NetBiosName ?? "").Replace("\"", "\"\"");
                var nbDomain = InputValidator.SanitizeCsvCell(r.NetBiosDomain ?? "").Replace("\"", "\"\"");
                var snmpName = InputValidator.SanitizeCsvCell(r.SnmpSysName ?? "").Replace("\"", "\"\"");
                var snmpDescr = InputValidator.SanitizeCsvCell(r.SnmpSysDescr ?? "").Replace("\"", "\"\"");
                var snmpLoc = InputValidator.SanitizeCsvCell(r.SnmpSysLocation ?? "").Replace("\"", "\"\"");
                var mdns = InputValidator.SanitizeCsvCell(r.MdnsServicesList ?? "").Replace("\"", "\"\"");
                var manufacturer = InputValidator.SanitizeCsvCell(r.Manufacturer ?? "").Replace("\"", "\"\"");
                var vlan = InputValidator.SanitizeCsvCell(r.VlanSegment ?? "").Replace("\"", "\"\"");
                var ssdpDevice = InputValidator.SanitizeCsvCell(r.SsdpDeviceType ?? "").Replace("\"", "\"\"");
                var snmpOid = InputValidator.SanitizeCsvCell(r.SnmpObjectId ?? "").Replace("\"", "\"\"");
                var ntlmDns = InputValidator.SanitizeCsvCell(r.NtlmDns ?? "").Replace("\"", "\"\"");
                var ntlmDomain = InputValidator.SanitizeCsvCell(r.NtlmDomain ?? "").Replace("\"", "\"\"");
                var ntlmBuild = InputValidator.SanitizeCsvCell(r.NtlmBuild ?? "").Replace("\"", "\"\"");
                var sshHash = InputValidator.SanitizeCsvCell(r.SshHashFingerprint ?? "").Replace("\"", "\"\"");
                var favHash = InputValidator.SanitizeCsvCell(r.FaviconHashValue ?? "");
                sb.AppendLine($"{InputValidator.SanitizeCsvCell(r.IpAddress)},\"{hostname}\",\"{os}\",\"{ports}\",\"{services}\",\"{tls}\",\"{certSubject}\",\"{certExpires}\",\"{certAlgorithm}\",\"{certStatus}\",\"{role}\",{r.Confidence},\"{nbName}\",\"{nbDomain}\",\"{snmpName}\",\"{snmpDescr}\",\"{snmpLoc}\",\"{snmpOid}\",\"{mdns}\",\"{manufacturer}\",\"{vlan}\",\"{ssdpDevice}\",\"{ntlmDns}\",\"{ntlmDomain}\",\"{ntlmBuild}\",\"{sshHash}\",\"{favHash}\"");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"NetworkCartography CSV export failed: {ex.Message}");
        }
    }

    private void OnExportDrawioClick(object sender, RoutedEventArgs e)
    {
        if (_lastSnapshot is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Draw.io (*.drawio)|*.drawio",
            FileName = $"network-map_{_lastSnapshot.Profile.Subnet.Replace('/', '-')}_{_lastSnapshot.Timestamp:yyyyMMdd_HHmmss}.drawio"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var xml = DrawIoExporter.Generate(_lastSnapshot);
            File.WriteAllText(dialog.FileName, xml, Encoding.UTF8);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"NetworkCartography Draw.io export failed: {ex.Message}");
        }
    }

    private void OnEditDiagramClick(object sender, RoutedEventArgs e)
    {
        if (_lastSnapshot is null || _openToolAction is null) return;

        try
        {
            // Generate draw.io XML from the last scan snapshot
            var xml = DrawIoExporter.Generate(_lastSnapshot);

            // Save to temp file so the diagram editor can load it
            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"heimdall_netmap_{DateTime.Now:yyyyMMdd_HHmmss}.drawio");
            File.WriteAllText(tempFile, xml, Encoding.UTF8);

            // Open in diagram editor tool tab
            _openToolAction("DIAGRAM", L("PaletteToolDiagram"),
                new ToolContext(Argument: tempFile));
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"NetworkCartography edit diagram failed: {ex.Message}");
        }
    }

    private void PopulateHistory()
    {
        _historyList = ScanHistoryManager.ListSnapshots();
        CmbHistory.Items.Clear();
        CmbHistory.Items.Add(new ComboBoxItem { Content = L("ToolNetMapNoComparison") });
        foreach (var (_, timestamp, subnet) in _historyList)
        {
            CmbHistory.Items.Add(new ComboBoxItem
            {
                Content = $"{timestamp:yyyy-MM-dd HH:mm} \u2014 {subnet}"
            });
        }
        CmbHistory.SelectedIndex = 0;
    }

    private void OnHistorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (CmbHistory.SelectedIndex <= 0 || _lastSnapshot is null)
        {
            DiffPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var idx = CmbHistory.SelectedIndex - 1;
        if (idx >= _historyList.Count) return;

        var oldSnapshot = ScanHistoryManager.LoadSnapshot(_historyList[idx].FileName);
        if (oldSnapshot is null) return;

        var diff = ScanHistoryManager.ComputeDiff(oldSnapshot, _lastSnapshot);
        ShowDiffResults(diff);
    }

    private void ShowDiffResults(ScanDiff diff)
    {
        var parts = new List<string>();

        if (diff.NewHosts.Count > 0)
            parts.Add(string.Format(L("ToolNetMapDiffNew"), diff.NewHosts.Count));
        if (diff.RemovedHosts.Count > 0)
            parts.Add(string.Format(L("ToolNetMapDiffRemoved"), diff.RemovedHosts.Count));
        if (diff.ModifiedHosts.Count > 0)
            parts.Add(string.Format(L("ToolNetMapDiffChanged"), diff.ModifiedHosts.Count));

        if (parts.Count == 0)
        {
            DiffPanel.Visibility = Visibility.Collapsed;
            return;
        }

        TxtDiff.Text = string.Join(" | ", parts);
        DiffPanel.Visibility = Visibility.Visible;
    }

    private void PopulateRouteSelector()
    {
        CmbRouteVia.Items.Clear();
        CmbRouteVia.Items.Add(L("ToolTunnelDirect"));
        if (_gateways is not null)
        {
            foreach (var gw in _gateways)
            {
                CmbRouteVia.Items.Add($"{gw.Name} ({gw.Host}:{gw.Port})");
            }
        }
        CmbRouteVia.SelectedIndex = 0;
    }

    private void OnRouteViaChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbRouteVia.SelectedIndex <= 0 || _gateways is null)
        {
            _selectedGateway = null;
            return;
        }
        var idx = CmbRouteVia.SelectedIndex - 1;
        _selectedGateway = idx < _gateways.Count ? _gateways[idx] : null;

        if (_selectedGateway is not null)
        {
            _ = DetectRemoteSubnetsAsync(_selectedGateway);
        }
    }

    /// <summary>
    /// Detects the local machine's primary IPv4 subnet by enumerating network interfaces.
    /// Returns the first non-loopback unicast address as a normalized CIDR (e.g. "10.0.1.0/24"),
    /// preferring interfaces with a default gateway.
    /// </summary>
    private static string? DetectLocalSubnet()
    {
        try
        {
            // Prefer interfaces that have a default gateway (= real connected networks)
            string? fallback = null;

            foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                var props = iface.GetIPProperties();
                var hasGateway = false;
                foreach (var gw in props.GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !gw.Address.Equals(System.Net.IPAddress.Any))
                    {
                        hasGateway = true;
                        break;
                    }
                }

                foreach (var uni in props.UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    var ip = uni.Address.ToString();
                    if (ip.StartsWith("127.", StringComparison.Ordinal)) continue;
                    if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue; // APIPA

                    var cidr = NormalizeCidrFromIpAndPrefix(ip, uni.PrefixLength);
                    if (hasGateway) return cidr;
                    fallback ??= cidr;
                }
            }

            return fallback;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Connects to the selected gateway via SSH, discovers its network
    /// interfaces, and auto-populates TxtSubnet with the first non-loopback
    /// IPv4 CIDR found.
    /// </summary>
    private async Task DetectRemoteSubnetsAsync(Core.Configuration.SshGatewayDto gateway)
    {
        // Cancel any previous detection still in progress
        _subnetDetectCts?.Cancel();
        _subnetDetectCts?.Dispose();
        _subnetDetectCts = new CancellationTokenSource();
        var ct = _subnetDetectCts.Token;

        await Dispatcher.InvokeAsync(() =>
            TxtStatus.Text = string.Format(L("ToolNetMapDetectingSubnet"), gateway.Name));

        try
        {
            using var sshClient = await Task.Run(
                () => ToolGatewayConnector.Connect(gateway), ct).ConfigureAwait(false);

            // Try Linux first: ip -4 addr show
            var output = ExecuteSshCommand(sshClient, "ip -4 addr show 2>/dev/null");
            var subnets = ParseLinuxInterfaces(output);

            // Fallback: ifconfig (older Linux, macOS, BSD)
            if (subnets.Count == 0)
            {
                output = ExecuteSshCommand(sshClient, "ifconfig 2>/dev/null");
                subnets = ParseIfconfigInterfaces(output);
            }

            // Fallback: Windows ipconfig
            if (subnets.Count == 0)
            {
                output = ExecuteSshCommand(sshClient, "ipconfig 2>nul");
                subnets = ParseWindowsIpconfig(output);
            }

            if (subnets.Count > 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    TxtSubnet.Text = subnets[0];
                    TxtStatus.Text = string.Format(
                        L("ToolNetMapSubnetDetected"),
                        subnets.Count,
                        gateway.Name);
                    if (subnets.Count > 1)
                    {
                        TxtSubnet.ToolTip = string.Join("\n", subnets);
                    }
                });
            }
            else
            {
                await Dispatcher.InvokeAsync(() =>
                    TxtStatus.Text = L("ToolNetMapSubnetDetectFailed"));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer gateway selection — ignore silently
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Subnet detection failed for {gateway.Name}: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
                TxtStatus.Text = L("ToolNetMapSubnetDetectFailed"));
        }
    }

    private static string ExecuteSshCommand(Renci.SshNet.SshClient client, string command)
    {
        using var cmd = client.CreateCommand(command);
        cmd.CommandTimeout = TimeSpan.FromSeconds(5);
        return cmd.Execute();
    }

    /// <summary>
    /// Parses output of <c>ip -4 addr show</c> to extract non-loopback CIDRs.
    /// </summary>
    private static List<string> ParseLinuxInterfaces(string output)
    {
        var subnets = new List<string>();
        if (string.IsNullOrWhiteSpace(output)) return subnets;

        // Match lines like: "    inet 192.168.1.5/24 brd 192.168.1.255 scope global eth0"
        foreach (System.Text.RegularExpressions.Match match in s_linuxInetRegex.Matches(output))
        {
            var cidr = match.Groups[1].Value;
            // Skip loopback 127.x.x.x
            if (cidr.StartsWith("127.", StringComparison.Ordinal)) continue;
            // Normalize to network address: 192.168.1.5/24 -> 192.168.1.0/24
            subnets.Add(NormalizeCidr(cidr));
        }
        return subnets.Distinct().ToList();
    }

    /// <summary>
    /// Parses output of <c>ifconfig</c> to extract non-loopback CIDRs.
    /// </summary>
    private static List<string> ParseIfconfigInterfaces(string output)
    {
        var subnets = new List<string>();
        if (string.IsNullOrWhiteSpace(output)) return subnets;

        // Match "inet 192.168.1.5 netmask 255.255.255.0" or "inet addr:192.168.1.5 Mask:255.255.255.0"
        foreach (System.Text.RegularExpressions.Match match in s_ifconfigInetRegex.Matches(output))
        {
            var ip = match.Groups[1].Value;
            var mask = match.Groups[2].Value;
            if (ip.StartsWith("127.", StringComparison.Ordinal)) continue;
            var prefix = MaskToPrefix(mask);
            if (prefix > 0)
                subnets.Add(NormalizeCidrFromIpAndPrefix(ip, prefix));
        }
        return subnets.Distinct().ToList();
    }

    /// <summary>
    /// Parses output of <c>ipconfig</c> (Windows) to extract non-loopback CIDRs.
    /// </summary>
    private static List<string> ParseWindowsIpconfig(string output)
    {
        var subnets = new List<string>();
        if (string.IsNullOrWhiteSpace(output)) return subnets;

        string? lastIp = null;
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            // "IPv4 Address. . . . . . . . . . . : 192.168.1.5"
            if (trimmed.Contains("IPv4", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(':'))
            {
                lastIp = trimmed[(trimmed.LastIndexOf(':') + 1)..].Trim();
            }
            // "Subnet Mask . . . . . . . . . . . : 255.255.255.0"
            else if (lastIp is not null && trimmed.Contains("Mask", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(':'))
            {
                var mask = trimmed[(trimmed.LastIndexOf(':') + 1)..].Trim();
                if (!lastIp.StartsWith("127.", StringComparison.Ordinal))
                {
                    var prefix = MaskToPrefix(mask);
                    if (prefix > 0)
                        subnets.Add(NormalizeCidrFromIpAndPrefix(lastIp, prefix));
                }
                lastIp = null;
            }
        }
        return subnets.Distinct().ToList();
    }

    /// <summary>
    /// Converts "192.168.1.5/24" to "192.168.1.0/24" (network address).
    /// </summary>
    private static string NormalizeCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix)) return cidr;
        return NormalizeCidrFromIpAndPrefix(parts[0], prefix);
    }

    private static string NormalizeCidrFromIpAndPrefix(string ip, int prefix)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var addr)) return $"{ip}/{prefix}";
        var bytes = addr.GetAddressBytes();
        var maskBits = prefix;
        for (int i = 0; i < 4; i++)
        {
            if (maskBits >= 8) { maskBits -= 8; continue; }
            bytes[i] = (byte)(bytes[i] & (0xFF << (8 - maskBits)));
            maskBits = 0;
        }
        return $"{new System.Net.IPAddress(bytes)}/{prefix}";
    }

    private static int MaskToPrefix(string mask)
    {
        if (!System.Net.IPAddress.TryParse(mask, out var addr)) return 0;
        var bits = BitConverter.ToUInt32(addr.GetAddressBytes().Reverse().ToArray(), 0);
        int count = 0;
        while ((bits & 0x80000000) != 0) { count++; bits <<= 1; }
        return count;
    }

    private void OnResultsContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not CartographyRowViewModel row)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();

        if (_openToolAction is not null)
        {
            var hostItems = ToolContextMenuHelper.BuildHostActions(
                row.IpAddress, row.Hostname, row.OpenPorts, _localizer, _openToolAction);
            foreach (var item in hostItems)
            {
                menu.Items.Add(item);
            }
        }
        else
        {
            // Fallback: copy actions only
            var copyIp = new MenuItem { Header = L("ToolCtxCopyIp") };
            copyIp.Click += (_, _) => Clipboard.SetText(row.IpAddress);
            menu.Items.Add(copyIp);

            if (!string.IsNullOrWhiteSpace(row.Hostname) && row.Hostname != "\u2014")
            {
                var copyHost = new MenuItem { Header = L("ToolCtxCopyHostname") };
                copyHost.Click += (_, _) => Clipboard.SetText(row.Hostname);
                menu.Items.Add(copyHost);
            }
        }

        // Copy row as CSV
        var csvText = $"{row.IpAddress},{row.Hostname},{row.OsGuess},{row.PortSummary},{row.ServiceSummary},{row.PrimaryRoleName},{row.Manufacturer}";
        menu.Items.Add(ToolContextMenuHelper.BuildCopyRowAction(csvText, _localizer));
        menu.Items.Add(ToolContextMenuHelper.BuildCopyAllAction(ResultsGrid, _localizer));
        menu.Items.Add(new Separator());
        menu.Items.Add(ToolContextMenuHelper.BuildExportCsvAction(ResultsGrid, _localizer));

        ResultsGrid.ContextMenu = menu;
    }

    private void OnResultsLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is CartographyRowViewModel row &&
            !string.IsNullOrEmpty(row.DetailTooltip))
        {
            e.Row.ToolTip = row.DetailTooltip;
        }
    }

    private async Task LoadKbStatsAsync()
    {
        try
        {
            _knowledgeBase = await KnowledgeBaseManager.LoadAsync().ConfigureAwait(false);
            await Dispatcher.InvokeAsync(UpdateKbStats);
        }
        catch { /* KB load failed, stats remain empty */ }
    }

    private void UpdateKbStats()
    {
        var isEmpty = _knowledgeBase is null || KnowledgeBaseManager.HostCount(_knowledgeBase) == 0;
        BtnClearKb.IsEnabled = !isEmpty;

        if (isEmpty)
        {
            TxtKbStats.Text = L("ToolNetMapKbStatsNever");
            return;
        }
        var count = KnowledgeBaseManager.HostCount(_knowledgeBase!);
        var ago = FormatTimeAgo(_knowledgeBase!.LastUpdated);
        TxtKbStats.Text = string.Format(L("ToolNetMapKbStats"), count, ago);
    }

    private string FormatTimeAgo(DateTime utcTime)
    {
        var elapsed = DateTime.UtcNow - utcTime;
        if (elapsed.TotalMinutes < 5)
            return L("ToolNetMapKbTimeAgoJustNow");
        if (elapsed.TotalHours < 1)
            return string.Format(L("ToolNetMapKbTimeAgo1m"), (int)elapsed.TotalMinutes);
        if (elapsed.TotalHours < 24)
            return string.Format(L("ToolNetMapKbTimeAgo1h"), (int)elapsed.TotalHours);
        return string.Format(L("ToolNetMapKbTimeAgo1d"), (int)elapsed.TotalDays);
    }

    private async void OnClearKbClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            L("ToolNetMapKbClearConfirm"),
            L("ToolNetMapTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            _knowledgeBase = KnowledgeBaseManager.CreateEmpty();
            await KnowledgeBaseManager.SaveAsync(_knowledgeBase).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateKbStats();
                TxtStatus.Text = L("ToolNetMapKbCleared");
            });
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"KB clear failed: {ex.Message}");
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isScanning;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopScan();
        _subnetDetectCts?.Cancel();
        _subnetDetectCts?.Dispose();
        _subnetDetectCts = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Row view model for DataGrid binding.
    /// </summary>
    internal sealed class CartographyRowViewModel
    {
        public string IpAddress { get; init; } = "";
        public string? Hostname { get; init; }
        public string OsGuess { get; init; } = "";
        public string PortSummary { get; init; } = "";
        public string ServiceSummary { get; init; } = "";
        public string TlsStatus { get; init; } = "";
        public string? CertSubject { get; init; }
        public string? CertExpires { get; init; }
        public string? CertAlgorithm { get; init; }
        public string? CertStatus { get; init; }
        public string PrimaryRoleName { get; init; } = "";
        public string Confidence { get; init; } = "";
        public string DetailsSummary { get; init; } = "";
        public string DetailTooltip { get; init; } = "";
        public string VlanSegment { get; set; } = "";
        public string Manufacturer { get; init; } = "";

        // Raw fields for CSV export
        public string? NetBiosName { get; init; }
        public string? NetBiosDomain { get; init; }
        public string? SnmpSysName { get; init; }
        public string? SnmpSysDescr { get; init; }
        public string? SnmpSysLocation { get; init; }
        public string? MdnsServicesList { get; init; }
        public string? SsdpDeviceType { get; init; }
        public string? SnmpObjectId { get; init; }
        public string? NtlmDns { get; init; }
        public string? NtlmDomain { get; init; }
        public string? NtlmBuild { get; init; }
        public string? SshHashFingerprint { get; init; }
        public string? FaviconHashValue { get; init; }

        /// <summary>
        /// Raw list of open ports for cross-tool context menu actions.
        /// </summary>
        public IReadOnlyList<int> OpenPorts { get; init; } = [];
    }
}
