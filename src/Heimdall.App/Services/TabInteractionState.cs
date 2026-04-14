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

using System.Windows.Controls;
using Heimdall.App.ViewModels;

namespace Heimdall.App.Services;

/// <summary>
/// Holds transient state for the session <see cref="TabControl"/> drag-drop
/// interactions in <c>MainWindow</c>: drag origin, the session being dragged,
/// and the last <see cref="TabItem"/> currently rendered with the drop
/// highlight border. Imperative event handlers live in
/// <c>MainWindow.TabInteractions.cs</c>; this class owns only data.
/// </summary>
public sealed class TabInteractionState
{
    /// <summary>
    /// Mouse-down position captured in <c>SessionTabControl</c> space, used
    /// to detect whether the cursor has moved past the system drag threshold.
    /// </summary>
    public System.Windows.Point DragStartPoint { get; set; }

    /// <summary>
    /// The session being dragged. <c>null</c> when no drag is in flight or
    /// when the press started on a tab close button (drag is suppressed).
    /// </summary>
    public SessionTabViewModel? DragItem { get; set; }

    /// <summary>
    /// Last <see cref="TabItem"/> rendered with the drop-target border
    /// highlight. Cleared whenever the cursor moves to a new candidate or
    /// the drop completes / the cursor leaves the strip.
    /// </summary>
    public TabItem? LastDropHighlight { get; set; }
}
