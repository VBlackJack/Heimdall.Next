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
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Monitors the local ARP table for changes and anomalies (new hosts, MAC changes, disappearances).
/// </summary>
public partial class ArpMonitorView : UserControl, IToolView
{
    private static readonly int ProcessTimeoutMs = 5000;

    private static readonly Regex MacOsArpRegex = new(
        @"\((.*?)\)\s+at\s+([a-fA-F0-9:]+)",
        RegexOptions.Compiled);

    private LocalizationManager? _localizer;
    private Action<bool>? _setBusy;
    private DispatcherTimer? _refreshTimer;
    private bool _isRunning;
    private bool _disposed;

    private readonly ObservableCollection<ArpEntry> _entries = [];
    private readonly Dictionary<string, ArpEntry> _knownEntries = new(StringComparer.OrdinalIgnoreCase);

    public ArpMonitorView()
    {
        InitializeComponent();
        ArpGrid.ItemsSource = _entries;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolArpTitle");
        BtnToggle.Content = L("ToolArpBtnStart");
        BtnScanNow.Content = L("ToolArpBtnScanNow");
        LblInterval.Text = L("ToolArpInterval");
        BtnCopy.Content = L("ToolArpBtnCopy");
        TxtEmptyState.Text = L("ToolArpEmptyState");

        Interval5s.Content = L("ToolArpInterval5");
        Interval10s.Content = L("ToolArpInterval10");
        Interval30s.Content = L("ToolArpInterval30");
        Interval60s.Content = L("ToolArpInterval60");

        ColIp.Header = L("ToolArpColIp");
        ColMac.Header = L("ToolArpColMac");
        ColVendor.Header = L("ToolArpColVendor");
        ColStatus.Header = L("ToolArpColStatus");
        ColFirstSeen.Header = L("ToolArpColFirstSeen");
        ColLastSeen.Header = L("ToolArpColLastSeen");

        TxtTotal.Text = string.Format(L("ToolArpTotal"), 0);
        TxtLastRefresh.Text = "";

        AutomationProperties.SetName(BtnToggle, L("ToolArpBtnStart"));
        AutomationProperties.SetName(BtnScanNow, L("ToolArpBtnScanNow"));
        AutomationProperties.SetName(BtnCopy, L("ToolArpBtnCopy"));
        AutomationProperties.SetName(CmbInterval, L("ToolArpInterval"));
        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            StopMonitoring();
        }
        else
        {
            StartMonitoring();
        }
    }

    private void OnScanNowClick(object sender, RoutedEventArgs e)
    {
        _ = RefreshArpAsync();
    }

    private void StartMonitoring()
    {
        _isRunning = true;
        _setBusy?.Invoke(true);

        BtnToggle.Content = L("ToolArpBtnStop");
        BtnToggle.Foreground = (Brush)FindResource("ErrorBrush");
        BtnToggle.Style = (Style)FindResource("SecondaryButtonStyle");
        AutomationProperties.SetName(BtnToggle, L("ToolArpBtnStop"));

        BtnScanNow.IsEnabled = true;
        CmbInterval.IsEnabled = false;

        var intervalMs = GetSelectedIntervalMs();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _refreshTimer.Tick += OnTimerTick;
        _refreshTimer.Start();

        // Fire first scan immediately
        _ = RefreshArpAsync();
    }

    private void StopMonitoring()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        _isRunning = false;
        _setBusy?.Invoke(false);

        BtnToggle.Content = L("ToolArpBtnStart");
        BtnToggle.Foreground = (Brush)FindResource("TextPrimaryBrush");
        BtnToggle.Style = (Style)FindResource("PrimaryButtonStyle");
        AutomationProperties.SetName(BtnToggle, L("ToolArpBtnStart"));

        BtnScanNow.IsEnabled = false;
        CmbInterval.IsEnabled = true;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        await RefreshArpAsync();
    }

    private async Task RefreshArpAsync()
    {
        Dictionary<string, string> current;
        try
        {
            current = await Task.Run(ReadArpTable);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"ArpMonitor: failed to read ARP table: {ex.Message}");
            return;
        }

        var now = DateTime.Now.ToString("HH:mm:ss");
        var successBrush = (Brush)FindResource("SuccessBrush");
        var stableBrush = (Brush)FindResource("TextSecondaryBrush");
        var warningBrush = (Brush)FindResource("WarningBrush");
        var errorBrush = (Brush)FindResource("ErrorBrush");

        // Track which known entries are still present
        var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (ip, mac) in current)
        {
            seenIps.Add(ip);

            if (_knownEntries.TryGetValue(ip, out var existing))
            {
                if (!string.Equals(existing.Mac, mac, StringComparison.OrdinalIgnoreCase))
                {
                    // MAC changed — potential ARP spoofing
                    existing.PreviousMac = existing.Mac;
                    existing.Mac = mac;
                    existing.Vendor = LookupVendor(mac);
                    existing.Status = "changed";
                    existing.StatusDisplay = L("ToolArpStatusChanged");
                    existing.StatusBrush = warningBrush;
                    existing.LastSeen = now;
                    ShowAlert(ip, existing.PreviousMac, mac);
                }
                else
                {
                    if (existing.Status != "stable")
                    {
                        existing.Status = "stable";
                        existing.StatusDisplay = L("ToolArpStatusStable");
                        existing.StatusBrush = stableBrush;
                    }
                    existing.LastSeen = now;
                }
            }
            else
            {
                // New entry
                var entry = new ArpEntry
                {
                    Ip = ip,
                    Mac = mac,
                    Vendor = LookupVendor(mac),
                    Status = "new",
                    StatusDisplay = L("ToolArpStatusNew"),
                    StatusBrush = successBrush,
                    FirstSeen = now,
                    LastSeen = now,
                    PreviousMac = ""
                };
                _knownEntries[ip] = entry;
                _entries.Add(entry);
            }
        }

        // Mark gone entries
        foreach (var known in _knownEntries.Values)
        {
            if (!seenIps.Contains(known.Ip) && known.Status != "gone")
            {
                known.Status = "gone";
                known.StatusDisplay = L("ToolArpStatusGone");
                known.StatusBrush = errorBrush;
            }
        }

        // Update visibility
        if (_entries.Count > 0)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            GridPanel.Visibility = Visibility.Visible;
        }

        // Update footer
        TxtTotal.Text = string.Format(L("ToolArpTotal"), _entries.Count);
        TxtLastRefresh.Text = string.Format(L("ToolArpLastRefresh"), now);
    }

    private void ShowAlert(string ip, string oldMac, string newMac)
    {
        TxtAlertTitle.Text = L("ToolArpAlertTitle");
        TxtAlertMessage.Text = string.Format(L("ToolArpAlertMacChanged"), ip, oldMac, newMac);
        AlertBanner.Visibility = Visibility.Visible;
    }

    private void OnDismissAlertClick(object sender, RoutedEventArgs e)
    {
        AlertBanner.Visibility = Visibility.Collapsed;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{L("ToolArpColIp")}\t{L("ToolArpColMac")}\t{L("ToolArpColVendor")}\t{L("ToolArpColStatus")}\t{L("ToolArpColFirstSeen")}\t{L("ToolArpColLastSeen")}");

            foreach (var entry in _entries)
            {
                sb.AppendLine($"{entry.Ip}\t{entry.Mac}\t{entry.Vendor}\t{entry.StatusDisplay}\t{entry.FirstSeen}\t{entry.LastSeen}");
            }

            sb.AppendLine();
            sb.AppendLine(string.Format(L("ToolArpTotal"), _entries.Count));

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"ArpMonitor clipboard copy failed: {ex.Message}");
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpARPMON");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Returns the selected refresh interval in milliseconds from the ComboBox.
    /// </summary>
    private int GetSelectedIntervalMs()
    {
        if (CmbInterval.SelectedItem is ComboBoxItem item && item.Tag is string tagStr &&
            int.TryParse(tagStr, out var ms))
        {
            return ms;
        }

        return 10000;
    }

    /// <summary>
    /// Reads the local ARP table by running "arp -a" (Windows/macOS) or reading /proc/net/arp (Linux).
    /// Returns a dictionary mapping IP addresses to MAC addresses (dash-separated format).
    /// </summary>
    private static Dictionary<string, string> ReadArpTable()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc is null) return result;

                var output = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(ProcessTimeoutMs))
                {
                    try { proc.Kill(); } catch { /* already exited */ }
                }

                // Parse lines like: "  10.0.0.1             aa-bb-cc-dd-ee-ff     dynamic"
                foreach (var line in output.Split('\n'))
                {
                    var parts = line.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var ip = parts[0];
                        var mac = parts[1];
                        // Validate MAC format: exactly 5 dashes (aa-bb-cc-dd-ee-ff)
                        if (IPAddress.TryParse(ip, out _) &&
                            mac.Length == 17 && mac.Count(c => c == '-') == 5)
                        {
                            result[ip] = mac;
                        }
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (System.IO.File.Exists("/proc/net/arp"))
                {
                    var lines = System.IO.File.ReadAllLines("/proc/net/arp");
                    foreach (var line in lines.Skip(1)) // Skip header
                    {
                        var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            var ip = parts[0];
                            var mac = parts[3];
                            if (mac != "00:00:00:00:00:00" && IPAddress.TryParse(ip, out _))
                            {
                                result[ip] = mac.Replace(':', '-');
                            }
                        }
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc is null) return result;

                var output = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(ProcessTimeoutMs))
                {
                    try { proc.Kill(); } catch { /* already exited */ }
                }

                // Parse lines like: "? (192.168.1.1) at 00:11:22:33:44:55 on en0 ifscope [ether]"
                foreach (var line in output.Split('\n'))
                {
                    var match = MacOsArpRegex.Match(line);
                    if (match.Success)
                    {
                        result[match.Groups[1].Value] = match.Groups[2].Value.Replace(':', '-');
                    }
                }
            }
        }
        catch
        {
            // ARP table unavailable — return empty result
        }

        return result;
    }

    /// <summary>
    /// Looks up the vendor name from the MAC OUI (first 3 octets).
    /// </summary>
    private static string LookupVendor(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac) || mac.Length < 8)
        {
            return "";
        }

        var prefix = mac.Replace("-", ":")[..8].ToUpperInvariant();
        return OuiVendors.GetValueOrDefault(prefix, "");
    }

    /// <summary>
    /// Top OUI vendor prefixes for quick identification.
    /// </summary>
    private static readonly Dictionary<string, string> OuiVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Virtual / Hypervisors
        ["00:50:56"] = "VMware",
        ["00:0C:29"] = "VMware",
        ["00:05:69"] = "VMware",
        ["08:00:27"] = "VirtualBox",
        ["0A:00:27"] = "VirtualBox",
        ["52:54:00"] = "QEMU/KVM",
        ["00:15:5D"] = "Hyper-V",
        ["00:16:3E"] = "Xen",
        // Apple
        ["3C:22:FB"] = "Apple",
        ["A4:83:E7"] = "Apple",
        ["F0:18:98"] = "Apple",
        ["AC:DE:48"] = "Apple",
        ["14:7D:DA"] = "Apple",
        ["F8:FF:C2"] = "Apple",
        ["78:7B:8A"] = "Apple",
        // Raspberry Pi
        ["DC:A6:32"] = "Raspberry Pi",
        ["B8:27:EB"] = "Raspberry Pi",
        ["E4:5F:01"] = "Raspberry Pi",
        ["28:CD:C1"] = "Raspberry Pi",
        ["D8:3A:DD"] = "Raspberry Pi",
        // Intel
        ["00:1B:21"] = "Intel",
        ["3C:97:0E"] = "Intel",
        ["A4:C3:F0"] = "Intel",
        ["48:51:B7"] = "Intel",
        ["F8:F2:1E"] = "Intel",
        // Cisco
        ["00:1A:A1"] = "Cisco",
        ["00:26:0B"] = "Cisco",
        ["00:50:0F"] = "Cisco",
        ["58:AC:78"] = "Cisco",
        ["F4:CF:E2"] = "Cisco",
        // HP / HPE
        ["00:1E:0B"] = "HP",
        ["3C:D9:2B"] = "HP",
        ["94:57:A5"] = "HP",
        ["B4:B5:2F"] = "HP",
        // Dell
        ["00:14:22"] = "Dell",
        ["18:03:73"] = "Dell",
        ["F8:DB:88"] = "Dell",
        ["B0:83:FE"] = "Dell",
        // Lenovo
        ["00:06:1B"] = "Lenovo",
        ["28:D2:44"] = "Lenovo",
        ["E8:2A:44"] = "Lenovo",
        // Realtek
        ["00:E0:4C"] = "Realtek",
        ["52:54:AB"] = "Realtek",
        // TP-Link
        ["50:C7:BF"] = "TP-Link",
        ["EC:08:6B"] = "TP-Link",
        ["60:32:B1"] = "TP-Link",
        ["14:EB:B6"] = "TP-Link",
        // Ubiquiti
        ["04:18:D6"] = "Ubiquiti",
        ["24:A4:3C"] = "Ubiquiti",
        ["68:D7:9A"] = "Ubiquiti",
        ["FC:EC:DA"] = "Ubiquiti",
        // Netgear
        ["00:26:F2"] = "Netgear",
        ["28:C6:8E"] = "Netgear",
        ["A4:2B:8C"] = "Netgear",
        // ASUS
        ["00:1A:92"] = "ASUS",
        ["04:D4:C4"] = "ASUS",
        ["2C:FD:A1"] = "ASUS",
        // Synology
        ["00:11:32"] = "Synology",
        // QNAP
        ["00:08:9B"] = "QNAP",
        // Aruba / HPE Aruba
        ["00:0B:86"] = "Aruba",
        ["24:DE:C6"] = "Aruba",
        // Juniper
        ["00:05:85"] = "Juniper",
        ["88:E0:F3"] = "Juniper",
        // MikroTik
        ["00:0C:42"] = "MikroTik",
        ["48:8F:5A"] = "MikroTik",
        // Fortinet
        ["00:09:0F"] = "Fortinet",
        ["70:4C:A5"] = "Fortinet",
        // Samsung
        ["00:16:32"] = "Samsung",
        ["8C:F5:A3"] = "Samsung",
        ["50:A4:C8"] = "Samsung",
        // Huawei
        ["00:E0:FC"] = "Huawei",
        ["48:46:FB"] = "Huawei",
        // Amazon (Echo, Ring, etc.)
        ["FC:65:DE"] = "Amazon",
        ["A4:08:01"] = "Amazon",
        // Google
        ["F4:F5:D8"] = "Google",
        ["3C:5A:B4"] = "Google",
        // Microsoft
        ["00:50:F2"] = "Microsoft",
        ["28:18:78"] = "Microsoft",
        // Sonos
        ["00:0E:58"] = "Sonos",
        ["B8:E9:37"] = "Sonos",
        // Broadcom
        ["00:10:18"] = "Broadcom",
        // D-Link
        ["00:1C:F0"] = "D-Link",
        ["28:10:7B"] = "D-Link",
    };

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isRunning;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopMonitoring();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents a single ARP table entry with change tracking and notification support.
    /// </summary>
    public sealed class ArpEntry : INotifyPropertyChanged
    {
        private string _ip = "";
        private string _mac = "";
        private string _vendor = "";
        private string _status = "";
        private string _statusDisplay = "";
        private Brush _statusBrush = Brushes.Transparent;
        private string _firstSeen = "";
        private string _lastSeen = "";
        private string _previousMac = "";

        public string Ip
        {
            get => _ip;
            init { _ip = value; OnPropertyChanged(); }
        }

        public string Mac
        {
            get => _mac;
            set { if (_mac != value) { _mac = value; OnPropertyChanged(); } }
        }

        public string Vendor
        {
            get => _vendor;
            set { if (_vendor != value) { _vendor = value; OnPropertyChanged(); } }
        }

        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        public string StatusDisplay
        {
            get => _statusDisplay;
            set { if (_statusDisplay != value) { _statusDisplay = value; OnPropertyChanged(); } }
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            set { if (!ReferenceEquals(_statusBrush, value)) { _statusBrush = value; OnPropertyChanged(); } }
        }

        public string FirstSeen
        {
            get => _firstSeen;
            init { _firstSeen = value; OnPropertyChanged(); }
        }

        public string LastSeen
        {
            get => _lastSeen;
            set { if (_lastSeen != value) { _lastSeen = value; OnPropertyChanged(); } }
        }

        public string PreviousMac
        {
            get => _previousMac;
            set { if (_previousMac != value) { _previousMac = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
