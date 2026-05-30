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
    private readonly TimeSpan _commandTimeout;

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
    /// <param name="timeoutMs">Command execution timeout in milliseconds.</param>
    public CommandCredentialProvider(string? commandTemplate, string? databasePath, int timeoutMs = 10000)
    {
        _commandTemplate = commandTemplate;
        _databasePath = databasePath;
        _commandTimeout = TimeSpan.FromMilliseconds(timeoutMs);
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
            linkedCts.CancelAfter(_commandTimeout);

            // Drain stderr concurrently to prevent pipe buffer deadlock (4 KB limit on Windows).
            // Not logged - external credential tools may echo credential fragments to stderr.
            Task<string> stderrDrain = process.StandardError.ReadToEndAsync(linkedCts.Token);

            string stdout;
            try
            {
                stdout = await process.StandardOutput.ReadToEndAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Any timeout/cancellation - during the stdout read OR the exit wait - must kill the external
                // process before `using` disposes it (Dispose does not terminate the process), otherwise a hung
                // credential tool is left orphaned holding its database lock.
                TryKillProcess(process);
                throw;
            }
            finally
            {
                // Observe the stderr drain so a cancelled/closed pipe never surfaces as an unobserved task
                // exception. Its content is intentionally discarded (it may contain credential fragments).
                await ObserveSilentlyAsync(stderrDrain).ConfigureAwait(false);
            }

            if (process.ExitCode != 0)
            {
                // Log only exit code — stderr may contain credential fragments from
                // the external tool and must not be persisted to log files.
                Logging.FileLogger.Warn(
                    $"CommandCredentialProvider: command exited with code {process.ExitCode}");
                return null;
            }

            string password = stdout.Trim();
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
    /// Sanitization is context-aware: the executable is extracted from the
    /// raw template to determine whether a shell interpreter is the target.
    /// Shell targets (cmd.exe, .bat, .cmd, PowerShell, WSL, WSH) get strict
    /// stripping; regular executables (keepassxc-cli, bw, op) get relaxed
    /// stripping that preserves parentheses, single quotes, and percent signs
    /// in legitimate values (double quotes are always stripped).
    /// </summary>
    internal string ExpandTemplate(
        string template,
        string host,
        int port,
        string? username,
        string? title)
    {
        var (executable, _) = SplitCommand(template);
        Func<string?, string> sanitize = InputValidator.IsShellTarget(executable)
            ? SanitizeStrict
            : SanitizeRelaxed;

        return template
            .Replace("{Host}", sanitize(host), StringComparison.OrdinalIgnoreCase)
            .Replace("{Port}", port.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{User}", sanitize(username), StringComparison.OrdinalIgnoreCase)
            .Replace("{Title}", sanitize(title), StringComparison.OrdinalIgnoreCase)
            .Replace("{Database}", sanitize(_databasePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips all shell metacharacters — used when the template targets a shell
    /// interpreter (cmd.exe, .bat, .cmd, PowerShell) or when unknown.
    /// </summary>
    private static string SanitizeStrict(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(value, @"[;&|`$<>()!""'\r\n%^]", "");
    }

    /// <summary>
    /// Strips only characters that affect MSVC CRT argument parsing (double
    /// quotes) or could chain/redirect process execution. Preserves
    /// parentheses, single quotes, percent signs, etc. Double quotes are
    /// still stripped as they control argument boundary parsing in all
    /// Windows executables. Used when the template targets a regular executable.
    /// </summary>
    private static string SanitizeRelaxed(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(value, @"[;&|`$<>""\r\n]", "");
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

    /// <summary>
    /// Awaits a background task and swallows any exception, so a cancelled or closed pipe never
    /// surfaces as an unobserved task exception. Used to observe the stderr drain.
    /// </summary>
    private static async Task ObserveSilentlyAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: the stderr drain may be cancelled or the pipe closed when the process is killed.
        }
    }
}
