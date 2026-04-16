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
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Factory for embedded session host controls (terminals, RDP, SFTP, tools).
/// </summary>
public interface IEmbeddedSessionManager
{
    /// <summary>
    /// Optional callback invoked when a terminal view broadcasts input.
    /// Parameters: (byte[] data, object? senderView).
    /// Wired by MainViewModel to relay keystrokes to all other terminals.
    /// </summary>
    Action<byte[], object?>? BroadcastCallback { get; set; }

    /// <summary>
    /// Optional callback invoked when an embedded view's Split button is clicked.
    /// Parameters: (SessionTabViewModel session).
    /// Wired by MainWindow code-behind to show the split picker context menu.
    /// </summary>
    Action<SessionTabViewModel>? SplitRequestedCallback { get; set; }

    /// <summary>
    /// Func that returns the current broadcast mode state.
    /// Wired by MainViewModel so newly created views show the badge immediately.
    /// </summary>
    Func<bool>? IsBroadcastActive { get; set; }

    /// <summary>
    /// Optional callback invoked when an embedded SSH view requests reconnection.
    /// Parameters: (SessionTabViewModel session, string serverId, string connectionType).
    /// Wired by MainViewModel to restart the connection using the original server.
    /// </summary>
    Action<SessionTabViewModel, string, string>? ReconnectRequestedCallback { get; set; }

    /// <summary>
    /// Optional callback for cross-tool navigation. Allows tool views to open other tools.
    /// Parameters: (string toolId, string title, ToolContext? context).
    /// Wired by MainViewModel to delegate to <c>OpenToolTabAsync</c>.
    /// </summary>
    Func<string, string, ToolContext?, Task>? OpenToolCallback { get; set; }

    /// <summary>
    /// Creates a visual host for an embedded connection session.
    /// </summary>
    object CreateHostControl(
        SessionTabViewModel sessionTab,
        string displayName,
        string connectionType,
        ISessionResult session,
        AppSettings? settings = null);

    /// <summary>
    /// Creates a host control for a tool tab (non-connection UI surface).
    /// </summary>
    object CreateToolControl(
        SessionTabViewModel sessionTab,
        string toolId,
        ToolContext? context,
        AppSettings? settings = null);
}
