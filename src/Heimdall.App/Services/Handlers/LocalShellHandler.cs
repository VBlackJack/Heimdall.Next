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
using Heimdall.Core.Security;
using Heimdall.Core.StateMachine;

namespace Heimdall.App.Services.Handlers;

/// <summary>
/// Handles local shell connection logic.
/// </summary>
internal sealed class LocalShellHandler : IProtocolHandler
{
    private readonly ConnectionStateMachine _connectionSm;

    public LocalShellHandler(ConnectionStateMachine connectionSm)
    {
        _connectionSm = connectionSm;
    }

    public string Protocol => "LOCAL";

    /// <summary>
    /// Launches a local shell session (PowerShell, cmd, bash, etc.) via ConPTY.
    /// </summary>
    public async Task<ConnectionResult> ConnectAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct,
        RdpModeOverride rdpModeOverride = RdpModeOverride.UseProfile)
    {
        ArgumentNullException.ThrowIfNull(server);

        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);
        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingLocal);

        var executable = server.LocalShellExecutable ?? "powershell.exe";
        var arguments = server.LocalShellArguments ?? string.Empty;

        if (executable.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
            executable.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            var policyPrefix = string.Empty;
            if (!string.Equals(settings.PowerShellExecutionPolicy, "Default", StringComparison.OrdinalIgnoreCase) &&
                InputValidator.IsValidExecutionPolicy(settings.PowerShellExecutionPolicy))
            {
                policyPrefix = $"-ExecutionPolicy {settings.PowerShellExecutionPolicy}";
            }

            var noLogo = string.Empty;
            if (!arguments.Contains("-NoLogo", StringComparison.OrdinalIgnoreCase))
            {
                noLogo = "-NoLogo";
            }

            var prefix = string.Join(" ", new[] { policyPrefix, noLogo }.Where(s => !string.IsNullOrEmpty(s)));
            if (!string.IsNullOrEmpty(prefix))
            {
                arguments = string.IsNullOrWhiteSpace(arguments) ? prefix : $"{prefix} {arguments}";
            }
        }

        var workingDir = !string.IsNullOrWhiteSpace(server.LocalShellWorkingDirectory) &&
                         Directory.Exists(server.LocalShellWorkingDirectory)
            ? server.LocalShellWorkingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var elevationMode = server.EffectiveElevationMode;
        var originalExe = executable;
        var originalArgs = arguments;
        var usedGsudo = false;

        if (elevationMode == ElevationMode.Runas)
        {
            return LaunchExternalElevated(server, executable, arguments, workingDir);
        }

        if (elevationMode is ElevationMode.Auto or ElevationMode.Gsudo)
        {
            var elevationWrapper = ResolveElevationWrapper();
            if (elevationWrapper is not null)
            {
                var directFlag = Path.GetFileNameWithoutExtension(elevationWrapper)
                    .Equals("gsudo", StringComparison.OrdinalIgnoreCase) ? "--direct " : string.Empty;
                var quotedExe = $"\"{executable}\"";
                arguments = string.IsNullOrWhiteSpace(arguments)
                    ? $"{directFlag}{quotedExe}"
                    : $"{directFlag}{quotedExe} {arguments}";
                executable = elevationWrapper;
                usedGsudo = true;
                Core.Logging.FileLogger.Info(
                    $"Elevation via {Path.GetFileName(elevationWrapper)}: {executable} {arguments}");
            }
            else if (elevationMode == ElevationMode.Gsudo)
            {
                Core.Logging.FileLogger.Warn("gsudo mode requested but gsudo not found.");
                _connectionSm.SetError(server.Id, "gsudo not found");
                return new ConnectionResult(
                    false,
                    "gsudo not found. Install gsudo or switch elevation mode to Auto.",
                    null);
            }
            else
            {
                Core.Logging.FileLogger.Warn(
                    "Elevation requested but neither gsudo nor sudo found. Running without elevation.");
            }
        }

        Core.Logging.FileLogger.Info($"Launching local shell: {executable} {arguments} (cwd={workingDir})");

        Heimdall.Terminal.ITerminalSession session;
        if (Heimdall.Terminal.ConPty.ConPtySession.IsAvailable)
        {
            session = new Heimdall.Terminal.ConPty.ConPtySession();
        }
        else
        {
            session = new Heimdall.Terminal.PipeModeSession();
        }

        session.EnvironmentVariables = BuildContextEnvironment(server);

        try
        {
            await session.StartAsync(executable, arguments, workingDirectory: workingDir)
                .ConfigureAwait(false);
            Core.Logging.FileLogger.Info(
                $"Local shell started: PID={session.ProcessId} via {(session is Heimdall.Terminal.ConPty.ConPtySession ? "ConPTY" : "PipeMode")}");
        }
        catch (Exception ex) when (usedGsudo && elevationMode == ElevationMode.Auto)
        {
            session.Dispose();
            Core.Logging.FileLogger.Warn(
                $"gsudo elevation failed ({ex.Message}), falling back to external elevated window");
            return LaunchExternalElevated(server, originalExe, originalArgs, workingDir);
        }
        catch (Exception ex)
        {
            session.Dispose();
            Core.Logging.FileLogger.Error("Local shell launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
        return new ConnectionResult(
            true,
            null,
            new LocalShellBundle(session, workingDir, executable, server.LocalShellElevated));
    }

    /// <summary>
    /// Launches an elevated shell in a separate window via ShellExecute "runas" verb.
    /// </summary>
    private ConnectionResult LaunchExternalElevated(
        ServerProfileDto server,
        string executable,
        string arguments,
        string workingDir)
    {
        Core.Logging.FileLogger.Info(
            $"Launching external elevated shell via runas: {executable} {arguments} (cwd={workingDir})");

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = workingDir
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                _connectionSm.SetError(server.Id, "Failed to start elevated process");
                return new ConnectionResult(false, "Failed to start elevated process", null);
            }

            Core.Logging.FileLogger.Info($"External elevated shell started: PID={process.Id}");

            _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
            return new ConnectionResult(
                true,
                null,
                new LocalShellBundle(null, workingDir, executable, true, ExternalProcessId: process.Id));
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Core.Logging.FileLogger.Info("User cancelled elevation prompt");
            _connectionSm.SetError(server.Id, "Elevation cancelled by user");
            return new ConnectionResult(false, "Elevation cancelled by user", null);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error("External elevated shell launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }
    }

    /// <summary>
    /// Finds an elevation wrapper (gsudo or Windows 11 sudo) for running local shells as Administrator.
    /// </summary>
    private static string? ResolveElevationWrapper()
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, AppConstants.EmbeddedToolsSubdir, "gsudo.exe");
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        try
        {
            var gsudoPath = ConnectionHelpers.FindInPath("gsudo.exe");
            if (gsudoPath is not null)
            {
                return gsudoPath;
            }
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"[LocalShellHandler] gsudo PATH lookup: {ex.Message}");
        }

        try
        {
            var sudoPath = ConnectionHelpers.FindInPath("sudo.exe");
            if (sudoPath is not null)
            {
                return sudoPath;
            }
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"[LocalShellHandler] sudo PATH lookup: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Builds a dictionary of HEIMDALL_* environment variables from the server profile.
    /// </summary>
    private static Dictionary<string, string>? BuildContextEnvironment(ServerProfileDto server)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(server.RemoteServer))
        {
            env["HEIMDALL_HOST"] = server.RemoteServer;
        }

        if (!string.IsNullOrWhiteSpace(server.DisplayName))
        {
            env["HEIMDALL_NAME"] = server.DisplayName;
        }

        if (server.RemotePort > 0)
        {
            env["HEIMDALL_PORT"] = server.RemotePort.ToString();
        }

        if (!string.IsNullOrWhiteSpace(server.SshUsername))
        {
            env["HEIMDALL_USER"] = server.SshUsername;
        }
        else if (!string.IsNullOrWhiteSpace(server.RdpUsername))
        {
            env["HEIMDALL_USER"] = server.RdpUsername;
        }

        if (!string.IsNullOrWhiteSpace(server.ConnectionType))
        {
            env["HEIMDALL_TYPE"] = server.ConnectionType;
        }

        if (!string.IsNullOrWhiteSpace(server.Group))
        {
            env["HEIMDALL_GROUP"] = server.Group;
        }

        if (!string.IsNullOrWhiteSpace(server.Environment))
        {
            env["HEIMDALL_ENV"] = server.Environment;
        }

        return env.Count > 0 ? env : null;
    }
}
