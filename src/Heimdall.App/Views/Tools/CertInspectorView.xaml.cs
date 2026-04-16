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
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using Heimdall.App.Services;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Ssh;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// SSL/TLS certificate inspector that retrieves and displays certificate details
/// for any host:port combination, or scans multiple ports to discover TLS certificates.
/// </summary>
public partial class CertInspectorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private string _lastDetails = string.Empty;
    private bool _isChecking;
    private bool _disposed;
    private Action<bool>? _setBusy;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private string _selectedProfile = "quick";

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PerPortTimeout = TimeSpan.FromSeconds(10);
    private const int DaysWarningThreshold = 30;
    private const int MaxConcurrentTlsProbes = 10;

    /// <summary>
    /// Expiration status for color-coded display.
    /// </summary>
    public enum ExpirationStatus { Valid, Warning, Expired }

    /// <summary>
    /// Holds the full result of an SSL/TLS certificate inspection.
    /// </summary>
    private sealed record CertInspectionResult(
        X509Certificate2 Certificate,
        SslProtocols TlsProtocol,
        List<ChainCertInfo> ChainElements);

    /// <summary>
    /// Holds summary information for a single certificate in the chain.
    /// </summary>
    public sealed class ChainCertInfo
    {
        public string Subject { get; init; } = string.Empty;
        public string Expiry { get; init; } = string.Empty;
    }

    /// <summary>
    /// Display-ready data for a single certificate discovered during a multi-port scan.
    /// </summary>
    public sealed class CertScanResultItem
    {
        public int Port { get; init; }
        public string PortLabel { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
        public string Issuer { get; init; } = string.Empty;
        public string ValidFrom { get; init; } = string.Empty;
        public string ValidTo { get; init; } = string.Empty;
        public int DaysRemaining { get; init; }
        public string Serial { get; init; } = string.Empty;
        public string Thumbprint { get; init; } = string.Empty;
        public string SigAlgorithm { get; init; } = string.Empty;
        public string KeySizeText { get; init; } = string.Empty;
        public List<string> Sans { get; init; } = [];
        public string TlsVersion { get; init; } = string.Empty;
        public string HostnameMatchText { get; init; } = string.Empty;
        public bool HostnameMatches { get; init; }
        public Brush HostnameMatchBrush { get; init; } = Brushes.Transparent;
        public string ExpirationText { get; init; } = string.Empty;
        public ExpirationStatus ExpirationStatus { get; init; }
        public Brush ExpirationBrush { get; init; } = Brushes.Transparent;
        public List<ChainCertInfo> Chain { get; init; } = [];
        public string ChainHeader { get; init; } = string.Empty;
        public string DetailsText { get; init; } = string.Empty;

        // Labels for the DataTemplate bindings
        public string SubjectLabel { get; init; } = string.Empty;
        public string IssuerLabel { get; init; } = string.Empty;
        public string ValidFromLabel { get; init; } = string.Empty;
        public string ValidToLabel { get; init; } = string.Empty;
        public string SerialLabel { get; init; } = string.Empty;
        public string ThumbprintLabel { get; init; } = string.Empty;
        public string SigAlgorithmLabel { get; init; } = string.Empty;
        public string KeySizeLabel { get; init; } = string.Empty;
        public string TlsVersionLabel { get; init; } = string.Empty;
        public string SansLabel { get; init; } = string.Empty;
    }

    public CertInspectorView()
    {
        InitializeComponent();
        TxtHost.KeyDown += OnInputKeyDown;
        TxtPort.KeyDown += OnInputKeyDown;
        TxtCustomPorts.KeyDown += OnInputKeyDown;
        TxtPort.TextChanged += OnPortTextChanged;
    }

    /// <summary>
    /// Initializes the view with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();
        TxtHost.Clear();
        TxtPort.Clear();
        TxtCustomPorts.Clear();
        _selectedProfile = "quick";

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHost.Text = context.TargetHost;
        }

        if (context?.TargetPort is > 0)
        {
            TxtPort.Text = context.TargetPort.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            ParseArgument(context.Argument);
        }

        // Populate SSH gateway selector for tunnel-based inspection
        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();
        UpdateMode();
        UpdateProfileButtonStyles();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHost.Focus();
            TxtHost.SelectAll();
        });
    }

    private void ParseArgument(string argument)
    {
        var trimmed = argument.Trim();

        // Try host:port format
        var colonIndex = trimmed.LastIndexOf(':');
        if (colonIndex > 0 && int.TryParse(trimmed[(colonIndex + 1)..], out var port) && port is > 0 and <= 65535)
        {
            TxtHost.Text = trimmed[..colonIndex];
            TxtPort.Text = port.ToString();
        }
        else
        {
            TxtHost.Text = trimmed;
        }
    }

    private bool IsScanMode => string.IsNullOrWhiteSpace(TxtPort.Text);

    private void OnPortTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateMode();
    }

    private void UpdateMode()
    {
        if (ProfileBar is null) return; // Guard during InitializeComponent
        var scanMode = IsScanMode;
        ProfileBar.Visibility = scanMode ? Visibility.Visible : Visibility.Collapsed;
        BtnCheck.Content = scanMode ? L("ToolCertBtnScan") : L("ToolCertBtnCheck");
        AutomationProperties.SetName(BtnCheck, scanMode ? L("ToolCertBtnScan") : L("ToolCertBtnCheck"));
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolCertTitle");
        LblHost.Text = L("ToolCertHostLabel");
        BtnCheck.Content = IsScanMode ? L("ToolCertBtnScan") : L("ToolCertBtnCheck");
        LblSubject.Text = L("ToolCertSubject");
        LblIssuer.Text = L("ToolCertIssuer");
        LblValidFrom.Text = L("ToolCertValidFrom");
        LblValidTo.Text = L("ToolCertValidTo");
        LblSerial.Text = L("ToolCertSerial");
        LblThumbprint.Text = L("ToolCertThumbprint");
        LblSigAlg.Text = L("ToolCertSigAlg");
        LblKeySize.Text = L("ToolCertKeySize");
        LblSans.Text = L("ToolCertSans");
        BtnCopy.Content = L("ToolCertBtnCopy");
        LblTlsVersion.Text = L("ToolCertTlsVersion");
        LblChainTitle.Text = L("ToolCertChainTitle");

        AutomationProperties.SetName(BtnCheck, IsScanMode ? L("ToolCertBtnScan") : L("ToolCertBtnCheck"));
        AutomationProperties.SetName(TxtHost, L("ToolCertHostLabel"));
        AutomationProperties.SetName(TxtPort, L("ToolCertPortLabel"));
        AutomationProperties.SetName(BtnCopy, L("ToolCertBtnCopy"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        LblRouteVia.Text = L("ToolTunnelRouteVia");
        AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtHost.Tag = L("ToolWatermarkHostname");
        TxtPort.Tag = L("ToolCertPortWatermark");
        TxtEmptyState.Text = L("ToolCertEmptyState");

        // Scan mode elements
        BtnProfileQuick.Content = L("ToolCertScanProfileQuick");
        BtnProfileExtended.Content = L("ToolCertScanProfileExtended");
        BtnProfileCustom.Content = L("ToolCertScanProfileCustom");
        BtnCopyAll.Content = L("ToolCertBtnCopyAll");
        BtnExportCsv.Content = L("ToolCertBtnExport");

        BtnProfileQuick.ToolTip = $"{L("ToolCertScanProfileQuick")} ({NetworkToolPresets.TlsQuickScanPorts.Length} ports)";
        BtnProfileExtended.ToolTip = $"{L("ToolCertScanProfileExtended")} ({NetworkToolPresets.TlsExtendedScanPorts.Length} ports)";
        AutomationProperties.SetName(BtnProfileQuick, L("ToolCertScanProfileQuick"));
        AutomationProperties.SetName(BtnProfileExtended, L("ToolCertScanProfileExtended"));
        AutomationProperties.SetName(BtnProfileCustom, L("ToolCertScanProfileCustom"));
        AutomationProperties.SetName(BtnCopyAll, L("ToolCertBtnCopyAll"));
        AutomationProperties.SetName(BtnExportCsv, L("ToolCertBtnExport"));
        AutomationProperties.SetName(TxtCustomPorts, L("ToolCertScanProfileCustom"));
        AutomationProperties.SetName(ScanProgress, L("ToolCertA11yScanProgress"));
        AutomationProperties.SetName(LoadingBar, L("ToolCertA11yLoading"));

        BtnCopyAll.ToolTip = L("ToolBtnCopyToClipboard");
        BtnExportCsv.ToolTip = L("ToolCertBtnExport");
        TxtCustomPorts.Tag = L("ToolWatermarkPortList");
        AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpCERT").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_isChecking)
            {
                StopScan();
            }
            else
            {
                _ = CheckCertificateAsync();
            }
            e.Handled = true;
        }
    }

    private void OnCheckClick(object sender, RoutedEventArgs e)
    {
        if (_isChecking)
        {
            StopScan();
            return;
        }
        _ = CheckCertificateAsync();
    }

    // ===== Mode dispatch =====

    private async Task CheckCertificateAsync()
    {
        if (_isChecking)
        {
            return;
        }

        if (IsScanMode)
        {
            await ScanMultiplePortsAsync();
        }
        else
        {
            await CheckSinglePortAsync();
        }
    }

    // ===== Single-port mode (original behavior) =====

    private async Task CheckSinglePortAsync()
    {
        var host = TxtHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            TxtError.Text = L("ErrorInvalidHost");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!int.TryParse(TxtPort.Text.Trim(), out var port) || port is <= 0 or > 65535)
        {
            TxtError.Text = L("ToolCertErrorInvalidPort");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource(ConnectionTimeout);

        TxtError.Visibility = Visibility.Collapsed;
        SingleResultPanel.Visibility = Visibility.Collapsed;
        DetailsPanel.Visibility = Visibility.Collapsed;
        ExpirationBanner.Visibility = Visibility.Collapsed;
        TlsHostPanel.Visibility = Visibility.Collapsed;
        ChainPanel.Visibility = Visibility.Collapsed;
        BtnCopy.Visibility = Visibility.Collapsed;
        ScanResultsPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        LoadingBar.Visibility = Visibility.Visible;
        BtnCheck.IsEnabled = false;
        SetScanInputsEnabled(false);
        _isChecking = true;
        _setBusy?.Invoke(true);

        try
        {
            CertInspectionResult result;

            if (_selectedGateway is not null)
            {
                result = await Task.Run(() =>
                {
                    using var client = ConnectToGateway(_selectedGateway);
                    try
                    {
                        return RetrieveCertificateViaTunnel(client, host, port, _cts.Token);
                    }
                    finally
                    {
                        try { client.Disconnect(); } catch { /* best effort */ }
                    }
                }, _cts.Token);
            }
            else
            {
                result = await RetrieveCertificateAsync(host, port, _cts.Token);
            }

            SingleResultPanel.Visibility = Visibility.Visible;
            DisplayCertificate(result, host);
        }
        catch (OperationCanceledException)
        {
            TxtError.Text = L("ToolCertErrorTimeout");
            TxtError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"CertInspector certificate retrieval failed: {ex.Message}");
            var errorMsg = _selectedGateway is not null
                ? string.Format(L("ToolTunnelFailed"), ex.Message)
                : string.Format(L("ToolCertErrorConnection"), ex.Message);
            TxtError.Text = errorMsg;
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            _isChecking = false;
            _setBusy?.Invoke(false);
            LoadingBar.Visibility = Visibility.Collapsed;
            SetScanInputsEnabled(true);
            UpdateMode();
            BtnCheck.IsEnabled = true;
        }
    }

    // ===== Multi-port scan mode =====

    private async Task ScanMultiplePortsAsync()
    {
        var host = TxtHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            TxtError.Text = L("ErrorInvalidHost");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        var ports = GetScanPorts();
        if (ports.Count == 0)
        {
            TxtError.Text = L("ToolCertScanErrorCustomPorts");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        // Reset UI
        TxtError.Visibility = Visibility.Collapsed;
        SingleResultPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        LoadingBar.Visibility = Visibility.Collapsed;
        ScanResultsPanel.Visibility = Visibility.Collapsed;
        TxtScanNoResults.Visibility = Visibility.Collapsed;
        ScanFooterPanel.Visibility = Visibility.Collapsed;
        ScanResultsList.ItemsSource = null;

        // Show progress
        ScanProgress.Maximum = ports.Count;
        ScanProgress.Value = 0;
        TxtProgressPercent.Text = "0%";
        TxtProgressCount.Text = string.Format(L("ToolCertScanProgress"), 0, ports.Count);
        ScanProgressPanel.Visibility = Visibility.Visible;

        _isChecking = true;
        _setBusy?.Invoke(true);
        BtnCheck.Content = L("ToolCertBtnStop");
        BtnCheck.Foreground = (Brush)FindResource("ErrorBrush");
        BtnCheck.Style = (Style)FindResource("SecondaryButtonStyle");
        AutomationProperties.SetName(BtnCheck, L("ToolCertBtnStop"));
        SetScanInputsEnabled(false);

        Renci.SshNet.SshClient? tunnelClient = null;
        if (_selectedGateway is not null)
        {
            try
            {
                tunnelClient = ConnectToGateway(_selectedGateway);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"CertInspector gateway connection failed: {ex.Message}");
                TxtError.Text = string.Format(L("ToolTunnelFailed"), ex.Message);
                TxtError.Visibility = Visibility.Visible;
                ResetScanUi();
                return;
            }
        }

        var results = new List<CertScanResultItem>();
        var completed = 0;
        var ct = _cts.Token;

        // Serialize SSH CreateCommand() calls on the shared tunnel client
        var commandLock = tunnelClient is not null ? new SemaphoreSlim(1, 1) : null;

        try
        {
            using var semaphore = new SemaphoreSlim(MaxConcurrentTlsProbes);

            var tasks = ports.Select(async port =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    CertInspectionResult? result = null;
                    try
                    {
                        // Per-port timeout linked with the global cancel token
                        using var portTimeout = new CancellationTokenSource(PerPortTimeout);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, portTimeout.Token);

                        if (tunnelClient is not null)
                        {
                            await commandLock!.WaitAsync(ct).ConfigureAwait(false);
                            try
                            {
                                result = RetrieveCertificateViaTunnel(tunnelClient, host, port, linked.Token);
                            }
                            finally
                            {
                                commandLock.Release();
                            }
                        }
                        else
                        {
                            result = await RetrieveCertificateAsync(host, port, linked.Token);
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Per-port timeout — not a scan cancellation. Skip this port.
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        // Port does not speak TLS or is unreachable — skip silently
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        completed++;
                        ScanProgress.Value = completed;
                        var percent = (int)(completed * 100.0 / ports.Count);
                        TxtProgressPercent.Text = $"{percent}%";
                        TxtProgressCount.Text = string.Format(L("ToolCertScanProgress"), completed, ports.Count);

                        if (result is not null)
                        {
                            try
                            {
                                var item = BuildScanResultItem(result, host, port);
                                results.Add(item);
                            }
                            catch (Exception ex)
                            {
                                Core.Logging.FileLogger.Warn(
                                    $"CertInspector failed to process certificate on port {port}: {ex.Message}");
                            }
                        }
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Scan was stopped by user — show partial results below
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"CertInspector scan failed: {ex.Message}");
            TxtError.Text = string.Format(L("ToolCertErrorConnection"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            commandLock?.Dispose();
            if (tunnelClient is not null)
            {
                try { tunnelClient.Disconnect(); } catch { /* best effort */ }
                tunnelClient.Dispose();
            }
            ResetScanUi();
        }

        // Display results (including partial results after cancel)
        ScanResultsPanel.Visibility = Visibility.Visible;

        if (results.Count == 0)
        {
            TxtScanNoResults.Text = L("ToolCertScanNoResults");
            TxtScanNoResults.Visibility = Visibility.Visible;
        }
        else
        {
            var sorted = results.OrderBy(r => r.Port).ToList();
            ScanResultsList.ItemsSource = sorted;
            TxtScanSummary.Text = string.Format(L("ToolCertScanFound"), sorted.Count, ports.Count);
            ScanFooterPanel.Visibility = Visibility.Visible;
        }
    }

    private void StopScan()
    {
        _cts?.Cancel();
    }

    private void ResetScanUi()
    {
        _isChecking = false;
        _setBusy?.Invoke(false);
        ScanProgressPanel.Visibility = Visibility.Collapsed;
        BtnCheck.Foreground = (Brush)FindResource("TextPrimaryBrush");
        BtnCheck.Style = (Style)FindResource("PrimaryButtonStyle");
        SetScanInputsEnabled(true);
        UpdateMode();
    }

    private void SetScanInputsEnabled(bool enabled)
    {
        TxtHost.IsReadOnly = !enabled;
        TxtPort.IsReadOnly = !enabled;
        TxtCustomPorts.IsReadOnly = !enabled;
        CmbRouteVia.IsEnabled = enabled;
        BtnProfileQuick.IsEnabled = enabled;
        BtnProfileExtended.IsEnabled = enabled;
        BtnProfileCustom.IsEnabled = enabled;
    }

    private List<int> GetScanPorts()
    {
        return _selectedProfile switch
        {
            "extended" => [.. NetworkToolPresets.TlsExtendedScanPorts],
            "custom" => ParsePorts(TxtCustomPorts.Text),
            _ => [.. NetworkToolPresets.TlsQuickScanPorts],
        };
    }

    // ===== Profile buttons =====

    private void OnProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string profile)
        {
            _selectedProfile = profile;
            TxtCustomPorts.Visibility = profile == "custom" ? Visibility.Visible : Visibility.Collapsed;
            UpdateProfileButtonStyles();
        }
    }

    private void UpdateProfileButtonStyles()
    {
        if (BtnProfileQuick is null) return; // Guard during InitializeComponent
        BtnProfileQuick.Style = (Style)FindResource(
            _selectedProfile == "quick" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        BtnProfileExtended.Style = (Style)FindResource(
            _selectedProfile == "extended" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        BtnProfileCustom.Style = (Style)FindResource(
            _selectedProfile == "custom" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
    }

    // ===== Result builders =====

    private CertScanResultItem BuildScanResultItem(CertInspectionResult result, string host, int port)
    {
        using var cert = result.Certificate;
        var serviceLabel = NetworkToolPresets.GetTlsServiceLabel(port);
        var portLabel = string.Format(L("ToolCertScanPortHeader"), port, serviceLabel);
        var sans = ExtractSans(cert);
        var keySize = GetPublicKeySize(cert);
        var sha256Bytes = cert.GetCertHash(HashAlgorithmName.SHA256);
        var daysRemaining = (cert.NotAfter - DateTime.UtcNow).Days;
        var hostnameMatches = CheckHostnameMatch(cert, sans, host);

        var status = daysRemaining < 0
            ? ExpirationStatus.Expired
            : daysRemaining <= DaysWarningThreshold
                ? ExpirationStatus.Warning
                : ExpirationStatus.Valid;

        var expirationBrush = status switch
        {
            ExpirationStatus.Expired => (Brush)FindResource("ErrorBrush"),
            ExpirationStatus.Warning => (Brush)FindResource("WarningBrush"),
            _ => (Brush)FindResource("SuccessBrush"),
        };

        var expirationText = status switch
        {
            ExpirationStatus.Expired => L("ToolCertExpired"),
            ExpirationStatus.Warning => string.Format(L("ToolCertExpiringSoon"), daysRemaining),
            _ => string.Format(L("ToolCertValid"), daysRemaining),
        };

        var hostnameMatchText = hostnameMatches
            ? "\u2714 " + L("ToolCertHostnameMatch")
            : "\u2716 " + L("ToolCertHostnameMismatch");

        var hostnameMatchBrush = hostnameMatches
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("ErrorBrush");

        var detailsText = BuildDetailsText(cert, $"{host}:{port}", sha256Bytes, sans, daysRemaining,
            result.TlsProtocol, hostnameMatches);

        return new CertScanResultItem
        {
            Port = port,
            PortLabel = portLabel,
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            ValidFrom = cert.NotBefore.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            ValidTo = $"{cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC} ({daysRemaining} {L("ToolCertDaysRemaining")})",
            DaysRemaining = daysRemaining,
            Serial = cert.SerialNumber,
            Thumbprint = Convert.ToHexString(sha256Bytes),
            SigAlgorithm = cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "-",
            KeySizeText = keySize > 0 ? string.Format(L("ToolCertKeySizeBits"), keySize) : "-",
            Sans = sans.Count > 0 ? sans : ["-"],
            TlsVersion = FormatTlsProtocol(result.TlsProtocol),
            HostnameMatchText = hostnameMatchText,
            HostnameMatches = hostnameMatches,
            HostnameMatchBrush = hostnameMatchBrush,
            ExpirationText = expirationText,
            ExpirationStatus = status,
            ExpirationBrush = expirationBrush,
            Chain = result.ChainElements,
            ChainHeader = $"{L("ToolCertChainTitle")} ({result.ChainElements.Count})",
            DetailsText = detailsText,
            SubjectLabel = L("ToolCertSubject"),
            IssuerLabel = L("ToolCertIssuer"),
            ValidFromLabel = L("ToolCertValidFrom"),
            ValidToLabel = L("ToolCertValidTo"),
            SerialLabel = L("ToolCertSerial"),
            ThumbprintLabel = L("ToolCertThumbprint"),
            SigAlgorithmLabel = L("ToolCertSigAlg"),
            KeySizeLabel = L("ToolCertKeySize"),
            TlsVersionLabel = L("ToolCertTlsVersion"),
            SansLabel = L("ToolCertSans"),
        };
    }

    // ===== Copy / Export =====

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastDetails))
        {
            try { Clipboard.SetText(_lastDetails); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopyAllClick(object sender, RoutedEventArgs e)
    {
        if (ScanResultsList.ItemsSource is not IEnumerable<CertScanResultItem> items) return;
        var list = items.ToList();
        if (list.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var item in list)
        {
            sb.AppendLine($"=== {item.PortLabel} ===");
            sb.AppendLine(item.DetailsText);
            sb.AppendLine();
        }
        try { Clipboard.SetText(sb.ToString()); }
        catch (System.Runtime.InteropServices.ExternalException) { return; }
        CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (ScanResultsList.ItemsSource is not IEnumerable<CertScanResultItem> items) return;
        var list = items.ToList();
        if (list.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = L("FileDialogCsvFilter"),
            FileName = $"certscan_{SanitizeFileName(TxtHost.Text.Trim())}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dialog.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",",
            L("ToolCertCsvPort"), L("ToolCertCsvService"), L("ToolCertCsvSubject"),
            L("ToolCertCsvIssuer"), L("ToolCertCsvValidFrom"), L("ToolCertCsvValidTo"),
            L("ToolCertCsvDaysRemaining"), L("ToolCertCsvTlsVersion"), L("ToolCertCsvKeySize"),
            L("ToolCertCsvHostnameMatch"), L("ToolCertCsvSans")));
        foreach (var r in list)
        {
            var service = InputValidator.SanitizeCsvCell(NetworkToolPresets.GetTlsServiceLabel(r.Port));
            var sans = InputValidator.SanitizeCsvCell(string.Join("; ", r.Sans)).Replace("\"", "\"\"");
            var subject = InputValidator.SanitizeCsvCell(r.Subject).Replace("\"", "\"\"");
            var issuer = InputValidator.SanitizeCsvCell(r.Issuer).Replace("\"", "\"\"");
            var validTo = InputValidator.SanitizeCsvCell(r.ValidTo).Replace("\"", "\"\"");
            sb.AppendLine(string.Join(",",
                r.Port,
                $"\"{service}\"",
                $"\"{subject}\"",
                $"\"{issuer}\"",
                r.ValidFrom,
                $"\"{validTo}\"",
                r.DaysRemaining,
                InputValidator.SanitizeCsvCell(r.TlsVersion),
                InputValidator.SanitizeCsvCell(r.KeySizeText),
                r.HostnameMatches,
                $"\"{sans}\""));
        }
        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
        CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
    }

    // ===== Certificate retrieval =====

    private async Task<CertInspectionResult> RetrieveCertificateAsync(string host, int port, CancellationToken ct)
    {
        X509Certificate? remoteCert = null;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct).AsTask().ConfigureAwait(false);

        using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) =>
        {
            remoteCert = cert;
            return true;
        });

        await ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = host }, ct).ConfigureAwait(false);

        if (remoteCert == null)
        {
            throw new InvalidOperationException(L("ErrorNoCertReceived"));
        }

        var cert2 = new X509Certificate2(remoteCert);
        remoteCert.Dispose();
        var tlsProtocol = ssl.SslProtocol;

        var chainElements = new List<ChainCertInfo>();
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(cert2);

        foreach (var element in chain.ChainElements)
        {
            chainElements.Add(new ChainCertInfo
            {
                Subject = element.Certificate.Subject,
                Expiry = element.Certificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss UTC")
            });
        }

        return new CertInspectionResult(cert2, tlsProtocol, chainElements);
    }

    private CertInspectionResult RetrieveCertificateViaTunnel(
        Renci.SshNet.SshClient sshClient, string host, int port, CancellationToken ct)
    {
        var escapedHost = InputValidator.EscapeShellArg(host);
        using var cmd = sshClient.CreateCommand(
            $"echo | openssl s_client -connect {escapedHost}:{port} -servername {escapedHost} 2>/dev/null");
        cmd.CommandTimeout = TimeSpan.FromSeconds(10);
        cmd.Execute();
        var pemOutput = cmd.Result ?? string.Empty;

        var beginMarker = "-----BEGIN CERTIFICATE-----";
        var endMarker = "-----END CERTIFICATE-----";
        var beginIdx = pemOutput.IndexOf(beginMarker, StringComparison.Ordinal);
        var endIdx = pemOutput.IndexOf(endMarker, StringComparison.Ordinal);

        if (beginIdx < 0 || endIdx < 0)
        {
            throw new InvalidOperationException(L("ErrorNoCertReceivedViaTunnel"));
        }

        var pemBlock = pemOutput[beginIdx..(endIdx + endMarker.Length)];
        var base64 = pemBlock
            .Replace(beginMarker, "")
            .Replace(endMarker, "")
            .Replace("\r", "")
            .Replace("\n", "");
        var certBytes = Convert.FromBase64String(base64);
        var cert = X509CertificateLoader.LoadCertificate(certBytes);

        var tlsProtocol = SslProtocols.None;
        if (pemOutput.Contains("TLSv1.3", StringComparison.OrdinalIgnoreCase))
            tlsProtocol = SslProtocols.Tls13;
        else if (pemOutput.Contains("TLSv1.2", StringComparison.OrdinalIgnoreCase))
            tlsProtocol = SslProtocols.Tls12;

        var chainElements = new List<ChainCertInfo>();
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(cert);

        foreach (var element in chain.ChainElements)
        {
            chainElements.Add(new ChainCertInfo
            {
                Subject = element.Certificate.Subject,
                Expiry = element.Certificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss UTC")
            });
        }

        return new CertInspectionResult(cert, tlsProtocol, chainElements);
    }

    // ===== Single-port display (original) =====

    private void DisplayCertificate(CertInspectionResult result, string host)
    {
        using var cert = result.Certificate;

        TxtSubject.Text = cert.Subject;
        TxtIssuer.Text = cert.Issuer;
        TxtValidFrom.Text = cert.NotBefore.ToString("yyyy-MM-dd HH:mm:ss UTC");
        TxtSerial.Text = cert.SerialNumber;
        TxtSigAlg.Text = cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "-";

        var keySize = GetPublicKeySize(cert);
        TxtKeySize.Text = keySize > 0 ? string.Format(L("ToolCertKeySizeBits"), keySize) : "-";

        var sha256Bytes = cert.GetCertHash(HashAlgorithmName.SHA256);
        TxtThumbprint.Text = Convert.ToHexString(sha256Bytes);

        var daysRemaining = (cert.NotAfter - DateTime.UtcNow).Days;
        TxtValidTo.Text = $"{cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC} ({daysRemaining} {L("ToolCertDaysRemaining")})";

        UpdateExpirationBanner(daysRemaining);

        var sans = ExtractSans(cert);
        SansList.ItemsSource = sans.Count > 0 ? sans : ["-"];

        TxtTlsVersion.Text = FormatTlsProtocol(result.TlsProtocol);

        var hostnameMatches = CheckHostnameMatch(cert, sans, host);
        if (hostnameMatches)
        {
            TxtHostnameMatch.Text = "\u2714 " + L("ToolCertHostnameMatch");
            TxtHostnameMatch.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            TxtHostnameMatch.Text = "\u2716 " + L("ToolCertHostnameMismatch");
            TxtHostnameMatch.Foreground = (Brush)FindResource("ErrorBrush");
        }

        TlsHostPanel.Visibility = Visibility.Visible;

        ChainList.ItemsSource = result.ChainElements;
        ChainPanel.Visibility = result.ChainElements.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _lastDetails = BuildDetailsText(cert, host, sha256Bytes, sans, daysRemaining, result.TlsProtocol, hostnameMatches);

        DetailsPanel.Visibility = Visibility.Visible;
        BtnCopy.Visibility = Visibility.Visible;
    }

    // ===== Shared helpers =====

    private static string FormatTlsProtocol(SslProtocols protocol)
    {
        return protocol switch
        {
            SslProtocols.Tls12 => "TLS 1.2",
            SslProtocols.Tls13 => "TLS 1.3",
#pragma warning disable CA5397, CS0618, SYSLIB0039
            SslProtocols.Tls11 => "TLS 1.1",
            SslProtocols.Tls => "TLS 1.0",
            SslProtocols.Ssl3 => "SSL 3.0",
            SslProtocols.Ssl2 => "SSL 2.0",
#pragma warning restore CA5397, CS0618, SYSLIB0039
            _ => protocol.ToString()
        };
    }

    private static bool CheckHostnameMatch(X509Certificate2 cert, List<string> sans, string host)
    {
        foreach (var san in sans)
        {
            if (MatchesHostname(san, host))
            {
                return true;
            }
        }

        var cn = ExtractCn(cert.Subject);
        if (!string.IsNullOrEmpty(cn) && MatchesHostname(cn, host))
        {
            return true;
        }

        return false;
    }

    private static bool MatchesHostname(string pattern, string host)
    {
        if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            var dotIndex = host.IndexOf('.', StringComparison.Ordinal);
            if (dotIndex > 0)
            {
                var hostSuffix = host[dotIndex..];
                return string.Equals(suffix, hostSuffix, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string ExtractCn(string subject)
    {
        const string cnPrefix = "CN=";
        var startIndex = subject.IndexOf(cnPrefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        startIndex += cnPrefix.Length;
        var endIndex = subject.IndexOf(',', startIndex);
        return endIndex < 0 ? subject[startIndex..].Trim() : subject[startIndex..endIndex].Trim();
    }

    private void UpdateExpirationBanner(int daysRemaining)
    {
        ExpirationBanner.Visibility = Visibility.Visible;

        if (daysRemaining < 0)
        {
            ExpirationBanner.Background = (Brush)FindResource("ErrorBrush");
            TxtExpiration.Text = L("ToolCertExpired");
        }
        else if (daysRemaining <= DaysWarningThreshold)
        {
            ExpirationBanner.Background = (Brush)FindResource("WarningBrush");
            TxtExpiration.Text = string.Format(L("ToolCertExpiringSoon"), daysRemaining);
        }
        else
        {
            ExpirationBanner.Background = (Brush)FindResource("SuccessBrush");
            TxtExpiration.Text = string.Format(L("ToolCertValid"), daysRemaining);
        }
    }

    private static List<string> ExtractSans(X509Certificate2 cert)
    {
        var sans = new List<string>();
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value != "2.5.29.17") continue;

            var sanExt = (X509SubjectAlternativeNameExtension)ext;
            foreach (var name in sanExt.EnumerateDnsNames())
            {
                sans.Add(name);
            }

            foreach (var ip in sanExt.EnumerateIPAddresses())
            {
                sans.Add(ip.ToString());
            }
        }

        return sans;
    }

    private static int GetPublicKeySize(X509Certificate2 cert)
    {
        using var rsa = cert.GetRSAPublicKey();
        if (rsa != null) return rsa.KeySize;

        using var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa != null) return ecdsa.KeySize;

        return 0;
    }

    private string BuildDetailsText(X509Certificate2 cert, string host, byte[] sha256Bytes, List<string> sans, int daysRemaining, SslProtocols tlsProtocol, bool hostnameMatches)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{L("ToolCertDetailHost")}: {host}");
        sb.AppendLine($"{L("ToolCertTlsVersion")}: {FormatTlsProtocol(tlsProtocol)}");
        sb.AppendLine($"{(hostnameMatches ? L("ToolCertHostnameMatch") : L("ToolCertHostnameMismatch"))}");
        sb.AppendLine($"{L("ToolCertSubject")}: {cert.Subject}");
        sb.AppendLine($"{L("ToolCertIssuer")}: {cert.Issuer}");
        sb.AppendLine($"{L("ToolCertValidFrom")}: {cert.NotBefore:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"{L("ToolCertValidTo")}: {cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC} ({daysRemaining} {L("ToolCertDaysRemaining")})");
        sb.AppendLine($"{L("ToolCertSerial")}: {cert.SerialNumber}");
        sb.AppendLine($"{L("ToolCertSha256Label")}: {Convert.ToHexString(sha256Bytes)}");
        sb.AppendLine($"{L("ToolCertSigAlg")}: {cert.SignatureAlgorithm.FriendlyName}");
        sb.AppendLine($"{L("ToolCertKeySize")}: {string.Format(L("ToolCertKeySizeBits"), GetPublicKeySize(cert))}");

        if (sans.Count > 0)
        {
            sb.AppendLine($"{L("ToolCertSans")}: {string.Join(", ", sans)}");
        }

        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }

    private static List<int> ParsePorts(string input)
    {
        var ports = new HashSet<int>();
        foreach (var segment in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Contains('-'))
            {
                var rangeParts = segment.Split('-', 2);
                if (int.TryParse(rangeParts[0].Trim(), out var start) &&
                    int.TryParse(rangeParts[1].Trim(), out var end) &&
                    start >= 1 && end <= 65535 && start <= end)
                {
                    for (var p = start; p <= end; p++) ports.Add(p);
                }
            }
            else if (int.TryParse(segment, out var port) && port is >= 1 and <= 65535)
            {
                ports.Add(port);
            }
        }
        return ports.OrderBy(p => p).ToList();
    }

    // ===== Gateway + Route via =====

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

    private static Renci.SshNet.SshClient ConnectToGateway(SshGatewayDto gateway)
        => ToolGatewayConnector.Connect(gateway);

    // ===== Lifecycle =====

    public bool CanClose() => !_isChecking;

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TxtHost.KeyDown -= OnInputKeyDown;
        TxtPort.KeyDown -= OnInputKeyDown;
        TxtCustomPorts.KeyDown -= OnInputKeyDown;
        TxtPort.TextChanged -= OnPortTextChanged;
        _setBusy?.Invoke(false);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}
