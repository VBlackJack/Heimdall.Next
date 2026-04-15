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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Heimdall.App.Services;
using Heimdall.App.Theming;
using Heimdall.App.ViewModels;

namespace Heimdall.App;

/// <summary>
/// Partial of <see cref="MainWindow"/> hosting the session
/// <see cref="TreeView"/> interaction handlers: selection, double-click,
/// right-click pre-selection, keyboard context menu, and drag-drop between
/// folders. All transient state lives in the <c>_treeState</c> field
/// (<see cref="TreeInteractionState"/>); this file only contains the WPF
/// event handlers that mutate that state and poke the named XAML elements
/// (<c>SessionTreeView</c>, <c>SessionDetailPanel</c>, <c>ToolDetailPanel</c>,
/// <c>Mw_Detail*</c>, <c>Mw_ToolDetail*</c>).
/// </summary>
public partial class MainWindow
{
    // ── Keyboard context menu (Apps / Shift+F10) ─────────────────────

    /// <summary>
    /// Opens the TreeView context menu via keyboard (Shift+F10 or Apps key).
    /// Positions the menu at the selected TreeViewItem rather than at the mouse cursor.
    /// </summary>
    private void OpenTreeViewKeyboardContextMenu(MainViewModel vm)
    {
        if (!SessionTreeView.IsKeyboardFocusWithin)
        {
            return;
        }

        var target = SessionTreeView.SelectedItem;
        var menu = _contextMenuFactory.CreateTreeContextMenu(target, vm, this);

        // Try to position the menu at the selected item's location
        var container = TreeInteractionState.FindTreeViewItemContainer(SessionTreeView, target);
        if (container is not null)
        {
            menu.PlacementTarget = container;
            menu.Placement = PlacementMode.Bottom;
        }
        else
        {
            menu.PlacementTarget = SessionTreeView;
            menu.Placement = PlacementMode.Center;
        }

        SessionTreeView.ContextMenu = menu;
        menu.IsOpen = true;
    }

    // ── Selection + detail panel switch ──────────────────────────────

