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

using System.Windows;
using System.Windows.Controls;

namespace Heimdall.App.Services;

/// <summary>
/// Holds transient state for the session <see cref="TreeView"/> interactions
/// in <c>MainWindow</c>: drag-drop tracking and right-click /
/// keyboard-context-menu targeting. Also exposes a pure static helper for
/// resolving a data item to its (possibly virtualized) <see cref="TreeViewItem"/>
/// container. Imperative event handlers live in <c>MainWindow.TreeInteractions.cs</c>;
/// this class owns only data and pure functions.
/// </summary>
public sealed class TreeInteractionState
{
    /// <summary>Mouse position captured on left button down — start of a potential drag.</summary>
    public System.Windows.Point DragStartPoint { get; set; }

    /// <summary>True while a drag-drop operation is currently in flight.</summary>
    public bool DragInProgress { get; set; }

    /// <summary>
    /// True when the next TreeView SelectedItemChanged notification should not
    /// resynchronize the ViewModel selection because a Ctrl/Shift gesture
    /// already updated the multi-selection explicitly.
    /// </summary>
    public bool SuppressSelectedItemSync { get; set; }

    /// <summary>
    /// Last <see cref="TreeViewItem"/> visually highlighted as a drop target.
    /// Cleared whenever the cursor leaves the candidate or the drop completes.
    /// </summary>
    public TreeViewItem? LastDropHighlight { get; set; }

    /// <summary>
    /// True when the upcoming <see cref="ContextMenu"/> opening was triggered
    /// by a right-click (preview mouse down captured a target). False when the
    /// menu is opening for some other reason — e.g. a keyboard shortcut.
    /// </summary>
    public bool ContextTargetFromPointer { get; set; }

    /// <summary>
    /// True when the right-click landed in the empty area of the <see cref="TreeView"/>
    /// (no <see cref="TreeViewItem"/> ancestor). Used to surface a root-scoped
    /// menu instead of an item-scoped one.
    /// </summary>
    public bool ContextPointerHitEmptyArea { get; set; }

    /// <summary>
    /// Data item the context menu should target. Resolved from the right-click
    /// hit-test result or the current <see cref="TreeView"/> selection.
    /// </summary>
    public object? ContextTarget { get; set; }

    /// <summary>
    /// Walks the <see cref="ItemContainerGenerator"/> hierarchy of
    /// <paramref name="parent"/> to find the <see cref="TreeViewItem"/>
    /// container for <paramref name="item"/>. Required because virtualized
    /// <see cref="TreeView"/>s only materialize visible containers.
    /// </summary>
    /// <returns>The matching container, or <c>null</c> if not realized.</returns>
    public static TreeViewItem? FindTreeViewItemContainer(ItemsControl parent, object? item)
    {
        if (item is null)
        {
            return null;
        }

        // Direct lookup on the immediate container generator
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
        {
            return direct;
        }

        // Recurse into expanded child containers (for nested items)
        for (var i = 0; i < parent.Items.Count; i++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem childContainer)
            {
                continue;
            }

            if (childContainer.DataContext == item)
            {
                return childContainer;
            }

            var nested = FindTreeViewItemContainer(childContainer, item);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
