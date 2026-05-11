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

namespace Heimdall.App.ViewModels;

internal static class SidebarDisplayNameFormatter
{
    internal const int DefaultMaxLength = MaxLength;

    private const int MaxLength = 40;
    private const int MinSuffixBudget = 6;
    private const char Ellipsis = '\u2026';

    internal static string? Format(string? displayName, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        var trimmedName = displayName.Trim();
        if (maxLength < MinSuffixBudget || trimmedName.Length <= maxLength)
        {
            return displayName;
        }

        if (!TrySplitFinalParenthesizedSuffix(trimmedName, out var head, out var suffix))
        {
            return displayName;
        }

        if (head.Length > maxLength)
        {
            return displayName;
        }

        var available = maxLength - head.Length - 3;
        if (available < MinSuffixBudget)
        {
            return head;
        }

        var suffixLength = Math.Min(suffix.Length, available - 1);
        var suffixText = suffix[..suffixLength].TrimEnd();
        return $"{head} ({suffixText}{Ellipsis})";
    }

    private static bool TrySplitFinalParenthesizedSuffix(
        string displayName,
        out string baseText,
        out string suffix)
    {
        baseText = "";
        suffix = "";

        if (displayName.Length == 0 || displayName[^1] != ')')
        {
            return false;
        }

        var depth = 0;
        for (var index = displayName.Length - 1; index >= 0; index--)
        {
            var character = displayName[index];
            if (character == ')')
            {
                depth++;
                continue;
            }

            if (character != '(')
            {
                continue;
            }

            depth--;
            if (depth < 0)
            {
                return false;
            }

            if (depth != 0)
            {
                continue;
            }

            baseText = displayName[..index].TrimEnd();
            suffix = displayName[(index + 1)..^1].Trim();
            return baseText.Length > 0 && suffix.Length > 0;
        }

        return false;
    }
}
