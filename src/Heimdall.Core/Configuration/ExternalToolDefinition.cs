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

namespace Heimdall.Core.Configuration;

/// <summary>
/// Defines an external tool that can be launched from the server context menu.
/// Supports variable placeholders in <see cref="Arguments"/>:
/// <c>{Host}</c>, <c>{Port}</c>, <c>{User}</c>, <c>{ServerName}</c>,
/// <c>{Protocol}</c>, <c>{KeyFile}</c>, <c>{Project}</c>, <c>{Gateway}</c>.
/// </summary>
public class ExternalToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string? IconGlyph { get; set; }

    /// <summary>Working directory for the launched process. Empty = inherit from parent.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Launch with elevated privileges (UAC prompt on Windows).</summary>
    public bool RunAsAdministrator { get; set; }

    /// <summary>Run the process hidden (no console window flash for scripts).</summary>
    public bool RunHidden { get; set; }

    /// <summary>
    /// Supported placeholder variables and their descriptions.
    /// </summary>
    public static readonly (string Variable, string DescriptionKey)[] SupportedPlaceholders =
    [
        ("{Host}",       "ExtToolVarHost"),
        ("{Port}",       "ExtToolVarPort"),
        ("{User}",       "ExtToolVarUser"),
        ("{ServerName}", "ExtToolVarServerName"),
        ("{Protocol}",   "ExtToolVarProtocol"),
        ("{KeyFile}",    "ExtToolVarKeyFile"),
        ("{Project}",    "ExtToolVarProject"),
        ("{Gateway}",    "ExtToolVarGateway"),
    ];

    /// <summary>
    /// Replaces variable placeholders in <see cref="Arguments"/> with actual server values.
    /// Values are sanitized to prevent OS command injection (CWE-78).
    /// </summary>
    public string ResolveArguments(
        string host,
        int port,
        string user,
        string? serverName = null,
        string? protocol = null,
        string? keyFile = null,
        string? project = null,
        string? gateway = null)
    {
        return Arguments
            .Replace("{Host}", SanitizeValue(host), StringComparison.OrdinalIgnoreCase)
            .Replace("{Port}", port.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{User}", SanitizeValue(user), StringComparison.OrdinalIgnoreCase)
            .Replace("{ServerName}", SanitizeValue(serverName ?? ""), StringComparison.OrdinalIgnoreCase)
            .Replace("{Protocol}", SanitizeValue(protocol ?? ""), StringComparison.OrdinalIgnoreCase)
            .Replace("{KeyFile}", SanitizeValue(keyFile ?? ""), StringComparison.OrdinalIgnoreCase)
            .Replace("{Project}", SanitizeValue(project ?? ""), StringComparison.OrdinalIgnoreCase)
            .Replace("{Gateway}", SanitizeValue(gateway ?? ""), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips shell metacharacters that could be used for command injection
    /// when arguments are passed through the Windows shell.
    /// </summary>
    private static string SanitizeValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Remove characters that are dangerous in Windows shell context
        return System.Text.RegularExpressions.Regex.Replace(value, @"[;&|`$<>()!""'\r\n%^]", "");
    }
}
