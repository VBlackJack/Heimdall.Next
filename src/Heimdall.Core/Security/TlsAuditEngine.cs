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
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Heimdall.Core.Security;

/// <summary>
/// Overall TLS configuration grade (A+ is best, F is worst, T is unreachable).
/// </summary>
public enum TlsGrade
{
    APlus,
    A,
    B,
    C,
    D,
    F,
    T
}

/// <summary>
/// Rating category for a TLS protocol version.
/// </summary>
public enum TlsProtocolRating
{
    Strong,
    Weak,
    Critical
}

/// <summary>
/// Strength classification for a cipher suite.
/// </summary>
public enum CipherStrength
{
    Strong,
    Acceptable,
    Weak
}

/// <summary>
/// Severity level for a TLS audit finding.
/// </summary>
public enum TlsFindingSeverity
{
    Pass,
    Info,
    Warning,
    Critical
}

/// <summary>
/// Definition of a TLS protocol version to probe.
/// </summary>
public sealed class TlsProtocolDefinition
{
    public required string Name { get; init; }
    public required SslProtocols Protocol { get; init; }
    public required TlsProtocolRating Rating { get; init; }
}

/// <summary>
/// Result of a single protocol version test.
/// </summary>
public sealed class TlsProtocolTestResult
{
    public required string Name { get; init; }
    public required bool Supported { get; init; }
    public required TlsProtocolRating Rating { get; init; }
}

/// <summary>
/// Result of a cipher suite probe with classification details.
/// </summary>
public sealed class TlsCipherInfo
{
    public required string SuiteName { get; init; }
    public required CipherStrength Strength { get; init; }
    public required string KeyExchange { get; init; }
    public required string Authentication { get; init; }
    public required string Encryption { get; init; }
}

