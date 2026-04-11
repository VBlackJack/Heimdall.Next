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
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Core.StateMachine;
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Orchestrates the full connection lifecycle for RDP, SSH, and SFTP sessions.
/// Resolves gateway chains, opens tunnels, performs preflight checks, and
/// delegates to the appropriate session engine.
/// </summary>
public partial class ConnectionService : IDisposable
{
    private bool _disposed;
    private TimeSpan HostKeyProbeTimeout =>
        TimeSpan.FromMilliseconds(_currentSettings?.HostKeyProbeTimeoutMs ?? 8000);

    private readonly ConfigManager _configManager;
    private readonly TunnelManager _tunnelManager;
    private readonly HostKeyStore _hostKeyStore;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;
    private readonly X11ServerManager _x11ServerManager;

    /// <summary>
    /// Cached snapshot of the current application settings, kept up-to-date
    /// by subscribing to <see cref="ConfigManager.SettingsChanged"/>.
    /// May be null until the first settings load completes.
    /// </summary>
    private AppSettings? _currentSettings;

    /// <summary>
    /// Returns the latest cached settings snapshot, or null if settings
    /// have not been loaded yet.
    /// </summary>
    public AppSettings? CurrentSettings => _currentSettings;

    /// <summary>
    /// Delegate wired by the shell to surface transient status messages
    /// (e.g., "retrying via Plink…") in the global status bar.
    /// Same pattern as <see cref="SplitService.SetStatusText"/>.
    /// </summary>
    internal Action<string>? SetStatusText { get; set; }

    public ConnectionService(
        ConfigManager configManager,
        TunnelManager tunnelManager,
        HostKeyStore hostKeyStore,
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer,
        X11ServerManager x11ServerManager)
    {
        _configManager = configManager;
        _tunnelManager = tunnelManager;
        _hostKeyStore = hostKeyStore;
        _connectionSm = connectionSm;
        _localizer = localizer;
        _x11ServerManager = x11ServerManager;

        _configManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// Updates the cached settings when the configuration is saved.
    /// </summary>
    private void OnSettingsChanged(AppSettings newSettings)
    {
        _currentSettings = newSettings;
        Core.Logging.FileLogger.Info("ConnectionService: settings refreshed at runtime");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _configManager.SettingsChanged -= OnSettingsChanged;
    }

    /// <summary>
    /// Runs authentication preflight checks for a server's gateway.
    /// </summary>
    /// <param name="server">The server DTO to check.</param>
    /// <param name="settings">Current application settings containing gateway definitions.</param>
    /// <returns>A preflight result indicating pass or fail.</returns>
    public PreflightResult RunPreflight(ServerProfileDto server, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrEmpty(server.SshGatewayId) || server.UseDirectConnection)
        {
            return PreflightResult.Ok();
        }

        var gateway = settings.SshGateways.FirstOrDefault(
            g => string.Equals(g.Id, server.SshGatewayId, StringComparison.OrdinalIgnoreCase));

        if (gateway is null)
        {
            return PreflightResult.Fail(
                SshFailureCode.Unknown,
                _localizer.Format("ErrorGatewayNotFound", server.SshGatewayId));
        }

        var connParams = BuildGatewayParams(gateway);
        bool isTunnel = server.ConnectionType?.Equals("RDP", StringComparison.OrdinalIgnoreCase) == true;
        return AuthPreflightChecker.Check(connParams, isTunnelMode: isTunnel);
    }

    /// <summary>
    /// Converts a gateway DTO to SSH connection parameters, decrypting the password if present.
    /// </summary>
    private static SshConnectionParams BuildGatewayParams(SshGatewayDto gateway)
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
    /// otherwise falls back to the embedded copy in Assets/Tools/.
    /// </summary>
    private static string? ResolvePlinkPath(string? settingsPath)
    {
        // User-configured path takes priority
        if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
        {
            return settingsPath;
        }

        // Embedded plink.exe shipped with the application
        var embeddedPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Tools", "plink.exe");
        if (File.Exists(embeddedPath))
        {
            Core.Logging.FileLogger.Info($"Using embedded plink: {embeddedPath}");
            return embeddedPath;
        }

        return null;
    }

    /// <summary>
    /// Resolves the path to putty.exe: uses the user-configured path if valid,
    /// falls back to the directory containing plink.exe, then the embedded
    /// copy in Assets/Tools/.
    /// </summary>
    private static string? ResolvePuttyPath(string? settingsPath, string? plinkPath)
    {
        // User-configured PuTTY path takes priority
        if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
        {
            return settingsPath;
        }

        // Try to find putty.exe next to plink.exe
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

        // Embedded putty.exe shipped with the application
        var embeddedPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Tools", "putty.exe");
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
    private static string? DecryptPassword(string? encryptedValue)
    {
        return CredentialProtector.Unprotect(encryptedValue);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}

/// <summary>
/// Immutable result of a connection attempt.
/// </summary>
/// <param name="Success">Whether the connection was established.</param>
/// <param name="ErrorMessage">Error description on failure; null on success.</param>
/// <param name="Session">
/// The typed session result on success. Null on failure.
/// Concrete types: <see cref="RdpSessionResult"/>, <see cref="SshSessionResult"/>,
/// <see cref="TerminalSessionResult"/>, <see cref="SftpSessionBundle"/>,
/// <see cref="LocalShellBundle"/>.
/// </param>
public record ConnectionResult(bool Success, string? ErrorMessage, Heimdall.Core.Models.ISessionResult? Session);

/// <summary>Wraps a <see cref="ServerProfileDto"/> for embedded RDP sessions.</summary>
/// <param name="Server">Server profile DTO.</param>
/// <param name="TunnelPort">Dynamically allocated tunnel port, or null for direct connections.</param>
public record RdpSessionResult(ServerProfileDto Server, int? TunnelPort = null) : Heimdall.Core.Models.ISessionResult;

/// <summary>Wraps an SSH.NET shell session.</summary>
public record SshSessionResult(Heimdall.Ssh.SshShellSession Session) : Heimdall.Core.Models.ISessionResult;

/// <summary>Wraps a terminal session (Plink pipe mode or ConPTY).</summary>
public record TerminalSessionResult(Heimdall.Terminal.ITerminalSession Session) : Heimdall.Core.Models.ISessionResult;

/// <summary>
/// Bundles an SFTP browser session with the SSH connection parameters needed for
/// sudo operations (edit files owned by root via <c>sudo cat</c> / <c>sudo tee</c>).
/// </summary>
public record SftpSessionBundle(SftpBrowser Browser, SshConnectionParams SshParams) : Heimdall.Core.Models.ISessionResult;

/// <summary>
/// Bundles a local shell terminal session with the resolved working directory
/// so the file browser panel can start at the same path as the shell.
/// </summary>
public record LocalShellBundle(
    Heimdall.Terminal.ITerminalSession? Session,
    string WorkingDirectory,
    string ShellExecutable,
    bool IsElevated = false,
    int? ExternalProcessId = null) : Heimdall.Core.Models.ISessionResult
{
    /// <summary>True when the shell was launched in a separate elevated window (no embedded terminal).</summary>
    public bool IsExternal => Session is null && ExternalProcessId is not null;
}
