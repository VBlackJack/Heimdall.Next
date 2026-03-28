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
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// WHOIS lookup tool that queries WHOIS servers over raw TCP (port 43)
/// for domain and IP registration information.
/// </summary>
public partial class WhoisLookupView : UserControl, IToolView
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private const int WhoisPort = 43;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isQuerying;
    private Action<bool>? _setBusy;

    public WhoisLookupView()
    {
        InitializeComponent();
        TxtDomain.KeyDown += OnDomainKeyDown;
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
            TxtDomain.Text = context.TargetHost;
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtDomain.Focus();
            TxtDomain.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolWhoisTitle");
        LblDomain.Text = L("ToolWhoisDomainLabel");
        BtnLookup.Content = L("ToolWhoisBtnLookup");
        TxtStatus.Text = string.Empty;
        BtnCopyResults.Content = L("ToolWhoisBtnCopy");
        BtnCopyResults.ToolTip = L("ToolBtnCopyToClipboard");

        System.Windows.Automation.AutomationProperties.SetName(TxtDomain, L("ToolWhoisDomainLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnLookup, L("ToolWhoisBtnLookup"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyResults, L("ToolWhoisBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(TxtResults, L("ToolWhoisResults"));
        System.Windows.Automation.AutomationProperties.SetName(LoadingBar, L("ToolWhoisStatusQuerying"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtDomain.Tag = L("ToolWatermarkExampleDomainOrIp");
        TxtEmptyState.Text = L("ToolWhoisEmptyState");
    }

    private void OnDomainKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = PerformWhoisAsync();
            e.Handled = true;
        }
    }

    private void OnLookupClick(object sender, RoutedEventArgs e)
    {
        _ = PerformWhoisAsync();
    }

    private async Task PerformWhoisAsync()
    {
        var domain = TxtDomain.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        TxtStatus.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(domain))
        {
            TxtError.Text = L("ToolWhoisErrorDomainRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!InputValidator.ValidateDomain(domain) &&
            !System.Net.IPAddress.TryParse(domain, out _))
        {
            TxtError.Text = L("ToolWhoisErrorInvalidDomain");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(QueryTimeout);

        _isQuerying = true;
        _setBusy?.Invoke(true);
        BtnLookup.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        TxtStatus.Text = L("ToolWhoisStatusQuerying");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await WhoisQueryAsync(domain, _cts.Token);
            stopwatch.Stop();

            if (_cts.IsCancellationRequested) return;

            TxtResultHeader.Text = string.Format(L("ToolWhoisResultHeader"), domain);
            TxtResults.Text = result;
            ResultsPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            TxtStatus.Text = string.Format(L("ToolWhoisStatusComplete"), stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            TxtError.Text = L("ToolWhoisErrorTimeout");
            TxtError.Visibility = Visibility.Visible;
        }
        catch (SocketException ex)
        {
            TxtError.Text = string.Format(L("ToolWhoisErrorFailed"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TxtError.Text = string.Format(L("ToolWhoisErrorFailed"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            _isQuerying = false;
            _setBusy?.Invoke(false);
            BtnLookup.IsEnabled = true;
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Performs a WHOIS query over raw TCP to the appropriate WHOIS server.
    /// </summary>
    internal static async Task<string> WhoisQueryAsync(string domain, CancellationToken ct)
    {
        var server = domain.Contains('.') ? GetWhoisServer(domain) : "whois.iana.org";
        using var client = new TcpClient();
        await client.ConnectAsync(server, WhoisPort, ct);
        using var stream = client.GetStream();
        var query = Encoding.ASCII.GetBytes(domain + "\r\n");
        await stream.WriteAsync(query, ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Resolves the appropriate WHOIS server for a given domain based on its TLD.
    /// </summary>
    internal static string GetWhoisServer(string domain)
    {
        var tld = domain.Split('.')[^1].ToLowerInvariant();
        return tld switch
        {
            "com" or "net" => "whois.verisign-grs.com",
            "org" => "whois.pir.org",
            "io" => "whois.nic.io",
            "dev" => "whois.nic.google",
            "fr" => "whois.nic.fr",
            "de" => "whois.denic.de",
            "uk" => "whois.nic.uk",
            "eu" => "whois.eu",
            "nl" => "whois.domain-registry.nl",
            "be" => "whois.dns.be",
            "ch" => "whois.nic.ch",
            "au" => "whois.auda.org.au",
            "ca" => "whois.cira.ca",
            "jp" => "whois.jprs.jp",
            _ => "whois.iana.org"
        };
    }

    private void OnCopyResultsClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtResults.Text))
        {
            try
            {
                Clipboard.SetText(TxtResults.Text);
                CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"WhoisLookup clipboard copy failed: {ex.Message}");
            }
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpWHOIS");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public bool CanClose() => !_isQuerying;

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _setBusy?.Invoke(false);
        GC.SuppressFinalize(this);
    }
}
