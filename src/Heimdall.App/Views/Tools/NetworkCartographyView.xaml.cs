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

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Network cartography tool that performs subnet-wide discovery with port scanning,
/// service identification, banner grabbing, and heuristic role classification.
/// </summary>
public partial class NetworkCartographyView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isScanning;
    private bool _disposed;
    private int _totalHostCount;
    private NetworkScanSnapshot? _lastSnapshot;
    private List<(string FileName, DateTime Timestamp, string Subnet)> _historyList = [];
    private List<Heimdall.Core.Configuration.SshGatewayDto>? _gateways;
    private Heimdall.Core.Configuration.SshGatewayDto? _selectedGateway;
    private Action<string, string, ToolContext?>? _openToolAction;

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
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtSubnet.Text = context.TargetHost;
        }

        // Populate gateway selector for "Route via" tunnel support
        if (context?.SshGateways is System.Collections.IList gateways)
        {
            _gateways = gateways.Cast<Heimdall.Core.Configuration.SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();
        PopulateHistory();

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

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtEmptyState.Text = L("ToolEmptyStateNetMap");
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

        const int largeSubnetThreshold = 512;
        if (ipList.Count > largeSubnetThreshold)
        {
            var warning = string.Format(L("ToolNetMapWarningLargeSubnet"), ipList.Count);
            var result = MessageBox.Show(
                warning,
                L("ToolNetMapTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;
        }

        _totalHostCount = ipList.Count;
        _results.Clear();
        _cts = new CancellationTokenSource();
        _isScanning = true;

        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = true;
        TxtSubnet.IsReadOnly = true;
        CmbDepth.IsEnabled = false;
        ChkSkipPing.IsEnabled = false;
        ChkReverseDns.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ScanProgress.Value = 0;
        TxtStatus.Text = "";
        TxtStats.Text = "";

        var depth = CmbDepth.SelectedItem is ComboBoxItem item && item.Tag is ScanDepth d
            ? d : ScanDepth.Quick;

        var profile = new ScanProfile(
            Subnet: subnet,
            Depth: depth,
            CustomPorts: null,
            MaxConcurrency: 50,
            TimeoutMs: 2000,
            SkipPing: ChkSkipPing.IsChecked == true,
            ReverseDns: ChkReverseDns.IsChecked == true);

        var engine = new CartographyEngine();

        engine.HostDiscoveryProgress += (completed, total) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                ScanProgress.Maximum = total;
                ScanProgress.Value = completed;
                TxtStatus.Text = string.Format(L("ToolNetMapStatusDiscovery"), completed, total);
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
                snapshot = await engine.ScanAsync(profile, _cts.Token).ConfigureAwait(false);
            }

            try
            {
                await ScanHistoryManager.SaveSnapshotAsync(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"NetworkCartography history save failed: {ex.Message}");
            }

            await Dispatcher.InvokeAsync(() =>
            {
                _lastSnapshot = snapshot;
                var totalServices = snapshot.Hosts.Sum(h => h.Services.Count);
                var totalRoles = snapshot.Hosts.Count(h => h.PrimaryRole is not null);
                var durationText = snapshot.Duration.TotalSeconds < 60
                    ? $"{snapshot.Duration.TotalSeconds:F1}s"
                    : $"{snapshot.Duration.TotalMinutes:F1}m";

                TxtStatus.Text = string.Format(
                    L("ToolNetMapStatusComplete"),
                    snapshot.Hosts.Count, totalServices, totalRoles, durationText);

                var totalPorts = snapshot.Hosts.Sum(h => h.Services.Count);
                var serviceNames = snapshot.Hosts
                    .SelectMany(h => h.Services)
                    .Where(s => s.ServiceName is not null)
                    .Select(s => s.ServiceName!)
                    .Distinct()
                    .Count();
                TxtStats.Text = string.Format(
                    L("ToolNetMapStats"),
                    snapshot.Hosts.Count, totalPorts, serviceNames);

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

        // Decrypt gateway password
        var password = !string.IsNullOrEmpty(gw.SshPasswordEncrypted)
            ? Heimdall.Core.Security.CredentialProtector.Unprotect(gw.SshPasswordEncrypted)
            : null;

        var connParams = new Heimdall.Ssh.SshConnectionParams
        {
            Host = gw.Host,
            Port = gw.Port,
            Username = gw.User,
            Password = password,
            KeyPath = gw.KeyPath
        };

        var connInfo = Heimdall.Ssh.SshConnectionFactory.Create(connParams);
        using var sshClient = new Renci.SshNet.SshClient(connInfo);

        await Dispatcher.InvokeAsync(() =>
            TxtStatus.Text = string.Format(L("ToolTunnelConnecting"), gw.Name));

        await Task.Run(() => sshClient.Connect(), ct).ConfigureAwait(false);

        var ipList = CartographyEngine.ParseCidr(profile.Subnet);
        var hosts = new List<HostScanResult>();
        var ports = profile.CustomPorts ?? (profile.Depth == ScanDepth.Quick
            ? CartographyEngine.QuickPorts : CartographyEngine.StandardPorts);
        var completed = 0;

        // Batch scan: for each IP, test ports via /dev/tcp
        foreach (var ip in ipList)
        {
            ct.ThrowIfCancellationRequested();
            var openServices = new List<ServiceResult>();

            foreach (var port in ports)
            {
                try
                {
                    using var cmd = sshClient.CreateCommand(
                        $"(echo >/dev/tcp/{ip}/{port}) 2>/dev/null && echo OPEN || echo CLOSED");
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(profile.TimeoutMs);
                    var result = await Task.Run(() => cmd.Execute(), ct).ConfigureAwait(false);

                    if (result?.Trim() == "OPEN")
                    {
                        var svcName = RoleClassifier.GetPortServiceName(port);
                        openServices.Add(new ServiceResult(port, true,
                            svcName != $"Port-{port}" ? svcName : null,
                            null, null, 0));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* probe failed, port closed or filtered */ }
            }

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
                ScanProgress.Maximum = ipList.Count;
                ScanProgress.Value = completed;
                TxtStatus.Text = string.Format(L("ToolNetMapStatusScanning"),
                    ip, completed, ipList.Count);
            });
        }

        sshClient.Disconnect();

        var orderedHosts = hosts.OrderBy(h => CartographyEngine.IpToLong(h.IpAddress)).ToList();
        var vlans = VlanDetector.InferFromHosts(orderedHosts);

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

        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled = false;
        TxtSubnet.IsReadOnly = false;
        CmbDepth.IsEnabled = true;
        ChkSkipPing.IsEnabled = true;
        ChkReverseDns.IsEnabled = true;
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
            OpenPorts = openPortsList
        };
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
                var hostname = (r.Hostname ?? "").Replace("\"", "\"\"");
                var os = (r.OsGuess ?? "").Replace("\"", "\"\"");
                var ports = r.PortSummary.Replace("\"", "\"\"");
                var services = r.ServiceSummary.Replace("\"", "\"\"");
                var tls = (r.TlsStatus ?? "").Replace("\"", "\"\"");
                var certSubject = (r.CertSubject ?? "").Replace("\"", "\"\"");
                var certExpires = r.CertExpires ?? "";
                var certAlgorithm = (r.CertAlgorithm ?? "").Replace("\"", "\"\"");
                var certStatus = (r.CertStatus ?? "").Replace("\"", "\"\"");
                var role = (r.PrimaryRoleName ?? "").Replace("\"", "\"\"");
                var nbName = (r.NetBiosName ?? "").Replace("\"", "\"\"");
                var nbDomain = (r.NetBiosDomain ?? "").Replace("\"", "\"\"");
                var snmpName = (r.SnmpSysName ?? "").Replace("\"", "\"\"");
                var snmpDescr = (r.SnmpSysDescr ?? "").Replace("\"", "\"\"");
                var snmpLoc = (r.SnmpSysLocation ?? "").Replace("\"", "\"\"");
                var mdns = (r.MdnsServicesList ?? "").Replace("\"", "\"\"");
                var manufacturer = (r.Manufacturer ?? "").Replace("\"", "\"\"");
                var vlan = (r.VlanSegment ?? "").Replace("\"", "\"\"");
                sb.AppendLine($"{r.IpAddress},\"{hostname}\",\"{os}\",\"{ports}\",\"{services}\",\"{tls}\",\"{certSubject}\",\"{certExpires}\",\"{certAlgorithm}\",\"{certStatus}\",\"{role}\",{r.Confidence},\"{nbName}\",\"{nbDomain}\",\"{snmpName}\",\"{snmpDescr}\",\"{snmpLoc}\",\"{mdns}\",\"{manufacturer}\",\"{vlan}\"");
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

    private string L(string key) => _localizer?[key] ?? key;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopScan();
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
        public string VlanSegment { get; init; } = "";
        public string Manufacturer { get; init; } = "";

        // Raw fields for CSV export
        public string? NetBiosName { get; init; }
        public string? NetBiosDomain { get; init; }
        public string? SnmpSysName { get; init; }
        public string? SnmpSysDescr { get; init; }
        public string? SnmpSysLocation { get; init; }
        public string? MdnsServicesList { get; init; }

        /// <summary>
        /// Raw list of open ports for cross-tool context menu actions.
        /// </summary>
        public IReadOnlyList<int> OpenPorts { get; init; } = [];
    }
}
