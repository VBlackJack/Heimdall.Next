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

using System.Globalization;
using System.IO;
using System.Text;
using Heimdall.Core.Localization;
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

    public static NoteDraft Create(
        NoteTemplateKind templateKind,
        ToolContext? context,
        DateTime nowLocal,
        LocalizationManager? localizer = null)
    {
        return templateKind switch
        {
            NoteTemplateKind.Daily => BuildDaily(context, nowLocal, localizer),
            NoteTemplateKind.Incident => BuildIncident(context, nowLocal, localizer),
            NoteTemplateKind.Procedure => BuildProcedure(context, nowLocal, localizer),
            _ => BuildBlank(context, nowLocal, localizer)
        };
    }

    private static NoteDraft BuildBlank(ToolContext? context, DateTime nowLocal, LocalizationManager? loc)
    {
        var title = BuildContextualTitle(L(loc, "ToolNotesTplWorkingNote"), context);
        var fileName = $"{TimestampPrefix(nowLocal)}-{SlugifyValue(title)}.md";

        var content = new StringBuilder()
            .AppendLine($"# {title}")
            .AppendLine()
            .AppendLine(BuildMetadataLine(nowLocal, context))
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplNotes")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplCommands")}")
            .AppendLine()
            .AppendLine("```bash")
            .AppendLine()
            .AppendLine("```")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplNext")}")
            .AppendLine()
            .AppendLine("- ")
            .ToString();

        return new NoteDraft(fileName, content);
    }

    private static NoteDraft BuildDaily(ToolContext? context, DateTime nowLocal, LocalizationManager? loc)
    {
        var relativePath = Path.Combine("daily", nowLocal.ToString("yyyy"), $"{nowLocal:yyyy-MM-dd}.md");
        var dailyLabel = L(loc, "ToolNotesTplDailyNote");
        var suffix = BuildContextSuffix(context);
        var title = suffix.Length > 0
            ? $"{dailyLabel} - {nowLocal:yyyy-MM-dd} - {suffix}"
            : $"{dailyLabel} - {nowLocal:yyyy-MM-dd}";

        var content = new StringBuilder()
            .AppendLine($"# {title}")
            .AppendLine()
            .AppendLine(BuildMetadataLine(nowLocal, context))
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplFocus")}")
            .AppendLine()
            .AppendLine("- ")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplJournal")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplCommands")}")
            .AppendLine()
            .AppendLine("```bash")
            .AppendLine()
            .AppendLine("```")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplFollowUp")}")
            .AppendLine()
            .AppendLine("- ")
            .ToString();

        return new NoteDraft(relativePath, content);
    }

    private static NoteDraft BuildIncident(ToolContext? context, DateTime nowLocal, LocalizationManager? loc)
    {
        var suffix = BuildContextSuffix(context);
        var incidentLabel = L(loc, "ToolNotesTplIncident");
        var title = suffix.Length > 0
            ? $"{incidentLabel} - {suffix}"
            : L(loc, "ToolNotesTplIncidentReport");
        var fileName = $"{TimestampPrefix(nowLocal)}-{SlugifyValue(title)}.md";

        var content = new StringBuilder()
            .AppendLine($"# {title}")
            .AppendLine()
            .AppendLine(BuildMetadataLine(nowLocal, context))
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplSummary")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplImpact")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplTimeline")}")
            .AppendLine()
            .AppendLine($"- {nowLocal:HH:mm} - {L(loc, "ToolNotesTplIncidentStarted")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplInvestigation")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplActions")}")
            .AppendLine()
            .AppendLine("- ")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplResolution")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplFollowUp")}")
            .AppendLine()
            .AppendLine("- ")
            .ToString();

        return new NoteDraft(fileName, content);
    }

    private static NoteDraft BuildProcedure(ToolContext? context, DateTime nowLocal, LocalizationManager? loc)
    {
        var suffix = BuildContextSuffix(context);
        var procedureLabel = L(loc, "ToolNotesTplProcedure");
        var title = suffix.Length > 0
            ? $"{procedureLabel} - {suffix}"
            : procedureLabel;
        var fileName = $"{TimestampPrefix(nowLocal)}-{SlugifyValue(title)}.md";

        var content = new StringBuilder()
            .AppendLine($"# {title}")
            .AppendLine()
            .AppendLine(BuildMetadataLine(nowLocal, context))
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplPurpose")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplScope")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplPreconditions")}")
            .AppendLine()
            .AppendLine("- ")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplSteps")}")
            .AppendLine()
            .AppendLine("1. ")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplValidation")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplRollback")}")
            .AppendLine()
            .AppendLine($"## {L(loc, "ToolNotesTplReferences")}")
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

    private static string L(LocalizationManager? loc, string key) => loc?[key] ?? key;

    private static string Slugify(string value)
    {
        var normalized = RemoveDiacritics(value);
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;

        foreach (var ch in normalized)
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

    public static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
