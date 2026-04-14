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
using Heimdall.App.Services;
using Heimdall.App.ViewModels;

namespace Heimdall.App;

/// <summary>
/// Partial of <see cref="MainWindow"/> hosting the imperative window-UI
/// handlers that mutate named XAML elements: fullscreen toggle, sidebar
/// hide/show, tree scroll save/restore, and folder expand/collapse-all.
/// All transient state lives in the <c>_uiState</c> field
/// (<see cref="WindowUIState"/>); this file only contains the glue that
/// reads/writes that state and pokes the visual tree accordingly.
/// </summary>
public partial class MainWindow
{
    // ── Tree scroll save/restore ─────────────────────────────────────

    private void SaveTreeViewScrollPosition()
    {
        var sv = WindowUIState.FindScrollViewer(SessionTreeView);
        if (sv is not null)
        {
            _uiState.TreeScrollVerticalOffset = sv.VerticalOffset;
            _uiState.TreeScrollHorizontalOffset = sv.HorizontalOffset;
        }
    }

    private void RestoreTreeViewScrollPosition()
    {
        // Defer to allow the TreeView to re-render before restoring scroll position
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                var sv = WindowUIState.FindScrollViewer(SessionTreeView);
                if (sv is not null)
                {
                    sv.ScrollToVerticalOffset(_uiState.TreeScrollVerticalOffset);
                    sv.ScrollToHorizontalOffset(_uiState.TreeScrollHorizontalOffset);
                }
            }));
    }

    // ── Fullscreen toggle ────────────────────────────────────────────

    private void OnToggleFullscreenClick(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (DataContext is not MainViewModel)
            return;

        if (_uiState.IsFullscreen)
        {
            // Exit fullscreen
            _uiState.IsFullscreen = false;
            FullscreenBar.Visibility = Visibility.Collapsed;
            NotifyEmbeddedViewsFullscreen(false);

            // Show toolbar, TreeView, status bar
            ToolbarRow.Height = new GridLength(WindowUIState.ToolbarHeight);
            StatusBarRow.Height = new GridLength(WindowUIState.StatusBarHeight);
            SessionTreeColumn.Width = new GridLength(WindowUIState.DefaultSidebarWidth);
            SessionTreeColumn.MinWidth = WindowUIState.MinSidebarWidth;
            SessionTreeColumn.MaxWidth = WindowUIState.MaxSidebarWidth;
            SplitterColumn.Width = GridLength.Auto;

            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _uiState.PreFullscreenState;
            if (_uiState.PreFullscreenState == WindowState.Normal)
            {
                Width = _uiState.PreFullscreenWidth;
                Height = _uiState.PreFullscreenHeight;
            }
        }
        else
        {
            // Enter fullscreen
            _uiState.IsFullscreen = true;
            _uiState.PreFullscreenState = WindowState;
            _uiState.PreFullscreenWidth = ActualWidth;
            _uiState.PreFullscreenHeight = ActualHeight;

            // Hide toolbar, TreeView, status bar
            ToolbarRow.Height = new GridLength(0);
            StatusBarRow.Height = new GridLength(0);
            SessionTreeColumn.MinWidth = 0;
            SessionTreeColumn.MaxWidth = 0;
            SessionTreeColumn.Width = new GridLength(0);
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
        if (_uiState.IsFullscreen) ToggleFullscreen();
    }

    // ── Sidebar toggle ───────────────────────────────────────────────

    private void OnToggleSidebarClick(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar()
    {
        if (_uiState.IsSidebarHidden)
        {
            _uiState.IsSidebarHidden = false;
            SessionTreeColumn.MinWidth = WindowUIState.MinSidebarWidth;
            SessionTreeColumn.MaxWidth = WindowUIState.MaxSidebarWidth;
            SessionTreeColumn.Width = new GridLength(_uiState.SavedSidebarWidth);
            SplitterColumn.Width = GridLength.Auto;
            ShowSidebarButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            _uiState.IsSidebarHidden = true;
            _uiState.SavedSidebarWidth = SessionTreeColumn.ActualWidth;
            SessionTreeColumn.MinWidth = 0;
            SessionTreeColumn.MaxWidth = 0;
            SessionTreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            ShowSidebarButton.Visibility = Visibility.Visible;
        }
    }

    // ── Folder expand/collapse all ───────────────────────────────────

    private void OnExpandAllClick(object sender, RoutedEventArgs e)
    {
        SetAllFoldersExpanded(true);
    }

    private void OnCollapseAllClick(object sender, RoutedEventArgs e)
    {
        SetAllFoldersExpanded(false);
    }

    private void SetAllFoldersExpanded(bool expanded)
    {
        if (DataContext is not MainViewModel vm) return;

        foreach (var folder in vm.ServerList.GroupedServers)
        {
            WindowUIState.SetFolderExpandedRecursive(folder, expanded);
        }
    }
}
