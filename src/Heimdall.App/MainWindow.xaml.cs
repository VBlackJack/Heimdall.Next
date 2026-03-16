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
using System.Windows.Media;
using Heimdall.App.Theming;
using Heimdall.App.ViewModels;

namespace Heimdall.App;

/// <summary>
/// Main application window. All logic lives in <see cref="MainViewModel"/>.
/// Code-behind is limited to keyboard shortcut routing, TreeView interaction, and window lifecycle.
/// </summary>
public partial class MainWindow : Window
{
    private object? _treeContextTarget;
    private bool _treeContextTargetFromPointer;
    private bool _treeContextPointerHitEmptyArea;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
        DataContext = viewModel;

        TabServers.Checked += (_, _) => { viewModel.SelectedTab = "Servers"; UpdateTabVisibility(viewModel); };
        TabTunnels.Checked += (_, _) => { viewModel.SelectedTab = "Tunnels"; UpdateTabVisibility(viewModel); };
        TabScheduled.Checked += (_, _) => { viewModel.SelectedTab = "Scheduled"; UpdateTabVisibility(viewModel); };
        TabSettings.Checked += (_, _) => { viewModel.SelectedTab = "Settings"; UpdateTabVisibility(viewModel); };

        Loaded += async (_, _) =>
        {
            if (viewModel.LoadCommand.CanExecute(null))
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };

