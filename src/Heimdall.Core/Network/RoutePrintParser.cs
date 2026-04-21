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

namespace Heimdall.Core.Network;

/// <summary>
/// Parses Windows <c>route print</c> output into IPv4 route rows using the
/// same locale-aware heuristics as the original view.
/// </summary>
public static class RoutePrintParser
{
    public static bool IsIpv4SectionHeader(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && line.Contains("IPv4", StringComparison.OrdinalIgnoreCase)
        && (line.Contains("Route", StringComparison.OrdinalIgnoreCase)
            || line.Contains("routage", StringComparison.OrdinalIgnoreCase));

    public static bool IsActiveRoutesHeader(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && (line.Contains("Active", StringComparison.OrdinalIgnoreCase)
            || line.Contains("actif", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Itin", StringComparison.OrdinalIgnoreCase));

    public static bool IsEndOfSection(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && (line.StartsWith("==", StringComparison.Ordinal)
            || line.Contains("Persistent", StringComparison.OrdinalIgnoreCase)
            || line.Contains("persistant", StringComparison.OrdinalIgnoreCase)
            || line.Contains("IPv6", StringComparison.OrdinalIgnoreCase));

    public static bool IsColumnHeader(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && (line.Contains("Destination", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Masque", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Netmask", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<RouteEntry> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var entries = new List<RouteEntry>();
        var lines = output.Split('\n');
        var inIpv4Section = false;
        var inActiveRoutes = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (!inIpv4Section && IsIpv4SectionHeader(line))
            {
                inIpv4Section = true;
                continue;
            }

            if (inIpv4Section && !inActiveRoutes && IsActiveRoutesHeader(line))
            {
                inActiveRoutes = true;
                continue;
            }

            if (inActiveRoutes && IsColumnHeader(line))
            {
                continue;
            }

            if (inActiveRoutes && IsEndOfSection(line))
            {
                break;
            }

            if (!inActiveRoutes || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            if (!parts[0].Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            entries.Add(new RouteEntry(
                parts[0],
                parts[1],
                parts[2],
                parts[3],
                parts[4]));
        }

        return entries;
    }
}
