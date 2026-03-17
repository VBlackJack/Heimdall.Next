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

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _connectionType = "";

    [ObservableProperty]
    private string _status = "Connecting";

    [ObservableProperty]
    private string _environmentColor = "";

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

    [ObservableProperty]
    private string _secondaryConnectionType = "";
}
