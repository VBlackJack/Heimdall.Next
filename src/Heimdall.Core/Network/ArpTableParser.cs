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

using System.Net;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Network;

/// <summary>
/// Parses platform-specific ARP table text into an IP-to-MAC mapping.
/// </summary>
public static partial class ArpTableParser
{
    [GeneratedRegex(@"\((.*?)\)\s+at\s+([a-fA-F0-9:]+)", RegexOptions.Compiled)]
    private static partial Regex MacOsArpRegex();

    public static Dictionary<string, string> ParseWindows(string? output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output))
        {
            return result;
        }

        foreach (var line in output.Split('\n'))
        {
            var parts = line.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var ip = parts[0];
                var mac = parts[1];
                if (IPAddress.TryParse(ip, out _) &&
                    mac.Length == 17 && mac.Count(c => c == '-') == 5)
                {
                    result[ip] = mac;
                }
            }
        }

        return result;
    }

    public static Dictionary<string, string> ParseLinuxProcNet(string? procContent)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(procContent))
        {
            return result;
        }

        var lines = procContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                var ip = parts[0];
                var mac = parts[3];
                if (mac != "00:00:00:00:00:00" && IPAddress.TryParse(ip, out _))
                {
                    result[ip] = mac.Replace(':', '-');
                }
            }
        }

        return result;
    }

    public static Dictionary<string, string> ParseMacOs(string? output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output))
        {
            return result;
        }

        foreach (var line in output.Split('\n'))
        {
            var match = MacOsArpRegex().Match(line);
            if (match.Success)
            {
                result[match.Groups[1].Value] = match.Groups[2].Value.Replace(':', '-');
            }
        }

        return result;
    }
}
