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

namespace TwinShell.Core.Utilities;

public static class GitUrlValidator
{
    private static readonly Regex ScpLikeSyntaxRegex = new(
        @"^[^@/\s]+@[^:/\s]+:.+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsAllowed(string? url, bool hasToken, out string failureReason)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            failureReason = "Remote URL is not configured.";
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            string scheme = uri.Scheme.ToLowerInvariant();
            switch (scheme)
            {
                case "https":
                case "ssh":
                case "git":
                    failureReason = string.Empty;
                    return true;
                case "http":
                    if (hasToken)
                    {
                        failureReason = "Refusing to send the access token over cleartext HTTP. Use HTTPS or SSH.";
                        return false;
                    }

                    failureReason = string.Empty;
                    return true;
                case "file":
                    failureReason = "The file:// scheme is not allowed for the Git remote.";
                    return false;
                default:
                    failureReason = $"Unsupported Git remote scheme: {uri.Scheme}.";
                    return false;
            }
        }

        if (ScpLikeSyntaxRegex.IsMatch(url))
        {
            failureReason = string.Empty;
            return true;
        }

        failureReason = "The Git remote URL is not a valid URL.";
        return false;
    }
}
