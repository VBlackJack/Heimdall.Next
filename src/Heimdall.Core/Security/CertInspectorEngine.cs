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
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Heimdall.Core.Security;

/// <summary>
/// Certificate expiration status for color-coded display.
/// </summary>
public enum CertExpirationStatus
{
    Valid,
    Warning,
    Expired
}

/// <summary>
/// A single element in the certificate chain.
/// </summary>
public sealed class CertChainElement
{
    public required string Subject { get; init; }
    public required string Expiry { get; init; }
}

/// <summary>
/// Result of a certificate probe (direct or tunneled).
/// Returned by <c>ICertProber</c> implementations.
/// </summary>
public sealed class CertProbeResult : IDisposable
{
    public required X509Certificate2 Certificate { get; init; }
    public required SslProtocols Protocol { get; init; }
    public required IReadOnlyList<CertChainElement> Chain { get; init; }

    public void Dispose() => Certificate.Dispose();
}

/// <summary>
/// Fully analyzed certificate summary, decoupled from WPF.
/// Contains all data needed for display and reporting.
/// </summary>
public sealed class CertInspectionSummary
{
    public required string Subject { get; init; }
    public required string Issuer { get; init; }
    public required DateTime NotBefore { get; init; }
    public required DateTime NotAfter { get; init; }
    public required int DaysRemaining { get; init; }
    public required string Serial { get; init; }
    public required string Thumbprint { get; init; }
    public required string SigAlgorithm { get; init; }
    public required int KeySizeBits { get; init; }
    public required IReadOnlyList<string> Sans { get; init; }
    public required string TlsVersion { get; init; }
    public required bool HostnameMatches { get; init; }
    public required CertExpirationStatus ExpirationStatus { get; init; }
    public required IReadOnlyList<CertChainElement> Chain { get; init; }
    public required int Port { get; init; }
}

/// <summary>
/// Pure analysis engine for X.509 certificate inspection.
/// Hostname matching, SAN extraction, expiration evaluation,
/// chain building, report/CSV generation, and direct TLS probing.
/// Thread-safe, stateless.
/// </summary>
public static class CertInspectorEngine
{
    public const int DefaultWarningThresholdDays = 30;
    public const int MaxConcurrentProbes = 10;
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PerPortTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Checks whether the certificate matches the given hostname by examining
    /// SANs first, then falling back to the CN.
    /// </summary>
    public static bool CheckHostnameMatch(X509Certificate2 cert, IReadOnlyList<string> sans, string host)
    {
        foreach (var san in sans)
        {
            if (MatchesHostname(san, host))
                return true;
        }

        var commonName = ExtractCn(cert.Subject);
        return !string.IsNullOrEmpty(commonName) && MatchesHostname(commonName, host);
    }

