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

using Heimdall.Core.Models;

namespace Heimdall.Core.StateMachine;

/// <summary>
/// Manages connection state transitions for multiple server connections.
/// Thread-safe: all state mutations are protected by a lock per connection entry.
/// </summary>
public sealed class ConnectionStateMachine
{
    private readonly Dictionary<string, ConnectionStateData> _connections = new();
    private readonly object _lock = new();

    private static readonly Dictionary<ConnectionState, HashSet<ConnectionState>> ValidTransitions = new()
    {
        [ConnectionState.Disconnected] = [ConnectionState.Initializing, ConnectionState.Error],
        [ConnectionState.Initializing] = [ConnectionState.ValidatingConfig, ConnectionState.Error, ConnectionState.Disconnected],
        [ConnectionState.ValidatingConfig] = [ConnectionState.EstablishingTunnel, ConnectionState.LaunchingRdp, ConnectionState.LaunchingSsh, ConnectionState.LaunchingSftp, ConnectionState.LaunchingFtp, ConnectionState.LaunchingLocal, ConnectionState.LaunchingVnc, ConnectionState.LaunchingTelnet, ConnectionState.LaunchingCitrix, ConnectionState.Error, ConnectionState.Disconnected],
        [ConnectionState.EstablishingTunnel] = [ConnectionState.TunnelEstablished, ConnectionState.Error, ConnectionState.Disconnected],
        [ConnectionState.TunnelEstablished] = [ConnectionState.LaunchingRdp, ConnectionState.LaunchingSsh, ConnectionState.LaunchingSftp, ConnectionState.LaunchingFtp, ConnectionState.LaunchingVnc, ConnectionState.LaunchingTelnet, ConnectionState.LaunchingCitrix, ConnectionState.Error, ConnectionState.Disconnecting],
        [ConnectionState.LaunchingRdp] = [ConnectionState.Connected, ConnectionState.Error, ConnectionState.Disconnecting, ConnectionState.Disconnected],
        [ConnectionState.LaunchingSsh] = [ConnectionState.Connected, ConnectionState.Error, ConnectionState.Disconnecting],
        [ConnectionState.LaunchingSftp] = [ConnectionState.Connected, ConnectionState.Error, ConnectionState.Disconnecting],
        [ConnectionState.LaunchingLocal] = [ConnectionState.Connected, ConnectionState.Error, ConnectionState.Disconnecting],
        [ConnectionState.LaunchingVnc] = [ConnectionState.Connected, ConnectionState.Error, ConnectionState.Disconnecting],
        [ConnectionState.LaunchingFtp] = [ConnectionState.Connected, ConnectionState.Error, ConnectionState.Disconnecting],
        [ConnectionState.LaunchingTelnet] = [ConnectionState.Connected, ConnectionState.Error, ConnectionState.Disconnecting],
        [ConnectionState.LaunchingCitrix] = [ConnectionState.Connected, ConnectionState.Error, ConnectionState.Disconnecting],
        [ConnectionState.Connected] = [ConnectionState.Disconnecting, ConnectionState.Disconnected, ConnectionState.Error],
        [ConnectionState.Disconnecting] = [ConnectionState.Disconnected, ConnectionState.Error],
        [ConnectionState.Error] = [ConnectionState.Disconnected, ConnectionState.Initializing],
    };

    private static readonly Dictionary<ConnectionState, StateMetadata> Metadata = new()
    {
        [ConnectionState.Disconnected] = new("StatusReady", "LogDisconnected", IsTerminal: false, AllowsUserAction: true, IsProgress: false),
        [ConnectionState.Initializing] = new("StatusConnectingProgress", "LogInitializing", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.ValidatingConfig] = new("StatusConnectingProgress", "LogValidating", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.EstablishingTunnel] = new("StatusEstablishingTunnel", "LogTunnelCreating", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.TunnelEstablished] = new("StatusTunnelEstablished", "LogTunnelCreated", IsTerminal: false, AllowsUserAction: true, IsProgress: false),
        [ConnectionState.LaunchingRdp] = new("StatusConnecting", "LogRdpLaunching", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.LaunchingSsh] = new("StatusLaunchingSsh", "LogSshLaunching", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.LaunchingSftp] = new("StatusLaunchingSftp", "LogSftpLaunching", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.LaunchingLocal] = new("StatusLaunchingLocal", "LogLocalShellLaunching", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.LaunchingVnc] = new("StatusVncConnecting", "LogVncLaunching", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.LaunchingFtp] = new("StatusFtpConnecting", "LogFtpLaunching", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.LaunchingTelnet] = new("StatusTelnetConnecting", "LogTelnetLaunching", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.LaunchingCitrix] = new("StatusCitrixConnecting", "LogCitrixLaunching", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.Connected] = new("StatusConnected", "LogRdpConnection", IsTerminal: false, AllowsUserAction: true, IsProgress: false),
        [ConnectionState.Disconnecting] = new("StatusDisconnecting", "LogDisconnecting", IsTerminal: false, AllowsUserAction: false, IsProgress: true),
        [ConnectionState.Error] = new("StatusError", "LogError", IsTerminal: false, AllowsUserAction: true, IsProgress: false),
    };

