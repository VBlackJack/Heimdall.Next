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
using System.Net;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;

namespace Heimdall.App.Services.Handlers;

/// <summary>
/// Handles SSH connection logic.
/// </summary>
internal sealed class SshHandler : IProtocolHandler
{
    private readonly ITunnelService _tunnelService;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;
    private readonly HostKeyStore _hostKeyStore;
    private readonly X11ServerManager _x11ServerManager;

    internal Action<string>? SetStatusText { get; set; }

    public SshHandler(
        ITunnelService tunnelService,
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer,
        HostKeyStore hostKeyStore,
        X11ServerManager x11ServerManager)
    {
        _tunnelService = tunnelService;
        _connectionSm = connectionSm;
        _localizer = localizer;
        _hostKeyStore = hostKeyStore;
        _x11ServerManager = x11ServerManager;
    }

    public string Protocol => "SSH";

    /// <summary>
    /// Establishes an SSH shell connection, optionally through a tunnel.
    /// Returns a connected <see cref="SshShellSession"/> on success.
    /// </summary>
    public async Task<ConnectionResult> ConnectAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        Core.Logging.FileLogger.Info(
            $"ConnectSshAsync: {server.DisplayName} ({server.RemoteServer}:{server.SshPort}) Gateway={server.SshGatewayId ?? "none"}");
        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);

        var (tunnelOk, _, targetHost, targetPort, tunnelError) =
            await _tunnelService.SetupTunnelIfNeededAsync(server, server.SshPort, settings, ct)
                .ConfigureAwait(false);

        if (!tunnelOk)
        {
            return new ConnectionResult(
                false,
                tunnelError,
                null,
                SshSessionDiagnosticFactory.CreateGatewayFailure(tunnelError));
        }

        if (server.SshX11Forwarding)
        {
            await _x11ServerManager.EnsureRunningAsync().ConfigureAwait(false);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingSsh);

        var sshMode = server.SshMode ?? "Embedded";
        Core.Logging.FileLogger.Info($"SSH mode: {sshMode}");

        if (string.Equals(sshMode, "External", StringComparison.OrdinalIgnoreCase))
        {
            return ConnectSshExternal(server, settings, targetHost, targetPort);
        }

        var sshParams = new SshConnectionParams
        {
            Host = targetHost,
            Port = targetPort,
            Username = server.SshUsername ?? string.Empty,
            Password = ConnectionHelpers.DecryptPassword(server.SshPasswordEncrypted),
            KeyPath = string.IsNullOrWhiteSpace(server.SshKeyPath) ? null : server.SshKeyPath,
            AgentForwarding = server.SshAgentForwarding,
            Compression = server.SshCompression,
            X11Forwarding = server.SshX11Forwarding
        };

        if (SshConnectionFactory.RequiresPageantFallback(sshParams))
        {
            Core.Logging.FileLogger.Info(
                $"SSH using Plink fallback (Pageant) for {server.DisplayName}");
            return await ConnectSshViaPlinkAsync(
                    server,
                    settings,
                    targetHost,
                    targetPort,
                    originalFailure: null,
                    ct)
                .ConfigureAwait(false);
        }

        var session = new SshShellSession();
        try
        {
            await session.ConnectAsync(sshParams, hostKeyStore: _hostKeyStore, cancellationToken: ct)
                .ConfigureAwait(false);
            if (session.Client is { } connectedClient)
            {
                connectedClient.KeepAliveInterval =
                    TimeSpan.FromSeconds(settings.SshKeepAliveIntervalSeconds);
            }
        }
        catch (Exception ex)
        {
            session.Dispose();
            var failure = FailureClassifier.Classify(ex, sshParams);

            if (failure.Code is SshFailureCode.AuthRejected
                    or SshFailureCode.KeyRejected
                    or SshFailureCode.PasswordRejected
                    or SshFailureCode.NoSupportedAuth
                    or SshFailureCode.KeyboardInteractiveNoPassword)
            {
                Core.Logging.FileLogger.Info(
                    $"SSH.NET auth failed ({failure.Code}), falling back to Plink: {failure.Message}");
                SetStatusText?.Invoke(_localizer["StatusSshRetryingViaPlink"]);
                return await ConnectSshViaPlinkAsync(server, settings, targetHost, targetPort, failure.Code, ct)
                    .ConfigureAwait(false);
            }

            _connectionSm.SetError(server.Id, failure.Message);
            return new ConnectionResult(
                false,
                failure.Message,
                null,
                SshSessionDiagnosticFactory.FromClassifiedFailure(failure));
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
        return new ConnectionResult(true, null, new SshSessionResult(session));
    }

    /// <summary>
    /// Launches putty.exe as an external (non-embedded) SSH session.
    /// Used when <see cref="ServerProfileDto.SshMode"/> is "External".
    /// </summary>
    private ConnectionResult ConnectSshExternal(
        ServerProfileDto server,
        AppSettings settings,
        string targetHost,
        int targetPort)
    {
        var puttyPath = ConnectionHelpers.ResolvePuttyPath(settings.PuttyPath, settings.PlinkPath);
        if (string.IsNullOrWhiteSpace(puttyPath) || !File.Exists(puttyPath))
        {
            var msg = _localizer["ErrorPuttyNotConfigured"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(
                false,
                msg,
                null,
                SshSessionDiagnosticFactory.CreateGenericFailure("ErrorPuttyNotConfigured", msg));
        }

        if (!string.IsNullOrEmpty(server.SshUsername) &&
            !InputValidator.Validate(server.SshUsername, "SshUser"))
        {
            var msg = _localizer["ErrorInvalidSshUsername"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(
                false,
                msg,
                null,
                SshSessionDiagnosticFactory.CreatePreflightFailure("ErrorInvalidSshUsername", msg));
        }

        if (!IsValidSshHost(targetHost))
        {
            var msg = _localizer["ErrorInvalidTargetHost"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(
                false,
                msg,
                null,
                SshSessionDiagnosticFactory.CreatePreflightFailure("ErrorInvalidTargetHost", msg));
        }

        if (!InputValidator.ValidatePortRange(targetPort))
        {
            const string msg = "Invalid SSH target port.";
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null, SshSessionDiagnosticFactory.CreatePreflightFailure("ErrorConnectionFailed", msg));
        }

        if (!TryValidateKeyPath(server.SshKeyPath, out var keyPathError))
        {
            _connectionSm.SetError(server.Id, keyPathError);
            return new ConnectionResult(false, keyPathError, null, SshSessionDiagnosticFactory.CreatePreflightFailure("ErrorConnectionFailed", keyPathError));
        }

        var target = !string.IsNullOrEmpty(server.SshUsername)
            ? $"{server.SshUsername}@{targetHost}"
            : targetHost;

        try
        {
            var psi = BuildPuttyStartInfo(
                puttyPath,
                server.SshKeyPath,
                server.SshCompression,
                server.SshAgentForwarding,
                server.SshX11Forwarding,
                targetPort,
                target);
            var arguments = string.Join(' ', psi.ArgumentList);
            Core.Logging.FileLogger.Info($"Launching PuTTY: {puttyPath} {arguments}");

            using var process = System.Diagnostics.Process.Start(psi);

            Core.Logging.FileLogger.Info(
                $"Launched putty.exe PID={process?.Id ?? 0} for {server.DisplayName} ({targetHost}:{targetPort})");
            _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
            return new ConnectionResult(true, null, null);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error("PuTTY launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(
                false,
                ex.Message,
                null,
                SshSessionDiagnosticFactory.CreateGenericFailure("ErrorConnectionFailed", ex.Message));
        }
    }

    /// <summary>
    /// Launches plink.exe as an interactive SSH session via ConPTY.
    /// Used when SSH.NET cannot authenticate.
    /// </summary>
    private async Task<ConnectionResult> ConnectSshViaPlinkAsync(
        ServerProfileDto server,
        AppSettings settings,
        string targetHost,
        int targetPort,
        SshFailureCode? originalFailure,
        CancellationToken ct)
    {
        var plinkPath = ConnectionHelpers.ResolvePlinkPath(settings.PlinkPath);
        if (string.IsNullOrWhiteSpace(plinkPath) || !File.Exists(plinkPath))
        {
            var msg = originalFailure is not null
                ? _localizer.Format("ErrorPlinkNotConfiguredWithReason", originalFailure)
                : _localizer["ErrorPlinkNotConfigured"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(
                false,
                msg,
                null,
                SshSessionDiagnosticFactory.CreatePlinkFallbackFailure(
                    "ErrorPlinkNotConfigured",
                    msg,
                    originalFailure));
        }

        if (!string.IsNullOrEmpty(server.SshUsername) &&
            !InputValidator.Validate(server.SshUsername, "SshUser"))
        {
            var msg = _localizer["ErrorInvalidSshUsername"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(
                false,
                msg,
                null,
                SshSessionDiagnosticFactory.CreatePlinkFallbackFailure("ErrorInvalidSshUsername", msg));
        }

        if (!IsValidSshHost(targetHost))
        {
            var msg = _localizer["ErrorInvalidTargetHost"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(
                false,
                msg,
                null,
                SshSessionDiagnosticFactory.CreatePlinkFallbackFailure("ErrorInvalidTargetHost", msg));
        }

        if (!InputValidator.ValidatePortRange(targetPort))
        {
            const string msg = "Invalid SSH target port.";
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null, SshSessionDiagnosticFactory.CreatePlinkFallbackFailure("ErrorConnectionFailed", msg));
        }

        if (!TryValidateKeyPath(server.SshKeyPath, out var keyPathError))
        {
            _connectionSm.SetError(server.Id, keyPathError);
            return new ConnectionResult(false, keyPathError, null, SshSessionDiagnosticFactory.CreatePlinkFallbackFailure("ErrorConnectionFailed", keyPathError));
        }

        var target = !string.IsNullOrEmpty(server.SshUsername)
            ? $"{server.SshUsername}@{targetHost}"
            : targetHost;

        var hostKeyArg = await ProbeHostKeyFingerprintAsync(
                plinkPath,
                targetHost,
                targetPort,
                server.SshUsername,
                settings,
                ct)
            .ConfigureAwait(false);

        var args = BuildPipeModeArguments(
            server.SshKeyPath,
            server.SshCompression,
            server.SshAgentForwarding,
            server.SshX11Forwarding,
            targetPort,
            target,
            hostKeyArg);

        Core.Logging.FileLogger.Info($"SSH via Plink (pipe mode): {plinkPath} {args}");

        var pipeSession = new Heimdall.Terminal.PipeModeSession();
        try
        {
            await pipeSession.StartAsync(plinkPath, args.ToString()).ConfigureAwait(false);
            Core.Logging.FileLogger.Info($"Plink SSH pipe session started: PID={pipeSession.ProcessId}");
        }
        catch (Exception ex)
        {
            pipeSession.Dispose();
            Core.Logging.FileLogger.Error("Plink SSH launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(
                false,
                ex.Message,
                null,
                SshSessionDiagnosticFactory.CreatePipeModeFailure(ex.Message));
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
        return new ConnectionResult(true, null, new TerminalSessionResult(pipeSession));
    }

    /// <summary>
    /// Probes the SSH host key fingerprint by running <c>plink -batch</c>.
    /// Returns null if the key is already cached or the fingerprint cannot be parsed.
    /// </summary>
    private async Task<string?> ProbeHostKeyFingerprintAsync(
        string plinkPath,
        string host,
        int port,
        string? username,
        AppSettings settings,
        CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(username) &&
                !InputValidator.Validate(username, "SshUser"))
            {
                return null;
            }

            if (!IsValidSshHost(host))
            {
                return null;
            }

            if (!InputValidator.ValidatePortRange(port))
            {
                return null;
            }

            var userPrefix = string.IsNullOrWhiteSpace(username) ? string.Empty : $"{username}@";
            var probeTarget = $"{userPrefix}{host}";
            var probeParts = new[] { "-v", "-batch", "-ssh", "-P", port.ToString(), probeTarget };
            var probeArgs = string.Join(' ', probeParts);

            Core.Logging.FileLogger.Info($"Host key probe: {plinkPath} {probeArgs}");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = plinkPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            foreach (var part in probeParts)
            {
                psi.ArgumentList.Add(part);
            }

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(settings.HostKeyProbeTimeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            string stderr;
            try
            {
                stderr = await process.StandardError.ReadToEndAsync(linked.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception ex)
                {
                    Core.Logging.FileLogger.Warn($"[SshHandler] host key probe kill: {ex.Message}");
                }

                stderr = string.Empty;
            }

            Core.Logging.FileLogger.Info(
                $"Host key probe stderr ({stderr.Length} chars): {stderr.Trim().Replace('\n', ' ')}");

            if (string.IsNullOrWhiteSpace(stderr))
            {
                return null;
            }

            var match = System.Text.RegularExpressions.Regex.Match(
                stderr,
                @"(ssh-\S+)\s+\d+\s+(SHA256:\S+)",
                System.Text.RegularExpressions.RegexOptions.Multiline);

            if (match.Success)
            {
                var fingerprint = match.Groups[2].Value;
                Core.Logging.FileLogger.Info($"Extracted host key fingerprint: {fingerprint}");
                return fingerprint;
            }

            var sha256Match = System.Text.RegularExpressions.Regex.Match(stderr, @"SHA256:(\S+)");

            if (sha256Match.Success)
            {
                var fingerprint = "SHA256:" + sha256Match.Groups[1].Value;
                Core.Logging.FileLogger.Info(
                    $"Extracted host key fingerprint (fallback): {fingerprint}");
                return fingerprint;
            }

            Core.Logging.FileLogger.Warn($"Could not parse fingerprint from stderr for {host}:{port}");
            return null;
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Host key probe failed: {ex.Message}");
            return null;
        }
    }

    internal static System.Diagnostics.ProcessStartInfo BuildPuttyStartInfo(
        string puttyPath,
        string? keyPath,
        bool compression,
        bool agentForwarding,
        bool x11Forwarding,
        int port,
        string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = puttyPath,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("-ssh");
        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(keyPath);
        }

        if (compression)
        {
            psi.ArgumentList.Add("-C");
        }

        if (agentForwarding)
        {
            psi.ArgumentList.Add("-A");
        }

        if (x11Forwarding)
        {
            psi.ArgumentList.Add("-X");
        }

        psi.ArgumentList.Add("-P");
        psi.ArgumentList.Add(port.ToString());
        psi.ArgumentList.Add(target);
        return psi;
    }

    internal static string BuildPipeModeArguments(
        string? keyPath,
        bool compression,
        bool agentForwarding,
        bool x11Forwarding,
        int port,
        string target,
        string? hostKeyFingerprint)
    {
        var argParts = new List<string> { "-ssh", "-t", "-no-antispoof" };
        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            argParts.Add("-i");
            argParts.Add($"\"{InputValidator.EscapeForDoubleQuotedString(keyPath)}\"");
        }

        if (compression)
        {
            argParts.Add("-C");
        }

        if (agentForwarding)
        {
            argParts.Add("-A");
        }

        if (x11Forwarding)
        {
            argParts.Add("-X");
        }

        argParts.Add("-P");
        argParts.Add(port.ToString());
        argParts.Add(target);

        if (!string.IsNullOrWhiteSpace(hostKeyFingerprint))
        {
            argParts.Add("-hostkey");
            argParts.Add($"\"{InputValidator.EscapeForDoubleQuotedString(hostKeyFingerprint)}\"");
        }

        return string.Join(' ', argParts);
    }

    internal static bool TryValidateKeyPath(string? keyPath, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            errorMessage = string.Empty;
            return true;
        }

        if (keyPath.Contains('\0') || keyPath.Contains('"'))
        {
            errorMessage = $"Invalid SSH key path: {keyPath}";
            return false;
        }

        if (!Path.IsPathRooted(keyPath))
        {
            errorMessage = $"SSH key path must be absolute: {keyPath}";
            return false;
        }

        if (!File.Exists(keyPath))
        {
            errorMessage = $"SSH key file not found: {keyPath}";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool IsValidSshHost(string host)
    {
        return !string.IsNullOrWhiteSpace(host)
            && (InputValidator.ValidateDomain(host) || IPAddress.TryParse(host, out _));
    }
}
