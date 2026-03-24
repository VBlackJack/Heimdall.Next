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

using System.IO;
using System.Text;
using Heimdall.Core.Models;

namespace Heimdall.App.Services;

public enum NoteTemplateKind
{
    Blank,
    Daily,
    Incident,
    Procedure
}

public sealed record NoteDraft(string RelativePath, string Content);

public static class NotesTemplateFactory
{
    public static string SlugifyValue(string value) => Slugify(value);

    public static NoteDraft Create(NoteTemplateKind templateKind, ToolContext? context, DateTime nowLocal)
    {
        return templateKind switch
        {
            NoteTemplateKind.Daily => BuildDaily(context, nowLocal),
            NoteTemplateKind.Incident => BuildIncident(context, nowLocal),
            NoteTemplateKind.Procedure => BuildProcedure(context, nowLocal),
            _ => BuildBlank(context, nowLocal)
        };
    }

    private static NoteDraft BuildBlank(ToolContext? context, DateTime nowLocal)
    {
        var title = BuildContextualTitle("Working Note", context);
        var fileName = $"{TimestampPrefix(nowLocal)}-{SlugifyValue(title)}.md";

        var content = new StringBuilder()
            .AppendLine($"# {title}")
            .AppendLine()
            .AppendLine(BuildMetadataLine(nowLocal, context))
            .AppendLine()
            .AppendLine("## Notes")
            .AppendLine()
            .AppendLine("## Commands")
            .AppendLine()
            .AppendLine("```bash")
            .AppendLine()
            .AppendLine("```")
            .AppendLine()
            .AppendLine("## Next")
            .AppendLine()
            .AppendLine("- ")
            .ToString();

        return new NoteDraft(fileName, content);
    }

    private static NoteDraft BuildDaily(ToolContext? context, DateTime nowLocal)
    {
        var relativePath = Path.Combine("daily", nowLocal.ToString("yyyy"), $"{nowLocal:yyyy-MM-dd}.md");
        var suffix = BuildContextSuffix(context);
        var title = suffix.Length > 0
            ? $"Daily Note - {nowLocal:yyyy-MM-dd} - {suffix}"
            : $"Daily Note - {nowLocal:yyyy-MM-dd}";

        var content = new StringBuilder()
            .AppendLine($"# {title}")
            .AppendLine()
            .AppendLine(BuildMetadataLine(nowLocal, context))
            .AppendLine()
            .AppendLine("## Focus")
            .AppendLine()
            .AppendLine("- ")
            .AppendLine()
            .AppendLine("## Journal")
            .AppendLine()
            .AppendLine("## Commands")
            .AppendLine()
            .AppendLine("```bash")
            .AppendLine()
            .AppendLine("```")
            .AppendLine()
            .AppendLine("## Follow-up")
            .AppendLine()
            .AppendLine("- ")
            .ToString();

        return new NoteDraft(relativePath, content);
    }

    private static NoteDraft BuildIncident(ToolContext? context, DateTime nowLocal)
    {
        var suffix = BuildContextSuffix(context);
        var title = suffix.Length > 0
            ? $"Incident - {suffix}"
            : "Incident Report";
        var fileName = $"{TimestampPrefix(nowLocal)}-{SlugifyValue(title)}.md";

        var content = new StringBuilder()
            .AppendLine($"# {title}")
            .AppendLine()
            .AppendLine(BuildMetadataLine(nowLocal, context))
            .AppendLine()
            .AppendLine("## Summary")
            .AppendLine()
            .AppendLine("## Impact")
            .AppendLine()
            .AppendLine("## Timeline")
            .AppendLine()
            .AppendLine($"- {nowLocal:HH:mm} - Incident started")
            .AppendLine()
            .AppendLine("## Investigation")
            .AppendLine()
            .AppendLine("## Actions")
            .AppendLine()
            .AppendLine("- ")
            .AppendLine()
            .AppendLine("## Resolution")
            .AppendLine()
            .AppendLine("## Follow-up")
            .AppendLine()
            .AppendLine("- ")
            .ToString();

        return new NoteDraft(fileName, content);
    }

    private static NoteDraft BuildProcedure(ToolContext? context, DateTime nowLocal)
    {
        var suffix = BuildContextSuffix(context);
        var title = suffix.Length > 0
            ? $"Procedure - {suffix}"
            : "Procedure";
        var fileName = $"{TimestampPrefix(nowLocal)}-{SlugifyValue(title)}.md";

        var content = new StringBuilder()
            .AppendLine($"# {title}")
            .AppendLine()
            .AppendLine(BuildMetadataLine(nowLocal, context))
            .AppendLine()
            .AppendLine("## Purpose")
            .AppendLine()
            .AppendLine("## Scope")
            .AppendLine()
            .AppendLine("## Preconditions")
            .AppendLine()
            .AppendLine("- ")
            .AppendLine()
            .AppendLine("## Steps")
            .AppendLine()
            .AppendLine("1. ")
            .AppendLine()
            .AppendLine("## Validation")
            .AppendLine()
            .AppendLine("## Rollback")
            .AppendLine()
            .AppendLine("## References")
            .AppendLine()
            .AppendLine("- ")
            .ToString();

        return new NoteDraft(fileName, content);
    }

    private static string BuildMetadataLine(DateTime nowLocal, ToolContext? context)
    {
        var parts = new List<string> { $"created {nowLocal:yyyy-MM-dd HH:mm}" };

        if (!string.IsNullOrWhiteSpace(context?.DisplayName))
        {
            parts.Add($"display {context.DisplayName}");
        }

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            parts.Add($"host {context.TargetHost}");
        }

        if (context?.TargetPort is int port)
        {
            parts.Add($"port {port}");
        }

        if (!string.IsNullOrWhiteSpace(context?.Username))
        {
            parts.Add($"user {context.Username}");
        }

        if (!string.IsNullOrWhiteSpace(context?.ProjectName))
        {
            parts.Add($"project {context.ProjectName}");
        }

        if (!string.IsNullOrWhiteSpace(context?.GroupName))
        {
            parts.Add($"group {context.GroupName}");
        }

        if (!string.IsNullOrWhiteSpace(context?.ConnectionType))
        {
            parts.Add($"type {context.ConnectionType}");
        }

        return $"> {string.Join(" | ", parts)}";
    }

    private static string BuildContextualTitle(string fallback, ToolContext? context)
    {
        if (!string.IsNullOrWhiteSpace(context?.DisplayName))
        {
            return $"{fallback} - {context.DisplayName}";
        }

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            return $"{fallback} - {context.TargetHost}";
        }

        return fallback;
    }

    private static string BuildContextSuffix(ToolContext? context)
    {
        if (!string.IsNullOrWhiteSpace(context?.DisplayName))
        {
            return context.DisplayName!;
        }

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            return context.TargetHost!;
        }

        if (!string.IsNullOrWhiteSpace(context?.ProjectName))
        {
            return context.ProjectName!;
        }

        return string.Empty;
    }

    private static string TimestampPrefix(DateTime nowLocal) => nowLocal.ToString("yyyyMMdd-HHmmss");

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousDash = false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