/// <summary>
/// A single finding from the TLS audit.
/// </summary>
public sealed class TlsAuditFinding
{
    public required TlsFindingSeverity Severity { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Summary of a server certificate's key properties.
/// </summary>
public sealed class TlsCertificateSummary
{
    public required string Subject { get; init; }
    public required string Issuer { get; init; }
    public required DateTime NotAfter { get; init; }
    public required int KeySizeBits { get; init; }
}

/// <summary>
/// Pure analysis engine for TLS/SSL server audits.
/// Contains protocol definitions, cipher classification, grading,
/// findings generation, report building, and direct SslStream probing.
/// Thread-safe, stateless.
/// </summary>
public static class TlsAuditEngine
{
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    public const int DefaultTlsPort = 443;

#pragma warning disable CA5397, CS0618, SYSLIB0039
    /// <summary>Protocol versions to test, from weakest to strongest.</summary>
    public static IReadOnlyList<TlsProtocolDefinition> ProtocolDefinitions { get; } =
    [
        new() { Name = "SSL 3.0", Protocol = SslProtocols.Ssl3, Rating = TlsProtocolRating.Critical },
        new() { Name = "TLS 1.0", Protocol = SslProtocols.Tls, Rating = TlsProtocolRating.Weak },
        new() { Name = "TLS 1.1", Protocol = SslProtocols.Tls11, Rating = TlsProtocolRating.Weak },
        new() { Name = "TLS 1.2", Protocol = SslProtocols.Tls12, Rating = TlsProtocolRating.Strong },
        new() { Name = "TLS 1.3", Protocol = SslProtocols.Tls13, Rating = TlsProtocolRating.Strong }
    ];
#pragma warning restore CA5397, CS0618, SYSLIB0039

    /// <summary>Known TLS 1.2 cipher suites to probe.</summary>
    public static IReadOnlyList<TlsCipherSuite> KnownTls12CipherSuites { get; } =
    [
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA256,
        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA,
        TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5
    ];

    /// <summary>Maps .NET TlsCipherSuite enum to OpenSSL cipher names (for tunnel mode).</summary>
    public static IReadOnlyDictionary<TlsCipherSuite, string> OpenSslCipherNames { get; } =
        new Dictionary<TlsCipherSuite, string>
        {
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384] = "ECDHE-RSA-AES256-GCM-SHA384",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256] = "ECDHE-RSA-AES128-GCM-SHA256",
            [TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384] = "ECDHE-ECDSA-AES256-GCM-SHA384",
            [TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256] = "ECDHE-ECDSA-AES128-GCM-SHA256",
            [TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384] = "DHE-RSA-AES256-GCM-SHA384",
            [TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256] = "DHE-RSA-AES128-GCM-SHA256",
            [TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384] = "AES256-GCM-SHA384",
            [TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256] = "AES128-GCM-SHA256",
            [TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256] = "AES256-SHA256",
            [TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA256] = "AES128-SHA256",
            [TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA] = "AES256-SHA",
            [TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA] = "AES128-SHA",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384] = "ECDHE-RSA-AES256-SHA384",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256] = "ECDHE-RSA-AES128-SHA256",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA] = "ECDHE-RSA-AES256-SHA",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA] = "ECDHE-RSA-AES128-SHA",
            [TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA] = "DES-CBC3-SHA",
            [TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA] = "RC4-SHA",
            [TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5] = "RC4-MD5"
        };

    /// <summary>
    /// Classifies a cipher suite into strength, key exchange, authentication,
    /// and encryption components.
    /// </summary>
    public static (CipherStrength Strength, string KeyExchange, string Authentication, string Encryption) ClassifyCipher(
        TlsCipherSuite suite)
    {
        var name = suite.ToString();
        var kx = "RSA";
        var auth = "RSA";
        var enc = "AES";
        var strength = CipherStrength.Acceptable;

        if (name.Contains("ECDHE", StringComparison.Ordinal))
            kx = "ECDHE";
        else if (name.Contains("DHE", StringComparison.Ordinal))
            kx = "DHE";

        if (name.Contains("ECDSA", StringComparison.Ordinal))
            auth = "ECDSA";

        if (name.Contains("AES_256_GCM", StringComparison.Ordinal))
            enc = "AES-256-GCM";
        else if (name.Contains("AES_128_GCM", StringComparison.Ordinal))
            enc = "AES-128-GCM";
        else if (name.Contains("AES_256_CBC", StringComparison.Ordinal))
            enc = "AES-256-CBC";
        else if (name.Contains("AES_128_CBC", StringComparison.Ordinal))
            enc = "AES-128-CBC";
        else if (name.Contains("3DES", StringComparison.Ordinal))
            enc = "3DES-CBC";
        else if (name.Contains("RC4", StringComparison.Ordinal))
            enc = "RC4";

        if (enc.Contains("RC4", StringComparison.Ordinal) || enc.Contains("3DES", StringComparison.Ordinal))
            strength = CipherStrength.Weak;
        else if ((kx == "ECDHE" || kx == "DHE") && enc.Contains("GCM", StringComparison.Ordinal))
            strength = CipherStrength.Strong;

        return (strength, kx, auth, enc);
    }

    /// <summary>
    /// Calculates the overall TLS grade based on protocol support and cipher analysis.
    /// </summary>
    public static TlsGrade CalculateGrade(
        IReadOnlyList<TlsProtocolTestResult> protocols,
        IReadOnlyList<TlsCipherInfo> ciphers)
    {
        var ssl3 = protocols.Any(p => p is { Name: "SSL 3.0", Supported: true });
        var tls10 = protocols.Any(p => p is { Name: "TLS 1.0", Supported: true });
        var tls11 = protocols.Any(p => p is { Name: "TLS 1.1", Supported: true });
        var tls12 = protocols.Any(p => p is { Name: "TLS 1.2", Supported: true });
        var tls13 = protocols.Any(p => p is { Name: "TLS 1.3", Supported: true });

        var hasWeakCiphers = ciphers.Any(c =>
            c.SuiteName.Contains("3DES", StringComparison.Ordinal)
            || c.SuiteName.Contains("RC4", StringComparison.Ordinal));

        var hasCbcCiphers = ciphers.Any(c => c.SuiteName.Contains("CBC", StringComparison.Ordinal));

        var hasNonPfs = ciphers.Any(c =>
            !c.SuiteName.Contains("ECDHE", StringComparison.Ordinal)
            && !c.SuiteName.Contains("DHE", StringComparison.Ordinal));

        if (ssl3) return TlsGrade.F;
        if (hasWeakCiphers) return TlsGrade.D;
        if (tls10 || tls11) return TlsGrade.C;
        if (hasCbcCiphers) return TlsGrade.B;
        if (tls12 && tls13 && !hasNonPfs) return TlsGrade.APlus;
        if (tls12 || tls13) return TlsGrade.A;
        return TlsGrade.T;
    }

    /// <summary>
    /// Builds the list of audit findings based on protocol and cipher results.
    /// </summary>
    public static List<TlsAuditFinding> BuildFindings(
        IReadOnlyList<TlsProtocolTestResult> protocols,
        IReadOnlyList<TlsCipherInfo> ciphers,
        Func<string, string>? localize = null)
    {
        var findings = new List<TlsAuditFinding>();

        if (protocols.Any(p => p is { Name: "SSL 3.0", Supported: true }))
        {
            findings.Add(new TlsAuditFinding
            {
                Severity = TlsFindingSeverity.Critical,
                Message = L(localize, "ToolTlsAuditFindingSsl3")
            });
        }

        if (protocols.Any(p => p is { Name: "TLS 1.0", Supported: true }))
        {
            findings.Add(new TlsAuditFinding
            {
                Severity = TlsFindingSeverity.Warning,
                Message = L(localize, "ToolTlsAuditFindingTls10")
            });
        }

        if (protocols.Any(p => p is { Name: "TLS 1.1", Supported: true }))
        {
            findings.Add(new TlsAuditFinding
            {
                Severity = TlsFindingSeverity.Warning,
                Message = L(localize, "ToolTlsAuditFindingTls11")
            });
        }

        if (protocols.All(p => p.Name != "TLS 1.3" || !p.Supported))
        {
            findings.Add(new TlsAuditFinding
            {
                Severity = TlsFindingSeverity.Info,
                Message = L(localize, "ToolTlsAuditFindingNoTls13")
            });
        }

        var weakCiphers = ciphers.Where(c =>
            c.SuiteName.Contains("3DES", StringComparison.Ordinal)
            || c.SuiteName.Contains("RC4", StringComparison.Ordinal));

        foreach (var weakCipher in weakCiphers)
        {
            findings.Add(new TlsAuditFinding
            {
                Severity = TlsFindingSeverity.Critical,
                Message = string.Format(L(localize, "ToolTlsAuditFindingWeakCipher"), weakCipher.SuiteName)
            });
        }

        var nonPfsCiphers = ciphers.Where(c =>
            !c.SuiteName.Contains("ECDHE", StringComparison.Ordinal)
            && !c.SuiteName.Contains("DHE", StringComparison.Ordinal));

        if (nonPfsCiphers.Any())
        {
            findings.Add(new TlsAuditFinding
            {
                Severity = TlsFindingSeverity.Info,
                Message = L(localize, "ToolTlsAuditFindingNoPfs")
            });
        }

        if (findings.Count == 0)
        {
            findings.Add(new TlsAuditFinding
            {
                Severity = TlsFindingSeverity.Pass,
                Message = L(localize, "ToolTlsAuditFindingAllGood")
            });
        }

        return findings;
    }

    /// <summary>
    /// Builds a plain-text report suitable for clipboard copy.
    /// </summary>
    public static string BuildReportText(
        TlsGrade grade,
        IReadOnlyList<TlsProtocolTestResult> protocols,
        IReadOnlyList<TlsCipherInfo> ciphers,
        TlsCertificateSummary? cert,
        IReadOnlyList<TlsAuditFinding> findings,
        string host,
        int port,
        Func<string, string>? localize = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(L(localize, "ToolTlsAuditReportTitle"), host, port));
        sb.AppendLine(string.Format(L(localize, "ToolTlsAuditReportGrade"), GradeToString(grade)));
        sb.AppendLine();

        sb.AppendLine(L(localize, "ToolTlsAuditReportProtocols"));
        foreach (var protocol in protocols)
        {
            var statusLabel = protocol.Supported
                ? L(localize, "ToolTlsAuditSupported")
                : L(localize, "ToolTlsAuditNotSupported");
            sb.AppendLine($"  {protocol.Name}: {statusLabel}");
        }
        sb.AppendLine();

        if (ciphers.Count > 0)
        {
            sb.AppendLine(L(localize, "ToolTlsAuditReportCiphers"));
            foreach (var cipher in ciphers)
            {
                sb.AppendLine($"  {cipher.SuiteName}  [{cipher.KeyExchange}/{cipher.Authentication}/{cipher.Encryption}]");
            }
            sb.AppendLine();
        }

        if (cert is not null)
        {
            sb.AppendLine(L(localize, "ToolTlsAuditReportCert"));
            sb.AppendLine($"  {L(localize, "ToolTlsAuditCertSubject")}: {cert.Subject}");
            sb.AppendLine($"  {L(localize, "ToolTlsAuditCertIssuer")}: {cert.Issuer}");
            sb.AppendLine($"  {L(localize, "ToolTlsAuditCertExpires")}: {cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC}");
            if (cert.KeySizeBits > 0)
            {
                sb.AppendLine(
                    $"  {L(localize, "ToolTlsAuditCertKeySize")}: {string.Format(L(localize, "ToolTlsAuditBits"), cert.KeySizeBits)}");
            }
            sb.AppendLine();
        }

        if (findings.Count > 0)
        {
            sb.AppendLine(L(localize, "ToolTlsAuditReportFindings"));
            foreach (var finding in findings)
            {
                var icon = finding.Severity switch
                {
                    TlsFindingSeverity.Pass => "\u2714",
                    TlsFindingSeverity.Info => "\u2139",
                    TlsFindingSeverity.Warning => "\u26A0",
                    TlsFindingSeverity.Critical => "\u26A0",
                    _ => string.Empty
                };

                sb.AppendLine($"  {icon} {finding.Message}");
            }
        }

        return sb.ToString();
    }

    /// <summary>Converts a TlsGrade enum to its display string.</summary>
    public static string GradeToString(TlsGrade grade) => grade switch
    {
        TlsGrade.APlus => "A+",
        TlsGrade.A => "A",
        TlsGrade.B => "B",
        TlsGrade.C => "C",
        TlsGrade.D => "D",
        TlsGrade.F => "F",
        TlsGrade.T => "T",
        _ => "?"
    };

    /// <summary>
    /// Formats an <see cref="SslProtocols"/> value into a human-readable string.
    /// </summary>
    public static string FormatTlsProtocol(SslProtocols protocol) => protocol switch
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

    /// <summary>
    /// Extracts the public key size (in bits) from a certificate.
    /// Returns 0 if the key algorithm is unsupported.
    /// </summary>
    public static int GetPublicKeySize(X509Certificate2 cert)
    {
        try
        {
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is not null)
                return rsa.KeySize;

            using var ecdsa = cert.GetECDsaPublicKey();
            if (ecdsa is not null)
                return ecdsa.KeySize;
        }
        catch
        {
            // Unsupported key algorithm.
        }

        return 0;
    }

    /// <summary>
    /// Builds a cipher info record from a supported cipher suite.
    /// </summary>
    public static TlsCipherInfo BuildCipherInfo(TlsCipherSuite suite)
    {
        var (strength, keyExchange, authentication, encryption) = ClassifyCipher(suite);
        return new TlsCipherInfo
        {
            SuiteName = suite.ToString(),
            Strength = strength,
            KeyExchange = keyExchange,
            Authentication = authentication,
            Encryption = encryption
        };
    }

    /// <summary>
    /// Extracts a summary from an X509 certificate for display/reporting.
    /// </summary>
    public static TlsCertificateSummary ExtractCertificateSummary(X509Certificate2 cert)
    {
        return new TlsCertificateSummary
        {
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            NotAfter = cert.NotAfter,
            KeySizeBits = GetPublicKeySize(cert)
        };
    }

    /// <summary>
    /// Tests whether a server supports a specific TLS/SSL protocol version
    /// via direct SslStream connection.
    /// </summary>
    public static async Task<bool> TestProtocolAsync(
        string host, int port, SslProtocols protocol, CancellationToken ct)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectionTimeout);
        CancellationToken probeCt = timeoutCts.Token;

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, probeCt);
#pragma warning disable CA5397, CS0618, SYSLIB0039
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = protocol
            }, probeCt);
