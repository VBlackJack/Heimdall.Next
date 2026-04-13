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

namespace Heimdall.Ssh;

/// <summary>
/// Result of an authentication pre-flight check.
/// </summary>
/// <param name="Success">Whether all pre-flight checks passed.</param>
/// <param name="FailureCode">Failure code if checks failed.</param>
/// <param name="Message">Human-readable failure description.</param>
public sealed record PreflightResult(bool Success, SshFailureCode? FailureCode, string? Message)
{
    /// <summary>Create a passing result.</summary>
    public static PreflightResult Ok() => new(true, null, null);

    /// <summary>Create a failing result with a specific code and message.</summary>
    public static PreflightResult Fail(SshFailureCode code, string message) => new(false, code, message);
}

/// <summary>
/// Validates SSH connection prerequisites before attempting a connection.
/// Checks key file existence, Pageant availability, and auth method availability.
/// </summary>
public static class AuthPreflightChecker
{
    /// <summary>
    /// Run pre-flight checks against the given connection parameters.
    /// </summary>
    /// <param name="connectionParams">SSH connection parameters to validate.</param>
    /// <param name="isTunnelMode">
    /// Whether the connection is a background tunnel (non-interactive).
    /// Tunnel mode requires Pageant for key-based auth without a password,
    /// because there is no interactive passphrase prompt.
    /// </param>
    /// <returns>A <see cref="PreflightResult"/> indicating pass or fail with details.</returns>
    public static PreflightResult Check(SshConnectionParams connectionParams, bool isTunnelMode = false)
    {
        ArgumentNullException.ThrowIfNull(connectionParams);

        // Key file specified but does not exist on disk
        if (!string.IsNullOrEmpty(connectionParams.KeyPath) && !File.Exists(connectionParams.KeyPath))
        {
            return PreflightResult.Fail(
                SshFailureCode.KeyFileNotFound,
                $"Key file not found: {connectionParams.KeyPath}");
        }

        // Tunnel mode: key specified but no password — needs Pageant for passphrase
        if (isTunnelMode
            && !string.IsNullOrEmpty(connectionParams.KeyPath)
            && string.IsNullOrEmpty(connectionParams.Password))
        {
            var pageantCheck = CheckPageantAvailability();
            if (pageantCheck is not null)
                return pageantCheck;
        }

        // Tunnel mode: no key and no password — needs Pageant as sole auth source
        if (isTunnelMode
            && string.IsNullOrEmpty(connectionParams.KeyPath)
            && string.IsNullOrEmpty(connectionParams.Password))
        {
            var pageantCheck = CheckPageantAvailability();
            if (pageantCheck is not null)
                return pageantCheck;
        }

        return PreflightResult.Ok();
    }

    /// <summary>
    /// Check whether the Pageant SSH agent process is running.
    /// </summary>
    public static bool IsPageantRunning()
    {
        try
        {
            return Process.GetProcessesByName("pageant").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies that Pageant is running and has at least one identity loaded.
    /// Returns a failure result if not, or null if Pageant is ready.
    /// </summary>
    private static PreflightResult? CheckPageantAvailability()
    {
        if (!IsPageantRunning())
        {
            return PreflightResult.Fail(
                SshFailureCode.PageantKeyUnavailable,
                "Pageant not running. Start Pageant and load the key, or configure a password.");
        }

        try
        {
            var client = new Pageant.PageantClient();
            var identities = client.GetIdentities();
            if (identities.Count == 0)
            {
                return PreflightResult.Fail(
                    SshFailureCode.PageantNoIdentities,
                    "Pageant is running but has no keys loaded. Load a key in Pageant before connecting.");
            }
        }
        catch
        {
            // Pageant IPC failed — fall through and let the connection attempt handle it
        }

        return null;
    }
}
