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

        // Check ALL panes in the split tree for connected status (not just the primary shim)
        var anyConnected = Core.Models.SplitTreeHelper.EnumerateLeaves(session.RootContent)
            .Any(p => string.Equals(p.Status, "Connected", StringComparison.Ordinal));
        if (anyConnected)
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
    /// Recursively cleans up all panes in the split tree.
    /// </summary>
    private void CloseSessionInternal(SessionTabViewModel session)
    {
        var leaves = Core.Models.SplitTreeHelper.EnumerateLeaves(session.RootContent).ToList();

        // Check CanClose for all tool panes before proceeding (any busy tool blocks the close)
        foreach (var pane in leaves)
        {
            if (pane.ConnectionType.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase)
                && pane.HostControl is Core.Models.IToolView toolView
                && !toolView.CanClose())
            {
                return;
            }
        }

        // Recursively clean up all panes in the tree (primary + all splits)
        foreach (var pane in leaves)
        {
            // Tool panes bypass ConnectionStateMachine, history, and tunnels
            if (!pane.ConnectionType.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(pane.ServerId))
            {
                var historyId = !string.IsNullOrEmpty(pane.OriginalServerId)
                    ? pane.OriginalServerId : pane.ServerId;
                Core.Logging.ConnectionHistory.RecordDisconnect(
                    historyId, pane.Title, pane.ConnectionType);

                var stateData = _connectionSm.GetStateData(pane.ServerId);
                if (stateData?.TunnelLocalPort is int localPort)
                {
                    _tunnelManager.ReleaseReference(localPort);
                }

                _connectionSm.Reset(pane.ServerId);
            }

            SafeDispose(pane.HostControl as IDisposable);
            pane.HostControl = null;
        }

        ActiveSessions.Remove(session);

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
