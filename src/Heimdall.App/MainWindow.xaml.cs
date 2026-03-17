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

        TabServers.Checked += OnServersTabChecked;
        TabTunnels.Checked += OnTunnelsTabChecked;
        TabScheduled.Checked += OnScheduledTabChecked;
        TabSettings.Checked += OnSettingsTabChecked;
        TabAbout.Checked += OnAboutTabChecked;
        viewModel.Connection.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ConnectionViewModel.HasActiveSessions), StringComparison.Ordinal))
            {
                Heimdall.Core.Logging.FileLogger.Info(
                    $"Navigation session state changed: hasSessions={viewModel.Connection.HasActiveSessions}, selectedTab={viewModel.SelectedTab}");
                UpdateTabVisibility(viewModel);
            }
        };

        // Wire split button callback from embedded views
        viewModel.EmbeddedSessionManager.SplitRequestedCallback = OnEmbeddedSplitRequested;

        Loaded += async (_, _) =>
        {
            if (viewModel.LoadCommand.CanExecute(null))
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }

            PopulateAboutSection();
        };

        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Handles split requests from embedded view header buttons by showing the
    /// split picker context menu with a default vertical orientation.
    /// </summary>
    private void OnEmbeddedSplitRequested(SessionTabViewModel session)
    {
        if (session.IsSplit)
        {
            UnsplitSession(session);
        }
        else
        {
            RequestSplitSession(session, Heimdall.Core.Models.SplitOrientation.Vertical);
        }
    }

    private void PopulateAboutSection()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var infoVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(asm)
            ?.InformationalVersion ?? "unknown";
        if (DataContext is MainViewModel aboutVm)
            AboutVersionText.Text = string.Format(aboutVm.Localize("AboutVersion"), infoVersion);
        else
            AboutVersionText.Text = $"Version {infoVersion}";
        AboutRuntimeText.Text = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        AboutPlatformText.Text = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})";
    }

    private void OnServersTabChecked(object sender, RoutedEventArgs e)
    {
        SwitchToTab("Servers");
    }

    private void OnTunnelsTabChecked(object sender, RoutedEventArgs e)
    {
        SwitchToTab("Tunnels");
    }

    private void OnScheduledTabChecked(object sender, RoutedEventArgs e)
    {
        SwitchToTab("Scheduled");
    }

    private void OnSettingsTabChecked(object sender, RoutedEventArgs e)
    {
        SwitchToTab("Settings");
    }

    private async void OnAddFolderFromMenu(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var name = await vm.DialogService.ShowInputAsync(
            vm.Localize("TreeCtxNewGroup"),
            vm.Localize("ServerFieldGroup"));

        if (!string.IsNullOrWhiteSpace(name))
        {
            var settings = await vm.ConfigManager.LoadSettingsAsync();
            var path = name.Trim();
            if (!settings.EmptyGroups.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                settings.EmptyGroups.Add(path);
                await vm.ConfigManager.SaveSettingsAsync(settings);
                var servers = await vm.ConfigManager.LoadServersAsync();
                vm.ServerList.LoadServers(servers, settings);
            }
        }
    }

    private void OnAboutTabChecked(object sender, RoutedEventArgs e)
    {
        SwitchToTab("About");
    }

    private void SwitchToTab(string tabName)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        Heimdall.Core.Logging.FileLogger.Info(
            $"Navigation request: tab={tabName}, current={vm.SelectedTab}, hasSessions={vm.Connection.HasActiveSessions}");

        vm.SelectedTab = tabName;
        UpdateTabVisibility(vm);

        Heimdall.Core.Logging.FileLogger.Info(
            $"Navigation applied: tab={vm.SelectedTab}, serversVisible={vm.IsServersTabSelected}, tunnelsVisible={vm.IsTunnelsTabSelected}, scheduledVisible={vm.IsScheduledTabSelected}, settingsVisible={vm.IsSettingsTabSelected}");
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

            case Key.K when Keyboard.Modifiers == ModifierKeys.Control:
                if (DataContext is MainViewModel vm2)
                {
                    vm2.OpenCommandPaletteCommand.Execute(null);
                    PaletteInput.Focus();
                }
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

    // ── TreeView drag-drop: move servers between groups/projects ──

    private System.Windows.Point _treeDragStartPoint;
    private bool _treeDragInProgress;

    private void OnTreeViewDragStart(object sender, MouseButtonEventArgs e)
    {
        _treeDragStartPoint = e.GetPosition(null);
        _treeDragInProgress = false;
    }

    private void OnTreeViewDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _treeDragInProgress)
        {
            return;
        }

        var pos = e.GetPosition(null);
        var diff = pos - _treeDragStartPoint;

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

        _treeDragInProgress = true;
        var data = new System.Windows.DataObject("HeimdallServer", serverItem);
        DragDrop.DoDragDrop(treeViewItem, data, System.Windows.DragDropEffects.Move);
        _treeDragInProgress = false;
    }

    private void OnTreeViewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = System.Windows.DragDropEffects.None;

        if (!e.Data.GetDataPresent("HeimdallServer"))
        {
            e.Handled = true;
            return;
        }

        var target = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (target?.DataContext is FolderViewModel)
        {
            e.Effects = System.Windows.DragDropEffects.Move;
        }

        e.Handled = true;
    }

    private async void OnTreeViewDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("HeimdallServer"))
        {
            return;
        }

        var serverItem = e.Data.GetData("HeimdallServer") as ServerItemViewModel;
        if (serverItem is null || DataContext is not MainViewModel vm)
        {
            return;
        }

        var target = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (target?.DataContext is FolderViewModel folder)
        {
            string targetGroup = folder.FullPath;

            if (!string.Equals(serverItem.Group, targetGroup, StringComparison.OrdinalIgnoreCase))
            {
                var servers = await vm.ConfigManager.LoadServersAsync();
                var dto = servers.FirstOrDefault(
                    s => string.Equals(s.Id, serverItem.Id, StringComparison.Ordinal));

                if (dto is not null)
                {
                    dto.Group = string.IsNullOrWhiteSpace(targetGroup) ? null : targetGroup;
                    // Preserve ProjectId during folder moves (backward compatibility)
                    await vm.ConfigManager.SaveServersAsync(servers);
                    var settings = await vm.ConfigManager.LoadSettingsAsync();
                    vm.ServerList.LoadServers(servers, settings);
                    vm.StatusText = string.Format(vm.Localize("StatusMovedToGroup"), serverItem.DisplayName, folder.Name);
                }
            }
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
            FolderViewModel folder => CreateFolderContextMenu(vm, folder),
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

    /// <summary>
    /// Recursively collects all <see cref="ServerItemViewModel"/> instances from a folder and its sub-folders.
    /// </summary>
    private static List<ServerItemViewModel> GetAllServersRecursive(FolderViewModel folder)
    {
        var result = new List<ServerItemViewModel>(folder.Servers);
        foreach (var sub in folder.SubFolders)
        {
            result.AddRange(GetAllServersRecursive(sub));
        }
        return result;
    }

    private ContextMenu CreateFolderContextMenu(MainViewModel vm, FolderViewModel folder)
    {
        var menu = CreateContextMenu();

        // Connect all servers in this folder (recursively)
        var allServers = GetAllServersRecursive(folder);
        var connectAllItem = new MenuItem
        {
            Header = string.Format(vm.Localize("TreeCtxConnectAllCount"), allServers.Count),
            IsEnabled = allServers.Count > 0
        };
        connectAllItem.Click += async (_, _) =>
        {
            var confirmed = await vm.DialogService.ShowConfirmAsync(
                vm.Localize("ConfirmConnectAllTitle"),
                string.Format(vm.Localize("ConfirmConnectAllMessage"), allServers.Count));

            if (!confirmed) return;

            for (int i = 0; i < allServers.Count; i++)
            {
                var server = allServers[i];
                vm.StatusText = string.Format(
                    vm.Localize("StatusConnectAllProgress"),
                    i + 1, allServers.Count, server.DisplayName);

                if (vm.ServerList.ConnectCommand.CanExecute(server))
                {
                    vm.ServerList.ConnectCommand.Execute(server);
                    if (i < allServers.Count - 1)
                    {
                        await Task.Delay(500);
                    }
                }
            }
            vm.StatusText = string.Format(
                vm.Localize("StatusConnectedAllCount"), allServers.Count);
        };
        menu.Items.Add(connectAllItem);

        menu.Items.Add(new Separator());

        // Add server to this folder
        var seed = new ServerDialogSeed(null, folder.FullPath);
        menu.Items.Add(CreateMenuItem(
            vm.Localize("DialogTitleAddServer"),
            vm.ServerList.AddServerCommand,
            seed));

        // Add sub-folder (via input dialog)
        var addSubItem = new MenuItem { Header = vm.Localize("TreeCtxNewGroup") };
        addSubItem.Click += async (_, _) =>
        {
            var name = await vm.DialogService.ShowInputAsync(
                vm.Localize("TreeCtxNewGroup"),
                vm.Localize("ServerFieldGroup"));

            if (!string.IsNullOrWhiteSpace(name))
            {
                string newPath = string.IsNullOrEmpty(folder.FullPath)
                    ? name.Trim()
                    : $"{folder.FullPath}/{name.Trim()}";

                var settings = await vm.ConfigManager.LoadSettingsAsync();
                if (!settings.EmptyGroups.Contains(newPath, StringComparer.OrdinalIgnoreCase))
                {
                    settings.EmptyGroups.Add(newPath);
                    await vm.ConfigManager.SaveSettingsAsync(settings);
                    var servers = await vm.ConfigManager.LoadServersAsync();
                    vm.ServerList.LoadServers(servers, settings);
                }
            }
        };
        menu.Items.Add(addSubItem);

        menu.Items.Add(new Separator());

        // Rename folder
        if (!string.IsNullOrEmpty(folder.FullPath))
        {
            var renameItem = new MenuItem { Header = vm.Localize("TreeCtxRenameGroup") };
            renameItem.Click += async (_, _) =>
            {
                var newName = await vm.DialogService.ShowInputAsync(
                    vm.Localize("TreeCtxRenameGroup"),
                    vm.Localize("ServerFieldGroup"),
                    folder.Name);

                if (!string.IsNullOrWhiteSpace(newName) &&
                    !string.Equals(newName.Trim(), folder.Name, StringComparison.Ordinal))
                {
                    var servers = await vm.ConfigManager.LoadServersAsync();
                    string oldPath = folder.FullPath;
                    string parentPath = oldPath.Contains('/')
                        ? oldPath[..oldPath.LastIndexOf('/')]
                        : "";
                    string newPath = string.IsNullOrEmpty(parentPath)
                        ? newName.Trim()
                        : $"{parentPath}/{newName.Trim()}";

                    // Rename in server Group paths
                    foreach (var dto in servers)
                    {
                        if (dto.Group is not null &&
                            (dto.Group.Equals(oldPath, StringComparison.OrdinalIgnoreCase) ||
                             dto.Group.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase)))
                        {
                            dto.Group = newPath + dto.Group[oldPath.Length..];
                        }
                    }

                    // Rename in EmptyGroups
                    var settings = await vm.ConfigManager.LoadSettingsAsync();
                    for (int i = 0; i < settings.EmptyGroups.Count; i++)
                    {
                        var eg = settings.EmptyGroups[i];
                        if (eg.Equals(oldPath, StringComparison.OrdinalIgnoreCase) ||
                            eg.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.EmptyGroups[i] = newPath + eg[oldPath.Length..];
                        }
                    }

                    await vm.ConfigManager.SaveSettingsAsync(settings);
                    await vm.ConfigManager.SaveServersAsync(servers);
                    vm.ServerList.LoadServers(servers, settings);
                }
            };
            menu.Items.Add(renameItem);

            // Delete folder (move servers to root)
            var deleteItem = new MenuItem
            {
                Header = vm.Localize("TreeCtxDeleteGroup"),
                Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush
                    ?? new System.Windows.Media.SolidColorBrush(Colors.Red)
            };
            deleteItem.Click += async (_, _) =>
            {
                var confirmed = await vm.DialogService.ShowConfirmAsync(
                    vm.Localize("TreeCtxDeleteGroup"),
                    string.Format(vm.Localize("TreeCtxDeleteGroupConfirm"), folder.Name),
                    "warning");

                if (!confirmed) return;

                var servers = await vm.ConfigManager.LoadServersAsync();
                foreach (var dto in servers)
                {
                    if (dto.Group is not null &&
                        (dto.Group.Equals(folder.FullPath, StringComparison.OrdinalIgnoreCase) ||
                         dto.Group.StartsWith(folder.FullPath + "/", StringComparison.OrdinalIgnoreCase)))
                    {
                        dto.Group = null;
                    }
                }

                var settings = await vm.ConfigManager.LoadSettingsAsync();
                settings.EmptyGroups.RemoveAll(p =>
                    p.Equals(folder.FullPath, StringComparison.OrdinalIgnoreCase) ||
                    p.StartsWith(folder.FullPath + "/", StringComparison.OrdinalIgnoreCase));
                await vm.ConfigManager.SaveSettingsAsync(settings);
                await vm.ConfigManager.SaveServersAsync(servers);
                vm.ServerList.LoadServers(servers, settings);
            };
            menu.Items.Add(deleteItem);
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

        // New root folder
        var newFolderItem = new MenuItem { Header = vm.Localize("TreeCtxNewGroup") };
        newFolderItem.Click += async (_, _) =>
        {
            var name = await vm.DialogService.ShowInputAsync(
                vm.Localize("TreeCtxNewGroup"),
                vm.Localize("ServerFieldGroup"));

            if (!string.IsNullOrWhiteSpace(name))
            {
                var settings = await vm.ConfigManager.LoadSettingsAsync();
                var path = name.Trim();
                if (!settings.EmptyGroups.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    settings.EmptyGroups.Add(path);
                    await vm.ConfigManager.SaveSettingsAsync(settings);
                    var servers = await vm.ConfigManager.LoadServersAsync();
                    vm.ServerList.LoadServers(servers, settings);
                }
            }
        };
        menu.Items.Add(newFolderItem);

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

        // SFTP, local file browser, and their ListViews have their own context menus.
        // Check the visual tree source to avoid overriding them.
        var clickSource = e.OriginalSource as DependencyObject;
        if (clickSource is not null)
        {
            // Check for any ancestor that has its own context menu
            if (FindAncestor<Views.EmbeddedSftpView>(clickSource) is not null
                || FindAncestor<Views.LocalFileBrowserView>(clickSource) is not null)
            {
                return;
            }

            // Also check for ListViewItem inside a split pane (the view might not
            // be a direct ancestor due to ContentPresenter wrapping)
            var listViewItem = FindAncestor<System.Windows.Controls.ListViewItem>(clickSource);
            if (listViewItem is not null && session.IsSplit)
            {
                return;
            }
        }

        var menu = new System.Windows.Controls.ContextMenu();

        var disconnectItem = new System.Windows.Controls.MenuItem { Header = vm.Localize("SessionDisconnect") };
        disconnectItem.Click += (_, _) => vm.Connection.CloseSessionCommand.Execute(session);
        menu.Items.Add(disconnectItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var aspectMenu = new System.Windows.Controls.MenuItem { Header = vm.Localize("SessionAspectRatio") };
        foreach (var (label, tag) in new[] { ("Stretch", "Stretch"), ("Auto", "Auto"), ("16:9", "Ratio16x9"), ("4:3", "Ratio4x3"), ("21:9", "Ratio21x9") })
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, Tag = tag };
            item.Click += OnAspectRatioClick;
            aspectMenu.Items.Add(item);
        }
        menu.Items.Add(aspectMenu);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var fullscreenItem = new System.Windows.Controls.MenuItem { Header = vm.Localize("SessionFullscreen") };
        fullscreenItem.Click += OnToggleFullscreenClick;
        menu.Items.Add(fullscreenItem);

        // Duplicate tab (reconnect same server in new tab)
        var duplicateItem = new System.Windows.Controls.MenuItem { Header = vm.Localize("SessionDuplicateTab") };
        duplicateItem.Click += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(session.ServerId) && vm.ServerList.ConnectCommand is not null)
            {
                var serverVm = vm.ServerList.Servers.FirstOrDefault(
                    s => string.Equals(s.Id, session.ServerId, StringComparison.Ordinal));
                if (serverVm is not null)
                {
                    vm.ServerList.ConnectCommand.Execute(serverVm);
                }
            }
        };
        menu.Items.Add(duplicateItem);

        var closeItem = new System.Windows.Controls.MenuItem { Header = vm.Localize("SessionCloseSession") };
        closeItem.Click += (_, _) => vm.Connection.CloseSessionCommand.Execute(session);
        menu.Items.Add(closeItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        if (!session.IsSplit)
        {
            var splitH = new System.Windows.Controls.MenuItem { Header = vm.Localize("SplitHorizontal") };
            splitH.Click += (_, _) => RequestSplitSession(session, Heimdall.Core.Models.SplitOrientation.Horizontal);
            menu.Items.Add(splitH);

            var splitV = new System.Windows.Controls.MenuItem { Header = vm.Localize("SplitVertical") };
            splitV.Click += (_, _) => RequestSplitSession(session, Heimdall.Core.Models.SplitOrientation.Vertical);
            menu.Items.Add(splitV);
        }
        else
        {
            var unsplit = new System.Windows.Controls.MenuItem { Header = vm.Localize("SplitUnsplit") };
            unsplit.Click += (_, _) => UnsplitSession(session);
            menu.Items.Add(unsplit);
        }

        menu.PlacementTarget = SessionTabControl;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void RequestSplitSession(
        SessionTabViewModel session,
        Heimdall.Core.Models.SplitOrientation orientation)
    {
        if (DataContext is not MainViewModel vm) return;

        // Build a picker context menu with two sections:
        // 1. Open sessions (already connected — move to split pane)
        // 2. Servers from TreeView (new connection)
        var menu = new System.Windows.Controls.ContextMenu();

        // Section: existing open sessions
        var openSessions = vm.Connection.ActiveSessions
            .Where(s => s != session && !s.IsSplit)
            .ToList();

        if (openSessions.Count > 0)
        {
            var header = new System.Windows.Controls.MenuItem
            {
                Header = vm.Localize("SplitMoveOpenSession"),
                IsEnabled = false,
                FontWeight = FontWeights.SemiBold
            };
            menu.Items.Add(header);

            foreach (var openSession in openSessions)
            {
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = $"{openSession.Title} ({openSession.ConnectionType})",
                    Tag = openSession
                };
                item.Click += (_, _) =>
                {
                    // Move the open session's host control to the secondary pane
                    vm.Connection.ActiveSessions.Remove(openSession);
                    session.SecondaryHostControl = openSession.HostControl;
                    session.SecondaryServerId = openSession.ServerId;
                    session.SecondaryConnectionType = openSession.ConnectionType;
                    session.SplitOrientation = orientation;
                    session.IsSplit = true;
                    vm.Connection.HasActiveSessions = vm.Connection.ActiveSessions.Count > 0;
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new System.Windows.Controls.Separator());
        }

        // Section: new connection from server list
        var newConnHeader = new System.Windows.Controls.MenuItem
        {
            Header = vm.Localize("SplitNewConnection"),
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        };
        menu.Items.Add(newConnHeader);

        // Flatten the folder tree: collect all servers recursively
        void AddFolderServers(FolderViewModel folder)
        {
            AddServersToSplitMenu(menu, folder.Servers, session, orientation, vm, "", folder.FullPath);
            foreach (var sub in folder.SubFolders)
            {
                AddFolderServers(sub);
            }
        }

        foreach (var folder in vm.ServerList.GroupedServers)
        {
            AddFolderServers(folder);
        }

        menu.PlacementTarget = SessionTabControl;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void AddServersToSplitMenu(
        System.Windows.Controls.ContextMenu menu,
        System.Collections.ObjectModel.ObservableCollection<ServerItemViewModel> servers,
        SessionTabViewModel session,
        Heimdall.Core.Models.SplitOrientation orientation,
        MainViewModel vm,
        string projectName,
        string? groupName)
    {
        foreach (var server in servers)
        {
            string label = string.IsNullOrWhiteSpace(groupName)
                ? $"{server.DisplayName} ({server.ConnectionType})"
                : $"{server.DisplayName} ({server.ConnectionType}) — {groupName}";

            var item = new System.Windows.Controls.MenuItem { Header = label };
            var capturedServer = server;
            item.Click += async (_, _) =>
            {
                await vm.SplitSessionWithServerAsync(session, capturedServer.Id, orientation);
            };
            menu.Items.Add(item);
        }
    }

    private void UnsplitSession(SessionTabViewModel session)
    {
        if (!session.IsSplit) return;
        if (DataContext is not MainViewModel vm) return;

        var secondaryControl = session.SecondaryHostControl;
        var secondaryServerId = session.SecondaryServerId;
        var secondaryType = session.SecondaryConnectionType;

        // Remove from split
        session.SecondaryHostControl = null;
        session.SecondaryServerId = "";
        session.SecondaryConnectionType = "";
        session.IsSplit = false;

        // Restore as independent tab (instead of disposing)
        if (secondaryControl is not null && !string.IsNullOrEmpty(secondaryServerId))
        {
            var restoredTab = vm.Connection.AddSession(
                secondaryServerId,
                $"Restored ({secondaryType})",
                secondaryType);
            restoredTab.HostControl = secondaryControl;
            restoredTab.Status = "Connected";
        }
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

        Heimdall.Core.Logging.FileLogger.Info(
            $"UpdateTabVisibility: selectedTab={vm.SelectedTab}, isServers={isServers}, hasSessions={hasSessions}, sidebarHidden={_sidebarHidden}");

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
            ShowSidebarButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            _sidebarHidden = true;
            _savedSidebarWidth = ServerTreeColumn.ActualWidth;
            ServerTreeColumn.MinWidth = 0;
            ServerTreeColumn.MaxWidth = 0;
            ServerTreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            ShowSidebarButton.Visibility = Visibility.Visible;
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

    // --- Command Palette event handlers ---

    private void OnPaletteKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Escape)
        {
            vm.CloseCommandPaletteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+Enter = connect in split pane
            var target = vm.SelectedPaletteItem ?? vm.PaletteResults.FirstOrDefault();
            _ = vm.ConnectSplitFromPaletteCommand.ExecuteAsync(target);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _ = vm.ConnectFromPaletteCommand.ExecuteAsync(
                vm.SelectedPaletteItem ?? vm.PaletteResults.FirstOrDefault());
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (PaletteResultsList.SelectedIndex < PaletteResultsList.Items.Count - 1)
                PaletteResultsList.SelectedIndex++;
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (PaletteResultsList.SelectedIndex > 0)
                PaletteResultsList.SelectedIndex--;
            e.Handled = true;
        }
    }

    private void OnPaletteBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseCommandPaletteCommand.Execute(null);
    }

    private void OnPaletteBorderClick(object sender, MouseButtonEventArgs e)
    {
        // Prevent closing when clicking inside the palette
        e.Handled = true;
    }

    private void OnPaletteItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedPaletteItem is not null)
            _ = vm.ConnectFromPaletteCommand.ExecuteAsync(vm.SelectedPaletteItem);
    }
}
