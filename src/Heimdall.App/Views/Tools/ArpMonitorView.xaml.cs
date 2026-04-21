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

using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Monitors the local ARP table for changes and anomalies (new hosts, MAC changes, disappearances).
/// </summary>
public partial class ArpMonitorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private Action<bool>? _setBusy;
    private bool _disposed;
    private readonly ToolAsyncStateController _viewState;
    private readonly ArpMonitorViewModel _vm;

    public ArpMonitorView()
    {
        InitializeComponent();
        var reader = (Application.Current as App)?.Services?.GetService<IArpTableReader>()
            ?? new DefaultArpTableReader();
        _vm = new ArpMonitorViewModel(reader);
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.MacChangedDetected += OnMacChangedDetected;
        DataContext = _vm;
        _viewState = new ToolAsyncStateController(
            null,
            LoadingBar,
            TxtError,
            EmptyStatePanel,
            GridPanel,
            null);
        PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter && !e.Handled) { OnToggleClick(s, e); e.Handled = true; } };
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        _vm.Initialize(localizer);
        ApplyLocalization();
        SyncViewShell();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolArpTitle");
        BtnScanNow.Content = L("ToolArpBtnScanNow");
        LblInterval.Text = L("ToolArpInterval");
        BtnCopy.Content = L("ToolArpBtnCopy");

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

        AutomationProperties.SetName(BtnScanNow, L("ToolArpBtnScanNow"));
        AutomationProperties.SetName(BtnCopy, L("ToolArpBtnCopy"));
        AutomationProperties.SetName(CmbInterval, L("ToolArpInterval"));
        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        AutomationProperties.SetName(ArpGrid, L("ToolArpTitle"));
        AutomationProperties.SetName(BtnDismissAlert, L("ToolArpDismissAlert"));
        AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
        AutomationProperties.SetName(LoadingBar, L("ToolArpA11yLoading"));
        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        UpdateMonitoringUi();
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRunning)
        {
            _vm.Stop();
        }
        else
        {
            _ = _vm.StartAsync(GetSelectedIntervalMs());
        }
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
        if (_vm.Entries.Count == 0)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{L("ToolArpColIp")}\t{L("ToolArpColMac")}\t{L("ToolArpColVendor")}\t{L("ToolArpColStatus")}\t{L("ToolArpColFirstSeen")}\t{L("ToolArpColLastSeen")}");

            foreach (var entry in _vm.Entries)
            {
                sb.AppendLine($"{entry.Ip}\t{entry.Mac}\t{entry.Vendor}\t{entry.StatusDisplay}\t{entry.FirstSeen}\t{entry.LastSeen}");
            }

            sb.AppendLine();
            sb.AppendLine(string.Format(L("ToolArpTotal"), _vm.Entries.Count));

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
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpARPMON").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.PropertyName is nameof(ArpMonitorViewModel.IsRunning) or nameof(ArpMonitorViewModel.IsRefreshing))
        {
            UpdateMonitoringUi();
        }

        if (e.PropertyName is nameof(ArpMonitorViewModel.IsRefreshing)
            or nameof(ArpMonitorViewModel.HasError)
            or nameof(ArpMonitorViewModel.HasResults))
        {
            SyncViewShell();
        }

        if (e.PropertyName == nameof(ArpMonitorViewModel.IsRefreshing) && !_vm.IsRefreshing && !_vm.HasError)
        {
            RefreshEntryVendors();
        }
    }

    private void OnMacChangedDetected(object? sender, ArpMacChangedEventArgs e)
    {
        ShowAlert(e.Ip, e.PreviousMac, e.NewMac);
    }

    private void UpdateMonitoringUi()
    {
        var toggleKey = _vm.IsRunning ? "ToolArpBtnStop" : "ToolArpBtnStart";
        BtnToggle.Content = L(toggleKey);
        BtnToggle.Foreground = (Brush)FindResource(_vm.IsRunning ? "ErrorBrush" : "TextPrimaryBrush");
        BtnToggle.Style = (Style)FindResource(_vm.IsRunning ? "SecondaryButtonStyle" : "PrimaryButtonStyle");
        AutomationProperties.SetName(BtnToggle, L(toggleKey));

        BtnScanNow.IsEnabled = _vm.IsRunning && !_vm.IsRefreshing;
        CmbInterval.IsEnabled = !_vm.IsRunning;
        _setBusy?.Invoke(_vm.IsRunning);
    }

    private void SyncViewShell()
    {
        if (_vm.IsRefreshing)
        {
            _viewState.Begin();
            return;
        }

        _viewState.End();

        if (_vm.HasError)
        {
            _viewState.ShowError(
                _vm.ErrorMessage,
                showEmptyState: !_vm.HasResults,
                keepResultsVisible: _vm.HasResults);
            return;
        }

        if (_vm.HasResults)
        {
            _viewState.ShowResults();
        }
        else
        {
            _viewState.Reset();
        }
    }

    private void RefreshEntryVendors()
    {
        foreach (var entry in _vm.Entries)
        {
            entry.Vendor = LookupVendor(entry.Mac);
        }
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

    public bool CanClose() => !_vm.IsRunning;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.MacChangedDetected -= OnMacChangedDetected;
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }
}