    /// <summary>
    /// Handles TreeView selection changes. Only updates the ViewModel when a
    /// server item (leaf node) is selected, ignoring group node selections.
    /// </summary>
    private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.NewValue is ServerItemViewModel server)
        {
            vm.ServerList.SelectedServer = server;

            // Pre-resolve DNS to warm the OS cache before the user clicks Connect
            if (!string.IsNullOrWhiteSpace(server.RemoteServer))
            {
                _ = System.Net.Dns.GetHostEntryAsync(server.RemoteServer)
                    .ContinueWith(_ => { }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }

            var isTool = server.ConnectionType?.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase) == true;
            if (isTool)
            {
                SessionDetailPanel.Visibility = Visibility.Collapsed;
                ToolDetailPanel.Visibility = Visibility.Visible;
                UpdateToolDetailPanel(vm, server.ConnectionType!);
            }
            else
            {
                SessionDetailPanel.Visibility = Visibility.Visible;
                ToolDetailPanel.Visibility = Visibility.Collapsed;
                Mw_DetailConnectBtn.Content = vm.Localize("DetailBtnConnect");
                Mw_DetailHostPort.Visibility = Visibility.Visible;
            }
        }
        else
        {
            vm.ServerList.SelectedServer = null;
            SessionDetailPanel.Visibility = Visibility.Collapsed;
            ToolDetailPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Populates the tool-specific detail panel with name, category, and description.
    /// </summary>
    private void UpdateToolDetailPanel(MainViewModel vm, string connectionType)
    {
        var toolId = connectionType["TOOL:".Length..];
        var desc = ToolRegistry.GetById(toolId);
        if (desc is null) return;

        Mw_ToolDetailName.Text = vm.Localize(desc.LabelKey);
        Mw_ToolDetailCategory.Text = vm.Localize(desc.CategoryLabelKey);

        var descKey = desc.DescriptionKey ?? $"ToolDesc{desc.Id}";
        var description = vm.Localize(descKey);
        Mw_ToolDetailDescription.Text = description != descKey ? description : "";

        Mw_ToolDetailOpenBtn.Content = vm.Localize("DetailBtnOpenInTab");
    }

    // ── Double-click → connect / open tool ───────────────────────────

    /// <summary>
    /// Handles double-click on a server item in the TreeView to initiate a connection.
    /// Ensures only server leaf nodes trigger a connection (not group headers).
    /// </summary>
    private void OnTreeViewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var server = vm.ServerList.SelectedServer;
        if (server is null) return;

        if (server.ConnectionType?.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase) == true)
        {
            var toolId = server.ConnectionType["TOOL:".Length..];
            vm.TrackRecentTool(toolId.ToUpperInvariant());
            var context = new Core.Models.ToolContext(
                TargetHost: server.RemoteServer,
                TargetPort: server.RemotePort > 0 ? (int?)server.RemotePort : null,
                Argument: server.RemoteServer);
            _ = vm.OpenToolTabAsync(toolId, server.DisplayName, context);
        }
        else if (vm.ServerList.ConnectCommand.CanExecute(server))
        {
            vm.ServerList.ConnectCommand.Execute(server);
        }
    }

    // ── Right-click pre-selection + context menu opening ─────────────

    private void OnTreeViewPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);

        _treeState.ContextTargetFromPointer = true;
        _treeState.ContextPointerHitEmptyArea = treeViewItem is null;
        _treeState.ContextTarget = treeViewItem?.DataContext;

        if (treeViewItem is not null)
        {
            treeViewItem.IsSelected = true;
            treeViewItem.Focus();
        }
    }

    private void OnTreeViewContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not TreeView treeView)
        {
            return;
        }

        object? target;
        if (_treeState.ContextTargetFromPointer)
        {
            target = _treeState.ContextPointerHitEmptyArea ? null : _treeState.ContextTarget;
        }
        else
        {
            target = treeView.SelectedItem;
        }

        _treeState.ContextTargetFromPointer = false;
        _treeState.ContextPointerHitEmptyArea = false;
        _treeState.ContextTarget = target;

        var menu = _contextMenuFactory.CreateTreeContextMenu(target, vm, this);
        menu.PlacementTarget = treeView;
        menu.Placement = PlacementMode.MousePoint;
        treeView.ContextMenu = menu;
    }

    // ── TreeView drag-drop: move servers between groups/projects ─────

    private void OnTreeViewDragStart(object sender, MouseButtonEventArgs e)
    {
        _treeState.DragStartPoint = e.GetPosition(null);
        _treeState.DragInProgress = false;
    }

    private void OnTreeViewDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _treeState.DragInProgress)
        {
            return;
        }

        var pos = e.GetPosition(null);
        var diff = pos - _treeState.DragStartPoint;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // Find the ServerItemViewModel being dragged
        var treeViewItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (treeViewItem?.DataContext is not ServerItemViewModel serverItem)
        {
            return;
        }

        _treeState.DragInProgress = true;
        var data = new System.Windows.DataObject("HeimdallServer", serverItem);
        DragDrop.DoDragDrop(treeViewItem, data, System.Windows.DragDropEffects.Move);
        _treeState.DragInProgress = false;
    }

    private void ClearDropHighlight()
    {
        if (_treeState.LastDropHighlight is not null)
        {
            DropTargetVisualState.SetIsDropTarget(_treeState.LastDropHighlight, false);
            _treeState.LastDropHighlight = null;
        }
    }

    private void OnTreeViewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = System.Windows.DragDropEffects.None;

        if (!e.Data.GetDataPresent("HeimdallServer"))
        {
            ClearDropHighlight();
            e.Handled = true;
            return;
        }

        var target = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        ClearDropHighlight();

        if (target?.DataContext is FolderViewModel)
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            DropTargetVisualState.SetIsDropTarget(target, true);
            _treeState.LastDropHighlight = target;
        }

        e.Handled = true;
    }

    private void OnTreeViewDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        ClearDropHighlight();
    }

    private async void OnTreeViewDrop(object sender, System.Windows.DragEventArgs e)
    {
        ClearDropHighlight();

        if (!e.Data.GetDataPresent("HeimdallServer"))
        {
            return;
        }

        if (e.Data.GetData("HeimdallServer") is not ServerItemViewModel serverItem
            || DataContext is not MainViewModel vm)
        {
            return;
        }

        var target = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (target?.DataContext is not FolderViewModel folder)
        {
            return;
        }

        var moved = await vm.ServerList.MoveServerToGroupAsync(serverItem, folder.FullPath);
        if (moved)
        {
            vm.StatusText = string.Format(
                vm.Localize("StatusMovedToGroup"),
                serverItem.DisplayName,
                folder.Name);
        }
    }
}
