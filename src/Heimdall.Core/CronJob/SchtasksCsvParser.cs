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

namespace Heimdall.Core.CronJob;

/// <summary>
/// Parses schtasks CSV output while preserving permissive header matching.
/// </summary>
public static class SchtasksCsvParser
{
    public static IReadOnlyList<WindowsTaskEntry> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<WindowsTaskEntry>();
        }

        var lines = csv.Split('\n');
        if (lines.Length < 2)
        {
            return Array.Empty<WindowsTaskEntry>();
        }

        var header = ParseCsvLine(lines[0]);
        var nameIdx = FindColumnIndex(header, "TaskName");
        var statusIdx = FindColumnIndex(header, "Status");
        var nextRunIdx = FindColumnIndex(header, "Next Run Time");
        var lastRunIdx = FindColumnIndex(header, "Last Run Time");
        var lastResultIdx = FindColumnIndex(header, "Last Result");

        if (nameIdx < 0)
        {
            return Array.Empty<WindowsTaskEntry>();
        }

        var results = new List<WindowsTaskEntry>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            var name = GetField(fields, nameIdx);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            results.Add(new WindowsTaskEntry(
                name,
                GetField(fields, statusIdx),
                GetField(fields, nextRunIdx),
                GetField(fields, lastRunIdx),
                GetField(fields, lastResultIdx)));
        }

        return results;
    }

    public static int FindColumnIndex(List<string> header, string columnName)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        for (var i = 0; i < header.Count; i++)
        {
            if (header[i].Contains(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public static List<string> ParseCsvLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    public static string GetField(List<string> fields, int index)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return index >= 0 && index < fields.Count ? fields[index] : string.Empty;
    }
}
