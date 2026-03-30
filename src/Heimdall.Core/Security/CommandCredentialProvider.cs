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

using System.Diagnostics;

namespace Heimdall.Core.Security;

/// <summary>
/// Credential provider that executes an external CLI command to retrieve passwords.
/// Works with any CLI-based password manager: KeePassXC, Bitwarden CLI, 1Password CLI, pass, etc.
/// </summary>
/// <remarks>
/// The command template supports the following placeholders:
/// <list type="bullet">
///   <item><c>{Host}</c> — Target server hostname or IP</item>
///   <item><c>{Port}</c> — Target port number</item>
///   <item><c>{User}</c> — Username hint</item>
///   <item><c>{Title}</c> — Server display name / entry title</item>
///   <item><c>{Database}</c> — Configured database path</item>
/// </list>
/// The command's stdout is captured and trimmed as the password.
/// </remarks>
public sealed class CommandCredentialProvider : ICredentialProvider
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    private readonly string? _commandTemplate;
    private readonly string? _databasePath;

    /// <summary>
    /// Initializes the provider with the command template and optional database path.
    /// </summary>
    /// <param name="commandTemplate">
    /// The full command with placeholders. Example:
    /// <c>keepassxc-cli show -s -a Password "{Database}" "{Title}"</c>
    /// </param>
    /// <param name="databasePath">
    /// Path to the password database file (replaces <c>{Database}</c>).
    /// </param>
    public CommandCredentialProvider(string? commandTemplate, string? databasePath)
    {
        _commandTemplate = commandTemplate;
        _databasePath = databasePath;
    }

    /// <inheritdoc />
    public string Name => "Command";

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_commandTemplate);

    /// <inheritdoc />
    public async Task<CredentialResult?> GetCredentialAsync(
        string serverHost,
        int port,
        string? username,
        string? title,
        CancellationToken ct = default)
    {
        if (!IsAvailable || _commandTemplate is null)
        {
            return null;
        }

        var expandedCommand = ExpandTemplate(_commandTemplate, serverHost, port, username, title);
        var (executable, arguments) = SplitCommand(expandedCommand);

        Logging.FileLogger.Info($"CommandCredentialProvider: executing command for host={serverHost}");

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.Start();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(CommandTimeout);

            // Drain stderr concurrently to prevent pipe buffer deadlock (4 KB limit on Windows).
            // Not logged — external credential tools may echo credential fragments to stderr.
            _ = process.StandardError.ReadToEndAsync(linkedCts.Token);

            var stdout = await process.StandardOutput.ReadToEndAsync(linkedCts.Token)
                .ConfigureAwait(false);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                throw;
            }

            if (process.ExitCode != 0)
            {
                // Log only exit code — stderr may contain credential fragments from
                // the external tool and must not be persisted to log files.
                Logging.FileLogger.Warn(
                    $"CommandCredentialProvider: command exited with code {process.ExitCode}");
                return null;
            }

            var password = stdout.Trim();
            if (string.IsNullOrEmpty(password))
            {
                Logging.FileLogger.Warn("CommandCredentialProvider: command returned empty output");
                return null;
            }

            // Return the provided username (or empty) with the retrieved password
            return new CredentialResult(username ?? string.Empty, password);
        }
        catch (OperationCanceledException)
        {
            Logging.FileLogger.Warn("CommandCredentialProvider: command timed out");
            throw;
        }
        catch (Exception ex)
        {
            Logging.FileLogger.Warn($"CommandCredentialProvider: failed to execute command: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Replaces placeholders in the command template with actual values.
    /// Values are sanitized to prevent argument injection even though
    /// UseShellExecute is false (CWE-78 defense in depth).
    /// </summary>
    private string ExpandTemplate(
        string template,
        string host,
        int port,
        string? username,
        string? title)
    {
        return template
            .Replace("{Host}", SanitizeArgValue(host), StringComparison.OrdinalIgnoreCase)
            .Replace("{Port}", port.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{User}", SanitizeArgValue(username), StringComparison.OrdinalIgnoreCase)
            .Replace("{Title}", SanitizeArgValue(title), StringComparison.OrdinalIgnoreCase)
            .Replace("{Database}", SanitizeArgValue(_databasePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips shell metacharacters from a placeholder value to prevent injection.
    /// </summary>
    private static string SanitizeArgValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(value, @"[;&|`$<>()!""'\r\n%^]", "");
    }

    /// <summary>
    /// Splits a command line into executable and arguments, respecting quoted paths.
    /// </summary>
    private static (string Executable, string Arguments) SplitCommand(string commandLine)
    {
        var trimmed = commandLine.Trim();

        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 0)
            {
                var exe = trimmed[1..endQuote];
                var args = endQuote + 1 < trimmed.Length
                    ? trimmed[(endQuote + 1)..].TrimStart()
                    : string.Empty;
                return (exe, args);
            }
        }

        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return (trimmed, string.Empty);
        }

        return (trimmed[..spaceIndex], trimmed[(spaceIndex + 1)..]);
    }

    /// <summary>
    /// Attempts to kill a process that has timed out.
    /// </summary>
    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
