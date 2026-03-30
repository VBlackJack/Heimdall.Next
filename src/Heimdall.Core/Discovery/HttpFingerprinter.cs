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

using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Discovery;

/// <summary>
/// Identifies web frameworks and products from HTTP response characteristics:
/// cookies, error page signatures, and product-specific URL probes.
/// </summary>
public static class HttpFingerprinter
{
    // ── Cookie → Framework mapping ──────────────────────────────────
    private static readonly (string Cookie, string Framework)[] CookieFingerprints =
    [
        ("PHPSESSID", "PHP"),
        ("ASP.NET_SessionId", "ASP.NET"),
        ("__RequestVerificationToken", "ASP.NET"),
        ("JSESSIONID", "Java (Servlet)"),
        ("connect.sid", "Node.js (Express)"),
        ("laravel_session", "PHP (Laravel)"),
        ("XSRF-TOKEN", "PHP (Laravel)"),
        ("csrftoken", "Python (Django)"),
        ("sessionid", "Python (Django)"),
        ("sysauth", "OpenWrt (LuCI)"),
        ("sysauth_https", "OpenWrt (LuCI)"),
        ("SESSION", "Java (Spring)"),
    ];

    // ── Error page → Framework regex ────────────────────────────────
    private static readonly (Regex Pattern, string Framework)[] ErrorPagePatterns =
    [
        (new(@"(?i)Server Error in '/' Application\.|<title>Runtime Error</title>", RegexOptions.Compiled), "ASP.NET"),
        (new(@"(?i)__VIEWSTATE|WebResource\.axd", RegexOptions.Compiled), "ASP.NET WebForms"),
        (new(@"(?i)Apache Tomcat(?:/\d+(?:\.\d+)*)? - Error report", RegexOptions.Compiled), "Java (Tomcat)"),
        (new(@"(?i)Whitelabel Error Page|\{""timestamp"".*""status"":\d+,""error"":", RegexOptions.Compiled), "Java (Spring Boot)"),
        (new(@"(?i)Powered by Jetty|<title>Error \d+ .*Jetty", RegexOptions.Compiled), "Java (Jetty)"),
        (new(@"(?i)<center>nginx(?:/[0-9.]+)?</center>|openresty", RegexOptions.Compiled), "nginx"),
        (new(@"(?i)lighttpd", RegexOptions.Compiled), "lighttpd"),
    ];

    // ── Product-confirming URL paths ────────────────────────────────
    private static readonly (string Path, string ProductName, Regex? ConfirmPattern)[] ProductProbes =
    [
        ("/ISAPI/System/deviceInfo", "Hikvision", null),
        ("/RPC2_Login", "Dahua", null),
        ("/cgi-bin/magicBox.cgi?action=getSystemInfo", "Dahua", null),
        ("/webman/index.cgi", "Synology DSM", null),
        ("/cgi-bin/authLogin.cgi", "QNAP QTS", null),
        ("/cgi-bin/luci", "OpenWrt", new(@"(?i)LuCI", RegexOptions.Compiled)),
        ("/webfig/", "MikroTik RouterOS", new(@"(?i)RouterOS|mikrotik", RegexOptions.Compiled)),
        ("/remote/login", "FortiGate SSL-VPN", null),
        ("/api/v2/monitor/system/status", "FortiGate", null),
        ("/php/login.php", "Palo Alto PAN-OS", null),
        ("+CSCOE+/logon.html", "Cisco ASA/AnyConnect", null),
        ("/ui/", "VMware ESXi", new(@"(?i)VMware|Host Client", RegexOptions.Compiled)),
        ("/sdk", "VMware vCenter/ESXi", new(@"(?i)vmware|sdk", RegexOptions.Compiled)),
    ];

    /// <summary>
    /// Detects the web framework from Set-Cookie headers in an HTTP response.
    /// </summary>
    public static string? DetectFrameworkFromCookies(string? httpResponse)
    {
        if (string.IsNullOrEmpty(httpResponse)) return null;

        foreach (var (cookie, framework) in CookieFingerprints)
        {
            if (httpResponse.Contains(cookie, StringComparison.OrdinalIgnoreCase))
                return framework;
        }
        return null;
    }

    /// <summary>
    /// Detects the web framework from error page content (404/500 responses).
    /// </summary>
    public static string? DetectFrameworkFromErrorPage(string? errorPageContent)
    {
        if (string.IsNullOrEmpty(errorPageContent)) return null;

        foreach (var (pattern, framework) in ErrorPagePatterns)
        {
            if (pattern.IsMatch(errorPageContent))
                return framework;
        }
        return null;
    }

    /// <summary>
    /// Probes product-specific URLs to identify the exact device/application.
    /// Returns the first confirmed product match, or null.
    /// </summary>
    public static async Task<(string? ProductName, string? MatchedUrl)> ProbeProductUrlsAsync(
        string host, int port, bool useTls, int timeoutMs, CancellationToken ct)
    {
        foreach (var (path, product, confirmPattern) in ProductProbes)
        {
            try
            {
                var response = await HttpGetAsync(host, port, useTls, path, timeoutMs, ct)
                    .ConfigureAwait(false);
                if (response is null) continue;

                // Check for 200 OK (not 404/redirect)
                if (!response.StartsWith("HTTP/", StringComparison.Ordinal)) continue;
                var statusLine = response[..Math.Min(response.Length, 32)];
                if (!statusLine.Contains(" 200 ") && !statusLine.Contains(" 401 "))
                    continue;

                // If we have a confirmation pattern, check it
                if (confirmPattern is not null && !confirmPattern.IsMatch(response))
                    continue;

                return (product, path);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { continue; }
            catch (OperationCanceledException) { throw; }
            catch { /* probe failed, try next */ }
        }
        return (null, null);
    }

    private static async Task<string?> HttpGetAsync(
        string host, int port, bool useTls, string path,
        int timeoutMs, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

        await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
        Stream stream = client.GetStream();
        SslStream? ssl = null;

        try
        {
            if (useTls)
            {
                ssl = new SslStream(stream, leaveInnerStreamOpen: true, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host
                }, linked.Token).ConfigureAwait(false);
                stream = ssl;
            }

            var request = $"GET {path} HTTP/1.0\r\nHost: {host}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request), linked.Token)
                .ConfigureAwait(false);

            var buf = new byte[4096];
            var read = await stream.ReadAsync(buf, linked.Token).ConfigureAwait(false);
            return read > 0 ? Encoding.ASCII.GetString(buf, 0, read) : null;
        }
        finally
        {
            if (ssl is not null) await ssl.DisposeAsync().ConfigureAwait(false);
        }
    }
}
