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
/// Parses a limited ssh_config subset into importable candidates and diagnostics.
/// </summary>
public static class OpenSshConfigParser
{
    /// <summary>
    /// Parses OpenSSH config content into structured import candidates.
    /// </summary>
    /// <param name="contents">Raw ssh_config text content.</param>
    /// <returns>The parsed candidates and non-fatal diagnostics.</returns>
    public static OpenSshParseResult Parse(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var diagnostics = new List<OpenSshImportDiagnostic>();
        var candidates = new List<OpenSshImportCandidate>();
        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HostBlockState? currentBlock = null;
        var isInsideMatchBlock = false;

        var lines = contents.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var trimmedLine = StripComments(lines[index]).Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            if (!TrySplitDirective(trimmedLine, out var directive, out var value))
            {
                continue;
            }

            if (isInsideMatchBlock)
            {
                if (directive.Equals("host", StringComparison.OrdinalIgnoreCase))
                {
                    isInsideMatchBlock = false;
                }
                else if (directive.Equals("match", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new OpenSshImportDiagnostic(
                        OpenSshDiagnosticLevel.Warning,
                        lineNumber,
                        OpenSshDiagnosticCode.MatchBlockIgnored));
                    continue;
                }
                else
                {
                    continue;
                }
            }

            if (directive.Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                FinalizeCurrentBlock(currentBlock, diagnostics, candidates, seenAliases);
                currentBlock = CreateHostBlock(value, lineNumber, diagnostics, seenAliases);
                continue;
            }

            if (directive.Equals("match", StringComparison.OrdinalIgnoreCase))
            {
                FinalizeCurrentBlock(currentBlock, diagnostics, candidates, seenAliases);
                currentBlock = null;
                diagnostics.Add(new OpenSshImportDiagnostic(
                    OpenSshDiagnosticLevel.Warning,
                    lineNumber,
                    OpenSshDiagnosticCode.MatchBlockIgnored));
                isInsideMatchBlock = true;
                continue;
            }

            if (directive.Equals("include", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new OpenSshImportDiagnostic(
                    OpenSshDiagnosticLevel.Warning,
                    lineNumber,
                    OpenSshDiagnosticCode.IncludeDirectiveIgnored,
                    value));
                continue;
            }

            if (currentBlock is null)
            {
                diagnostics.Add(new OpenSshImportDiagnostic(
                    OpenSshDiagnosticLevel.Info,
                    lineNumber,
                    OpenSshDiagnosticCode.UnknownDirectiveIgnored,
                    directive));
                continue;
            }

            ApplyDirective(currentBlock, directive, value, userProfile, lineNumber, diagnostics);
        }

        FinalizeCurrentBlock(currentBlock, diagnostics, candidates, seenAliases);

