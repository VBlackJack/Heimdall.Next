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

using CommunityToolkit.Mvvm.ComponentModel;

namespace Heimdall.Core.Models;

/// <summary>
/// Leaf node in the recursive split pane tree, representing a single session pane.
/// Each pane owns one connection (RDP, SSH, SFTP, etc.) with its own state machine key.
/// </summary>
public partial class SessionPaneModel : ObservableObject, ISplitContent
{
    /// <summary>
    /// Stable identity for addressing this pane in tree operations.
    /// Generated once at creation, never changes.
    /// </summary>
    [ObservableProperty]
    private string _paneId = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The visual host control for this pane.
    /// Typed as object to keep the model free of WPF dependencies.
    /// </summary>
    [ObservableProperty]
    private object? _hostControl;

    /// <summary>
    /// Session-scoped ID used for state machine keying and tunnel tracking.
    /// </summary>
    [ObservableProperty]
    private string _serverId = "";

    /// <summary>
    /// The original server inventory ID, used for server lookups (reconnect,
    /// duplicate-tab, auto-SFTP).
    /// </summary>
    [ObservableProperty]
    private string _originalServerId = "";

    [ObservableProperty]
    private string _connectionType = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _tunnelRoute = "";

    [ObservableProperty]
    private string _environmentColor = "";
}
