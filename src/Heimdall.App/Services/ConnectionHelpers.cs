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

using System.IO;
using Heimdall.Core.Configuration;
using Heimdall.Core.Security;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Shared static utilities for connection handlers and tunnel orchestration.
/// </summary>
internal static class ConnectionHelpers
{
    /// <summary>
    /// Converts a gateway DTO to SSH connection parameters, decrypting the password if present.
    /// </summary>
    internal static SshConnectionParams CreateGatewayConnectionParams(SshGatewayDto gateway)
    {
        string? password = null;
        if (!string.IsNullOrEmpty(gateway.SshPasswordEncrypted))
        {
            password = DecryptPassword(gateway.SshPasswordEncrypted);
        }

        return new SshConnectionParams
        {
            Host = gateway.Host,
            Port = gateway.Port,
            Username = gateway.User,
            KeyPath = string.IsNullOrWhiteSpace(gateway.KeyPath) ? null : gateway.KeyPath,
            Password = password
        };
    }

    /// <summary>
    /// Resolves the path to plink.exe: uses the user-configured path if valid,
    /// otherwise falls back to the embedded tool copy.
    /// </summary>
    internal static string? ResolvePlinkPath(string? settingsPath)
    {
        if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
        {
            return settingsPath;
        }

        var embeddedPath = Path.Combine(
            AppContext.BaseDirectory,
            AppConstants.EmbeddedToolsSubdir,
            "plink.exe");
        if (File.Exists(embeddedPath))
        {
            Core.Logging.FileLogger.Info($"Using embedded plink: {embeddedPath}");
            return embeddedPath;
        }

        return null;
    }

    /// <summary>
    /// Resolves the path to putty.exe: uses the user-configured path if valid,
    /// falls back to the directory containing plink.exe, then the embedded tool copy.
    /// </summary>
    internal static string? ResolvePuttyPath(string? settingsPath, string? plinkPath)
    {
        if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
        {
            return settingsPath;
        }

        if (!string.IsNullOrWhiteSpace(plinkPath))
        {
            var plinkDir = Path.GetDirectoryName(plinkPath);
            if (!string.IsNullOrEmpty(plinkDir))
            {
                var candidate = Path.Combine(plinkDir, "putty.exe");
                if (File.Exists(candidate))
                {
                    Core.Logging.FileLogger.Info($"Using PuTTY found next to Plink: {candidate}");
                    return candidate;
                }
            }
        }

        var embeddedPath = Path.Combine(
            AppContext.BaseDirectory,
            AppConstants.EmbeddedToolsSubdir,
            "putty.exe");
        if (File.Exists(embeddedPath))
        {
            Core.Logging.FileLogger.Info($"Using embedded PuTTY: {embeddedPath}");
            return embeddedPath;
        }

        return null;
    }

    /// <summary>
    /// Decrypts a credential string. Supports both HMAC-protected and
    /// legacy DPAPI-only formats via <see cref="CredentialProtector"/>.
    /// </summary>
    internal static string? DecryptPassword(string? encryptedValue)
    {
        return CredentialProtector.Unprotect(encryptedValue);
    }

    /// <summary>
    /// Searches the PATH environment variable for an executable.
    /// Returns the first matching full path or null when not found.
    /// </summary>
    internal static string? FindInPath(string executableName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var fullPath = Path.Combine(dir, executableName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}
