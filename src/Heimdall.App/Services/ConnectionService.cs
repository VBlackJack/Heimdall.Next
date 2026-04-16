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

using Heimdall.App.Services.Handlers;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Thin router over protocol-specific connection handlers.
/// Maintains shared settings state and preflight checks while delegating
/// per-protocol connection work to handler implementations.
/// </summary>
public sealed class ConnectionService : IConnectionService
{
    private bool _disposed;

    private readonly IConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly ITunnelService _tunnelService;
    private readonly Dictionary<string, IProtocolHandler> _handlers;
    private Action<string>? _setStatusText;

    /// <summary>Cached snapshot of the latest application settings.</summary>
    private AppSettings? _currentSettings;

    /// <summary>Returns the latest cached settings snapshot, if any.</summary>
    public AppSettings? CurrentSettings => _currentSettings;

    /// <summary>Relay wired by the shell to surface transient status messages.</summary>
    internal Action<string>? SetStatusText
    {
        get => _setStatusText;
        set
        {
            _setStatusText = value;
            if (_handlers.TryGetValue("SSH", out var handler) && handler is SshHandler sshHandler)
            {
                sshHandler.SetStatusText = value;
            }
        }
    }

    public ConnectionService(
        IConfigManager configManager,
        LocalizationManager localizer,
        ITunnelService tunnelService,
        IEnumerable<IProtocolHandler> handlers)
    {
        _configManager = configManager;
        _localizer = localizer;
        _tunnelService = tunnelService;
        _handlers = handlers.ToDictionary(h => h.Protocol, StringComparer.OrdinalIgnoreCase);

        if (_handlers.TryGetValue("SSH", out var handler) && handler is SshHandler sshHandler)
        {
            sshHandler.SetStatusText = _setStatusText;
        }

        _configManager.SettingsChanged += OnSettingsChanged;
        Core.Logging.FileLogger.Info($"ConnectionService: registered {_handlers.Count} protocol handler skeleton(s)");
    }

    /// <summary>Updates the cached settings when the configuration is saved.</summary>
    private void OnSettingsChanged(AppSettings newSettings)
    {
        _currentSettings = newSettings;
        _tunnelService.UpdateSettings(newSettings);
        Core.Logging.FileLogger.Info("ConnectionService: settings refreshed at runtime");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _configManager.SettingsChanged -= OnSettingsChanged;
    }

    /// <summary>Runs authentication preflight checks for a server's gateway.</summary>
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

        var connParams = ConnectionHelpers.CreateGatewayConnectionParams(gateway);
        bool isTunnel = server.ConnectionType?.Equals("RDP", StringComparison.OrdinalIgnoreCase) == true;
        return AuthPreflightChecker.Check(connParams, isTunnelMode: isTunnel);
    }

    // --- Protocol dispatch -------------------------------------------------

    public Task<ConnectionResult> ConnectRdpAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
        => DispatchAsync("RDP", server, settings, ct);

    public Task<ConnectionResult> ConnectSshAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
        => DispatchAsync("SSH", server, settings, ct);

    public Task<ConnectionResult> ConnectSftpAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
        => DispatchAsync("SFTP", server, settings, ct);

    public Task<ConnectionResult> ConnectVncAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
        => DispatchAsync("VNC", server, settings, ct);

    public Task<ConnectionResult> ConnectTelnetAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
        => DispatchAsync("TELNET", server, settings, ct);

    public Task<ConnectionResult> ConnectFtpAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
        => DispatchAsync("FTP", server, settings, ct);

    public Task<ConnectionResult> ConnectCitrixAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
        => DispatchAsync("CITRIX", server, settings, ct);

    public Task<ConnectionResult> ConnectLocalShellAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
        => DispatchAsync("LOCAL", server, settings, ct);

    private Task<ConnectionResult> DispatchAsync(
        string protocol,
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        if (_handlers.TryGetValue(protocol, out var handler))
        {
            return handler.ConnectAsync(server, settings, ct);
        }

        var message = _localizer.Format("ErrorUnsupportedConnectionType", protocol);
        Core.Logging.FileLogger.Error(message);
        return Task.FromResult(new ConnectionResult(false, message, null));
    }
}
