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
using System.IO;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Services;

public partial class ConnectionService
{
    /// <summary>
    /// Launches a Citrix session via storebrowse.exe, SelfService.exe, or a direct .ica file.
    /// The Citrix Workspace App renders the session natively; Heimdall manages the lifecycle.
    /// </summary>
    public async Task<ConnectionResult> ConnectCitrixAsync(
        ServerProfileDto server, AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);
        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingCitrix);

        var launcher = ResolveCitrixLauncher();
        if (launcher is null)
        {
            var msg = _localizer["CitrixWorkspaceNotFound"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        Process? process = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(server.CitrixLaunchCommandLine))
            {
                // Launch via SelfService.exe with pre-authenticated cache arguments
                var selfServicePath = ResolveSelfServicePath();
                if (selfServicePath is null)
                {
                    var msg = _localizer["CitrixWorkspaceNotFound"];
                    _connectionSm.SetError(server.Id, msg);
                    return new ConnectionResult(false, msg, null);
                }

                Core.Logging.FileLogger.Info(
                    $"Citrix launch (SelfService cache): {selfServicePath} {server.CitrixLaunchCommandLine}");

                process = Process.Start(new ProcessStartInfo
                {
                    FileName = selfServicePath,
                    Arguments = server.CitrixLaunchCommandLine,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else if (!string.IsNullOrWhiteSpace(server.CitrixIcaFilePath)
                && File.Exists(server.CitrixIcaFilePath))
            {
                // Direct .ica file launch
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = server.CitrixIcaFilePath,
                    UseShellExecute = true
                });
            }
            else if (!string.IsNullOrWhiteSpace(server.CitrixStoreFrontUrl)
                     && !string.IsNullOrWhiteSpace(server.CitrixAppName))
            {
                // Sanitize user-configurable fields against shell metacharacters (CWE-78).
                if (server.CitrixAppName.AsSpan().IndexOfAny(['|', '&', ';', '`', '$', '\n', '\r']) >= 0
                    || server.CitrixStoreFrontUrl.AsSpan().IndexOfAny(['|', '&', ';', '`', '$', '\n', '\r']) >= 0)
                {
                    var msg = _localizer["CitrixNoConnectionConfigured"];
                    _connectionSm.SetError(server.Id, msg);
                    return new ConnectionResult(false, msg, null);
                }

                // Launch via storebrowse
                var argParts = new List<string> { "-L" };
                if (server.CitrixUseSso)
                    argParts.Add("-S");
                argParts.Add($"\"{server.CitrixAppName}\"");
                argParts.Add($"\"{server.CitrixStoreFrontUrl}\"");
                var args = string.Join(' ', argParts);
                Core.Logging.FileLogger.Info($"Citrix launch: {launcher} {args}");

                process = Process.Start(new ProcessStartInfo
                {
                    FileName = launcher,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                var msg = _localizer["CitrixNoConnectionConfigured"];
                _connectionSm.SetError(server.Id, msg);
                return new ConnectionResult(false, msg, null);
            }

            _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
            return new ConnectionResult(true, null, new CitrixSessionResult(
                process, server.CitrixStoreFrontUrl, server.CitrixAppName));
        }
        catch (Exception ex)
        {
            process?.Dispose();
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }
    }

    /// <summary>
    /// Resolves the Citrix Workspace launcher executable.
    /// Checks standard installation paths for storebrowse.exe and SelfService.exe,
    /// then falls back to PATH resolution.
    /// </summary>
    private static string? ResolveCitrixLauncher()
    {
        var paths = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Citrix", "ICA Client", "storebrowse.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Citrix", "ICA Client", "storebrowse.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Citrix", "ICA Client", "SelfService.exe"),
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return FindInPath("storebrowse.exe") ?? FindInPath("SelfService.exe");
    }

    /// <summary>
    /// Resolves the SelfService.exe path specifically (used for cache-based launch).
    /// </summary>
    private static string? ResolveSelfServicePath()
    {
        var paths = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Citrix", "ICA Client", "SelfServicePlugin", "SelfService.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Citrix", "ICA Client", "SelfService.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Citrix", "ICA Client", "SelfServicePlugin", "SelfService.exe"),
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return FindInPath("SelfService.exe");
    }
}

/// <summary>
/// Wraps a Citrix Workspace process handle for session lifecycle management.
/// </summary>
public record CitrixSessionResult(
    Process? Process,
    string? StoreFrontUrl = null,
    string? AppName = null) : ISessionResult;
