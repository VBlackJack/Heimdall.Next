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
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
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

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Fetches HTTP response headers for a URL and grades its security posture
/// against best practices (HSTS, CSP, X-Frame-Options, etc.).
/// </summary>
public partial class HttpHeaderAnalyzerView : UserControl, IToolView
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);
    private const int DefaultHttpPort = 80;
    private const int DefaultHttpsPort = 443;
    private const int MaxResponseBytes = 16384;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isAnalyzing;
    private bool _disposed;
    private Action<bool>? _setBusy;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private string _lastReport = string.Empty;

    public HttpHeaderAnalyzerView()
    {
        InitializeComponent();
        TxtUrl.KeyDown += OnUrlKeyDown;
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
            var host = context.TargetHost;
            if (context.TargetPort is > 0 and not 80 and not 443)
            {
                TxtUrl.Text = $"https://{host}:{context.TargetPort.Value}";
            }
            else
            {
                TxtUrl.Text = $"https://{host}";
            }
        }

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            TxtUrl.Text = context.Argument;
        }

        // Populate SSH gateway selector for tunnel-based analysis
        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtUrl.Focus();
            TxtUrl.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolHttpHeadersTitle");
        LblUrl.Text = L("ToolHttpHeadersUrl");
        BtnCheck.Content = L("ToolHttpHeadersBtnCheck");
        LblRouteVia.Text = L("ToolTunnelRouteVia");
        TxtEmptyState.Text = L("ToolHttpHeadersEmptyState");
        BtnCopyReport.Content = L("ToolHttpHeadersBtnCopy");
        BtnCopyReport.ToolTip = L("ToolBtnCopyToClipboard");
        TxtStatus.Text = string.Empty;

        AutomationProperties.SetName(TxtUrl, L("ToolHttpHeadersUrl"));
        AutomationProperties.SetName(BtnCheck, L("ToolHttpHeadersBtnCheck"));
        AutomationProperties.SetName(BtnCopyReport, L("ToolHttpHeadersBtnCopy"));
        AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        AutomationProperties.SetName(LoadingBar, L("ToolHttpHeadersA11yLoading"));
    }

    // ── Gateway routing ──────────────────────────────────────────────

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

    // ── Event handlers ───────────────────────────────────────────────

    private void OnUrlKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = PerformAnalysisAsync();
            e.Handled = true;
        }
    }

    private void OnCheckClick(object sender, RoutedEventArgs e)
    {
        _ = PerformAnalysisAsync();
    }

    // ── Core analysis flow ───────────────────────────────────────────

    private async Task PerformAnalysisAsync()
    {
        var rawUrl = TxtUrl.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        TxtStatus.Text = string.Empty;
        _lastReport = string.Empty;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            TxtError.Text = L("ToolHttpHeadersErrorUrlRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        // Normalize URL: default to HTTPS if no scheme
        if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            rawUrl = "https://" + rawUrl;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            TxtError.Text = L("ToolHttpHeadersErrorInvalidUrl");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(ConnectionTimeout);

        _isAnalyzing = true;
        _setBusy?.Invoke(true);
        BtnCheck.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        TxtStatus.Text = L("ToolHttpHeadersStatusAnalyzing");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Dictionary<string, string> headers;
            string rawResponse;

            if (_selectedGateway is not null)
            {
                (headers, rawResponse) = await Task.Run(
                    () => FetchHeadersViaTunnel(_selectedGateway, uri),
                    _cts.Token).ConfigureAwait(true);
            }
            else
            {
                var host = uri.Host;
                var useTls = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
                var port = uri.IsDefaultPort
                    ? (useTls ? DefaultHttpsPort : DefaultHttpPort)
                    : uri.Port;
                var path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;

                (headers, rawResponse) = await FetchHeadersAsync(
                    host, port, useTls, path, _cts.Token).ConfigureAwait(true);
            }

            stopwatch.Stop();

            if (_cts.IsCancellationRequested) return;

            var securityResults = EvaluateSecurityHeaders(headers);
            var disclosureResults = EvaluateDisclosureHeaders(headers);
            var grade = CalculateGrade(securityResults);

            DisplayResults(securityResults, disclosureResults, grade, rawResponse, uri.Host);
            TxtStatus.Text = string.Format(L("ToolHttpHeadersStatusComplete"), stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            TxtError.Text = L("ToolHttpHeadersErrorTimeout");
            TxtError.Visibility = Visibility.Visible;
        }
        catch (SocketException ex)
        {
            TxtError.Text = string.Format(L("ToolHttpHeadersErrorConnection"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        catch (IOException ex)
        {
            TxtError.Text = string.Format(L("ToolHttpHeadersErrorConnection"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TxtError.Text = string.Format(L("ToolHttpHeadersErrorConnection"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            _isAnalyzing = false;
            _setBusy?.Invoke(false);
            BtnCheck.IsEnabled = true;
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    // ── HTTP fetch via TcpClient (matches project pattern) ───────────

    /// <summary>
    /// Fetches HTTP response headers using raw TCP + optional SslStream.
    /// Sends HEAD first; falls back to GET if 405 is returned.
    /// </summary>
    internal static async Task<(Dictionary<string, string> Headers, string RawResponse)>
        FetchHeadersAsync(string host, int port, bool useTls, string path, CancellationToken ct)
    {
        var (statusCode, headers, raw) = await SendRequestAsync(host, port, useTls, path, "HEAD", ct)
            .ConfigureAwait(false);

        // Fall back to GET if HEAD is not allowed
        if (statusCode == 405)
        {
            (_, headers, raw) = await SendRequestAsync(host, port, useTls, path, "GET", ct)
                .ConfigureAwait(false);
        }

        return (headers, raw);
    }

    private static async Task<(int StatusCode, Dictionary<string, string> Headers, string RawResponse)>
        SendRequestAsync(string host, int port, bool useTls, string path, string method, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
        Stream stream = client.GetStream();

        SslStream? sslStream = null;
        try
        {
            if (useTls)
            {
                sslStream = new SslStream(stream, leaveInnerStreamOpen: true, (_, _, _, _) => true);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host
                }, ct).ConfigureAwait(false);
                stream = sslStream;
            }

            var sanitizedHost = host.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
            var sanitizedPath = path.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
            var request = $"{method} {sanitizedPath} HTTP/1.1\r\nHost: {sanitizedHost}\r\nConnection: close\r\nUser-Agent: Heimdall\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request), ct).ConfigureAwait(false);

            // Read response (headers only, stop after blank line)
            var buffer = new byte[MaxResponseBytes];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);
                if (bytesRead == 0) break;
                totalRead += bytesRead;

                // Check if we have the full header section (ends with \r\n\r\n)
                var currentText = Encoding.ASCII.GetString(buffer, 0, totalRead);
                if (currentText.Contains("\r\n\r\n")) break;
            }

            var rawResponse = Encoding.ASCII.GetString(buffer, 0, totalRead);
            return ParseHttpResponse(rawResponse);
        }
        finally
        {
            if (sslStream is not null) await sslStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static (int StatusCode, Dictionary<string, string> Headers, string RawResponse)
        ParseHttpResponse(string rawResponse)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var statusCode = 0;

        var headerEnd = rawResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = headerEnd >= 0 ? rawResponse[..headerEnd] : rawResponse;
        var lines = headerSection.Split("\r\n");

        if (lines.Length > 0)
        {
            // Parse status line: "HTTP/1.1 200 OK"
            var statusLine = lines[0];
            var parts = statusLine.Split(' ', 3);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var code))
            {
                statusCode = code;
            }
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var name = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            // For Set-Cookie and other multi-value headers, append
            if (headers.TryGetValue(name, out var existing))
            {
                headers[name] = existing + "; " + value;
            }
            else
            {
                headers[name] = value;
            }
        }

        return (statusCode, headers, headerSection);
    }

    // ── Tunnel mode via SSH gateway ──────────────────────────────────

    private static (Dictionary<string, string> Headers, string RawResponse) FetchHeadersViaTunnel(
        SshGatewayDto gateway, Uri uri)
    {
        using var client = ToolGatewayConnector.Connect(gateway);

        var curlCommand = $"curl -sI --max-time 10 {InputValidator.EscapeShellArg(uri.ToString())} 2>/dev/null";
        using var cmd = client.CreateCommand(curlCommand);
        cmd.CommandTimeout = TimeSpan.FromSeconds(15);
        var result = cmd.Execute()?.Trim() ?? string.Empty;

        // Parse curl -I output (same format as HTTP headers)
        var (_, headers, raw) = ParseHttpResponse(result + "\r\n\r\n");
        return (headers, result);
    }

    // ── Security header evaluation ───────────────────────────────────

    private List<HeaderCheckResult> EvaluateSecurityHeaders(Dictionary<string, string> headers)
    {
        var results = new List<HeaderCheckResult>();

        // Strict-Transport-Security
        results.Add(CheckHeader(headers, "Strict-Transport-Security",
            L("ToolHttpHeadersHsts"),
            L("ToolHttpHeadersRecHsts")));

        // Content-Security-Policy
        results.Add(CheckHeader(headers, "Content-Security-Policy",
            L("ToolHttpHeadersCsp"),
            L("ToolHttpHeadersRecCsp")));

        // X-Frame-Options
        results.Add(CheckHeader(headers, "X-Frame-Options",
            L("ToolHttpHeadersXfo"),
            L("ToolHttpHeadersRecXfo")));

        // X-Content-Type-Options
        results.Add(CheckHeaderExpectedValue(headers, "X-Content-Type-Options",
            L("ToolHttpHeadersXcto"),
            "nosniff",
            L("ToolHttpHeadersRecXcto")));

        // Referrer-Policy
        results.Add(CheckHeader(headers, "Referrer-Policy",
            L("ToolHttpHeadersReferrer"),
            L("ToolHttpHeadersRecReferrer")));

        // Permissions-Policy
        results.Add(CheckHeader(headers, "Permissions-Policy",
            L("ToolHttpHeadersPermissions"),
            L("ToolHttpHeadersRecPermissions")));

        // X-XSS-Protection (deprecated, but check)
        results.Add(EvaluateXssProtection(headers));

        // Set-Cookie flags
        results.Add(EvaluateCookieFlags(headers));

        return results;
    }

    private HeaderCheckResult CheckHeader(
        Dictionary<string, string> headers, string headerName, string displayName, string recommendation)
    {
        if (headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return new HeaderCheckResult
            {
                Name = displayName,
                Value = value,
                Recommendation = string.Empty,
                Status = "pass",
                StatusBrush = FindGreenBrush(),
                StatusIcon = "\u2713",
                RecommendationVisibility = Visibility.Collapsed
            };
        }

        return new HeaderCheckResult
        {
            Name = displayName,
            Value = L("ToolHttpHeadersMissing"),
            Recommendation = recommendation,
            Status = "fail",
            StatusBrush = FindRedBrush(),
            StatusIcon = "\u2717",
            RecommendationVisibility = Visibility.Visible
        };
    }

    private HeaderCheckResult CheckHeaderExpectedValue(
        Dictionary<string, string> headers, string headerName, string displayName,
        string expectedValue, string recommendation)
    {
        if (headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            var isCorrect = value.Equals(expectedValue, StringComparison.OrdinalIgnoreCase);
            return new HeaderCheckResult
            {
                Name = displayName,
                Value = value,
                Recommendation = isCorrect ? string.Empty : recommendation,
                Status = isCorrect ? "pass" : "warn",
                StatusBrush = isCorrect ? FindGreenBrush() : FindYellowBrush(),
                StatusIcon = isCorrect ? "\u2713" : "\u26A0",
                RecommendationVisibility = isCorrect ? Visibility.Collapsed : Visibility.Visible
            };
        }

        return new HeaderCheckResult
        {
            Name = displayName,
            Value = L("ToolHttpHeadersMissing"),
            Recommendation = recommendation,
            Status = "fail",
            StatusBrush = FindRedBrush(),
            StatusIcon = "\u2717",
            RecommendationVisibility = Visibility.Visible
        };
    }

    private HeaderCheckResult EvaluateXssProtection(Dictionary<string, string> headers)
    {
        var name = "X-XSS-Protection";
        if (headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            // X-XSS-Protection is deprecated; presence is a minor positive but not recommended
            return new HeaderCheckResult
            {
                Name = name,
                Value = value,
                Recommendation = L("ToolHttpHeadersRecXss"),
                Status = "warn",
                StatusBrush = FindYellowBrush(),
                StatusIcon = "\u26A0",
                RecommendationVisibility = Visibility.Visible
            };
        }

        // Absent is fine for modern browsers (CSP replaces it)
        return new HeaderCheckResult
        {
            Name = name,
            Value = L("ToolHttpHeadersMissing"),
            Recommendation = L("ToolHttpHeadersRecXssAbsent"),
            Status = "pass",
            StatusBrush = FindGreenBrush(),
            StatusIcon = "\u2713",
            RecommendationVisibility = Visibility.Visible
        };
    }

    private HeaderCheckResult EvaluateCookieFlags(Dictionary<string, string> headers)
    {
        var displayName = L("ToolHttpHeadersCookieFlags");

        if (!headers.TryGetValue("Set-Cookie", out var cookieValue) ||
            string.IsNullOrWhiteSpace(cookieValue))
        {
            return new HeaderCheckResult
            {
                Name = displayName,
                Value = L("ToolHttpHeadersNoCookies"),
                Recommendation = string.Empty,
                Status = "pass",
                StatusBrush = FindGreenBrush(),
                StatusIcon = "\u2713",
                RecommendationVisibility = Visibility.Collapsed
            };
        }

        var hasSecure = cookieValue.Contains("Secure", StringComparison.OrdinalIgnoreCase);
        var hasHttpOnly = cookieValue.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase);
        var hasSameSite = cookieValue.Contains("SameSite", StringComparison.OrdinalIgnoreCase);

        var missing = new List<string>();
        if (!hasSecure) missing.Add("Secure");
        if (!hasHttpOnly) missing.Add("HttpOnly");
        if (!hasSameSite) missing.Add("SameSite");

        if (missing.Count == 0)
        {
            return new HeaderCheckResult
            {
                Name = displayName,
                Value = "Secure, HttpOnly, SameSite",
                Recommendation = string.Empty,
                Status = "pass",
                StatusBrush = FindGreenBrush(),
                StatusIcon = "\u2713",
                RecommendationVisibility = Visibility.Collapsed
            };
        }

        var missingText = string.Join(", ", missing);
        return new HeaderCheckResult
        {
            Name = displayName,
            Value = string.Format(L("ToolHttpHeadersCookieMissing"), missingText),
            Recommendation = L("ToolHttpHeadersRecCookies"),
            Status = missing.Count >= 2 ? "fail" : "warn",
            StatusBrush = missing.Count >= 2 ? FindRedBrush() : FindYellowBrush(),
            StatusIcon = missing.Count >= 2 ? "\u2717" : "\u26A0",
            RecommendationVisibility = Visibility.Visible
        };
    }

    // ── Information disclosure evaluation ────────────────────────────

    private List<HeaderCheckResult> EvaluateDisclosureHeaders(Dictionary<string, string> headers)
    {
        var results = new List<HeaderCheckResult>();

        results.Add(CheckDisclosure(headers, "Server", L("ToolHttpHeadersServer")));
        results.Add(CheckDisclosure(headers, "X-Powered-By", "X-Powered-By"));
        results.Add(CheckDisclosure(headers, "X-AspNet-Version", "X-AspNet-Version"));

        return results;
    }

    private HeaderCheckResult CheckDisclosure(
        Dictionary<string, string> headers, string headerName, string displayName)
    {
        if (headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return new HeaderCheckResult
            {
                Name = displayName,
                Value = value,
                Recommendation = L("ToolHttpHeadersDisclosureWarn"),
                Status = "warn",
                StatusBrush = FindYellowBrush(),
                StatusIcon = "\u26A0",
                RecommendationVisibility = Visibility.Visible
            };
        }

        return new HeaderCheckResult
        {
            Name = displayName,
            Value = L("ToolHttpHeadersNotPresent"),
            Recommendation = string.Empty,
            Status = "pass",
            StatusBrush = FindGreenBrush(),
            StatusIcon = "\u2713",
            RecommendationVisibility = Visibility.Collapsed
        };
    }

    // ── Grading ──────────────────────────────────────────────────────

    private static string CalculateGrade(List<HeaderCheckResult> results)
    {
        var passCount = results.Count(r => r.Status == "pass");
        var warnCount = results.Count(r => r.Status == "warn");
        var failCount = results.Count(r => r.Status == "fail");
        var total = results.Count;

        if (total == 0) return "F";

        // Perfect score
        if (failCount == 0 && warnCount == 0) return "A+";
        if (failCount == 0 && warnCount <= 1) return "A";

        var score = (double)passCount / total;

        return score switch
        {
            >= 0.85 => "B+",
            >= 0.75 => "B",
            >= 0.60 => "C",
            >= 0.45 => "D",
            _ => "F"
        };
    }

    private Brush GetGradeBrush(string grade)
    {
        var key = grade switch
        {
            "A+" or "A" or "B+" or "B" => "SuccessBrush",
            "C" => "WarningBrush",
            _ => "ErrorBrush"
        };
        return TryFindResource(key) as Brush
            ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69));
    }

    // ── Display ──────────────────────────────────────────────────────

    private void DisplayResults(
        List<HeaderCheckResult> securityResults,
        List<HeaderCheckResult> disclosureResults,
        string grade,
        string rawResponse,
        string host)
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;

        // Grade badge
        TxtGradeLabel.Text = L("ToolHttpHeadersGrade");
        TxtGrade.Text = grade;
        GradeBanner.Background = GetGradeBrush(grade);

        // Security headers
        TxtSecuritySection.Text = L("ToolHttpHeadersSectionSecurity");
        SecurityHeadersList.ItemsSource = securityResults;

        // Disclosure headers
        TxtDisclosureSection.Text = L("ToolHttpHeadersSectionDisclosure");
        DisclosureHeadersList.ItemsSource = disclosureResults;

        // Raw headers
        RawHeadersExpander.Header = L("ToolHttpHeadersSectionRaw");
        AutomationProperties.SetName(RawHeadersExpander, L("ToolHttpHeadersSectionRaw"));
        TxtRawHeaders.Text = rawResponse;
        AutomationProperties.SetName(TxtRawHeaders, L("ToolHttpHeadersSectionRaw"));

        // Build text report for copy
        _lastReport = BuildTextReport(host, grade, securityResults, disclosureResults, rawResponse);
    }

    private string BuildTextReport(
        string host,
        string grade,
        List<HeaderCheckResult> securityResults,
        List<HeaderCheckResult> disclosureResults,
        string rawResponse)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(L("ToolHttpHeaderReportTitle"), host));
        sb.AppendLine(string.Format(L("ToolHttpHeaderReportGrade"), grade));
        sb.AppendLine();
        sb.AppendLine(L("ToolHttpHeaderReportSecHeaders"));
        foreach (var r in securityResults)
        {
            sb.AppendLine($"  {r.StatusIcon} {r.Name}: {r.Value}");
            if (r.RecommendationVisibility == Visibility.Visible && !string.IsNullOrEmpty(r.Recommendation))
            {
                sb.AppendLine($"    -> {r.Recommendation}");
            }
        }
        sb.AppendLine();
        sb.AppendLine(L("ToolHttpHeaderReportInfoDisc"));
        foreach (var r in disclosureResults)
        {
            sb.AppendLine($"  {r.StatusIcon} {r.Name}: {r.Value}");
            if (r.RecommendationVisibility == Visibility.Visible && !string.IsNullOrEmpty(r.Recommendation))
            {
                sb.AppendLine($"    -> {r.Recommendation}");
            }
        }
        sb.AppendLine();
        sb.AppendLine(L("ToolHttpHeaderReportRawHeaders"));
        sb.AppendLine(rawResponse);

        return sb.ToString();
    }

    // ── Actions ──────────────────────────────────────────────────────

    private void OnCopyReportClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastReport))
        {
            try
            {
                Clipboard.SetText(_lastReport);
                CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"HttpHeaderAnalyzer clipboard copy failed: {ex.Message}");
            }
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpHTTPHEADERS");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private Brush FindGreenBrush()
        => TryFindResource("SuccessTextBrush") as Brush ?? Brushes.Green;

    private Brush FindYellowBrush()
        => TryFindResource("WarningTextBrush") as Brush ?? Brushes.Orange;

    private Brush FindRedBrush()
        => TryFindResource("ErrorTextBrush") as Brush ?? Brushes.Red;

    private string L(string key) => _localizer?[key] ?? key;

    // ── Lifecycle ────────────────────────────────────────────────────

    public bool CanClose() => !_isAnalyzing;

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

// ── Data model for template binding ──────────────────────────────────

/// <summary>
/// Represents the evaluation result for a single HTTP header check.
/// </summary>
public sealed class HeaderCheckResult
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public Brush StatusBrush { get; init; } = Brushes.Transparent;
    public string StatusIcon { get; init; } = string.Empty;
    public Visibility RecommendationVisibility { get; init; } = Visibility.Visible;
}
