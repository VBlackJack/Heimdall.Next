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

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Manages split/merge operations for session tabs. Handles tree mutations,
/// per-session cancellation, tunnel lifecycle, and layout persistence.
/// </summary>
public sealed class SplitService
{
    public const int MaxPanesPerTab = 8;

    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly TunnelManager _tunnelManager;
    private readonly EmbeddedSessionManager _sessionManager;
    private readonly ConnectionService _connectionService;
    private readonly ToolRegistry _toolRegistry;

    /// <summary>
    /// Per-session cancellation tokens. Cancelled when a session tab is closed
    /// to abort in-progress split/reconnect operations gracefully.
    /// </summary>
    private readonly ConcurrentDictionary<SessionTabViewModel, CancellationTokenSource> _sessionCts = new();

    /// <summary>Split layout persistence for paired server suggestions.</summary>
    public SplitLayoutMemory LayoutMemory { get; }

    // ── Callbacks wired by MainViewModel after construction ──────────
    // Follows the same pattern as EmbeddedSessionManager's callback properties.

    internal Func<ObservableCollection<SessionTabViewModel>>? ActiveSessionsProvider { get; set; }
    internal Func<SessionTabViewModel?>? ActiveSessionProvider { get; set; }
    internal Action<SessionTabViewModel?>? SetActiveSession { get; set; }
    internal Action<bool>? SetHasActiveSessions { get; set; }
    internal Action<string>? SetStatusText { get; set; }

    public SplitService(
        ConfigManager configManager,
        LocalizationManager localizer,
        ConnectionStateMachine connectionSm,
        TunnelManager tunnelManager,
        EmbeddedSessionManager sessionManager,
        ConnectionService connectionService,
        ToolRegistry toolRegistry)
    {
        _configManager = configManager;
        _localizer = localizer;
        _connectionSm = connectionSm;
        _tunnelManager = tunnelManager;
        _sessionManager = sessionManager;
        _connectionService = connectionService;
        _toolRegistry = toolRegistry;

        LayoutMemory = new SplitLayoutMemory(configManager.ConfigPath);
    }

    // ── Session lifecycle ────────────────────────────────────────────

    /// <summary>
    /// Registers a session for cancellation tracking. Call when a new tab is created.
    /// </summary>
    public void RegisterSession(SessionTabViewModel session)
    {
        _sessionCts.TryAdd(session, new CancellationTokenSource());
    }

    /// <summary>
    /// Cancels in-progress split/reconnect operations and unregisters the session.
    /// Call before closing a tab to ensure async operations are aborted.
    /// The CTS is disposed after a short delay so in-flight operations can
    /// observe the cancellation token before the source is reclaimed.
    /// </summary>
    public void CancelSession(SessionTabViewModel session)
    {
        if (_sessionCts.TryRemove(session, out var cts))
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* Already disposed */ }

