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

namespace Heimdall.Core.Ssh;

/// <summary>
/// Pure parser for clear OpenSSH known_hosts entries.
/// </summary>
public static class KnownHostsParser
{
    private static readonly HashSet<string> SupportedKeyTypes =
    [
        "ssh-ed25519",
        "ecdsa-sha2-nistp256",
        "ecdsa-sha2-nistp384",
        "ecdsa-sha2-nistp521",
        "ssh-rsa",
        "ssh-dss",
        "sk-ecdsa-sha2-nistp256@openssh.com",
        "sk-ssh-ed25519@openssh.com"
    ];

    public static KnownHostsParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Parse(content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n'));
    }

    public static KnownHostsParseResult Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var entries = new List<KnownHostsRawEntry>();
        var diagnostics = new List<KnownHostsImportDiagnostic>();

        var lineNumber = 0;
        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine ?? string.Empty;
            var trimmedStart = line.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmedStart) || trimmedStart.StartsWith('#'))
            {
                continue;
            }

            if (trimmedStart.StartsWith("@cert-authority ", StringComparison.Ordinal) ||
                trimmedStart.StartsWith("@cert-authority\t", StringComparison.Ordinal))
            {
                diagnostics.Add(new KnownHostsImportDiagnostic(
                    KnownHostsDiagnosticLevel.Info,
                    lineNumber,
                    KnownHostsDiagnosticCode.CertAuthorityNotSupported));
                continue;
            }

            if (trimmedStart.StartsWith("@revoked ", StringComparison.Ordinal) ||
                trimmedStart.StartsWith("@revoked\t", StringComparison.Ordinal))
            {
                diagnostics.Add(new KnownHostsImportDiagnostic(
                    KnownHostsDiagnosticLevel.Info,
                    lineNumber,
                    KnownHostsDiagnosticCode.RevokedEntryNotSupported));
                continue;
            }

            var fields = trimmedStart.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3)
            {
                diagnostics.Add(new KnownHostsImportDiagnostic(
                    KnownHostsDiagnosticLevel.Warning,
                    lineNumber,
                    KnownHostsDiagnosticCode.MalformedLine,
                    $"{fields.Length} fields"));
                continue;
            }

            var hostsField = fields[0];
            var keyType = fields[1];
            var base64 = fields[2];

            if (hostsField.StartsWith("|1|", StringComparison.Ordinal))
            {
                diagnostics.Add(new KnownHostsImportDiagnostic(
                    KnownHostsDiagnosticLevel.Info,
                    lineNumber,
                    KnownHostsDiagnosticCode.HashedEntryNotSupported));
                continue;
            }

            if (!SupportedKeyTypes.Contains(keyType))
            {
                diagnostics.Add(new KnownHostsImportDiagnostic(
                    KnownHostsDiagnosticLevel.Info,
                    lineNumber,
                    KnownHostsDiagnosticCode.UnsupportedKeyType,
                    keyType));
                continue;
            }

            byte[] rawKeyBlob;
            try
            {
                rawKeyBlob = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                diagnostics.Add(new KnownHostsImportDiagnostic(
                    KnownHostsDiagnosticLevel.Warning,
                    lineNumber,
                    KnownHostsDiagnosticCode.MalformedLine,
                    "bad base64"));
                continue;
            }

            foreach (var token in hostsField.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (token.IndexOfAny(['*', '?', '!', '%']) >= 0)
                {
                    diagnostics.Add(new KnownHostsImportDiagnostic(
                        KnownHostsDiagnosticLevel.Info,
                        lineNumber,
                        KnownHostsDiagnosticCode.UnsupportedHostPattern,
                        token));
                    continue;
                }

                if (!TryParseHostToken(token, out var host, out var port, out var context))
                {
                    diagnostics.Add(new KnownHostsImportDiagnostic(
                        KnownHostsDiagnosticLevel.Info,
                        lineNumber,
                        KnownHostsDiagnosticCode.UnsupportedHostPattern,
                        context));
                    continue;
                }

                entries.Add(new KnownHostsRawEntry
                {
                    Host = host,
                    Port = port,
                    KeyType = keyType,
                    Base64Key = rawKeyBlob,
                    SourceLineNumber = lineNumber
                });
            }
        }

        return new KnownHostsParseResult(entries, diagnostics);
    }

    private static bool TryParseHostToken(
        string token,
        out string host,
        out int port,
        out string context)
    {
        host = string.Empty;
        port = 22;
        context = token;

        if (token.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = token.IndexOf(']');
            if (closing <= 0)
            {
                return false;
            }

            host = token[1..closing];
            if (host.Length == 0 || host.Contains("%", StringComparison.Ordinal))
            {
                context = token;
                return false;
            }

            if (closing == token.Length - 1)
            {
                return true;
            }

            if (token[closing + 1] != ':')
            {
                context = token;
                return false;
            }

            var portToken = token[(closing + 2)..];
            if (int.TryParse(portToken, out port) && port is >= 1 and <= 65535)
            {
                return true;
            }

            context = token;
            return false;
        }

        var colonCount = token.Count(ch => ch == ':');
        if (colonCount >= 2)
        {
            host = token;
            port = 22;
            return true;
        }

        if (colonCount == 1)
        {
            context = "bare colon non-IPv6";
            return false;
        }

        host = token;
        port = 22;
        return true;
    }
}
