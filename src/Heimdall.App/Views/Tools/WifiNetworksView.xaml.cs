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
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Scans visible Wi-Fi networks using <c>netsh wlan show networks mode=bssid</c>
/// and displays SSID, BSSID, signal strength, channel, authentication, encryption, and radio type.
/// </summary>
public partial class WifiNetworksView : UserControl, IToolView
{
    private const int ProcessTimeoutMs = 10000;

    private LocalizationManager? _localizer;
    private bool _isScanning;
    private bool _disposed;
    private Action<bool>? _setBusy;

    private readonly ObservableCollection<WifiEntry> _results = [];

    public WifiNetworksView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        UpdateResultsSurface();
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();
        UpdateResultsSurface();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolWifiTitle");
        BtnScan.Content = L("ToolWifiBtnScan");
        BtnCopy.Content = L("ToolBtnCopyToClipboard");

        ColSsid.Header = L("ToolWifiColSsid");
        ColBssid.Header = L("ToolWifiColBssid");
        ColSignal.Header = L("ToolWifiColSignal");
        ColChannel.Header = L("ToolWifiColChannel");
        ColAuth.Header = L("ToolWifiColAuth");
        ColEncryption.Header = L("ToolWifiColEncryption");
        ColRadio.Header = L("ToolWifiColRadio");

