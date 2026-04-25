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
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Core.Ssh;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;
using Heimdall.Ssh.Agents;

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
    private readonly IHostKeyTrustService _hostKeyTrustService;
    private readonly IHostKeyVerifier _hostKeyVerifier;
    private readonly X11ServerManager _x11ServerManager;
    private readonly IDialogService _dialogService;

    internal Action<string>? SetStatusText { get; set; }

    public SshHandler(
        ITunnelService tunnelService,
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer,
        HostKeyStore hostKeyStore,
        IHostKeyTrustService hostKeyTrustService,
        IHostKeyVerifier hostKeyVerifier,
        X11ServerManager x11ServerManager,
        IDialogService dialogService)
    {
        _tunnelService = tunnelService;
        _connectionSm = connectionSm;
        _localizer = localizer;
        _hostKeyStore = hostKeyStore;
        _hostKeyTrustService = hostKeyTrustService;
        _hostKeyVerifier = hostKeyVerifier;
        _x11ServerManager = x11ServerManager;
        _dialogService = dialogService;
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
            KeyPassphrase = ConnectionHelpers.DecryptPassword(server.SshKeyPassphraseEncrypted),
            KeyPath = string.IsNullOrWhiteSpace(server.SshKeyPath) ? null : server.SshKeyPath,
            SshAgentPreference = settings.SshAgentPreference,
            UseLegacyPasswordAsKeyPassphrase = server.UsesLegacySshCredentialMapping,
            LegacyCredentialName = server.DisplayName,
            AgentForwarding = server.SshAgentForwarding,
            Compression = server.SshCompression,
            X11Forwarding = server.SshX11Forwarding
        };

        var agentRegistry = SshAgentRegistry.CreateDefault(settings.SshAgentPreference);
        if (sshParams.AgentForwarding
            && !agentRegistry.HasPlinkCompatibleAgent()
            && agentRegistry.HasAnyNonPlinkAgent())
        {
            var message = _localizer["ErrorPlinkOpenSshAgentUnsupported"];
            _connectionSm.SetError(server.Id, message);
            return new ConnectionResult(
                false,
                message,
                null,
                SshSessionDiagnosticFactory.CreatePlinkFallbackFailure(
                    "ErrorPlinkOpenSshAgentUnsupported",
                    message));
        }

        if (SshConnectionFactory.RequiresPlinkFallback(sshParams))
        {
            Core.Logging.FileLogger.Info(
                $"SSH using Plink fallback for {server.DisplayName}");
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
            await session.ConnectAsync(
                    sshParams,
                    hostKeyStore: _hostKeyStore,
                    hostKeyVerifier: _hostKeyVerifier,
                    cancellationToken: ct)
                .ConfigureAwait(false);
            if (session.Client is { } connectedClient)
            {
                connectedClient.KeepAliveInterval =
                    TimeSpan.FromSeconds(settings.SshKeepAliveIntervalSeconds);
            }
        }
        catch (HostKeyRejectedException ex)
        {
            session.Dispose();

            if (ex.IsMismatch && !string.IsNullOrWhiteSpace(ex.StoredFingerprint))
            {
                var message = BuildHostKeyMismatchMessage(
                    ex.StoredFingerprint,
                    ex.PresentedFingerprint);
                _connectionSm.SetError(server.Id, message);
                return new ConnectionResult(
                    false,
                    message,
                    null,
                    SshSessionDiagnosticFactory.CreateHostKeyMismatchFailure(
                        ex.StoredFingerprint,
                        ex.PresentedFingerprint,
                        ex.Host,
                        ex.Port));
            }

            var cancelledMessage = BuildCancelledMessage();
            _connectionSm.SetError(server.Id, cancelledMessage);
            return new ConnectionResult(
                false,
                cancelledMessage,
                null,
                SshSessionDiagnosticFactory.FromClassifiedFailure(
                    new SshFailureInfo(SshFailureCode.Cancelled, cancelledMessage, false, ex)));
        }
        catch (Exception ex)
        {
            session.Dispose();
            var failure = FailureClassifier.Classify(ex, sshParams);

            if (failure.Code is SshFailureCode.AuthRejected
                    or SshFailureCode.KeyRejected
                    or SshFailureCode.PassphraseRejected
                    or SshFailureCode.PasswordRejected
                    or SshFailureCode.NoSupportedAuth
                    or SshFailureCode.KeyboardInteractiveNoPassword)
            {
                var fallbackAgentRegistry = SshAgentRegistry.CreateDefault(settings.SshAgentPreference);
                if (!fallbackAgentRegistry.HasPlinkCompatibleAgent() && fallbackAgentRegistry.HasAnyNonPlinkAgent())
                {
                    var message = _localizer["ErrorPlinkOpenSshAgentUnsupported"];
                    _connectionSm.SetError(server.Id, message);
                    return new ConnectionResult(
                        false,
                        message,
                        null,
                        SshSessionDiagnosticFactory.CreatePlinkFallbackFailure(
                            "ErrorPlinkOpenSshAgentUnsupported",
                            message,
                            failure.Code));
                }

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

        var storedFingerprint = _hostKeyTrustService.GetEntry(targetHost, targetPort)?.Fingerprint;
        string? hostKeyArg = storedFingerprint;

        if (!string.IsNullOrWhiteSpace(storedFingerprint))
        {
            Core.Logging.FileLogger.Info(
                $"Using pinned host key for Plink path {targetHost}:{targetPort}: {storedFingerprint}");

            var verifyPresentation = await ProbeHostKeyPresentationAsync(
                    plinkPath,
                    targetHost,
                    targetPort,
                    server.SshUsername,
                    settings,
                    ct)
                .ConfigureAwait(false);

            if (verifyPresentation is not null
                && !string.Equals(
                    verifyPresentation.Fingerprint,
                    storedFingerprint,
                    StringComparison.Ordinal))
            {
                Core.Logging.FileLogger.Error(
                    $"HOST KEY MISMATCH (plink path) for {targetHost}:{targetPort}! " +
                    $"Stored={storedFingerprint} Presented={verifyPresentation.Fingerprint}");

                var decision = await _hostKeyVerifier.VerifyAsync(
                        targetHost,
                        targetPort,
                        verifyPresentation.Algorithm,
                        verifyPresentation.Fingerprint,
                        storedFingerprint,
                        ct)
                    .ConfigureAwait(false);

                if (decision == HostKeyDecision.Accept)
                {
                    _hostKeyTrustService.Trust(
                        targetHost,
                        targetPort,
                        verifyPresentation.Fingerprint,
                        verifyPresentation.Algorithm,
                        HostKeySource.UserConfirmed);
                    hostKeyArg = verifyPresentation.Fingerprint;
                    Core.Logging.FileLogger.Warn(
                        $"User accepted replacement host key for {targetHost}:{targetPort}: {verifyPresentation.Fingerprint}");
                }
                else
                {
                    var msg = BuildHostKeyMismatchMessage(
                        storedFingerprint,
                        verifyPresentation.Fingerprint);
                    _connectionSm.SetError(server.Id, msg);
                    return new ConnectionResult(
                        false,
                        msg,
                        null,
                        SshSessionDiagnosticFactory.CreateHostKeyMismatchFailure(
                            storedFingerprint,
                            verifyPresentation.Fingerprint,
                            targetHost,
                            targetPort));
                }
            }
        }
        else
        {
            var probedPresentation = await ProbeHostKeyPresentationAsync(
                    plinkPath,
                    targetHost,
                    targetPort,
                    server.SshUsername,
                    settings,
                    ct)
                .ConfigureAwait(false);

            if (probedPresentation is not null)
            {
                var decision = await _hostKeyVerifier.VerifyAsync(
                        targetHost,
                        targetPort,
                        probedPresentation.Algorithm,
                        probedPresentation.Fingerprint,
                        storedFingerprint: null,
                        ct)
                    .ConfigureAwait(false);

                if (decision == HostKeyDecision.Accept)
                {
                    _hostKeyTrustService.Trust(
                        targetHost,
                        targetPort,
                        probedPresentation.Fingerprint,
                        probedPresentation.Algorithm,
                        HostKeySource.UserConfirmed);
                    hostKeyArg = probedPresentation.Fingerprint;
                    Core.Logging.FileLogger.Info(
                        $"User trusted Plink host key for {targetHost}:{targetPort} fingerprint={probedPresentation.Fingerprint}");
                }
                else
                {
                    var message = BuildCancelledMessage();
                    _connectionSm.SetError(server.Id, message);
                    return new ConnectionResult(
                        false,
                        message,
                        null,
                        SshSessionDiagnosticFactory.CreatePlinkFallbackFailure(
                            "ErrorSshCancelled",
                            message,
                            SshFailureCode.Cancelled));
                }
            }
        }

        var passwordFilePath = CreatePlinkPasswordFile(
            ConnectionHelpers.DecryptPassword(server.SshPasswordEncrypted));
        if (ShouldPromptForPlinkPassword(passwordFilePath, server.SshKeyPath))
        {
            var promptedPassword = await _dialogService.ShowPasswordInputAsync(
                    _localizer["DialogSshPasswordPromptTitle"],
                    _localizer.Format("DialogSshPasswordPromptMessage", target),
                    ct)
                .ConfigureAwait(false);

            passwordFilePath = CreatePlinkPasswordFile(promptedPassword);
            if (passwordFilePath is null)
            {
                var message = BuildCancelledMessage();
                _connectionSm.SetError(server.Id, message);
                return new ConnectionResult(
                    false,
                    message,
                    null,
                    SshSessionDiagnosticFactory.CreatePlinkFallbackFailure(
                        "ErrorSshCancelled",
                        message,
                        SshFailureCode.Cancelled));
            }
        }

        var args = BuildPipeModeArguments(
            server.SshKeyPath,
            server.SshCompression,
            server.SshAgentForwarding,
            server.SshX11Forwarding,
            targetPort,
            target,
            hostKeyArg,
            passwordFilePath);

        var terminalSession = new Heimdall.Terminal.PipeModeSession();
        Core.Logging.FileLogger.Info($"SSH via Plink ({terminalSession.GetType().Name}): {plinkPath} {args}");

        if (!string.IsNullOrEmpty(passwordFilePath))
        {
            var fileToDelete = passwordFilePath;
            terminalSession.ProcessExited += _ => DeletePlinkPasswordFile(fileToDelete);
        }

        try
        {
            await terminalSession.StartAsync(plinkPath, args).ConfigureAwait(false);
            Core.Logging.FileLogger.Info($"Plink SSH session started: PID={terminalSession.ProcessId}");
            passwordFilePath = null;
        }
        catch (Exception ex)
        {
            terminalSession.Dispose();
            DeletePlinkPasswordFile(passwordFilePath);
            Core.Logging.FileLogger.Error("Plink SSH launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(
                false,
                ex.Message,
                null,
                SshSessionDiagnosticFactory.CreatePipeModeFailure(ex.Message));
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
        return new ConnectionResult(true, null, new TerminalSessionResult(terminalSession));
    }

    internal static bool ShouldPromptForPlinkPassword(string? passwordFilePath, string? keyPath)
    {
        return string.IsNullOrEmpty(passwordFilePath) && string.IsNullOrWhiteSpace(keyPath);
    }

    private static string? CreatePlinkPasswordFile(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return null;
        }

        var passwordFilePath = Path.Combine(Path.GetTempPath(), $"heimdall_ssh_pw_{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows())
        {
            SecureFileWriter.WriteAndProtect(passwordFilePath, password);
        }
        else
        {
            File.WriteAllText(passwordFilePath, password);
            try
            {
                File.SetUnixFileMode(
                    passwordFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn(
                    $"[SshHandler] Unix file mode restriction failed for Plink password file: {ex.Message}");
            }
        }

        return passwordFilePath;
    }

    private static void DeletePlinkPasswordFile(string? passwordFilePath)
    {
        if (string.IsNullOrWhiteSpace(passwordFilePath))
        {
            return;
        }

        try
        {
            File.Delete(passwordFilePath);
        }
        catch (IOException ex)
        {
            Core.Logging.FileLogger.Warn($"[SshHandler] DeletePlinkPasswordFile: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Core.Logging.FileLogger.Warn($"[SshHandler] DeletePlinkPasswordFile: {ex.Message}");
        }
    }

    /// <summary>
    /// Probes the SSH host key presentation by running <c>plink -batch -v</c>.
    /// Returns null if the key is already cached or the fingerprint cannot be parsed.
    /// </summary>
    private Task<PlinkHostKeyPresentation?> ProbeHostKeyPresentationAsync(
        string plinkPath,
        string host,
        int port,
        string? username,
        AppSettings settings,
        CancellationToken ct)
    {
        return PlinkHostKeyProbe.ProbeAsync(
            plinkPath,
            host,
            port,
            username,
            settings.HostKeyProbeTimeoutMs,
            ct);
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
        string? hostKeyFingerprint,
        string? passwordFilePath = null)
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

        if (!string.IsNullOrWhiteSpace(hostKeyFingerprint))
        {
            argParts.Add("-hostkey");
            argParts.Add($"\"{InputValidator.EscapeForDoubleQuotedString(hostKeyFingerprint)}\"");
        }

        if (!string.IsNullOrWhiteSpace(passwordFilePath))
        {
            argParts.Add("-pwfile");
            argParts.Add($"\"{InputValidator.EscapeForDoubleQuotedString(passwordFilePath)}\"");
        }

        argParts.Add(target);
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

    private string BuildHostKeyMismatchMessage(
        string storedFingerprint,
        string presentedFingerprint)
    {
        var message = _localizer["ErrorHostKeyMismatch"];
        if (string.Equals(message, "ErrorHostKeyMismatch", StringComparison.Ordinal))
        {
            message = "SSH host key mismatch \u2014 possible MITM. Stored fingerprint differs from server-presented fingerprint.";
        }

        var detail = _localizer.Format(
            "ErrorHostKeyMismatchDetail",
            storedFingerprint,
            presentedFingerprint);
        if (string.Equals(detail, "ErrorHostKeyMismatchDetail", StringComparison.Ordinal))
        {
            detail = $"Stored: {storedFingerprint}. Presented: {presentedFingerprint}.";
        }

        return $"{message} {detail}";
    }

    private string BuildCancelledMessage()
    {
        var message = _localizer["ErrorSshCancelled"];
        return string.Equals(message, "ErrorSshCancelled", StringComparison.Ordinal)
            ? "Connection was cancelled."
            : message;
    }
}
