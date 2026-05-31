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
using System.Text.RegularExpressions;

namespace TwinShell.Core.Utilities;

public static class GitUrlSanitizer
{
    private const string NonePlaceholder = "(none)";

    private static readonly Regex LeadingUserInfoRegex = new(
        @"^[^/@\s]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmbeddedCredentialsRegex = new(
        @"([a-zA-Z][a-zA-Z0-9+.\-]*://)[^/@\s]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string SanitizeForLogging(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return NonePlaceholder;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                string host = FormatHost(uri);
                string port = uri.IsDefaultPort
                    ? string.Empty
                    : string.Create(CultureInfo.InvariantCulture, $":{uri.Port}");

                return $"{uri.Scheme}://{host}{port}{uri.AbsolutePath}";
            }

            return url;
        }

        return LeadingUserInfoRegex.Replace(url, string.Empty, 1);
    }

    public static string? RedactCredentials(string? text)
    {
        if (text == null || text.Length == 0)
        {
            return text;
        }

        return EmbeddedCredentialsRegex.Replace(text, "$1");
    }

    private static string FormatHost(Uri uri)
    {
        string host = uri.Host;

        if (host.Contains(':', StringComparison.Ordinal) &&
            !host.StartsWith("[", StringComparison.Ordinal) &&
            !host.EndsWith("]", StringComparison.Ordinal))
        {
            return $"[{host}]";
        }

        return host;
    }
}
