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

namespace Heimdall.App.ViewModels;

/// <summary>
/// Manages active embedded sessions (RDP, SSH, SFTP) displayed as tabs.
/// </summary>
public partial class ConnectionViewModel : ObservableObject
{
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;
    private readonly ConfigManager _configManager;

    [ObservableProperty]
    private ObservableCollection<SessionTabViewModel> _activeSessions = [];

    [ObservableProperty]
    private SessionTabViewModel? _activeSession;

    [ObservableProperty]
    private bool _hasActiveSessions;

    public ConnectionViewModel(
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer,
        ConfigManager configManager)
    {
        _connectionSm = connectionSm;
        _localizer = localizer;
        _configManager = configManager;
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
            _connectionSm.Reset(session.ServerId);
        }

        ActiveSessions.Clear();
        ActiveSession = null;
        HasActiveSessions = false;
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        // Fullscreen toggle will be implemented in Phase 4B with the view layer
    }
}
