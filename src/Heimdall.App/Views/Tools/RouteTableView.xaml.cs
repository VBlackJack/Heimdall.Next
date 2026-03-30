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
/// Displays the local IPv4 routing table by parsing <c>route print</c> output.
/// </summary>
public partial class RouteTableView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private readonly ObservableCollection<RouteEntry> _entries = [];

    public RouteTableView()
    {
        InitializeComponent();
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        RoutesGrid.ItemsSource = _entries;
        Refresh();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolRouteTableTitle");
        BtnRefresh.Content = L("ToolRouteTableBtnRefresh");
        BtnCopy.Content = L("ToolBtnCopyToClipboard");

        ColDest.Header = L("ToolRouteTableColDest");
        ColMask.Header = L("ToolRouteTableColMask");
        ColGateway.Header = L("ToolRouteTableColGateway");
        ColInterface.Header = L("ToolRouteTableColInterface");
        ColMetric.Header = L("ToolRouteTableColMetric");

        System.Windows.Automation.AutomationProperties.SetName(BtnRefresh, L("ToolRouteTableBtnRefresh"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolBtnCopyToClipboard"));
        System.Windows.Automation.AutomationProperties.SetName(RoutesGrid, L("ToolRouteTableTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        _entries.Clear();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "route",
                Arguments = "print",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            ParseRoutePrintOutput(output);
        }
        catch
        {
            // Silently handle — status bar shows zero routes
        }

        TxtStatus.Text = string.Format(CultureInfo.InvariantCulture,
            L("ToolRouteTableStatus"), _entries.Count,
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }

    // Locale-agnostic markers — matches EN ("IPv4 Route Table") and FR ("IPv4 Table de routage") etc.
    private static bool IsIpv4SectionHeader(string line) =>
        line.Contains("IPv4", StringComparison.OrdinalIgnoreCase)
        && (line.Contains("Route", StringComparison.OrdinalIgnoreCase) || line.Contains("routage", StringComparison.OrdinalIgnoreCase));

    private static bool IsActiveRoutesHeader(string line) =>
        line.Contains("Active", StringComparison.OrdinalIgnoreCase)
        || line.Contains("actif", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Itin", StringComparison.OrdinalIgnoreCase); // "Itinéraires actifs"

    private static bool IsEndOfSection(string line) =>
        line.StartsWith("==", StringComparison.Ordinal)
        || line.Contains("Persistent", StringComparison.OrdinalIgnoreCase)
        || line.Contains("persistant", StringComparison.OrdinalIgnoreCase)
        || line.Contains("IPv6", StringComparison.OrdinalIgnoreCase);

    private static bool IsColumnHeader(string line) =>
        line.Contains("Destination", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Masque", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Netmask", StringComparison.OrdinalIgnoreCase);

    private void ParseRoutePrintOutput(string output)
    {
        var lines = output.Split('\n');
        var inIpv4Section = false;
        var inActiveRoutes = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (!inIpv4Section && IsIpv4SectionHeader(line))
            {
                inIpv4Section = true;
                continue;
            }

            if (inIpv4Section && !inActiveRoutes && IsActiveRoutesHeader(line))
            {
                inActiveRoutes = true;
                continue;
            }

            if (inActiveRoutes && IsColumnHeader(line))
                continue;

            if (inActiveRoutes && IsEndOfSection(line))
                break;

            if (!inActiveRoutes || string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;

            // Validate: first field must look like an IP or network (contains dots)
            if (!parts[0].Contains('.')) continue;

            _entries.Add(new RouteEntry
            {
                Destination = parts[0],
                Mask = parts[1],
                Gateway = parts[2],
                Interface = parts[3],
                Metric = parts[4],
            });
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Destination\tMask\tGateway\tInterface\tMetric");
        foreach (var entry in _entries)
        {
            sb.Append(entry.Destination).Append('\t')
              .Append(entry.Mask).Append('\t')
              .Append(entry.Gateway).Append('\t')
              .Append(entry.Interface).Append('\t')
              .AppendLine(entry.Metric);
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible) { HelpPanel.Visibility = Visibility.Collapsed; return; }
        TxtHelpContent.Text = L("ToolHelpROUTES").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
        => HelpPanel.Visibility = Visibility.Collapsed;

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose() => GC.SuppressFinalize(this);

    // -- Data model -------------------------------------------------------

    public sealed class RouteEntry
    {
        public required string Destination { get; init; }
        public required string Mask { get; init; }
        public required string Gateway { get; init; }
        public required string Interface { get; init; }
        public required string Metric { get; init; }
    }
}
