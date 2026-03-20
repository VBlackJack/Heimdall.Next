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
using Heimdall.App.Services;
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
    private readonly IDialogService _dialogService;

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
        TunnelManager tunnelManager,
        IDialogService dialogService)
    {
        _connectionSm = connectionSm;
        _localizer = localizer;
        _configManager = configManager;
        _tunnelManager = tunnelManager;
        _dialogService = dialogService;
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
    private async Task CloseSession(SessionTabViewModel? session)
    {
        if (session is null)
        {
            return;
        }

        if (string.Equals(session.Status, "Connected", StringComparison.Ordinal))
        {
            var title = _localizer["ConfirmCloseSessionTitle"];
            var message = _localizer.Format("ConfirmCloseSessionMessage", session.Title);
            var confirmed = await _dialogService.ShowConfirmAsync(title, message, "warning");
            if (!confirmed)
            {
                return;
            }
        }

        CloseSessionInternal(session);
    }

    /// <summary>
    /// Closes a session without showing a confirmation dialog.
    /// Used by <see cref="CloseAllSessions"/> to avoid multiple prompts.
    /// </summary>
    private void CloseSessionInternal(SessionTabViewModel session)
    {
        // Tool tabs bypass ConnectionStateMachine, history, and tunnels
        if (session.ConnectionType.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase))
        {
            SafeDispose(session.HostControl as IDisposable);
            ActiveSessions.Remove(session);

            if (ActiveSession == session)
            {
                ActiveSession = ActiveSessions.LastOrDefault();
            }

            HasActiveSessions = ActiveSessions.Count > 0;
            return;
        }

        var historyId = !string.IsNullOrEmpty(session.OriginalServerId)
            ? session.OriginalServerId : session.ServerId;
        Core.Logging.ConnectionHistory.RecordDisconnect(
            historyId, session.Title, session.ConnectionType);

        // Close tunnel if the server has one bound to a local port
        var stateData = _connectionSm.GetStateData(session.ServerId);
        if (stateData?.TunnelLocalPort is int localPort)
        {
            _tunnelManager.ReleaseReference(localPort);
        }

        // Dispose secondary pane first if split
        if (session.IsSplit)
        {
            // Record disconnect and clean up state for the secondary session
            if (!string.IsNullOrEmpty(session.SecondaryServerId))
            {
                var secondaryHistoryId = !string.IsNullOrEmpty(session.SecondaryOriginalServerId)
                    ? session.SecondaryOriginalServerId : session.SecondaryServerId;
                Core.Logging.ConnectionHistory.RecordDisconnect(
                    secondaryHistoryId,
                    session.SecondaryTitle,
                    session.SecondaryConnectionType);

                var secondaryStateData = _connectionSm.GetStateData(session.SecondaryServerId);
                if (secondaryStateData?.TunnelLocalPort is int secondaryPort)
                {
                    _tunnelManager.ReleaseReference(secondaryPort);
                }

                _connectionSm.Reset(session.SecondaryServerId);
            }

            SafeDispose(session.SecondaryHostControl as IDisposable);
            session.SecondaryHostControl = null;
            session.SecondaryServerId = "";
            session.SecondaryConnectionType = "";
            session.SecondaryTitle = "";
            session.SecondaryStatus = "";
            session.SecondaryTunnelRoute = "";
            session.SecondaryEnvironmentColor = "";
            session.IsSplit = false;
        }

        SafeDispose(session.HostControl as IDisposable);

        ActiveSessions.Remove(session);
        _connectionSm.Reset(session.ServerId);

        if (ActiveSession == session)
        {
            ActiveSession = ActiveSessions.LastOrDefault();
        }

        HasActiveSessions = ActiveSessions.Count > 0;
    }

    [RelayCommand]
    private async Task CloseAllSessions()
    {
        // Count connected sessions to decide whether to prompt
        var connectedCount = ActiveSessions.Count(s =>
            string.Equals(s.Status, "Connected", StringComparison.Ordinal));

        if (connectedCount > 0)
        {
            var title = _localizer["ConfirmCloseAllTabs"];
            var message = _localizer.Format("ConfirmCloseAllTabsMessage", connectedCount);
            var confirmed = await _dialogService.ShowConfirmAsync(title, message, "warning");
            if (!confirmed)
            {
                return;
            }
        }

        foreach (var session in ActiveSessions.ToList())
        {
            CloseSessionInternal(session);
        }
    }

    /// <summary>
    /// Safely disposes a host control, ignoring ObjectDisposedException
    /// which is expected when tearing down already-closed controls.
    /// </summary>
    private static void SafeDispose(IDisposable? disposable)
    {
        if (disposable is null) return;
        try { disposable.Dispose(); }
        catch (ObjectDisposedException) { /* Expected when disposing already-closed host controls */ }
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        // Fullscreen toggle will be implemented in Phase 5B with the view layer
    }
}
