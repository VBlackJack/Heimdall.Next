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

namespace Heimdall.Core.Network;

/// <summary>
/// Pure helper that cleans raw <c>nslookup</c> output for display: strips the
/// "Server:" / "Address:" header block, trims each remaining line, and drops
/// blank lines inside the result body.
/// </summary>
public static class NslookupOutputParser
{
    /// <summary>
    /// Cleans the raw output of <c>nslookup</c>. The returned string contains
    /// one record line per source line, with leading/trailing whitespace removed.
    /// </summary>
    /// <param name="rawOutput">Raw stdout captured from <c>nslookup</c>.</param>
    /// <returns>
    /// Cleaned output, or an empty string when the input is null, empty,
    /// whitespace-only, or consists solely of the header block.
    /// </returns>
    public static string Parse(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return string.Empty;
        }

        var lines = rawOutput.Split('\n');
        var sb = new StringBuilder();
        var pastHeader = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (!pastHeader)
            {
                // Skip initial blank lines before we see any header or body content.
                if (line.Length == 0 && sb.Length == 0)
                {
                    continue;
                }

                // The header ends on the first blank line that follows a
                // non-empty line (typically right after "Address: 1.2.3.4#53").
                if (line.Length == 0)
                {
                    pastHeader = true;
                    continue;
                }

                // "Server:" / "Address:" header lines: skip and stay in header mode.
                if (line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Address:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Any other non-empty line means we have entered the body even
                // if nslookup did not emit a separating blank line (defensive
                // fallback for trimmed or truncated outputs).
                pastHeader = true;
            }

            if (pastHeader && line.Length > 0)
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString().TrimEnd();
    }
}