            // Deferred dispose: in-flight operations still hold token references
            // that must remain valid long enough for guard checks to observe cancellation.
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            {
                try { cts.Dispose(); }
                catch (ObjectDisposedException) { }
            }, TaskScheduler.Default);
        }
    }

    internal CancellationToken GetSessionToken(SessionTabViewModel session)
    {
        return _sessionCts.TryGetValue(session, out var cts) ? cts.Token : CancellationToken.None;
    }

    // ── Split with server (async connection) ─────────────────────────

    /// <summary>
    /// Connects to a server and inserts it as a sibling pane next to the target pane.
    /// The connection runs asynchronously with cancellation support and post-await guards.
    /// </summary>
    public async Task SplitSessionWithServerAsync(
        SessionTabViewModel session,
        string serverId,
        SplitOrientation orientation,
        string? paneId = null)
    {
        var ct = GetSessionToken(session);
        try
        {
            var targetPane = ResolvePaneOrPrimary(session, paneId);
            if (targetPane is null)
            {
                Core.Logging.FileLogger.Warn(
                    $"Split aborted: target pane '{paneId}' not found in session '{session.Title}'.");
                return;
            }

            if (SplitTreeHelper.CountLeaves(session.RootContent) >= MaxPanesPerTab)
            {
                SetStatusText?.Invoke(_localizer["SplitMaxPanesReached"]);
                Core.Logging.FileLogger.Info(
                    $"Split rejected: max {MaxPanesPerTab} panes reached for '{session.Title}'.");
                return;
            }

            var servers = await _configManager.LoadServersAsync();
            var settings = await _configManager.LoadSettingsAsync();
            ct.ThrowIfCancellationRequested();

            var serverDto = servers.FirstOrDefault(
                s => string.Equals(s.Id, serverId, StringComparison.Ordinal));
            if (serverDto is null)
            {
                Core.Logging.FileLogger.Warn(
                    $"Split aborted: server '{serverId}' not found in inventory.");
                return;
            }

            ForceEmbeddedMode(serverDto);

            // Create loading pane and insert into tree immediately (shows loading overlay).
            // OriginalServerId set early for proper cleanup if pane is closed during connection.
            var newPane = new SessionPaneModel
            {
                ServerId = "",
                OriginalServerId = serverId,
                ConnectionType = serverDto.ConnectionType ?? "",
                Title = serverDto.DisplayName,
                Status = _localizer["SplitSecondaryConnecting"],
            };
            var container = new SplitContainerModel
            {
                First = targetPane,
                Second = newPane,
                Orientation = orientation,
                SplitRatio = SplitContainerModel.DefaultRatio
            };
            session.RootContent = SplitTreeHelper.ReplacePane(
                session.RootContent, targetPane.PaneId, container);

            // Async connection — can be cancelled or session can be closed while waiting
            var result = await ConnectByProtocolAsync(serverDto, settings, ct);

            if (!result.Success || result.Session is null)
            {
                session.RootContent = SplitTreeHelper.RemovePane(
                    session.RootContent, newPane.PaneId) ?? session.PrimaryPane;

                SetStatusText?.Invoke(result.ErrorMessage ?? _localizer["ErrorSplitSessionFailed"]);
                Core.Logging.FileLogger.Warn(
                    $"Split connection failed for '{serverDto.DisplayName}': {result.ErrorMessage}");
                return;
            }

            // Post-await guard: verify session and pane still exist after async connection.
            var activeSessions = ActiveSessionsProvider?.Invoke();
            if (activeSessions is null
                || !activeSessions.Contains(session)
                || SplitTreeHelper.FindPane(session.RootContent, newPane.PaneId) is null)
            {
                SafeDispose(result.Session as IDisposable);
                CleanupOrphanedPane(serverId);
                Core.Logging.FileLogger.Info(
                    $"Split cancelled for '{serverDto.DisplayName}' — session or pane removed during connection.");
                return;
            }

            var hostControl = _sessionManager.CreateHostControl(
                session, serverDto.DisplayName, serverDto.ConnectionType ?? "SSH",
                result.Session, settings);

            newPane.HostControl = hostControl;
            newPane.ServerId = serverDto.Id;
            newPane.Status = "Connected";

            LayoutMemory.Record(
                targetPane.OriginalServerId, serverDto.Id,
                orientation, container.SplitRatio);

            Core.Logging.FileLogger.Info(
                $"Split session '{session.Title}' with '{serverDto.DisplayName}' as {orientation}.");
        }
        catch (OperationCanceledException)
        {
            Core.Logging.FileLogger.Info(
                $"Split cancelled for session '{session.Title}' — tab closed during connection.");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Split session error: {ex.Message}", ex);
            SetStatusText?.Invoke(_localizer["ErrorSplitSessionFailed"] + $" — {ex.Message}");
        }
    }

    // ── Split with tool (synchronous) ────────────────────────────────

    /// <summary>
    /// Docks a built-in tool into a new pane. No network connection needed.
    /// </summary>
    public void SplitSessionWithTool(
        SessionTabViewModel session,
        string paletteToolPayload,
        SplitOrientation orientation,
        string? paneId = null)
    {
        var pipeIndex = paletteToolPayload.IndexOf('|');
        var toolId = pipeIndex >= 0 ? paletteToolPayload[..pipeIndex] : paletteToolPayload;
        var argument = pipeIndex >= 0 ? paletteToolPayload[(pipeIndex + 1)..] : null;

        var descriptor = _toolRegistry.GetById(toolId);
        if (descriptor is null) return;

        var targetPane = ResolvePaneOrPrimary(session, paneId);
        if (targetPane is null) return;

        if (SplitTreeHelper.CountLeaves(session.RootContent) >= MaxPanesPerTab)
        {
            SetStatusText?.Invoke(_localizer["SplitMaxPanesReached"]);
            return;
        }

        var connectionType = $"TOOL:{toolId.ToUpperInvariant()}";
        var title = _localizer[descriptor.LabelKey];
        ToolContext? context = !string.IsNullOrEmpty(argument)
            ? new ToolContext(Argument: argument)
            : null;

        var newPane = new SessionPaneModel
        {
            ServerId = $"tool-{toolId.ToLowerInvariant()}-{Guid.NewGuid():N}",
            ConnectionType = connectionType,
            Title = title,
            Status = _localizer["StatusReady"],
        };

        newPane.HostControl = _sessionManager.CreateToolControl(
            session, toolId, context, null);

        var container = new SplitContainerModel
        {
            First = targetPane,
            Second = newPane,
            Orientation = orientation,
            SplitRatio = SplitContainerModel.DefaultRatio
        };
        session.RootContent = SplitTreeHelper.ReplacePane(
            session.RootContent, targetPane.PaneId, container);

        Core.Logging.FileLogger.Info(
            $"Split session '{session.Title}' with tool '{title}' as {orientation}.");
    }

    // ── Merge existing session ───────────────────────────────────────

    /// <summary>
    /// Merges an existing session tab into the target session's split tree.
    /// Reparents the source tab's content without reconnecting.
    /// </summary>
    public void MergeExistingSession(
        SessionTabViewModel target,
        string sourceSessionId,
        SplitOrientation orientation,
        string? targetPaneId = null)
    {
        var activeSessions = ActiveSessionsProvider?.Invoke();
        if (activeSessions is null) return;

        var source = activeSessions.FirstOrDefault(
            s => string.Equals(s.ServerId, sourceSessionId, StringComparison.Ordinal)
                 || string.Equals(s.OriginalServerId, sourceSessionId, StringComparison.Ordinal));

        // Check any leaf has a host control (not just the primary shim),
        // because in a split tab the primary pane may be disconnected while others are active.
        var sourceHasContent = source is not null && source != target
            && SplitTreeHelper.EnumerateLeaves(source.RootContent).Any(p => p.HostControl is not null);

        if (source is null || source == target || !sourceHasContent)
        {
            SetStatusText?.Invoke(_localizer["ErrorSplitSessionFailed"]);
            Core.Logging.FileLogger.Warn(
                $"Merge aborted: source '{sourceSessionId}' not found, same as target, or has no host control.");
            return;
        }

        var sourceLeafCount = SplitTreeHelper.CountLeaves(source.RootContent);
        if (SplitTreeHelper.CountLeaves(target.RootContent) + sourceLeafCount > MaxPanesPerTab)
        {
            SetStatusText?.Invoke(_localizer["SplitMaxPanesReached"]);
            return;
        }

        var targetPane = ResolvePaneOrPrimary(target, targetPaneId);
        if (targetPane is null) return;

        // Check CanClose for tool panes in the source tree (busy tool blocks the merge)
        foreach (var leaf in SplitTreeHelper.EnumerateLeaves(source.RootContent))
        {
            if (leaf.ConnectionType.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase)
                && leaf.HostControl is IToolView toolView
                && !toolView.CanClose())
            {
                SetStatusText?.Invoke(_localizer["SplitMergeBlockedByTool"]);
                Core.Logging.FileLogger.Info(
                    $"Merge blocked: source tool '{leaf.Title}' reports CanClose()=false.");
                return;
            }
        }

        // Step 1: Save all host control references from the source tree and detach
        // them from the WPF visual tree (UIElement single-parent rule).
        var hostControls = new Dictionary<string, object?>();
        foreach (var pane in SplitTreeHelper.EnumerateLeaves(source.RootContent))
        {
            hostControls[pane.PaneId] = pane.HostControl;
            pane.HostControl = null;
        }

        // Step 2: Detach source from the tab system
        var sourceContent = source.RootContent;
        source.RootContent = new SessionPaneModel();
        activeSessions.Remove(source);
        if (ActiveSessionProvider?.Invoke() == source)
            SetActiveSession?.Invoke(target);
        SetHasActiveSessions?.Invoke(activeSessions.Count > 0);

        // Cancel any in-progress operations for the source session
        CancelSession(source);

        // Step 3: Wrap target pane and source content in a new split container.
        // Restore prior ratio from split layout memory if available.
        var sourceOrigId = SplitTreeHelper.FirstLeaf(sourceContent)?.OriginalServerId ?? "";
        var priorLayout = LayoutMemory.FindPartner(targetPane.OriginalServerId);
        var mergeRatio = priorLayout is not null
            && (string.Equals(priorLayout.SecondaryServerId, sourceOrigId, StringComparison.Ordinal)
                || string.Equals(priorLayout.PrimaryServerId, sourceOrigId, StringComparison.Ordinal))
            ? priorLayout.Ratio
            : SplitContainerModel.DefaultRatio;

        var container = new SplitContainerModel
        {
            First = targetPane,
            Second = sourceContent,
            Orientation = orientation,
            SplitRatio = mergeRatio
        };
        target.RootContent = SplitTreeHelper.ReplacePane(
            target.RootContent, targetPane.PaneId, container);

        // Step 4: Restore host controls now that panes are in the new tree
        foreach (var (id, control) in hostControls)
        {
            var pane = SplitTreeHelper.FindPane(target.RootContent, id);
            if (pane is not null)
            {
                pane.HostControl = control;
            }
            else
            {
                Core.Logging.FileLogger.Warn(
                    $"Merge: orphaned host control for pane '{id}' — pane not found after reparent.");
            }
        }

        var sourceTitle = SplitTreeHelper.FirstLeaf(sourceContent)?.Title ?? "";
        Core.Logging.FileLogger.Info(
            $"Merged session '{sourceTitle}' into '{target.Title}' as {orientation} split.");

        var sourceOriginalId = SplitTreeHelper.FirstLeaf(sourceContent)?.OriginalServerId ?? "";
        LayoutMemory.Record(
            targetPane.OriginalServerId, sourceOriginalId,
            orientation, container.SplitRatio);
    }

    // ── Close pane ───────────────────────────────────────────────────

    /// <summary>
    /// Closes a specific pane in the split tree. Releases tunnel, resets state machine,
    /// and promotes the sibling. Fixed disposal order: detach → remove → dispose.
    /// </summary>
    public void ClosePane(SessionTabViewModel session, string paneId)
    {
        var pane = SplitTreeHelper.FindPane(session.RootContent, paneId);
        if (pane is null)
        {
            Core.Logging.FileLogger.Info(
                $"ClosePane: pane '{paneId}' not found — already removed or double-close.");
            return;
        }

        var isToolPane = pane.ConnectionType.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase);

        if (isToolPane)
        {
            if (pane.HostControl is IToolView toolView && !toolView.CanClose())
            {
                Core.Logging.FileLogger.Info(
                    $"ClosePane blocked: tool '{pane.Title}' reports CanClose()=false.");
                return;
            }
        }
        else if (!string.IsNullOrEmpty(pane.ServerId))
        {
            var historyId = !string.IsNullOrEmpty(pane.OriginalServerId)
                ? pane.OriginalServerId : pane.ServerId;
            Core.Logging.ConnectionHistory.RecordDisconnect(
                historyId, pane.Title, pane.ConnectionType);

            var stateData = _connectionSm.GetStateData(pane.ServerId);
            if (stateData?.TunnelLocalPort is int localPort)
                _tunnelManager.ReleaseReference(localPort);
            _connectionSm.Reset(pane.ServerId);
        }

        // Fixed disposal order: detach from visual tree FIRST, then remove from tree,
        // then dispose. Prevents RDP/ActiveX airspace issues during disposal.
        var hostControl = pane.HostControl;
        pane.HostControl = null;

        var newRoot = SplitTreeHelper.RemovePane(session.RootContent, paneId);
        session.RootContent = newRoot ?? new SessionPaneModel();

        SafeDispose(hostControl as IDisposable);
    }

    // ── Reconnect pane (async) ───────────────────────────────────────

    /// <summary>
    /// Reconnects a pane by closing its old connection and re-establishing it.
    /// Supports cancellation when the session tab is closed during reconnection.
    /// </summary>
    public async Task ReconnectPaneAsync(SessionTabViewModel session, string paneId)
    {
        var ct = GetSessionToken(session);
        var pane = SplitTreeHelper.FindPane(session.RootContent, paneId);
        if (pane is null) return;

        // Guard: if HostControl is already null, a reconnect is in progress
        if (pane.HostControl is null)
        {
            Core.Logging.FileLogger.Info(
                $"ReconnectPane skipped: pane '{paneId}' already reconnecting (HostControl is null).");
            return;
        }

        var serverId = pane.OriginalServerId;
        if (string.IsNullOrEmpty(serverId))
        {
            SetStatusText?.Invoke(_localizer["ErrorSplitSessionFailed"]);
            Core.Logging.FileLogger.Warn(
                $"ReconnectPane failed: pane '{paneId}' has no OriginalServerId.");
            return;
        }

        // Save old connection state for deferred cleanup (released only after successful reconnect)
        var oldServerId = pane.ServerId;

        // Dispose current host control (detach first)
        var oldHostControl = pane.HostControl;
        pane.HostControl = null;
        pane.Status = _localizer["SplitSecondaryConnecting"];
        SafeDispose(oldHostControl as IDisposable);

        var parent = SplitTreeHelper.FindParent(session.RootContent, paneId);
        var orientation = parent?.Orientation ?? SplitOrientation.Vertical;

        try
        {
            var servers = await _configManager.LoadServersAsync();
            var settings = await _configManager.LoadSettingsAsync();
            ct.ThrowIfCancellationRequested();

            var serverDto = servers.FirstOrDefault(
                s => string.Equals(s.Id, serverId, StringComparison.Ordinal));

            if (serverDto is null)
            {
                pane.Status = "Error";
                // Release old state on failure (nothing new to preserve)
                ReleaseOldConnectionState(oldServerId);
                Core.Logging.FileLogger.Warn(
                    $"ReconnectPane failed: server '{serverId}' no longer in inventory.");
                return;
            }

            ForceEmbeddedMode(serverDto);

            var result = await ConnectByProtocolAsync(serverDto, settings, ct);

            if (!result.Success || result.Session is null)
            {
                pane.Status = "Error";
                ReleaseOldConnectionState(oldServerId);
                SetStatusText?.Invoke(result.ErrorMessage ?? _localizer["ErrorSplitSessionFailed"]);
                Core.Logging.FileLogger.Warn(
                    $"ReconnectPane failed for '{serverDto.DisplayName}': {result.ErrorMessage}");
                return;
            }

            // Post-await guard
            var activeSessions = ActiveSessionsProvider?.Invoke();
            if (activeSessions is null
                || !activeSessions.Contains(session)
                || SplitTreeHelper.FindPane(session.RootContent, paneId) is null)
            {
                SafeDispose(result.Session as IDisposable);
                CleanupOrphanedPane(serverDto.Id);
                ReleaseOldConnectionState(oldServerId);
                Core.Logging.FileLogger.Info(
                    $"ReconnectPane cancelled for '{serverDto.DisplayName}' — session or pane removed.");
                return;
            }

            // Success: release old state AFTER new connection is confirmed
            ReleaseOldConnectionState(oldServerId);

            var hostControl = _sessionManager.CreateHostControl(
                session, serverDto.DisplayName, serverDto.ConnectionType ?? "SSH",
                result.Session, settings);

            pane.HostControl = hostControl;
            pane.ServerId = serverDto.Id;
            pane.Status = "Connected";

            // No LayoutMemory.Record here — reconnect targets the same server,
            // and the pair was already recorded when the split was first created.
        }
        catch (OperationCanceledException)
        {
            ReleaseOldConnectionState(oldServerId);
            Core.Logging.FileLogger.Info(
                $"ReconnectPane cancelled for session '{session.Title}' — tab closed during reconnection.");
        }
    }

    // ── Swap panes ───────────────────────────────────────────────────

    /// <summary>
    /// Swaps the First and Second children of a pane's parent container.
    /// </summary>
    public async Task SwapSplitPanesAsync(SessionTabViewModel session, string? paneId = null)
    {
        if (!session.IsSplit) return;

        var container = !string.IsNullOrEmpty(paneId)
            ? SplitTreeHelper.FindParent(session.RootContent, paneId)
            : session.RootContent as SplitContainerModel;

        if (container is null) return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        // Detach all host controls (UIElement single-parent rule)
        var hostControls = new Dictionary<string, object?>();
        foreach (var pane in SplitTreeHelper.EnumerateLeaves(container))
        {
            hostControls[pane.PaneId] = pane.HostControl;
            pane.HostControl = null;
        }

        // Let the current SessionPaneControls process HostControl=null and
        // release their visual children before the subtree swap. Doing the
        // detach and swap in one dispatcher turn still races WebView2/ActiveX
        // reparenting because WPF has not stabilized the intermediate state yet.
        await AwaitVisualTreeAsync(dispatcher);

        (container.First, container.Second) = (container.Second, container.First);
        session.NotifyShimPropertiesChanged();

        // Let ContentPresenters finish rebinding / recreating their template
        // children against the swapped tree before reattaching the live hosts.
        await AwaitVisualTreeAsync(dispatcher);

        foreach (var (id, control) in hostControls)
        {
            var pane = SplitTreeHelper.FindPane(session.RootContent, id);
            if (pane is not null) pane.HostControl = control;
        }

        // The primary/secondary shim properties are computed from the tree and
        // do not forward leaf PropertyChanged automatically. Nudge them again
        // after restore so tab overlays / headers observe the new host owner.
        session.NotifyShimPropertiesChanged();

        Core.Logging.FileLogger.Info(
            string.Format(_localizer["LogSplitSwapped"], session.Title));
    }

    // ── Toggle orientation ───────────────────────────────────────────

    /// <summary>
    /// Toggles the orientation of a pane's parent container (Horizontal ↔ Vertical).
    /// </summary>
    public void ToggleSplitOrientation(SessionTabViewModel session, string? paneId = null)
    {
        if (!session.IsSplit) return;

        var container = !string.IsNullOrEmpty(paneId)
            ? SplitTreeHelper.FindParent(session.RootContent, paneId)
            : session.RootContent as SplitContainerModel;

        if (container is null) return;

        container.Orientation = container.Orientation == SplitOrientation.Horizontal
            ? SplitOrientation.Vertical
            : SplitOrientation.Horizontal;

        Core.Logging.FileLogger.Info(
            string.Format(_localizer["LogSplitOrientationToggled"],
                session.Title, container.Orientation));
    }

    // ── Cleanup ──────────────────────────────────────────────────────

    /// <summary>
    /// Releases tunnel reference and resets state machine for a pane that was
    /// orphaned by tab close or detach while still connecting.
    /// </summary>
    public void CleanupOrphanedPane(string serverId)
    {
        if (string.IsNullOrEmpty(serverId)) return;

        var stateData = _connectionSm.GetStateData(serverId);
        if (stateData?.TunnelLocalPort is int port)
            _tunnelManager.ReleaseReference(port);
        _connectionSm.Reset(serverId);

        Core.Logging.FileLogger.Info(
            $"Cleaned up orphaned pane resources for server '{serverId}'.");
    }

    // ── Close all panes (tab teardown) ─────────────────────────────

    /// <summary>
    /// Tears down all panes in the session tree: releases tunnels, resets state machines,
    /// records disconnect history, and disposes host controls. Returns false if a busy
    /// tool pane blocked the close.
    /// </summary>
    public bool CloseAllPanes(SessionTabViewModel session)
    {
        var leaves = SplitTreeHelper.EnumerateLeaves(session.RootContent).ToList();

        // Check CanClose for all tool panes before proceeding (any busy tool blocks the close)
        foreach (var pane in leaves)
        {
            if (pane.ConnectionType.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase)
                && pane.HostControl is IToolView toolView
                && !toolView.CanClose())
            {
                return false;
            }
        }

        CancelSession(session);

        foreach (var pane in leaves)
        {
            if (!pane.ConnectionType.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(pane.ServerId))
            {
                var historyId = !string.IsNullOrEmpty(pane.OriginalServerId)
                    ? pane.OriginalServerId : pane.ServerId;
                Core.Logging.ConnectionHistory.RecordDisconnect(
                    historyId, pane.Title, pane.ConnectionType);

                var stateData = _connectionSm.GetStateData(pane.ServerId);
                if (stateData?.TunnelLocalPort is int localPort)
                    _tunnelManager.ReleaseReference(localPort);

                _connectionSm.Reset(pane.ServerId);
            }

            // Fixed disposal order: detach from visual tree, then dispose
            var hostControl = pane.HostControl;
            pane.HostControl = null;
            SafeDispose(hostControl as IDisposable);
        }

        return true;
    }

    // ── Private helpers ──────────────────────────────────────────────

    private SessionPaneModel? ResolvePaneOrPrimary(SessionTabViewModel session, string? paneId)
    {
        return !string.IsNullOrEmpty(paneId)
            ? SplitTreeHelper.FindPane(session.RootContent, paneId)
            : session.PrimaryPane;
    }

    /// <summary>
    /// Routes a connection attempt to the correct protocol handler.
    /// Deduplicates the switch statement used by split and reconnect flows.
    /// </summary>
    private async Task<ConnectionResult> ConnectByProtocolAsync(
        ServerProfileDto serverDto, AppSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return (serverDto.ConnectionType?.ToUpperInvariant()) switch
        {
            "SSH" => await _connectionService.ConnectSshAsync(serverDto, settings, ct),
            "SFTP" => await _connectionService.ConnectSftpAsync(serverDto, settings, ct),
            "LOCAL" => await _connectionService.ConnectLocalShellAsync(serverDto, settings, ct),
            "TELNET" => await _connectionService.ConnectTelnetAsync(serverDto, settings, ct),
            "VNC" => await _connectionService.ConnectVncAsync(serverDto, settings, ct),
            "FTP" => await _connectionService.ConnectFtpAsync(serverDto, settings, ct),
            "CITRIX" => await _connectionService.ConnectCitrixAsync(serverDto, settings, ct),
            _ => await _connectionService.ConnectRdpAsync(serverDto, settings, ct),
        };
    }

    /// <summary>
    /// Forces embedded mode for split panes (external processes cannot be docked).
    /// </summary>
    private static void ForceEmbeddedMode(ServerProfileDto serverDto)
    {
        if (string.Equals(serverDto.ConnectionType, "RDP", StringComparison.OrdinalIgnoreCase))
            serverDto.RdpMode = "Embedded";
        if (string.Equals(serverDto.ConnectionType, "SSH", StringComparison.OrdinalIgnoreCase))
            serverDto.SshMode = "Embedded";
    }

    /// <summary>
    /// Releases tunnel reference and resets state machine for a previous connection.
    /// Used by <see cref="ReconnectPaneAsync"/> to defer cleanup until after
    /// the new connection succeeds (or definitively fails).
    /// </summary>
    private void ReleaseOldConnectionState(string oldServerId)
    {
        if (string.IsNullOrEmpty(oldServerId)) return;

        var stateData = _connectionSm.GetStateData(oldServerId);
        if (stateData?.TunnelLocalPort is int port)
            _tunnelManager.ReleaseReference(port);
        _connectionSm.Reset(oldServerId);
    }

    private static void SafeDispose(IDisposable? disposable)
    {
        if (disposable is null) return;
        try { disposable.Dispose(); }
        catch (ObjectDisposedException) { /* Expected when disposing already-closed host controls */ }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Unexpected exception during host control disposal: {ex.Message}");
        }
    }

    private static async Task AwaitVisualTreeAsync(System.Windows.Threading.Dispatcher dispatcher)
    {
        await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
}