#pragma warning restore CA5397, CS0618, SYSLIB0039
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests whether a server supports a specific TLS 1.2 cipher suite
    /// via direct SslStream connection.
    /// </summary>
    public static async Task<bool> TestCipherSuiteAsync(
        string host, int port, TlsCipherSuite suite, CancellationToken ct)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectionTimeout);
        CancellationToken probeCt = timeoutCts.Token;

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, probeCt);
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);

            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12
            };

            try
            {
#pragma warning disable CA1416
                options.CipherSuitesPolicy = new CipherSuitesPolicy([suite]);
#pragma warning restore CA1416
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }

            await ssl.AuthenticateAsClientAsync(options, probeCt);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Retrieves the peer certificate from a direct TLS connection.
    /// </summary>
    public static async Task<X509Certificate2?> RetrieveCertificateAsync(
        string host, int port, CancellationToken ct)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectionTimeout);
        CancellationToken probeCt = timeoutCts.Token;

        X509Certificate? remoteCert = null;

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, probeCt).ConfigureAwait(false);

            using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) =>
            {
                remoteCert = cert;
                return true;
            });

            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = host },
                probeCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null; // timeout: certificate unavailable, preserve partial audit results
        }

        if (remoteCert is null)
            return null;

        using (remoteCert)
        {
            return new X509Certificate2(remoteCert);
        }
    }

    private static string L(Func<string, string>? localize, string key)
        => localize?.Invoke(key) ?? key;
}
