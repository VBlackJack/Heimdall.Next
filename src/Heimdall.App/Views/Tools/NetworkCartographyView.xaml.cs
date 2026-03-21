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

    private readonly ObservableCollection<CartographyRowViewModel> _results = [];

    public NetworkCartographyView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        TxtSubnet.KeyDown += OnSubnetKeyDown;
    }

    /// <inheritdoc />
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtSubnet.Text = context.TargetHost;
        }

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

        BtnExportDrawio.Content = L("ToolNetMapBtnExportDrawio");

        ColIp.Header = L("ToolNetMapColIp");
        ColHostname.Header = L("ToolNetMapColHostname");
        ColPorts.Header = L("ToolNetMapColPorts");
        ColServices.Header = L("ToolNetMapColServices");
        ColTls.Header = L("ToolNetMapColTls");
        ColRole.Header = L("ToolNetMapColRole");
        ColConfidence.Header = L("ToolNetMapColConfidence");
        ColVlan.Header = L("ToolNetMapColVlan");

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
        System.Windows.Automation.AutomationProperties.SetName(CmbHistory, L("ToolNetMapCompareWith"));
        System.Windows.Automation.AutomationProperties.SetName(ChkSkipPing, L("ToolNetMapSkipPing"));
        System.Windows.Automation.AutomationProperties.SetName(ChkReverseDns, L("ToolNetMapReverseDns"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
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

        engine.HostCompleted += hostResult =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                _results.Add(ToRow(hostResult));
            });
        };

        try
        {
            var snapshot = await engine.ScanAsync(profile, _cts.Token).ConfigureAwait(false);

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
        var openPorts = host.Services.Where(s => s.IsOpen).Select(s => s.Port).OrderBy(p => p);
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

        return new CartographyRowViewModel
        {
            IpAddress = host.IpAddress,
            Hostname = host.Hostname ?? "\u2014",
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
            PrimaryRoleName = host.PrimaryRole?.Role ?? "\u2014",
            Confidence = host.PrimaryRole is not null ? $"{host.PrimaryRole.Confidence}%" : "\u2014",
            VlanSegment = _lastSnapshot?.DetectedVlans?
                .FirstOrDefault(v => v.MemberIps.Contains(host.IpAddress))
                is { } vlan ? $"VLAN {vlan.VlanId} ({vlan.Subnet})" : "\u2014"
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
            sb.AppendLine("IP Address,Hostname,Open Ports,Services,TLS_Version,Cert_Subject,Cert_Expires,Cert_Algorithm,Cert_Status,Role,Confidence");

            foreach (var r in _results)
            {
                var hostname = (r.Hostname ?? "").Replace("\"", "\"\"");
                var ports = r.PortSummary.Replace("\"", "\"\"");
                var services = r.ServiceSummary.Replace("\"", "\"\"");
                var tls = (r.TlsStatus ?? "").Replace("\"", "\"\"");
                var certSubject = (r.CertSubject ?? "").Replace("\"", "\"\"");
                var certExpires = r.CertExpires ?? "";
                var certAlgorithm = (r.CertAlgorithm ?? "").Replace("\"", "\"\"");
                var certStatus = (r.CertStatus ?? "").Replace("\"", "\"\"");
                var role = (r.PrimaryRoleName ?? "").Replace("\"", "\"\"");
                sb.AppendLine($"{r.IpAddress},\"{hostname}\",\"{ports}\",\"{services}\",\"{tls}\",\"{certSubject}\",\"{certExpires}\",\"{certAlgorithm}\",\"{certStatus}\",\"{role}\",{r.Confidence}");
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
        public string PortSummary { get; init; } = "";
        public string ServiceSummary { get; init; } = "";
        public string TlsStatus { get; init; } = "";
        public string? CertSubject { get; init; }
        public string? CertExpires { get; init; }
        public string? CertAlgorithm { get; init; }
        public string? CertStatus { get; init; }
        public string PrimaryRoleName { get; init; } = "";
        public string Confidence { get; init; } = "";
        public string VlanSegment { get; init; } = "";
    }
}