        return new OpenSshParseResult(candidates, diagnostics);
    }

    private static HostBlockState? CreateHostBlock(
        string value,
        int lineNumber,
        ICollection<OpenSshImportDiagnostic> diagnostics,
        ISet<string> seenAliases)
    {
        var aliases = new List<string>();
        foreach (var alias in SplitTokens(value))
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (IsWildcardAlias(alias))
            {
                diagnostics.Add(new OpenSshImportDiagnostic(
                    OpenSshDiagnosticLevel.Warning,
                    lineNumber,
                    OpenSshDiagnosticCode.WildcardAliasIgnored,
                    alias));
                continue;
            }

            if (!seenAliases.Add(alias))
            {
                diagnostics.Add(new OpenSshImportDiagnostic(
                    OpenSshDiagnosticLevel.Warning,
                    lineNumber,
                    OpenSshDiagnosticCode.DuplicateAliasInFile,
                    alias));
                continue;
            }

            aliases.Add(alias);
        }

        return aliases.Count == 0
            ? null
            : new HostBlockState(lineNumber, aliases);
    }

    private static void ApplyDirective(
        HostBlockState block,
        string directive,
        string value,
        string userProfile,
        int lineNumber,
        ICollection<OpenSshImportDiagnostic> diagnostics)
    {
        if (directive.Equals("hostname", StringComparison.OrdinalIgnoreCase))
        {
            block.HostName = value;
            return;
        }

        if (directive.Equals("port", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(value, out var port) && port is >= 1 and <= 65535)
            {
                block.Port = port;
                return;
            }

            diagnostics.Add(new OpenSshImportDiagnostic(
                OpenSshDiagnosticLevel.Warning,
                lineNumber,
                OpenSshDiagnosticCode.InvalidPort,
                value));
            block.Port = 22;
            return;
        }

        if (directive.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            block.User = value;
            return;
        }

        if (directive.Equals("identityfile", StringComparison.OrdinalIgnoreCase))
        {
            block.IdentityFile = ExpandIdentityFile(value, userProfile, lineNumber, diagnostics);
            return;
        }

        if (directive.Equals("proxyjump", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new OpenSshImportDiagnostic(
                OpenSshDiagnosticLevel.Warning,
                lineNumber,
                OpenSshDiagnosticCode.ProxyJumpCapturedButNotMapped,
                value));
            return;
        }

        diagnostics.Add(new OpenSshImportDiagnostic(
            OpenSshDiagnosticLevel.Info,
            lineNumber,
            OpenSshDiagnosticCode.UnknownDirectiveIgnored,
            directive));
    }

    private static string ExpandIdentityFile(
        string value,
        string userProfile,
        int lineNumber,
        ICollection<OpenSshImportDiagnostic> diagnostics)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(userProfile) || !value.StartsWith('~'))
        {
            return value;
        }

        if (value.Length > 1 && value[1] is not '/' and not '\\')
        {
            return value;
        }

        var trimmed = value.Length == 1
            ? string.Empty
            : value[1..].TrimStart('/', '\\');
        var expanded = string.IsNullOrEmpty(trimmed)
            ? userProfile
            : Path.Combine(userProfile, trimmed.Replace('/', Path.DirectorySeparatorChar));

        diagnostics.Add(new OpenSshImportDiagnostic(
            OpenSshDiagnosticLevel.Info,
            lineNumber,
            OpenSshDiagnosticCode.IdentityFileTildeExpanded,
            value));

        return expanded;
    }

    private static void FinalizeCurrentBlock(
        HostBlockState? block,
        ICollection<OpenSshImportDiagnostic> diagnostics,
        ICollection<OpenSshImportCandidate> candidates,
        ISet<string> seenAliases)
    {
        if (block is null)
        {
            return;
        }

        foreach (var alias in block.Aliases)
        {
            var hostName = block.HostName;
            if (string.IsNullOrWhiteSpace(hostName))
            {
                hostName = alias;
                diagnostics.Add(new OpenSshImportDiagnostic(
                    OpenSshDiagnosticLevel.Info,
                    block.SourceLineNumber,
                    OpenSshDiagnosticCode.HostNameFallbackToAlias,
                    alias));
            }

            candidates.Add(new OpenSshImportCandidate
            {
                Alias = alias,
                HostName = hostName,
                Port = block.Port,
                User = block.User,
                IdentityFile = block.IdentityFile,
                SourceLineNumber = block.SourceLineNumber
            });
        }
    }

    private static string StripComments(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(line.Length);
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '"')
            {
                inQuotes = !inQuotes;
                builder.Append(current);
                continue;
            }

            if (current == '#' && !inQuotes)
            {
                break;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool TrySplitDirective(string line, out string directive, out string value)
    {
        directive = string.Empty;
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var firstWhitespaceIndex = line.IndexOfAny([' ', '\t']);
        if (firstWhitespaceIndex < 0)
        {
            directive = line.Trim();
            return !string.IsNullOrWhiteSpace(directive);
        }

        directive = line[..firstWhitespaceIndex].Trim();
        value = StripOuterQuotes(line[(firstWhitespaceIndex + 1)..].Trim());
        return !string.IsNullOrWhiteSpace(directive);
    }

    private static string StripOuterQuotes(string value)
    {
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;
    }

    private static IReadOnlyList<string> SplitTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        foreach (var current in value)
        {
            if (current == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(current) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(current);
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    private static bool IsWildcardAlias(string alias)
    {
        return alias.StartsWith('!') || alias.Contains('*') || alias.Contains('?');
    }

    private sealed class HostBlockState(int sourceLineNumber, IReadOnlyList<string> aliases)
    {
        public int SourceLineNumber { get; } = sourceLineNumber;

        public IReadOnlyList<string> Aliases { get; } = aliases;

        public string? HostName { get; set; }

        public int Port { get; set; } = 22;

        public string? User { get; set; }

        public string? IdentityFile { get; set; }
    }
}
