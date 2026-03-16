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

using System.Text.RegularExpressions;

namespace Heimdall.Terminal;

/// <summary>
/// Evaluates clipboard text before pasting into a terminal session.
/// Detects multi-line pastes and dangerous shell commands that could
/// cause data loss or unintended system changes.
/// </summary>
public static partial class SmartPasteGuard
{
    /// <summary>
    /// Risk level of pasted text.
    /// </summary>
    public enum PasteRisk
    {
        /// <summary>Single-line, no dangerous commands detected.</summary>
        Safe,

        /// <summary>Text contains newline characters (implicit command execution).</summary>
        MultiLine,

        /// <summary>Text matches a known destructive command pattern.</summary>
        Dangerous
    }

    /// <summary>
    /// The set of regex patterns used to detect dangerous commands.
    /// Each pattern is case-insensitive and anchored to word boundaries
    /// to reduce false positives on path fragments.
    /// </summary>
    private static readonly (string Label, Regex Pattern)[] DangerousPatterns =
    [
        ("rm -rf",     RmRfRegex()),
        ("rm -fr",     RmFrRegex()),
        ("mkfs",       MkfsRegex()),
        ("dd if=",     DdIfRegex()),
        ("format",     FormatRegex()),
        ("shutdown",   ShutdownRegex()),
        ("reboot",     RebootRegex()),
        ("init 0/6",   InitRegex()),
        ("halt",       HaltRegex()),
        ("poweroff",   PoweroffRegex()),
        ("> /dev/sda", DevSdaRegex()),
        (":(){ :|:&};:", ForkBombRegex()),
        ("chmod -R 777", ChmodRegex()),
        ("chown -R",   ChownRecursiveRegex()),
        ("wget | sh",  WgetPipeRegex()),
        ("curl | sh",  CurlPipeRegex()),
    ];

    /// <summary>
    /// Evaluates the risk level of pasting <paramref name="text"/> into a terminal.
    /// </summary>
    /// <param name="text">The clipboard text to evaluate.</param>
    /// <param name="isProduction">
    /// When true, multi-line pastes are elevated to <see cref="PasteRisk.Dangerous"/>
    /// as an extra safety measure in production environments.
    /// </param>
    /// <returns>The highest risk level detected.</returns>
    public static PasteRisk Evaluate(string text, bool isProduction = false)
    {
        if (string.IsNullOrEmpty(text))
            return PasteRisk.Safe;

        // Check for dangerous commands first (highest priority).
        string normalized = text.Trim();
        foreach (var (_, pattern) in DangerousPatterns)
        {
            if (pattern.IsMatch(normalized))
                return PasteRisk.Dangerous;
        }

        // Check for sudo prefixed dangerous commands.
        if (SudoPrefixRegex().IsMatch(normalized))
        {
            string afterSudo = SudoPrefixRegex().Replace(normalized, string.Empty).TrimStart();
            foreach (var (_, pattern) in DangerousPatterns)
            {
                if (pattern.IsMatch(afterSudo))
                    return PasteRisk.Dangerous;
            }
        }

        // Check for multi-line text (newlines trigger implicit command execution).
        bool hasNewlines = text.Contains('\n') || text.Contains('\r');
        if (hasNewlines)
            return isProduction ? PasteRisk.Dangerous : PasteRisk.MultiLine;

        return PasteRisk.Safe;
    }

    /// <summary>
    /// Returns the human-readable labels of all dangerous command patterns.
    /// Useful for displaying in confirmation dialogs.
    /// </summary>
    public static IReadOnlyList<string> GetDangerousPatterns()
        => DangerousPatterns.Select(p => p.Label).ToArray();

    // ========================================================================
    // Source-generated regex patterns (compiled at build time)
    // ========================================================================

    [GeneratedRegex(@"\brm\s+.*-\w*r\w*f", RegexOptions.IgnoreCase)]
    private static partial Regex RmRfRegex();

    [GeneratedRegex(@"\brm\s+.*-\w*f\w*r", RegexOptions.IgnoreCase)]
    private static partial Regex RmFrRegex();

    [GeneratedRegex(@"\bmkfs\b", RegexOptions.IgnoreCase)]
    private static partial Regex MkfsRegex();

    [GeneratedRegex(@"\bdd\s+if=", RegexOptions.IgnoreCase)]
    private static partial Regex DdIfRegex();

    [GeneratedRegex(@"\bformat\s+[a-z]:", RegexOptions.IgnoreCase)]
    private static partial Regex FormatRegex();

    [GeneratedRegex(@"\bshutdown\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShutdownRegex();

    [GeneratedRegex(@"\breboot\b", RegexOptions.IgnoreCase)]
    private static partial Regex RebootRegex();

    [GeneratedRegex(@"\binit\s+[06]\b", RegexOptions.IgnoreCase)]
    private static partial Regex InitRegex();

    [GeneratedRegex(@"\bhalt\b", RegexOptions.IgnoreCase)]
    private static partial Regex HaltRegex();

    [GeneratedRegex(@"\bpoweroff\b", RegexOptions.IgnoreCase)]
    private static partial Regex PoweroffRegex();

    [GeneratedRegex(@">\s*/dev/sd[a-z]", RegexOptions.IgnoreCase)]
    private static partial Regex DevSdaRegex();

    [GeneratedRegex(@":\(\)\s*\{\s*:\s*\|\s*:\s*&\s*\}\s*;?\s*:", RegexOptions.None)]
    private static partial Regex ForkBombRegex();

    [GeneratedRegex(@"\bchmod\s+.*-\w*R\w*\s+777\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChmodRegex();

    [GeneratedRegex(@"\bchown\s+.*-\w*R\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChownRecursiveRegex();

    [GeneratedRegex(@"\bwget\b.*\|\s*(ba)?sh\b", RegexOptions.IgnoreCase)]
    private static partial Regex WgetPipeRegex();

    [GeneratedRegex(@"\bcurl\b.*\|\s*(ba)?sh\b", RegexOptions.IgnoreCase)]
    private static partial Regex CurlPipeRegex();

    [GeneratedRegex(@"^\s*sudo\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SudoPrefixRegex();
}