    /// <summary>
    /// Raised after a successful state transition.
    /// Parameters: serverId, previousState, newState, optional error message.
    /// </summary>
    public event Action<string, ConnectionState, ConnectionState, string?>? StateChanged;

    /// <summary>
    /// Gets the current state for a server, returning Disconnected if unknown.
    /// </summary>
    public ConnectionState GetState(string serverId)
    {
        lock (_lock)
        {
            return _connections.TryGetValue(serverId, out var data)
                ? data.CurrentState
                : ConnectionState.Disconnected;
        }
    }

    /// <summary>
    /// Gets the full state data for a server, or null if not tracked.
    /// Returns a snapshot copy to prevent external mutation.
    /// </summary>
    public ConnectionStateData? GetStateData(string serverId)
    {
        lock (_lock)
        {
            return _connections.TryGetValue(serverId, out var data)
                ? data.Snapshot()
                : null;
        }
    }

    /// <summary>
    /// Attempts to transition a server connection to a new state.
    /// Creates a new tracking entry if the server is not yet tracked.
    /// </summary>
    /// <returns>True if the transition was valid and applied.</returns>
    public bool TryTransition(string serverId, ConnectionState newState)
    {
        ConnectionState previousState;

        lock (_lock)
        {
            var data = GetOrCreate(serverId);
            if (!IsValidTransition(data.CurrentState, newState))
            {
                return false;
            }

            previousState = data.CurrentState;
            data.PreviousState = previousState;
            data.CurrentState = newState;
            data.LastTransitionUtc = DateTime.UtcNow;

            if (newState == ConnectionState.Initializing)
            {
                data.ConnectedAtUtc = null;
            }

            if (newState == ConnectionState.Connected)
            {
                data.ConnectedAtUtc = DateTime.UtcNow;
            }

            if (newState == ConnectionState.Disconnected)
            {
                data.ErrorMessage = null;
                data.TunnelLocalPort = null;
                data.TunnelProcessId = null;
                data.ConnectedAtUtc = null;
            }
        }

        StateChanged?.Invoke(serverId, previousState, newState, null);
        return true;
    }

    /// <summary>
    /// Transitions a server to the Error state with a message.
    /// If the current state does not allow transitioning to Error, this is a no-op
    /// returning false.
    /// </summary>
    public bool SetError(string serverId, string errorMessage)
    {
        ConnectionState previousState;

        lock (_lock)
        {
            var data = GetOrCreate(serverId);
            if (!IsValidTransition(data.CurrentState, ConnectionState.Error))
            {
                return false;
            }

            previousState = data.CurrentState;
            data.PreviousState = previousState;
            data.CurrentState = ConnectionState.Error;
            data.ErrorMessage = errorMessage;
            data.LastTransitionUtc = DateTime.UtcNow;
        }

        StateChanged?.Invoke(serverId, previousState, ConnectionState.Error, errorMessage);
        return true;
    }

    /// <summary>
    /// Stores tunnel information (local port and process ID) for a server connection.
    /// </summary>
    public void SetTunnelInfo(string serverId, int localPort, int processId)
    {
        lock (_lock)
        {
            var data = GetOrCreate(serverId);
            data.TunnelLocalPort = localPort;
            data.TunnelProcessId = processId;
        }
    }

