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
using Heimdall.Ssh;

namespace Heimdall.App.Services;

public partial class ConnectionService
{
    /// <summary>
    /// Establishes an SSH shell connection, optionally through a tunnel.
    /// Returns a connected <see cref="SshShellSession"/> on success.
    /// </summary>
    public async Task<ConnectionResult> ConnectSshAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        Core.Logging.FileLogger.Info($"ConnectSshAsync: {server.DisplayName} ({server.RemoteServer}:{server.SshPort}) Gateway={server.SshGatewayId ?? "none"}");
        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.ValidatingConfig);

        var (tunnelOk, _, targetHost, targetPort, tunnelError) =
            await SetupTunnelIfNeededAsync(server, server.SshPort, settings, ct)
                .ConfigureAwait(false);

        if (!tunnelOk)
        {
            return new ConnectionResult(false, tunnelError, null);
        }

        // Ensure X11 server is available when X11 forwarding is requested
        if (server.SshX11Forwarding)
        {
            await _x11ServerManager.EnsureRunningAsync().ConfigureAwait(false);
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.LaunchingSsh);

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
            Username = server.SshUsername ?? "",
            Password = DecryptPassword(server.SshPasswordEncrypted),
            KeyPath = string.IsNullOrWhiteSpace(server.SshKeyPath) ? null : server.SshKeyPath,
            AgentForwarding = server.SshAgentForwarding,
            Compression = server.SshCompression,
            X11Forwarding = server.SshX11Forwarding
        };

        // Check if we need Plink fallback (Pageant-only auth)
        if (SshConnectionFactory.RequiresPageantFallback(sshParams))
        {
            Core.Logging.FileLogger.Info($"SSH using Plink fallback (Pageant) for {server.DisplayName}");
            return await ConnectSshViaPlinkAsync(server, settings, targetHost, targetPort, originalFailure: null, ct);
        }

        // Try SSH.NET first
        var session = new SshShellSession();
        try
        {
            await session.ConnectAsync(sshParams, hostKeyStore: _hostKeyStore, cancellationToken: ct).ConfigureAwait(false);
            if (session.Client is { } connectedClient)
            {
                var keepAlive = _currentSettings?.SshKeepAliveIntervalSeconds ?? 30;
                connectedClient.KeepAliveInterval = TimeSpan.FromSeconds(keepAlive);
            }
        }
        catch (Exception ex)
        {
            session.Dispose();
            var failure = FailureClassifier.Classify(ex, sshParams);

            // Fallback to Plink when SSH.NET auth is rejected.
            // Plink handles keyboard-interactive, password prompts, and Pageant natively —
            // so it succeeds in scenarios where SSH.NET's typed auth methods are rejected
            // (e.g., server with PasswordAuthentication no + KbdInteractiveAuthentication yes).
            // Pageant availability is NOT a prerequisite: Plink can prompt interactively.
            if (failure.Code is SshFailureCode.AuthRejected
                    or SshFailureCode.KeyRejected
                    or SshFailureCode.PasswordRejected
                    or SshFailureCode.NoSupportedAuth
                    or SshFailureCode.KeyboardInteractiveNoPassword)
            {
                Core.Logging.FileLogger.Info($"SSH.NET auth failed ({failure.Code}), falling back to Plink: {failure.Message}");
                SetStatusText?.Invoke(_localizer["StatusSshRetryingViaPlink"]);
                return await ConnectSshViaPlinkAsync(server, settings, targetHost, targetPort, failure.Code, ct);
            }

            _connectionSm.SetError(server.Id, failure.Message);
            return new ConnectionResult(false, failure.Message, null);
        }

        if (!string.IsNullOrWhiteSpace(server.PostConnectCommand))
        {
            var delayMs = server.PostConnectDelayMs > 0 ? server.PostConnectDelayMs : 800;
            await Task.Delay(delayMs, ct).ConfigureAwait(false);

            var lines = server.PostConnectCommand
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var line in lines)
            {
                session.Write(line + "\n");
                if (lines.Length > 1)
                    await Task.Delay(150, ct).ConfigureAwait(false);
            }

            Core.Logging.FileLogger.Info(
                $"Post-connect: sent {lines.Length} command(s) to {server.DisplayName}");
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);
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
        var puttyPath = ResolvePuttyPath(settings.PuttyPath, settings.PlinkPath);
        if (string.IsNullOrWhiteSpace(puttyPath) || !File.Exists(puttyPath))
        {
            var msg = _localizer["ErrorPuttyNotConfigured"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        // Validate user-supplied fields before building the command line (CWE-78)
        if (!string.IsNullOrEmpty(server.SshUsername) &&
            !InputValidator.Validate(server.SshUsername, "SshUser"))
        {
            var msg = _localizer["ErrorInvalidSshUsername"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        if (!InputValidator.Validate(targetHost, "Address"))
        {
            var msg = _localizer["ErrorInvalidTargetHost"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        // Build PuTTY arguments as a structured list (CWE-78 prevention)
        var args = new List<string> { "-ssh" };
        if (!string.IsNullOrEmpty(server.SshKeyPath))
        {
            args.Add("-i");
            args.Add($"\"{server.SshKeyPath}\"");
        }
        if (server.SshCompression)
            args.Add("-C");
        if (server.SshAgentForwarding)
            args.Add("-A");
        if (server.SshX11Forwarding)
            args.Add("-X");
        args.Add("-P");
        args.Add(targetPort.ToString());
        var target = !string.IsNullOrEmpty(server.SshUsername)
            ? $"{server.SshUsername}@{targetHost}"
            : targetHost;
        args.Add(target);

        try
        {
            var arguments = string.Join(' ', args);
            Core.Logging.FileLogger.Info($"Launching PuTTY: {puttyPath} {arguments}");

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = puttyPath,
                Arguments = arguments,
                UseShellExecute = false
            });

            Core.Logging.FileLogger.Info($"Launched putty.exe PID={process?.Id ?? 0} for {server.DisplayName} ({targetHost}:{targetPort})");
            _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);
            return new ConnectionResult(true, null, null);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error("PuTTY launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }
    }

    /// <summary>
    /// Launches plink.exe as an interactive SSH session via ConPTY.
    /// Used when SSH.NET cannot authenticate (Pageant-only auth).
    /// Returns an ITerminalSession that the embedded SSH view can use.
    /// </summary>
    private async Task<ConnectionResult> ConnectSshViaPlinkAsync(
        ServerProfileDto server,
        AppSettings settings,
        string targetHost,
        int targetPort,
        SshFailureCode? originalFailure,
        CancellationToken ct)
    {
        var plinkPath = ResolvePlinkPath(settings.PlinkPath);
        if (string.IsNullOrWhiteSpace(plinkPath) || !File.Exists(plinkPath))
        {
            var msg = originalFailure is not null
                ? _localizer.Format("ErrorPlinkNotConfiguredWithReason", originalFailure)
                : _localizer["ErrorPlinkNotConfigured"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        // Validate user-supplied fields before building the command line (CWE-78)
        if (!string.IsNullOrEmpty(server.SshUsername) &&
            !InputValidator.Validate(server.SshUsername, "SshUser"))
        {
            var msg = _localizer["ErrorInvalidSshUsername"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        if (!InputValidator.Validate(targetHost, "Address"))
        {
            var msg = _localizer["ErrorInvalidTargetHost"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        // Build plink arguments as a structured list to prevent argument injection
        var argParts = new List<string> { "-ssh", "-t", "-no-antispoof" };
        if (!string.IsNullOrEmpty(server.SshKeyPath))
        {
            argParts.Add("-i");
            argParts.Add($"\"{server.SshKeyPath}\"");
        }
        if (server.SshCompression)
            argParts.Add("-C");
        if (server.SshAgentForwarding)
            argParts.Add("-A");
        if (server.SshX11Forwarding)
            argParts.Add("-X");
        argParts.Add("-P");
        argParts.Add(targetPort.ToString());
        var target = !string.IsNullOrEmpty(server.SshUsername)
            ? $"{server.SshUsername}@{targetHost}"
            : targetHost;
        argParts.Add(target);

        var args = new System.Text.StringBuilder(string.Join(' ', argParts));

        // Probe host key fingerprint: Plink writes the host key prompt to CONOUT$
        // (not stderr), so pipe mode hangs if the key isn't cached. We run a quick
        // -batch probe that fails fast and prints the fingerprint to stderr, then
        // pass -hostkey to the real session to bypass the CONOUT$ prompt entirely.
        string? hostKeyArg = await ProbeHostKeyFingerprintAsync(
            plinkPath, targetHost, targetPort, server.SshUsername, ct);

        if (!string.IsNullOrEmpty(hostKeyArg))
        {
            args.Append($" -hostkey \"{hostKeyArg}\"");
        }

        Core.Logging.FileLogger.Info($"SSH via Plink (pipe mode): {plinkPath} {args}");

        // Use pipe mode (NOT ConPTY) — raw stdin/stdout redirection.
        // ConPTY converts VT input to Windows key events, breaking arrow keys.
        // Pipe mode passes VT sequences through raw. The -t flag forces remote PTY.
        var pipeSession = new Heimdall.Terminal.PipeModeSession();
        try
        {
            await pipeSession.StartAsync(plinkPath, args.ToString());
            Core.Logging.FileLogger.Info($"Plink SSH pipe session started: PID={pipeSession.ProcessId}");
        }
        catch (Exception ex)
        {
            pipeSession.Dispose();
            Core.Logging.FileLogger.Error("Plink SSH launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);
        return new ConnectionResult(true, null, new TerminalSessionResult(pipeSession));
    }

    /// <summary>
    /// Ensures the SSH host key for a target host:port is cached in PuTTY's registry.
    /// Runs a quick non-interactive plink probe that auto-accepts the host key by
    /// feeding "y" to stdin. This prevents the interactive session from hanging on
    /// the host key prompt (which Plink writes directly to the console device,
    /// bypassing redirected stderr in pipe mode).
    /// </summary>

    /// <summary>
    /// Probes the SSH host key fingerprint by running <c>plink -batch</c>.
    /// When the host key is not cached, <c>-batch</c> makes Plink fail immediately
    /// and print the fingerprint to stderr. We parse it and return the value for
    /// use with <c>-hostkey</c> on the real session. This bypasses the CONOUT$ prompt
    /// entirely — Plink never needs a console device.
    /// Returns null if the key is already cached (no fingerprint needed).
    /// </summary>
    private async Task<string?> ProbeHostKeyFingerprintAsync(
        string plinkPath, string host, int port, string? username, CancellationToken ct)
    {
        try
        {
            // Validate inputs before building probe command (CWE-78)
            if (!string.IsNullOrWhiteSpace(username) &&
                !InputValidator.Validate(username, "SshUser"))
            {
                return null;
            }
            if (!InputValidator.Validate(host, "Address"))
            {
                return null;
            }

            var userPrefix = string.IsNullOrWhiteSpace(username) ? "" : $"{username}@";
            // -v forces verbose output to stderr (including fingerprint)
            // -batch prevents CONOUT$ prompts (fails fast if key not cached)
            var probeParts = new List<string> { "-v", "-batch", "-ssh", "-P", port.ToString(), $"{userPrefix}{host}" };
            var probeArgs = string.Join(' ', probeParts);

            Core.Logging.FileLogger.Info($"Host key probe: {plinkPath} {probeArgs}");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = plinkPath,
                Arguments = probeArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();

            using var timeout = new CancellationTokenSource(HostKeyProbeTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            string stderr;
            try
            {
                stderr = await process.StandardError.ReadToEndAsync(linked.Token);
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch (Exception ex) { Core.Logging.FileLogger.Warn($"[ConnectionService] host key probe kill: {ex.Message}"); }
                stderr = "";
            }

            Core.Logging.FileLogger.Info(
                $"Host key probe stderr ({stderr.Length} chars): {stderr.Trim().Replace('\n', ' ')}");

            if (string.IsNullOrWhiteSpace(stderr))
            {
                return null;
            }

            // Parse fingerprint from stderr. Plink outputs lines like:
            // "ssh-ed25519 255 SHA256:xxxx..."
            // or "The host key is not cached... <fingerprint>"
            // We look for SHA256: or the key type pattern
            var match = System.Text.RegularExpressions.Regex.Match(
                stderr,
                @"(ssh-\S+)\s+\d+\s+(SHA256:\S+)",
                System.Text.RegularExpressions.RegexOptions.Multiline);

            if (match.Success)
            {
                var fingerprint = match.Groups[2].Value;
                Core.Logging.FileLogger.Info(
                    $"Extracted host key fingerprint: {fingerprint}");
                return fingerprint;
            }

            // Fallback: look for any SHA256: pattern
            var sha256Match = System.Text.RegularExpressions.Regex.Match(
                stderr, @"SHA256:(\S+)");

            if (sha256Match.Success)
            {
                var fingerprint = "SHA256:" + sha256Match.Groups[1].Value;
                Core.Logging.FileLogger.Info(
                    $"Extracted host key fingerprint (fallback): {fingerprint}");
                return fingerprint;
            }

            Core.Logging.FileLogger.Warn(
                $"Could not parse fingerprint from stderr for {host}:{port}");
            return null;
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Host key probe failed: {ex.Message}");
            return null;
        }
    }
}
