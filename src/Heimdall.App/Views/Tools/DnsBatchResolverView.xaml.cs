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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Batch DNS resolver tool that resolves multiple hostnames concurrently
/// and displays IPv4, IPv6, resolve time, and status for each entry.
/// </summary>
public partial class DnsBatchResolverView : UserControl, IToolView
{
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(30);

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isResolving;
    private Action<bool>? _setBusy;
    private readonly ToolAsyncStateController _viewState;
    private readonly ObservableCollection<DnsResult> _results = [];

    public DnsBatchResolverView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        _viewState = new ToolAsyncStateController(
            isBusy => _setBusy?.Invoke(isBusy),
            LoadingBar,
            TxtError,
            EmptyStatePanel,
            ResultsPanel,
            TxtStatus,
            BtnResolve,
            TxtHostnames);
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();
        TxtHostnames.Clear();

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHostnames.Text = context.TargetHost;
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHostnames.Focus();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolDnsBatchTitle");
        BtnResolve.Content = L("ToolDnsBatchBtnResolve");
        TxtStatus.Text = string.Empty;

        ColHostname.Header = L("ToolDnsBatchColHostname");
        ColIpv4.Header = L("ToolDnsBatchColIpv4");
        ColIpv6.Header = L("ToolDnsBatchColIpv6");
        ColTime.Header = L("ToolDnsBatchColTime");
        ColStatus.Header = L("ToolDnsBatchColStatus");

        BtnCopy.Content = L("ToolBtnCopyToClipboard");
        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        TxtHostnames.Tag = L("ToolDnsBatchInputPlaceholder");
        TxtEmptyState.Text = L("ToolDnsBatchInputPlaceholder");

        System.Windows.Automation.AutomationProperties.SetName(BtnResolve, L("ToolDnsBatchBtnResolve"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHostnames, L("ToolDnsBatchInputPlaceholder"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolBtnCopyToClipboard"));
        System.Windows.Automation.AutomationProperties.SetName(ResultsGrid, L("ToolDnsBatchTitle"));
        System.Windows.Automation.AutomationProperties.SetName(LoadingBar, L("ToolDnsBatchTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpDNSBATCH").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private void OnResolveClick(object sender, RoutedEventArgs e)
    {
        if (_isResolving)
        {
            StopResolve();
        }
        else
        {
            _ = PerformBatchResolveAsync();
        }
    }

    private async Task PerformBatchResolveAsync()
    {
        if (_disposed || _isResolving)
        {
            return;
        }

        var rawText = TxtHostnames.Text.Trim();
        _viewState.Reset(showEmptyState: false);
        _results.Clear();

        if (string.IsNullOrWhiteSpace(rawText))
        {
            _viewState.ShowError(L("ToolValidationHostRequired"), string.Empty, showEmptyState: true);
            return;
        }

        var hostnames = rawText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hostnames.Count == 0)
        {
            _viewState.ShowError(L("ToolValidationHostRequired"), string.Empty, showEmptyState: true);
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(ResolveTimeout);

        _isResolving = true;
        BtnResolve.Content = L("ToolDnsBatchBtnStop");
        BtnResolve.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
        BtnResolve.Style = (Style)FindResource("SecondaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnResolve, L("ToolDnsBatchBtnStop"));
        _viewState.Begin(L("ToolDnsBatchBtnResolve"));

        var ct = _cts.Token;

        try
        {
            var tasks = hostnames.Select(hostname => ResolveHostAsync(hostname, ct));
            var results = await Task.WhenAll(tasks);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            foreach (var result in results)
            {
                _results.Add(result);
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _viewState.ShowResults(string.Format(L("ToolDnsBatchStatus"), _results.Count, timestamp));
        }
        catch (OperationCanceledException)
        {
            // Resolve was cancelled — partial results may already be present
        }
        catch (Exception ex)
        {
            _viewState.ShowError(ex.Message, string.Empty, keepResultsVisible: _results.Count > 0);
        }
        finally
        {
            StopResolve();
        }
    }

    private static async Task<DnsResult> ResolveHostAsync(string hostname, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname, ct);
            sw.Stop();

            var ipv4 = addresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .FirstOrDefault() ?? "\u2014";

            var ipv6 = addresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(a => a.ToString())
                .FirstOrDefault() ?? "\u2014";

            return new DnsResult(hostname, ipv4, ipv6, (int)sw.ElapsedMilliseconds, "OK");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return new DnsResult(hostname, "\u2014", "\u2014", (int)sw.ElapsedMilliseconds, ex.SocketErrorCode.ToString());
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DnsResult(hostname, "\u2014", "\u2014", (int)sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private void StopResolve()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isResolving = false;
        _setBusy?.Invoke(false);

        BtnResolve.Content = L("ToolDnsBatchBtnResolve");
        BtnResolve.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
        BtnResolve.Style = (Style)FindResource("PrimaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnResolve, L("ToolDnsBatchBtnResolve"));
        BtnResolve.IsEnabled = true;
        TxtHostnames.IsReadOnly = false;

        UpdateResultsSurface();
    }

    private void UpdateResultsSurface()
    {
        var hasResults = _results.Count > 0;
        ResultsPanel.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasResults || _isResolving
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
            sb.AppendLine($"{L("ToolDnsBatchColHostname")}\t{L("ToolDnsBatchColIpv4")}\t{L("ToolDnsBatchColIpv6")}\t{L("ToolDnsBatchColTime")}\t{L("ToolDnsBatchColStatus")}");

            foreach (var r in _results)
            {
                sb.AppendLine($"{r.Hostname}\t{r.Ipv4}\t{r.Ipv6}\t{r.ResolveTimeMs}\t{r.Status}");
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard locked by another process
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isResolving;

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
        _setBusy?.Invoke(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents a single DNS batch resolution result for DataGrid binding.
    /// </summary>
    public sealed record DnsResult(string Hostname, string Ipv4, string Ipv6, int ResolveTimeMs, string Status);
}
