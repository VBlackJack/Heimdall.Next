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

namespace Heimdall.Core.CronJob;

/// <summary>
/// Parses raw crontab text into structured entries while preserving legacy heuristics.
/// </summary>
public static class CrontabParser
{
    public static IReadOnlyList<CrontabEntry> Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<CrontabEntry>();
        }

        var entries = new List<CrontabEntry>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 0 && !trimmed[..eqIdx].Contains(' '))
            {
                continue;
            }

            var parts = trimmed.Split([' ', '\t'], 6, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6)
            {
                continue;
            }

            entries.Add(new CrontabEntry(
                parts[0],
                parts[1],
                parts[2],
                parts[3],
                parts[4],
                string.Join(" ", parts[5..]),
                trimmed));
        }

        return entries;
    }
}