    /// <summary>
    /// Resets a server to Disconnected, clearing all associated data.
    /// Performs intermediate transitions (Disconnecting) when required by the state table.
    /// </summary>
    public void Reset(string serverId)
    {
        ConnectionState previousState;

        lock (_lock)
        {
            if (!_connections.TryGetValue(serverId, out var data))
            {
                return;
            }

            if (data.CurrentState == ConnectionState.Disconnected)
            {
                return;
            }

            previousState = data.CurrentState;

            // From Error, go directly to Disconnected (Error cannot transition to Disconnecting)
            if (data.CurrentState != ConnectionState.Error
                && IsValidTransition(data.CurrentState, ConnectionState.Disconnecting))
            {
                data.PreviousState = data.CurrentState;
                data.CurrentState = ConnectionState.Disconnecting;
                data.LastTransitionUtc = DateTime.UtcNow;
            }

            data.PreviousState = data.CurrentState;
            data.CurrentState = ConnectionState.Disconnected;
            data.ErrorMessage = null;
            data.TunnelLocalPort = null;
            data.TunnelProcessId = null;
            data.ConnectedAtUtc = null;
            data.LastTransitionUtc = DateTime.UtcNow;
        }

        StateChanged?.Invoke(serverId, previousState, ConnectionState.Disconnected, null);
    }

    /// <summary>
    /// Removes a server from tracking entirely.
    /// </summary>
    public void Remove(string serverId)
    {
        lock (_lock)
        {
            _connections.Remove(serverId);
        }
    }

    /// <summary>
    /// Returns a snapshot of all currently tracked connections.
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionStateData> GetActiveConnections()
    {
        lock (_lock)
        {
            return _connections
                .Where(kv => kv.Value.CurrentState != ConnectionState.Disconnected
                          && kv.Value.CurrentState != ConnectionState.Error)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Snapshot());
        }
    }

    /// <summary>
    /// Returns server IDs in a specific state.
    /// </summary>
    public IEnumerable<string> GetServersByState(ConnectionState state)
    {
        lock (_lock)
        {
            return _connections
                .Where(kv => kv.Value.CurrentState == state)
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Checks whether a transition from one state to another is valid
    /// according to the static transition table.
    /// </summary>
    public static bool IsValidTransition(ConnectionState from, ConnectionState to)
    {
        return ValidTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// Returns the metadata associated with a connection state.
    /// </summary>
    public static StateMetadata GetMetadata(ConnectionState state)
    {
        return Metadata[state];
    }

    private ConnectionStateData GetOrCreate(string serverId)
    {
        if (!_connections.TryGetValue(serverId, out var data))
        {
            data = new ConnectionStateData();
            _connections[serverId] = data;
        }

        return data;
    }
}

/// <summary>
/// Holds the mutable state data for a single server connection.
/// </summary>
public sealed class ConnectionStateData
{
    public ConnectionState CurrentState { get; set; } = ConnectionState.Disconnected;
    public ConnectionState PreviousState { get; set; } = ConnectionState.Disconnected;
    public string? ErrorMessage { get; set; }
    public int? TunnelLocalPort { get; set; }
    public int? TunnelProcessId { get; set; }
    public DateTime? ConnectedAtUtc { get; set; }
    public DateTime LastTransitionUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a shallow copy of this state data for safe external consumption.
    /// </summary>
    internal ConnectionStateData Snapshot() => new()
    {
        CurrentState = CurrentState,
        PreviousState = PreviousState,
        ErrorMessage = ErrorMessage,
        TunnelLocalPort = TunnelLocalPort,
        TunnelProcessId = TunnelProcessId,
        ConnectedAtUtc = ConnectedAtUtc,
        LastTransitionUtc = LastTransitionUtc,
    };
}

/// <summary>
/// Immutable metadata describing a connection state's UI and behavioral properties.
/// </summary>
/// <param name="DisplayKey">i18n key for user-facing display text.</param>
/// <param name="LogKey">i18n key for log messages.</param>
/// <param name="IsTerminal">Whether this state represents a final endpoint.</param>
/// <param name="AllowsUserAction">Whether user interactions are permitted in this state.</param>
/// <param name="IsProgress">Whether this state indicates an ongoing operation.</param>
public record StateMetadata(
    string DisplayKey,
    string LogKey,
    bool IsTerminal,
    bool AllowsUserAction,
    bool IsProgress
);
