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
using Heimdall.Core.Models;

namespace Heimdall.App.Services;

public partial class ConnectionService
{
    /// <summary>
    /// Launches a local shell session (PowerShell, cmd, bash, etc.) via ConPTY.
    /// Returns an <see cref="Heimdall.Terminal.ITerminalSession"/> on success.
    /// </summary>
    public async Task<ConnectionResult> ConnectLocalShellAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.ValidatingConfig);
        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.LaunchingLocal);

        string executable = server.LocalShellExecutable ?? "powershell.exe";
        string arguments = server.LocalShellArguments ?? "";
        string workingDir = !string.IsNullOrWhiteSpace(server.LocalShellWorkingDirectory)
            && Directory.Exists(server.LocalShellWorkingDirectory)
            ? server.LocalShellWorkingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Elevation: wrap command with gsudo or Windows 11 sudo
        if (server.LocalShellElevated)
        {
            var elevationWrapper = ResolveElevationWrapper();
            if (elevationWrapper is not null)
            {
                // Use structured quoting — executable is resolved from PATH or config,
                // arguments are passed through as-is (user-configurable shell args)
                var quotedExe = $"\"{executable}\"";
                arguments = string.IsNullOrWhiteSpace(arguments)
                    ? quotedExe
                    : $"{quotedExe} {arguments}";
                executable = elevationWrapper;
                Core.Logging.FileLogger.Info(
                    $"Elevation via {Path.GetFileName(elevationWrapper)}: {executable} {arguments}");
            }
            else
            {
                Core.Logging.FileLogger.Warn(
                    "Elevation requested but neither gsudo nor sudo found. Running without elevation.");
            }
        }

        Core.Logging.FileLogger.Info(
            $"Launching local shell: {executable} {arguments} (cwd={workingDir})");

        Heimdall.Terminal.ITerminalSession session;

        if (Heimdall.Terminal.ConPty.ConPtySession.IsAvailable)
        {
            session = new Heimdall.Terminal.ConPty.ConPtySession();
        }
        else
        {
            session = new Heimdall.Terminal.PipeModeSession();
        }

        try
        {
            await session.StartAsync(executable, arguments, workingDirectory: workingDir);
            Core.Logging.FileLogger.Info(
                $"Local shell started: PID={session.ProcessId} via {(session is Heimdall.Terminal.ConPty.ConPtySession ? "ConPTY" : "PipeMode")}");
        }
        catch (Exception ex)
        {
            session.Dispose();
            Core.Logging.FileLogger.Error("Local shell launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);
        return new ConnectionResult(true, null, new LocalShellBundle(session, workingDir, executable));
    }

    /// <summary>
    /// Finds an elevation wrapper (gsudo or Windows 11 sudo) for running
    /// local shells as Administrator within ConPTY/PipeMode.
    /// Returns null if neither is available.
    /// </summary>
    private static string? ResolveElevationWrapper()
    {
        // gsudo (open source, MIT): https://github.com/gerardog/gsudo
        // Works with ConPTY/PipeMode — wraps the child process with elevation
        try
        {
            var gsudoPath = FindInPath("gsudo.exe");
            if (gsudoPath is not null)
            {
                return gsudoPath;
            }
        }
        catch { }

        // Windows 11 24H2+ native sudo
        try
        {
            var sudoPath = FindInPath("sudo.exe");
            if (sudoPath is not null)
            {
                return sudoPath;
            }
        }
        catch { }

        return null;
    }

    private static string? FindInPath(string executableName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(dir, executableName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
        return null;
    }
}
