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

using System.Net;
using System.Text.Json;

namespace Heimdall.App.Services;

internal static class TerminalHtmlLocalizer
{
    // English fallbacks used when the localize callback returns null, whitespace,
    // or the raw key, which is how LocalizationManager signals a missing entry.
    // These constants must stay in sync with the marker text in terminal.html.
    internal const string FallbackLoadingLabel = "Initializing terminal\u2026";
    internal const string FallbackSearchPlaceholder = "Search...";
    internal const string FallbackSessionEnded = "--- Session ended ---";

    internal const string KeyLoadingLabel = "TerminalLoadingLabel";
    internal const string KeySearchPlaceholder = "TerminalSearchPlaceholder";
    internal const string KeySessionEnded = "TerminalSessionEnded";

    internal const string MarkerLoadingLabel = "Initializing terminal&#x2026;";
    internal const string MarkerSearchPlaceholder = "Search...";
    internal const string MarkerSessionEndedLiteral = "/*{{TERMINAL_SESSION_ENDED_LITERAL}}*/";

    public static string Localize(string html, Func<string, string?> localize)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(localize);

        string loadingLabel = ResolveOrFallback(localize, KeyLoadingLabel, FallbackLoadingLabel);
        string searchPlaceholder = ResolveOrFallback(localize, KeySearchPlaceholder, FallbackSearchPlaceholder);
        string sessionEnded = ResolveOrFallback(localize, KeySessionEnded, FallbackSessionEnded);

        string localizedHtml = html.Replace(
            MarkerLoadingLabel,
            WebUtility.HtmlEncode(loadingLabel),
            StringComparison.Ordinal);
        localizedHtml = localizedHtml.Replace(
            MarkerSearchPlaceholder,
            WebUtility.HtmlEncode(searchPlaceholder),
            StringComparison.Ordinal);
        localizedHtml = localizedHtml.Replace(
            MarkerSessionEndedLiteral,
            JsonSerializer.Serialize(sessionEnded),
            StringComparison.Ordinal);

        return localizedHtml;
    }

    private static string ResolveOrFallback(
        Func<string, string?> localize,
        string key,
        string fallback)
    {
        string? value = localize(key);
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, key, StringComparison.Ordinal))
        {
            return fallback;
        }

        return value;
    }
}
