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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;

namespace Heimdall.App.ViewModels;

/// <summary>
/// Manages active embedded sessions (RDP, SSH, SFTP) displayed as tabs.
/// </summary>
public partial class ConnectionViewModel : ObservableObject
{
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;
    private readonly ConfigManager _configManager;
    private readonly TunnelManager _tunnelManager;

    [ObservableProperty]
    private ObservableCollection<SessionTabViewModel> _activeSessions = [];

    [ObservableProperty]
    private SessionTabViewModel? _activeSession;

    [ObservableProperty]
    private bool _hasActiveSessions;

    public ConnectionViewModel(
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer,
        ConfigManager configManager,
        TunnelManager tunnelManager)
    {
        _connectionSm = connectionSm;
        _localizer = localizer;
        _configManager = configManager;
        _tunnelManager = tunnelManager;
    }

    /// <summary>
    /// Adds a new session tab for the given server.
    /// </summary>
    public SessionTabViewModel AddSession(string serverId, string title, string connectionType)
    {
        var session = new SessionTabViewModel
        {
            ServerId = serverId,
            Title = title,
            ConnectionType = connectionType,
            Status = "Connecting",
        };

        ActiveSessions.Add(session);
        ActiveSession = session;
        HasActiveSessions = ActiveSessions.Count > 0;

        return session;
    }

    [RelayCommand]
    private void CloseSession(SessionTabViewModel? session)
    {
        if (session is null)
        {
            return;
        }

        // Close tunnel if the server has one bound to a local port
        var stateData = _connectionSm.GetStateData(session.ServerId);
        if (stateData?.TunnelLocalPort is int localPort)
        {
            _tunnelManager.CloseTunnel(localPort);
        }

        // Dispose secondary pane first if split
        if (session.IsSplit && session.SecondaryHostControl is IDisposable secondaryDisposable)
        {
            try { secondaryDisposable.Dispose(); }
            catch (ObjectDisposedException) { }
            session.SecondaryHostControl = null;
            session.IsSplit = false;
        }

        // Dispose the primary host control
        if (session.HostControl is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch (ObjectDisposedException) { }
        }

        ActiveSessions.Remove(session);
        _connectionSm.Reset(session.ServerId);

        if (ActiveSession == session)
        {
            ActiveSession = ActiveSessions.LastOrDefault();
        }

        HasActiveSessions = ActiveSessions.Count > 0;
    }

    [RelayCommand]
    private void CloseAllSessions()
    {
        foreach (var session in ActiveSessions.ToList())
        {
            // Close tunnels
            var stateData = _connectionSm.GetStateData(session.ServerId);
            if (stateData?.TunnelLocalPort is int localPort)
            {
                _tunnelManager.CloseTunnel(localPort);
            }

            // Dispose secondary pane first if split
            if (session.IsSplit && session.SecondaryHostControl is IDisposable secondaryDisposable)
            {
                try { secondaryDisposable.Dispose(); }
                catch (ObjectDisposedException) { }
                session.SecondaryHostControl = null;
                session.IsSplit = false;
            }

            // Dispose primary host control
            if (session.HostControl is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (ObjectDisposedException) { }
            }

            _connectionSm.Reset(session.ServerId);
        }

        ActiveSessions.Clear();
        ActiveSession = null;
        HasActiveSessions = false;
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        // Fullscreen toggle will be implemented in Phase 5B with the view layer
    }
}
