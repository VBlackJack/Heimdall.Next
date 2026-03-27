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
using Heimdall.Core.Security;

namespace Heimdall.App.Services;

/// <summary>
/// Exports audit evidence as RFC 4180 compliant CSV for import into spreadsheets,
/// SIEM tools, or compliance tracking systems.
/// </summary>
public static class CsvEvidenceExporter
{
    private const string Header = "Chapter,CheckID,CheckName,Clause,Status,Host,Detail,RawData";

    /// <summary>
    /// Generates a CSV string containing all audit checks and their evidence.
    /// Checks with no per-host evidence emit a single row with the check summary.
    /// </summary>
    /// <param name="report">The completed audit report.</param>
    /// <returns>A complete CSV document as a string.</returns>
    public static string Generate(AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder(4096);
        sb.AppendLine(Header);

        foreach (var chapter in report.Chapters)
        {
            foreach (var check in chapter.Checks)
            {
                if (check.Evidence.Count == 0)
                {
                    AppendRow(sb, chapter, check, host: "", detail: check.Summary, rawData: "");
                }
                else
                {
                    foreach (var evidence in check.Evidence)
                    {
                        AppendRow(sb, chapter, check, evidence.Host, evidence.Detail, evidence.RawData);
                    }
                }
            }
        }

        return sb.ToString();
    }

    private static void AppendRow(
        StringBuilder sb,
        AuditChapter chapter,
        AuditCheck check,
        string host,
        string detail,
        string rawData)
    {
        sb.Append(Escape(chapter.Name)).Append(',');
        sb.Append(Escape(check.Id)).Append(',');
        sb.Append(Escape(check.Name)).Append(',');
        sb.Append(Escape(check.SecNumCloudClause)).Append(',');
        sb.Append(Escape(check.Status.ToString())).Append(',');
        sb.Append(Escape(host)).Append(',');
        sb.Append(Escape(detail)).Append(',');
        sb.AppendLine(Escape(rawData));
    }

    /// <summary>
    /// Wraps a value in double quotes and escapes embedded double quotes
    /// per RFC 4180.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return string.Concat("\"", value.Replace("\"", "\"\""), "\"");
    }
}