        System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolWifiBtnScan"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolBtnCopyToClipboard"));
        System.Windows.Automation.AutomationProperties.SetName(ResultsGrid, L("ToolWifiTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        TxtEmptyState.Text = L("ToolWifiStatus");
        TxtStatus.Text = string.Empty;
    }

    private void OnScanClick(object sender, RoutedEventArgs e)
    {
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        if (_disposed || _isScanning)
        {
            return;
        }

        _isScanning = true;
        _setBusy?.Invoke(true);
        BtnScan.IsEnabled = false;
        TxtError.Visibility = Visibility.Collapsed;
        _results.Clear();
        UpdateResultsSurface();

        try
        {
            var entries = await Task.Run(RunNetshScanAsync);

            foreach (var entry in entries.OrderByDescending(e => e.SignalValue))
            {
                _results.Add(entry);
            }

            TxtStatus.Text = string.Format(
                CultureInfo.InvariantCulture,
                L("ToolWifiStatus"),
                _results.Count,
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));

            UpdateResultsSurface();
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"WifiNetworks scan failed: {ex.Message}");
            TxtError.Text = ex.Message;
            TxtError.Visibility = Visibility.Visible;
            UpdateResultsSurface();
        }
        finally
        {
            _isScanning = false;
            _setBusy?.Invoke(false);
            BtnScan.IsEnabled = true;
        }
    }

    /// <summary>
    /// Runs <c>netsh wlan show networks mode=bssid</c> and parses the output into Wi-Fi entries.
    /// Each network block starts with "SSID N :" and may contain multiple BSSID sub-blocks.
    /// </summary>
    private static async Task<List<WifiEntry>> RunNetshScanAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = "wlan show networks mode=bssid",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start netsh process.");

        var outputTask = proc.StandardOutput.ReadToEndAsync();
        if (!proc.WaitForExit(ProcessTimeoutMs))
        {
            try { proc.Kill(); } catch { /* already exited */ }
        }
        var output = await outputTask;

        return ParseNetshOutput(output);
    }

    /// <summary>
    /// Parses the raw output of <c>netsh wlan show networks mode=bssid</c>.
    /// </summary>
    internal static List<WifiEntry> ParseNetshOutput(string output)
    {
        var results = new List<WifiEntry>();

        var currentSsid = string.Empty;
        var currentNetworkType = string.Empty;
        var currentAuth = string.Empty;
        var currentEncryption = string.Empty;

        var currentBssid = string.Empty;
        var currentSignal = string.Empty;
        var currentSignalValue = 0;
        var currentRadioType = string.Empty;
        var currentChannel = string.Empty;
        var inBssid = false;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Check for SSID line (but not BSSID)
            if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                // Flush previous BSSID block if any
                if (inBssid && !string.IsNullOrWhiteSpace(currentBssid))
                {
                    results.Add(new WifiEntry
                    {
                        Ssid = currentSsid,
                        Bssid = currentBssid,
                        Signal = currentSignal,
                        SignalValue = currentSignalValue,
                        Channel = currentChannel,
                        Auth = currentAuth,
                        Encryption = currentEncryption,
                        RadioType = currentRadioType,
                    });
                }

                currentSsid = ExtractValue(line);
                currentNetworkType = string.Empty;
                currentAuth = string.Empty;
                currentEncryption = string.Empty;
                currentBssid = string.Empty;
                inBssid = false;
                continue;
            }

            if (StartsWithAny(line, "Network type", "Type de r"))
            {
                currentNetworkType = ExtractValue(line);
                continue;
            }

            if (StartsWithAny(line, "Authentication", "Authentification"))
            {
                currentAuth = ExtractValue(line);
                continue;
            }

            if (StartsWithAny(line, "Encryption", "Chiffrement"))
            {
                currentEncryption = ExtractValue(line);
                continue;
            }

            if (line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                // Flush previous BSSID block if any
                if (inBssid && !string.IsNullOrWhiteSpace(currentBssid))
                {
                    results.Add(new WifiEntry
                    {
                        Ssid = currentSsid,
                        Bssid = currentBssid,
                        Signal = currentSignal,
                        SignalValue = currentSignalValue,
                        Channel = currentChannel,
                        Auth = currentAuth,
                        Encryption = currentEncryption,
                        RadioType = currentRadioType,
                    });
                }

                currentBssid = ExtractValue(line);
                currentSignal = string.Empty;
                currentSignalValue = 0;
                currentRadioType = string.Empty;
                currentChannel = string.Empty;
                inBssid = true;
                continue;
            }

            if (inBssid)
            {
                if (StartsWithAny(line, "Signal"))
                {
                    currentSignal = ExtractValue(line);
                    var numericPart = currentSignal.Replace("%", "").Trim();
                    int.TryParse(numericPart, CultureInfo.InvariantCulture, out currentSignalValue);
                    continue;
                }

                if (StartsWithAny(line, "Radio type", "Type de radio"))
                {
                    currentRadioType = ExtractValue(line);
                    continue;
                }

                if (StartsWithAny(line, "Channel", "Canal"))
                {
                    currentChannel = ExtractValue(line);
                    continue;
                }
            }
        }

        // Flush last BSSID block
        if (inBssid && !string.IsNullOrWhiteSpace(currentBssid))
        {
            results.Add(new WifiEntry
            {
                Ssid = currentSsid,
                Bssid = currentBssid,
                Signal = currentSignal,
                SignalValue = currentSignalValue,
                Channel = currentChannel,
                Auth = currentAuth,
                Encryption = currentEncryption,
                RadioType = currentRadioType,
            });
        }

        return results;
    }

    /// <summary>
    /// Extracts the value portion after the first colon in a line.
    /// </summary>
    private static string ExtractValue(string line)
    {
        var colonIndex = line.IndexOf(':');
        return colonIndex >= 0 ? line[(colonIndex + 1)..].Trim() : string.Empty;
    }

    /// <summary>
    /// Checks whether a line starts with any of the given field names (case-insensitive).
    /// Supports multiple locale variants (EN/FR) for Windows command output parsing.
    /// </summary>
    private static bool StartsWithAny(string line, params string[] fieldNames)
    {
        foreach (var name in fieldNames)
        {
            if (line.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void UpdateResultsSurface()
    {
        var hasResults = _results.Count > 0;
        var hasError = TxtError.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(TxtError.Text);

        ResultsPanel.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasResults || _isScanning || hasError
            ? Visibility.Collapsed
            : Visibility.Visible;
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
            sb.AppendLine($"{L("ToolWifiColSsid")}\t{L("ToolWifiColBssid")}\t{L("ToolWifiColSignal")}\t{L("ToolWifiColChannel")}\t{L("ToolWifiColAuth")}\t{L("ToolWifiColEncryption")}\t{L("ToolWifiColRadio")}");

            foreach (var entry in _results)
            {
                sb.AppendLine($"{entry.Ssid}\t{entry.Bssid}\t{entry.Signal}\t{entry.Channel}\t{entry.Auth}\t{entry.Encryption}\t{entry.RadioType}");
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"WifiNetworks clipboard copy failed: {ex.Message}");
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpWIFI").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isScanning;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents a single Wi-Fi network entry (one per BSSID) for DataGrid binding.
    /// </summary>
    public sealed class WifiEntry
    {
        public string Ssid { get; init; } = "";
        public string Bssid { get; init; } = "";
        public string Signal { get; init; } = "";
        public int SignalValue { get; init; }
        public string Channel { get; init; } = "";
        public string Auth { get; init; } = "";
        public string Encryption { get; init; } = "";
        public string RadioType { get; init; } = "";
    }
}