        KeyDown += OnKeyDown;
    }

    private void OnAddButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.N when Keyboard.Modifiers == ModifierKeys.Control:
                if (vm.ServerList.AddServerCommand.CanExecute(null))
                {
                    vm.ServerList.AddServerCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Delete:
                if (vm.ServerList.SelectedServer is not null &&
                    vm.ServerList.DeleteServerCommand.CanExecute(vm.ServerList.SelectedServer))
                {
                    vm.ServerList.DeleteServerCommand.Execute(vm.ServerList.SelectedServer);
                }
                e.Handled = true;
                break;

            case Key.E when Keyboard.Modifiers == ModifierKeys.Control:
                if (vm.ServerList.SelectedServer is not null &&
                    vm.ServerList.EditServerCommand.CanExecute(vm.ServerList.SelectedServer))
                {
                    vm.ServerList.EditServerCommand.Execute(vm.ServerList.SelectedServer);
                }
                e.Handled = true;
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;

            case Key.B when Keyboard.Modifiers == ModifierKeys.Control:
                ToggleSidebar();
                e.Handled = true;
                break;

            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Key.Escape when _isFullscreen:
                ToggleFullscreen();
                e.Handled = true;
                break;
        }
    }

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
        }
        else
        {
            vm.ServerList.SelectedServer = null;
        }
    }

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

        if (vm.ServerList.SelectedServer is not null &&
            vm.ServerList.ConnectCommand.CanExecute(vm.ServerList.SelectedServer))
        {
            vm.ServerList.ConnectCommand.Execute(vm.ServerList.SelectedServer);
        }
    }

    private void OnTreeViewPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);

        _treeContextTargetFromPointer = true;
        _treeContextPointerHitEmptyArea = treeViewItem is null;
        _treeContextTarget = treeViewItem?.DataContext;

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
        if (_treeContextTargetFromPointer)
        {
            target = _treeContextPointerHitEmptyArea ? null : _treeContextTarget;
        }
        else
        {
            target = treeView.SelectedItem;
        }

        _treeContextTargetFromPointer = false;
        _treeContextPointerHitEmptyArea = false;
        _treeContextTarget = target;

        var menu = CreateTreeContextMenu(vm, target);
        menu.PlacementTarget = treeView;
        menu.Placement = PlacementMode.MousePoint;
        treeView.ContextMenu = menu;
    }

    private ContextMenu CreateTreeContextMenu(MainViewModel vm, object? target)
    {
        return target switch
        {
            ServerItemViewModel server => CreateServerContextMenu(vm, server),
            ServerGroupViewModel group => CreateGroupContextMenu(vm, group),
            ServerProjectViewModel project => CreateProjectContextMenu(vm, project),
            _ => CreateEmptyAreaContextMenu(vm)
        };
    }

    private ContextMenu CreateServerContextMenu(MainViewModel vm, ServerItemViewModel server)
    {
        var menu = CreateContextMenu();

        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxConnect"),
            vm.ServerList.ConnectCommand,
            server));
        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxEdit"),
            vm.ServerList.EditServerCommand,
            server));
        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxDuplicate"),
            vm.ServerList.DuplicateServerCommand,
            server));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMoveToProjectMenu(vm, server));
        menu.Items.Add(CreateMoveToGroupMenu(vm, server));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxCopyHostname"),
            vm.ServerList.CopyHostnameCommand,
            server));
        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxCopyUsername"),
            vm.ServerList.CopyUsernameCommand,
            server,
            !string.IsNullOrWhiteSpace(server.Username)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxDelete"),
            vm.ServerList.DeleteServerCommand,
            server));

        return menu;
    }

    private ContextMenu CreateGroupContextMenu(MainViewModel vm, ServerGroupViewModel group)
    {
        var menu = CreateContextMenu();
        var context = new ServerGroupContext(
            group.ProjectId,
            group.ProjectName,
            group.GroupName,
            group.IsVirtualGroup);

        menu.Items.Add(CreateMenuItem(
            vm.Localize("DialogTitleAddServer"),
            vm.ServerList.AddServerToGroupCommand,
            context));
        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxRenameGroup"),
            vm.ServerList.RenameGroupCommand,
            context,
            !group.IsVirtualGroup));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxDeleteGroup"),
            vm.ServerList.DeleteGroupCommand,
            context,
            !group.IsVirtualGroup));

        return menu;
    }

    private ContextMenu CreateProjectContextMenu(MainViewModel vm, ServerProjectViewModel project)
    {
        var menu = CreateContextMenu();

        if (!project.IsVirtualProject)
        {
            menu.Items.Add(CreateMenuItem(
                vm.Localize("TreeCtxEditProject"),
                vm.EditProjectCommand,
                project));
        }

        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxNewGroup"),
            vm.ServerList.AddGroupCommand,
            project));
        menu.Items.Add(new Separator());

        if (project.IsVirtualProject)
        {
            menu.Items.Add(CreateMenuItem(
                vm.Localize("TreeCtxNewProject"),
                vm.AddProjectCommand));
        }
        else
        {
            menu.Items.Add(CreateMenuItem(
                vm.Localize("TreeCtxDeleteProject"),
                vm.DeleteProjectCommand,
                project));
        }

        return menu;
    }

    private ContextMenu CreateEmptyAreaContextMenu(MainViewModel vm)
    {
        var menu = CreateContextMenu();

        menu.Items.Add(CreateMenuItem(
            vm.Localize("DialogTitleAddServer"),
            vm.ServerList.AddServerCommand));
        menu.Items.Add(CreateMenuItem(
            vm.Localize("BtnAddGateway"),
            vm.Settings.AddGatewayCommand));
        menu.Items.Add(CreateMenuItem(
            vm.Localize("TreeCtxNewProject"),
            vm.AddProjectCommand));

        return menu;
    }

    private MenuItem CreateMoveToProjectMenu(MainViewModel vm, ServerItemViewModel server)
    {
        var item = new MenuItem
        {
            Header = vm.Localize("TreeCtxMoveToProject")
        };

        foreach (var project in vm.ServerList.GetProjectTargets(includeNoProject: true))
        {
            var targetProjectId = string.IsNullOrWhiteSpace(project.Id) ? null : project.Id;
            var child = CreateMenuItem(
                project.Name,
                vm.ServerList.MoveToProjectCommand,
                new ServerMoveToProjectRequest(server, targetProjectId),
                !string.Equals(server.ProjectId, project.Id, StringComparison.Ordinal));

            item.Items.Add(child);
        }

        return item;
    }

    private MenuItem CreateMoveToGroupMenu(MainViewModel vm, ServerItemViewModel server)
    {
        var item = new MenuItem
        {
            Header = vm.Localize("TreeCtxMoveToGroup")
        };

        foreach (var group in vm.ServerList.GetGroupTargets(server.ProjectId, includeNoGroup: true))
        {
            var targetGroupName = string.IsNullOrWhiteSpace(group.GroupName) ? null : group.GroupName;
            var child = CreateMenuItem(
                group.DisplayName,
                vm.ServerList.MoveToGroupCommand,
                new ServerMoveToGroupRequest(server, targetGroupName),
                !string.Equals(server.Group, group.GroupName, StringComparison.OrdinalIgnoreCase));

            item.Items.Add(child);
        }

        return item;
    }

    private static ContextMenu CreateContextMenu()
    {
        return new ContextMenu();
    }

    private static MenuItem CreateMenuItem(
        string header,
        ICommand command,
        object? parameter = null,
        bool isEnabled = true)
    {
        return new MenuItem
        {
            Header = header,
            Command = command,
            CommandParameter = parameter,
            IsEnabled = isEnabled
        };
    }

    private static void HideTabStripPanel(System.Windows.Controls.TabControl tabControl, bool hide)
    {
        // Find the TabPanel in the TabControl's visual tree and collapse it
        tabControl.ApplyTemplate();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(tabControl); i++)
        {
            var child = VisualTreeHelper.GetChild(tabControl, i);
            HideTabStripPanelRecursive(child, hide);
        }
    }

    // ── Tab drag & drop reordering ───────────────────────────────────

    private System.Windows.Point _tabDragStartPoint;
    private SessionTabViewModel? _tabDragItem;

    private void OnTabDragStart(object sender, MouseButtonEventArgs e)
    {
        _tabDragStartPoint = e.GetPosition(SessionTabControl);
        var tabItem = FindAncestor<System.Windows.Controls.TabItem>(e.OriginalSource as DependencyObject);
        _tabDragItem = tabItem?.DataContext as SessionTabViewModel;
    }

    private void OnTabDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_tabDragItem is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPos = e.GetPosition(SessionTabControl);
        var diff = _tabDragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var data = new System.Windows.DataObject("SessionTab", _tabDragItem);
            DragDrop.DoDragDrop(SessionTabControl, data, System.Windows.DragDropEffects.Move);
            _tabDragItem = null;
        }
    }

    private void OnTabDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!e.Data.GetDataPresent("SessionTab")) return;

        var draggedItem = e.Data.GetData("SessionTab") as SessionTabViewModel;
        if (draggedItem is null) return;

        // Find the drop target tab
        var dropTarget = FindAncestor<System.Windows.Controls.TabItem>(e.OriginalSource as DependencyObject);
        var targetItem = dropTarget?.DataContext as SessionTabViewModel;

        if (targetItem is null || targetItem == draggedItem) return;

        var sessions = vm.Connection.ActiveSessions;
        int oldIndex = sessions.IndexOf(draggedItem);
        int newIndex = sessions.IndexOf(targetItem);

        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
        {
            sessions.Move(oldIndex, newIndex);
        }
    }

    private static void HideTabStripPanelRecursive(DependencyObject parent, bool hide)
    {
        if (parent is System.Windows.Controls.Primitives.TabPanel tabPanel)
        {
            tabPanel.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            HideTabStripPanelRecursive(VisualTreeHelper.GetChild(parent, i), hide);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ── Session tab context menu handlers ──────────────────────────────

    private void OnSessionTabRightClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var session = vm.Connection.ActiveSession;
        if (session is null) return;

        var menu = new System.Windows.Controls.ContextMenu();

        var disconnectItem = new System.Windows.Controls.MenuItem { Header = "Disconnect" };
        disconnectItem.Click += (_, _) => vm.Connection.CloseSessionCommand.Execute(session);
        menu.Items.Add(disconnectItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var aspectMenu = new System.Windows.Controls.MenuItem { Header = "Aspect Ratio" };
        foreach (var (label, tag) in new[] { ("Stretch", "Stretch"), ("Auto", "Auto"), ("16:9", "Ratio16x9"), ("4:3", "Ratio4x3"), ("21:9", "Ratio21x9") })
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, Tag = tag };
            item.Click += OnAspectRatioClick;
            aspectMenu.Items.Add(item);
        }
        menu.Items.Add(aspectMenu);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var fullscreenItem = new System.Windows.Controls.MenuItem { Header = "Fullscreen (F11)" };
        fullscreenItem.Click += OnToggleFullscreenClick;
        menu.Items.Add(fullscreenItem);

        var closeItem = new System.Windows.Controls.MenuItem { Header = "Close Session" };
        closeItem.Click += (_, _) => vm.Connection.CloseSessionCommand.Execute(session);
        menu.Items.Add(closeItem);

        menu.PlacementTarget = SessionTabControl;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnAspectRatioClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem menuItem) return;
        if (DataContext is not MainViewModel vm) return;
        var session = vm.Connection.ActiveSession;
        if (session?.HostControl is not Views.EmbeddedRdpView rdpView) return;

        var ratioName = menuItem.Tag?.ToString() ?? "Stretch";
        Heimdall.Core.Logging.FileLogger.Info($"Aspect ratio changed to {ratioName}");
        // TODO: Apply aspect ratio to the embedded RDP session display
    }

    private bool _isFullscreen;

    private void OnToggleFullscreenClick(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private WindowState _preFullscreenState;
    private double _preFullscreenWidth;
    private double _preFullscreenHeight;

    private void ToggleFullscreen()
    {
        if (DataContext is not MainViewModel vm) return;

        if (_isFullscreen)
        {
            // Exit fullscreen
            _isFullscreen = false;
            FullscreenBar.Visibility = Visibility.Collapsed;
            NotifyEmbeddedViewsFullscreen(false);

            // Show toolbar, TreeView, status bar
            ToolbarRow.Height = new GridLength(48);
            StatusBarRow.Height = new GridLength(28);
            ServerTreeColumn.Width = new GridLength(260);
            ServerTreeColumn.MinWidth = 180;
            ServerTreeColumn.MaxWidth = 500;
            SplitterColumn.Width = GridLength.Auto;

            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _preFullscreenState;
            if (_preFullscreenState == WindowState.Normal)
            {
                Width = _preFullscreenWidth;
                Height = _preFullscreenHeight;
            }
        }
        else
        {
            // Enter fullscreen
            _isFullscreen = true;
            _preFullscreenState = WindowState;
            _preFullscreenWidth = ActualWidth;
            _preFullscreenHeight = ActualHeight;

            // Hide toolbar, TreeView, status bar
            ToolbarRow.Height = new GridLength(0);
            StatusBarRow.Height = new GridLength(0);
            ServerTreeColumn.MinWidth = 0;
            ServerTreeColumn.MaxWidth = 0;
            ServerTreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);

            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            FullscreenBar.Visibility = Visibility.Visible;

            // Hide session tab headers in fullscreen (session fills the screen)
            SessionTabControl.Padding = new Thickness(0);
            SessionTabControl.Margin = new Thickness(0);

            // Hide the session header bar inside embedded views
            NotifyEmbeddedViewsFullscreen(true);
        }
    }

    /// <summary>
    /// When switching to Tunnels/Scheduled/Settings while sessions are active,
    /// the Servers Grid must stay visible (for sessions) but TreeView hides.
    /// When returning to Servers, TreeView restores.
    /// </summary>
    private void UpdateTabVisibility(MainViewModel vm)
    {
        var isServers = vm.SelectedTab == "Servers";
        var hasSessions = vm.Connection.HasActiveSessions;

        // If not on Servers but sessions active, show sessions full-width
        if (!isServers && hasSessions)
        {
            // Hide TreeView temporarily
            if (!_sidebarHidden)
            {
                _savedSidebarWidth = ServerTreeColumn.ActualWidth;
                ServerTreeColumn.MinWidth = 0;
                ServerTreeColumn.MaxWidth = 0;
                ServerTreeColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
            }
        }
        else if (isServers && !_sidebarHidden)
        {
            // Restore TreeView
            ServerTreeColumn.MinWidth = 180;
            ServerTreeColumn.MaxWidth = 500;
            ServerTreeColumn.Width = new GridLength(_savedSidebarWidth > 0 ? _savedSidebarWidth : 260);
            SplitterColumn.Width = GridLength.Auto;
        }
    }

    private bool _sidebarHidden;
    private double _savedSidebarWidth = 260;

    private void OnToggleSidebarClick(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar()
    {
        if (_sidebarHidden)
        {
            _sidebarHidden = false;
            ServerTreeColumn.MinWidth = 180;
            ServerTreeColumn.MaxWidth = 500;
            ServerTreeColumn.Width = new GridLength(_savedSidebarWidth);
            SplitterColumn.Width = GridLength.Auto;
        }
        else
        {
            _sidebarHidden = true;
            _savedSidebarWidth = ServerTreeColumn.ActualWidth;
            ServerTreeColumn.MinWidth = 0;
            ServerTreeColumn.MaxWidth = 0;
            ServerTreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
        }
    }

    private void NotifyEmbeddedViewsFullscreen(bool isFullscreen)
    {
        if (DataContext is not MainViewModel vm) return;
        foreach (var session in vm.Connection.ActiveSessions)
        {
            if (session.HostControl is Views.EmbeddedRdpView rdpView)
                rdpView.SetFullscreen(isFullscreen);
            else if (session.HostControl is Views.EmbeddedSshView sshView)
                sshView.Visibility = Visibility.Visible; // SSH always visible
        }

        // Hide/show entire tab strip by collapsing the TabPanel
        // Single session fullscreen = no tab bar needed
        if (isFullscreen && vm.Connection.ActiveSessions.Count <= 1)
        {
            SessionTabControl.Tag = "fullscreen-notabs";
            // Use a style that hides the header panel
            SessionTabControl.SetValue(System.Windows.Controls.Control.PaddingProperty, new Thickness(0));
            // Walk the visual tree to find and hide the TabPanel
            HideTabStripPanel(SessionTabControl, true);
        }
        else
        {
            SessionTabControl.Tag = null;
            HideTabStripPanel(SessionTabControl, false);
        }
    }

    private void OnExitFullscreenClick(object sender, RoutedEventArgs e)
    {
        FullscreenBar.Visibility = Visibility.Collapsed;
        if (_isFullscreen) ToggleFullscreen();
    }
}
