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

using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;

namespace Heimdall.App.Services;

/// <summary>
/// Manages the N-pane binary tree split system for session tabs.
/// </summary>
public interface ISplitService
{
    /// <summary>Split layout persistence for paired server suggestions.</summary>
    SplitLayoutMemory LayoutMemory { get; }

    /// <summary>
    /// Registers a session for cancellation tracking. Call when a new tab is created.
    /// </summary>
    void RegisterSession(SessionTabViewModel session);

    /// <summary>
    /// Cancels in-progress split/reconnect operations and unregisters the session.
    /// </summary>
    void CancelSession(SessionTabViewModel session);

    /// <summary>
    /// Connects to a server and inserts it as a sibling pane next to the target pane.
    /// </summary>
    Task SplitSessionWithServerAsync(
        SessionTabViewModel session,
        string serverId,
        SplitOrientation orientation,
        string? paneId = null);

    /// <summary>
    /// Docks a built-in tool into a new pane. No network connection needed.
    /// </summary>
    void SplitSessionWithTool(
        SessionTabViewModel session,
        string paletteToolPayload,
        SplitOrientation orientation,
        string? paneId = null);

    /// <summary>
    /// Merges an existing session tab into the target session's split tree.
    /// </summary>
    void MergeExistingSession(
        SessionTabViewModel target,
        string sourceSessionId,
        SplitOrientation orientation,
        string? targetPaneId = null);

    /// <summary>
    /// Closes a single pane inside the split tree.
    /// </summary>
    void ClosePane(SessionTabViewModel session, string paneId);

    /// <summary>
    /// Reconnects the session represented by a specific pane.
    /// </summary>
    Task ReconnectPaneAsync(SessionTabViewModel session, string paneId);

    /// <summary>
    /// Swaps the children of the targeted split container.
    /// </summary>
    Task SwapSplitPanesAsync(SessionTabViewModel session, string? paneId = null);

    /// <summary>
    /// Toggles the orientation of the targeted split container.
    /// </summary>
    void ToggleSplitOrientation(SessionTabViewModel session, string? paneId = null);

    /// <summary>
    /// Cleans up a pane that was orphaned while an async connection was in flight.
    /// </summary>
    void CleanupOrphanedPane(string serverId);

    /// <summary>
    /// Closes all panes for a session. Returns false when a tool pane blocks closure.
    /// </summary>
    bool CloseAllPanes(SessionTabViewModel session);
}
