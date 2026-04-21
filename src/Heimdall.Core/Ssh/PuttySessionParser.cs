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

using System.Text;

namespace Heimdall.Core.Ssh;

/// <summary>
/// Parses raw PuTTY registry sessions into importable SSH candidates.
/// </summary>
public static class PuttySessionParser
{
    public static PuttySessionParseResult Parse(IReadOnlyList<RawPuttySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var diagnostics = new List<PuttySessionDiagnostic>();
        var candidates = new List<PuttySessionCandidate>();

        foreach (var session in sessions)
        {
            var displayName = DecodeSessionName(session.EncodedSessionName);
            if (string.Equals(displayName, "Default Settings", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new PuttySessionDiagnostic(
                    PuttyDiagnosticLevel.Info,
                    PuttyDiagnosticCode.DefaultSettingsKeySkipped,
                    displayName));
                continue;
            }

            var protocol = ReadString(session.Values, "Protocol");
            if (!string.Equals(protocol, "ssh", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new PuttySessionDiagnostic(
                    PuttyDiagnosticLevel.Info,
                    PuttyDiagnosticCode.NonSshProtocolIgnored,
                    displayName,
                    protocol ?? string.Empty));
                continue;
            }

            var hostName = ReadString(session.Values, "HostName");
            var port = ReadPort(session.Values, diagnostics, displayName);
            var userName = ReadString(session.Values, "UserName");
            var publicKeyFile = ReadString(session.Values, "PublicKeyFile");
            if (!string.IsNullOrWhiteSpace(publicKeyFile) &&
                publicKeyFile.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new PuttySessionDiagnostic(
                    PuttyDiagnosticLevel.Warning,
                    PuttyDiagnosticCode.PpkKeyCapturedNotConverted,
                    displayName,
                    publicKeyFile));
            }

            AppendProxyDiagnostic(session.Values, diagnostics, displayName);
            AppendForwardingDiagnostic(session.Values, diagnostics, displayName);
            AppendRemoteCommandDiagnostic(session.Values, diagnostics, displayName);

            if (string.IsNullOrWhiteSpace(hostName))
            {
                diagnostics.Add(new PuttySessionDiagnostic(
                    PuttyDiagnosticLevel.Warning,
                    PuttyDiagnosticCode.MissingHostName,
                    displayName));
            }

            candidates.Add(new PuttySessionCandidate
            {
                DisplayName = displayName,
                HostName = string.IsNullOrWhiteSpace(hostName) ? null : hostName,
                Port = port,
                UserName = string.IsNullOrWhiteSpace(userName) ? null : userName,
                PublicKeyFile = string.IsNullOrWhiteSpace(publicKeyFile) ? null : publicKeyFile,
                EncodedSessionName = session.EncodedSessionName
            });
        }

        return new PuttySessionParseResult(candidates, diagnostics);
    }

    public static string DecodeSessionName(string encodedName)
    {
        ArgumentNullException.ThrowIfNull(encodedName);

        if (encodedName.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(encodedName.Length);
        for (var i = 0; i < encodedName.Length; i++)
        {
            var current = encodedName[i];
            if (current == '%' && i + 2 < encodedName.Length &&
                IsHex(encodedName[i + 1]) && IsHex(encodedName[i + 2]))
            {
                var hex = encodedName.Substring(i + 1, 2);
                builder.Append((char)Convert.ToInt32(hex, 16));
                i += 2;
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool IsHex(char value) =>
        (value >= '0' && value <= '9') ||
        (value >= 'a' && value <= 'f') ||
        (value >= 'A' && value <= 'F');

    private static string? ReadString(IReadOnlyDictionary<string, object?> values, string name)
    {
        return values.TryGetValue(name, out var value)
            ? value?.ToString()
            : null;
    }

    private static int ReadPort(
        IReadOnlyDictionary<string, object?> values,
        ICollection<PuttySessionDiagnostic> diagnostics,
        string sessionName)
    {
        if (!values.TryGetValue("PortNumber", out var rawPort) || rawPort is null)
        {
            return 22;
        }

        if (rawPort is int port && port is >= 1 and <= 65535)
        {
            return port;
        }

        if (int.TryParse(rawPort.ToString(), out port) && port is >= 1 and <= 65535)
        {
            return port;
        }

        diagnostics.Add(new PuttySessionDiagnostic(
            PuttyDiagnosticLevel.Warning,
            PuttyDiagnosticCode.InvalidPortNumber,
            sessionName,
            rawPort.ToString()));
        return 22;
    }

    private static void AppendProxyDiagnostic(
        IReadOnlyDictionary<string, object?> values,
        ICollection<PuttySessionDiagnostic> diagnostics,
        string sessionName)
    {
        var method = ReadString(values, "ProxyMethod");
        var host = ReadString(values, "ProxyHost");
        var port = ReadString(values, "ProxyPort");
        var hasProxy = !string.IsNullOrWhiteSpace(host)
            || !string.IsNullOrWhiteSpace(port)
            || (int.TryParse(method, out var parsedMethod) && parsedMethod != 0);
        if (!hasProxy)
        {
            return;
        }

        diagnostics.Add(new PuttySessionDiagnostic(
            PuttyDiagnosticLevel.Info,
            PuttyDiagnosticCode.ProxyCapturedButNotMapped,
            sessionName,
            $"method={method ?? "0"} host={host ?? string.Empty} port={port ?? string.Empty}".Trim()));
    }

    private static void AppendForwardingDiagnostic(
        IReadOnlyDictionary<string, object?> values,
        ICollection<PuttySessionDiagnostic> diagnostics,
        string sessionName)
    {
        var value = ReadString(values, "PortForwardings");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var segments = value.Split(['\0', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
        diagnostics.Add(new PuttySessionDiagnostic(
            PuttyDiagnosticLevel.Info,
            PuttyDiagnosticCode.PortForwardingsCapturedButNotMapped,
            sessionName,
            segments.Length.ToString()));
    }

    private static void AppendRemoteCommandDiagnostic(
        IReadOnlyDictionary<string, object?> values,
        ICollection<PuttySessionDiagnostic> diagnostics,
        string sessionName)
    {
        var value = ReadString(values, "RemoteCommand");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var truncated = value.Length > 80
            ? $"{value[..80]}..."
            : value;
        diagnostics.Add(new PuttySessionDiagnostic(
            PuttyDiagnosticLevel.Info,
            PuttyDiagnosticCode.RemoteCommandCapturedButNotMapped,
            sessionName,
            truncated));
    }
}
