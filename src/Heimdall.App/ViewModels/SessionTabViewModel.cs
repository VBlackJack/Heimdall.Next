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
using Heimdall.Core.Models;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel for a single embedded session tab (RDP, SSH, SFTP, Local).
/// Supports split pane layout with a primary and optional secondary host control.
/// </summary>
public partial class SessionTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _serverId = "";

    /// <summary>
    /// The original server inventory ID, used for server lookups (reconnect,
    /// duplicate-tab, auto-SFTP). <see cref="ServerId"/> holds the session-
    /// scoped ID used for state machine keying.
    /// </summary>
    [ObservableProperty]
    private string _originalServerId = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _connectionType = "";

    [ObservableProperty]
    private string _status = "Connecting";

    [ObservableProperty]
    private string _environmentColor = "";

    /// <summary>
    /// Visual tunnel chain route text, e.g. "via GatewayA → GatewayB".
    /// Empty string when using a direct connection.
    /// </summary>
    [ObservableProperty]
    private string _tunnelRoute = "";

    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// The primary visual host control for this session.
    /// Typed as object to keep the ViewModel free of WPF dependencies.
    /// </summary>
    [ObservableProperty]
    private object? _hostControl;

    // ── Split pane support ───────────────────────────────────────

    [ObservableProperty]
    private bool _isSplit;

    [ObservableProperty]
    private SplitOrientation _splitOrientation;

    /// <summary>
    /// The secondary pane host control (visible only when <see cref="IsSplit"/> is true).
    /// </summary>
    [ObservableProperty]
    private object? _secondaryHostControl;

    [ObservableProperty]
    private string _secondaryServerId = "";

    /// <summary>
    /// Original server inventory ID for the secondary session (for reconnect/duplicate).
    /// </summary>
    [ObservableProperty]
    private string _secondaryOriginalServerId = "";

    [ObservableProperty]
    private string _secondaryConnectionType = "";

    /// <summary>
    /// Original display title of the secondary session (preserved across split/unsplit).
    /// </summary>
    [ObservableProperty]
    private string _secondaryTitle = "";

    /// <summary>
    /// Connection status of the secondary session (preserved across split/unsplit).
    /// </summary>
    [ObservableProperty]
    private string _secondaryStatus = "";

    /// <summary>
    /// Tunnel route text of the secondary session (preserved across split/unsplit).
    /// </summary>
    [ObservableProperty]
    private string _secondaryTunnelRoute = "";

    /// <summary>
    /// Environment color of the secondary session (preserved across split/unsplit).
    /// </summary>
    [ObservableProperty]
    private string _secondaryEnvironmentColor = "";
}