    /// <summary>
    /// Tests whether a hostname matches a pattern, supporting wildcard certificates.
    /// </summary>
    internal static bool MatchesHostname(string pattern, string host)
    {
        if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase))
            return true;

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

    /// <summary>
    /// Extracts the Common Name (CN) from an X.500 subject string.
    /// </summary>
    internal static string ExtractCn(string subject)
    {
        const string commonNamePrefix = "CN=";
        var startIndex = subject.IndexOf(commonNamePrefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
            return string.Empty;

        startIndex += commonNamePrefix.Length;
        var endIndex = subject.IndexOf(',', startIndex);
        return endIndex < 0 ? subject[startIndex..].Trim() : subject[startIndex..endIndex].Trim();
    }

    /// <summary>
    /// Extracts Subject Alternative Names (DNS names + IP addresses) from a certificate.
    /// </summary>
    public static List<string> ExtractSans(X509Certificate2 cert)
    {
        var sans = new List<string>();
        foreach (var extension in cert.Extensions)
        {
            if (extension.Oid?.Value != "2.5.29.17")
                continue;

            var sanExtension = (X509SubjectAlternativeNameExtension)extension;
            foreach (var name in sanExtension.EnumerateDnsNames())
                sans.Add(name);

            foreach (var address in sanExtension.EnumerateIPAddresses())
                sans.Add(address.ToString());
        }

        return sans;
    }

    /// <summary>
    /// Determines the expiration status based on days remaining.
    /// </summary>
    public static CertExpirationStatus DetermineExpirationStatus(
        int daysRemaining,
        int warningThresholdDays = DefaultWarningThresholdDays)
    {
        if (daysRemaining < 0)
            return CertExpirationStatus.Expired;

        if (daysRemaining <= warningThresholdDays)
            return CertExpirationStatus.Warning;

        return CertExpirationStatus.Valid;
    }

    /// <summary>
    /// Parses a comma-separated list of ports and ranges (e.g. "80,443,8000-8010").
    /// Returns an ordered deduplicated list of valid ports.
    /// </summary>
    public static List<int> ParsePorts(string input)
    {
        var ports = new HashSet<int>();
        foreach (var segment in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Contains('-', StringComparison.Ordinal))
            {
                var rangeParts = segment.Split('-', 2);
                if (int.TryParse(rangeParts[0].Trim(), out var start)
                    && int.TryParse(rangeParts[1].Trim(), out var end)
                    && start >= 1
                    && end <= 65535
                    && start <= end)
                {
                    for (var port = start; port <= end; port++)
                        ports.Add(port);
                }
            }
            else if (int.TryParse(segment, out var port) && port is >= 1 and <= 65535)
            {
                ports.Add(port);
            }
        }

        return ports.OrderBy(port => port).ToList();
    }

    /// <summary>
    /// Sanitizes a string for use as a file name, replacing invalid characters with underscores.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
            builder.Append(invalidChars.Contains(character) ? '_' : character);

        return builder.ToString();
    }

    /// <summary>
    /// Builds the certificate chain summary from an end-entity certificate.
    /// </summary>
    public static IReadOnlyList<CertChainElement> BuildChain(X509Certificate2 cert)
    {
        var chainElements = new List<CertChainElement>();

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(cert);

        foreach (var element in chain.ChainElements)
        {
            chainElements.Add(new CertChainElement
            {
                Subject = element.Certificate.Subject,
                Expiry = element.Certificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss UTC")
            });
        }

        return chainElements;
    }

    /// <summary>
    /// Analyzes a certificate probe result and produces a fully populated summary.
    /// </summary>
    public static CertInspectionSummary BuildSummary(
        CertProbeResult probeResult,
        string host,
        int port)
    {
        var cert = probeResult.Certificate;
        var sans = ExtractSans(cert);
        var keySize = TlsAuditEngine.GetPublicKeySize(cert);
        var thumbprintBytes = cert.GetCertHash(HashAlgorithmName.SHA256);
        var daysRemaining = (cert.NotAfter - DateTime.UtcNow).Days;
        var hostnameMatches = CheckHostnameMatch(cert, sans, host);
        var expirationStatus = DetermineExpirationStatus(daysRemaining);

        return new CertInspectionSummary
        {
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            NotBefore = cert.NotBefore,
            NotAfter = cert.NotAfter,
            DaysRemaining = daysRemaining,
            Serial = cert.SerialNumber,
            Thumbprint = Convert.ToHexString(thumbprintBytes),
            SigAlgorithm = cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "-",
            KeySizeBits = keySize,
            Sans = sans.Count > 0 ? sans : ["-"],
            TlsVersion = TlsAuditEngine.FormatTlsProtocol(probeResult.Protocol),
            HostnameMatches = hostnameMatches,
            ExpirationStatus = expirationStatus,
            Chain = probeResult.Chain,
            Port = port
        };
    }

    /// <summary>
    /// Builds a plain-text report for a single certificate (clipboard copy).
    /// </summary>
    public static string BuildDetailsText(
        CertInspectionSummary summary,
        string host,
        Func<string, string>? localize = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{L(localize, "ToolCertDetailHost")}: {host}:{summary.Port}");
        builder.AppendLine($"{L(localize, "ToolCertTlsVersion")}: {summary.TlsVersion}");
        builder.AppendLine(summary.HostnameMatches
            ? L(localize, "ToolCertHostnameMatch")
            : L(localize, "ToolCertHostnameMismatch"));
        builder.AppendLine($"{L(localize, "ToolCertSubject")}: {summary.Subject}");
        builder.AppendLine($"{L(localize, "ToolCertIssuer")}: {summary.Issuer}");
        builder.AppendLine($"{L(localize, "ToolCertValidFrom")}: {summary.NotBefore:yyyy-MM-dd HH:mm:ss UTC}");
        builder.AppendLine(
            $"{L(localize, "ToolCertValidTo")}: {summary.NotAfter:yyyy-MM-dd HH:mm:ss UTC} ({summary.DaysRemaining} {L(localize, "ToolCertDaysRemaining")})");
        builder.AppendLine($"{L(localize, "ToolCertSerial")}: {summary.Serial}");
        builder.AppendLine($"{L(localize, "ToolCertSha256Label")}: {summary.Thumbprint}");
        builder.AppendLine($"{L(localize, "ToolCertSigAlg")}: {summary.SigAlgorithm}");
        builder.AppendLine(
            $"{L(localize, "ToolCertKeySize")}: {(summary.KeySizeBits > 0 ? $"{summary.KeySizeBits} bits" : "-")}");

        var realSans = summary.Sans.Where(san => san != "-").ToList();
        if (realSans.Count > 0)
            builder.AppendLine($"{L(localize, "ToolCertSans")}: {string.Join(", ", realSans)}");

        return builder.ToString();
    }

    /// <summary>
    /// Builds CSV content for a collection of scan results.
    /// Uses <see cref="InputValidator.SanitizeCsvCell"/> for injection prevention.
    /// </summary>
    public static string BuildScanCsv(
        IReadOnlyList<CertInspectionSummary> results,
        Func<string, string>? localize = null,
        Func<int, string>? getServiceLabel = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",",
            L(localize, "ToolCertCsvPort"),
            L(localize, "ToolCertCsvService"),
            L(localize, "ToolCertCsvSubject"),
            L(localize, "ToolCertCsvIssuer"),
            L(localize, "ToolCertCsvValidFrom"),
            L(localize, "ToolCertCsvValidTo"),
            L(localize, "ToolCertCsvDaysRemaining"),
            L(localize, "ToolCertCsvTlsVersion"),
            L(localize, "ToolCertCsvKeySize"),
            L(localize, "ToolCertCsvHostnameMatch"),
            L(localize, "ToolCertCsvSans")));

        foreach (var result in results)
        {
            var service = InputValidator.SanitizeCsvCell(getServiceLabel?.Invoke(result.Port) ?? "TLS");
            var realSans = result.Sans.Where(san => san != "-").ToList();
            var sans = InputValidator.SanitizeCsvCell(string.Join("; ", realSans)).Replace("\"", "\"\"", StringComparison.Ordinal);
            var subject = InputValidator.SanitizeCsvCell(result.Subject).Replace("\"", "\"\"", StringComparison.Ordinal);
            var issuer = InputValidator.SanitizeCsvCell(result.Issuer).Replace("\"", "\"\"", StringComparison.Ordinal);
            var validTo = $"{result.NotAfter:yyyy-MM-dd HH:mm:ss UTC} ({result.DaysRemaining}d)";
            validTo = InputValidator.SanitizeCsvCell(validTo).Replace("\"", "\"\"", StringComparison.Ordinal);
            var keySizeText = result.KeySizeBits > 0 ? $"{result.KeySizeBits} bits" : "-";

            builder.AppendLine(string.Join(",",
                result.Port,
                $"\"{service}\"",
                $"\"{subject}\"",
                $"\"{issuer}\"",
                result.NotBefore.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                $"\"{validTo}\"",
                result.DaysRemaining,
                InputValidator.SanitizeCsvCell(result.TlsVersion),
                InputValidator.SanitizeCsvCell(keySizeText),
                result.HostnameMatches,
                $"\"{sans}\""));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Retrieves a certificate via direct SslStream connection, including
    /// the negotiated TLS protocol version and the full certificate chain.
    /// Caller must dispose the returned <see cref="CertProbeResult"/>.
    /// </summary>
    public static async Task<CertProbeResult> RetrieveCertificateAsync(
        string host,
        int port,
        CancellationToken ct)
    {
        X509Certificate? remoteCert = null;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);

        using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) =>
        {
            remoteCert = cert;
            return true;
        });

        await ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = host },
            ct).ConfigureAwait(false);

        if (remoteCert is null)
            throw new InvalidOperationException(L(null, "ErrorNoCertReceived"));

        var cert2 = new X509Certificate2(remoteCert);
        using (remoteCert)
        {
            return new CertProbeResult
            {
                Certificate = cert2,
                Protocol = ssl.SslProtocol,
                Chain = BuildChain(cert2)
            };
        }
    }

    private static string L(Func<string, string>? localize, string key)
        => localize?.Invoke(key) ?? key;
}
