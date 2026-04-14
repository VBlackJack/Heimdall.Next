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

using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Heimdall.App.Services;
using Heimdall.App.Theming;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Onboarding;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.App;

/// <summary>
/// Main application window. All logic lives in <see cref="MainViewModel"/>.
/// Code-behind is limited to keyboard shortcut routing, TreeView interaction, and window lifecycle.
/// </summary>
public partial class MainWindow : Window, IContextMenuCallbacks
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    /// <summary>
    /// Centralized tool registry — injected from the ViewModel. Replaces the former
    /// static <c>ToolTypeDefinitions</c> and <c>NetworkTools</c> arrays.
    /// </summary>
    private ToolRegistry ToolRegistry => ((MainViewModel)DataContext).ToolRegistry;

    private object? _treeContextTarget;
    private bool _treeContextTargetFromPointer;
    private bool _treeContextPointerHitEmptyArea;
    private readonly Services.ThemeService _themeService;
    private readonly ContextMenuFactory _contextMenuFactory;
    private readonly FileShareService _fileShareService;
    private readonly WindowUIState _uiState = new();
    private readonly ToolsTabPopulationService _toolsTabPopulation;
    private bool _sidebarTabRestored;
    private bool _closeConfirmed;
    private OnboardingFlowViewModel? _onboardingVm;

    // Stored delegate references so the long-lived service/ViewModel events
    // wired in the constructor can be unsubscribed in OnClosed. Without this,
    // the captured-`this` lambdas would keep the window alive past Close().
    private System.ComponentModel.PropertyChangedEventHandler? _connectionPropertyChangedHandler;
    private System.ComponentModel.PropertyChangedEventHandler? _serverListPropertyChangedHandler;
    private Action? _externalToolsChangedHandler;
    private Action<string>? _localeChangedHandler;

    public MainWindow(
        MainViewModel viewModel,
        Services.ThemeService themeService,
        ContextMenuFactory contextMenuFactory,
        ToolsTabPopulationService toolsTabPopulation,
        FileShareService fileShareService)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
        DataContext = viewModel;
        _themeService = themeService;
        _contextMenuFactory = contextMenuFactory;
        _toolsTabPopulation = toolsTabPopulation;
        _fileShareService = fileShareService;
        _fileShareService.SharingStarted += OnFileShareSharingStarted;
        _fileShareService.SharingStopped += OnFileShareSharingStopped;
        _fileShareService.FileServed += OnFileShareFileServed;
        _themeService.ThemeChanged += OnThemeServiceThemeChanged;
        ApplyLocalization();

        TabSessions.Checked += OnSessionsTabChecked;
        TabTunnels.Checked += OnTunnelsTabChecked;
        TabScheduled.Checked += OnScheduledTabChecked;
        TabTools.Checked += OnToolsTabChecked;
        TabSettings.Checked += OnSettingsTabChecked;
        TabAbout.Checked += OnAboutTabChecked;
        _connectionPropertyChangedHandler = (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ConnectionViewModel.HasActiveSessions), StringComparison.Ordinal))
            {
                Heimdall.Core.Logging.FileLogger.Info(
                    $"Navigation session state changed: hasSessions={viewModel.Connection.HasActiveSessions}, selectedTab={viewModel.SelectedTab}");
                UpdateTabVisibility(viewModel);
            }
        };
        viewModel.Connection.PropertyChanged += _connectionPropertyChangedHandler;

        _serverListPropertyChangedHandler = (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ServerListViewModel.SelectedServer), StringComparison.Ordinal))
            {
                Dispatcher.Invoke(UpdateToolLaunchContextLabels);
            }
        };
        viewModel.ServerList.PropertyChanged += _serverListPropertyChangedHandler;

        // Refresh Tools tab and Settings status when background scan discovers external tools
        _externalToolsChangedHandler = () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _toolsTabPopulated = false;
                PopulateToolsTab();

                // Invalidate the sidebar Tools cache too; rebuild if it is the
                // currently active sidebar tab, otherwise lazy-rebuild on next switch.
                _sidebarToolsPopulated = false;
                if (SidebarTabTools.IsChecked == true)
                {
                    BuildSidebarToolsData();
                }

                if (DataContext is MainViewModel vm)
                    UpdateExternalToolProviderStatus(vm);
            });
        };
        viewModel.ToolRegistry.ExternalToolsChanged += _externalToolsChangedHandler;

        // Wire split button callback from embedded views
        viewModel.EmbeddedSessionManager.SplitRequestedCallback = OnEmbeddedSplitRequested;

        Loaded += async (_, _) =>
        {
            if (viewModel.LoadCommand.CanExecute(null))
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }

            if (viewModel.CurrentSettings is { } loadedSettings)
            {
                RestoreWindowBounds(loadedSettings);
            }
            PopulateAboutSection();

            // Restore the active sidebar tab (Servers / Tools) from persisted setting.
            // ShowToolsPanel is reused as a bool flag: true = Tools tab, false = Servers tab.
            if (viewModel.Settings.ShowToolsPanel)
            {
                SidebarTabTools.IsChecked = true;
            }
            _sidebarTabRestored = true;

            // Show onboarding overlay on first launch
            if (viewModel.CurrentSettings is not null && !viewModel.CurrentSettings.OnboardingCompleted)
            {
                ShowOnboardingOverlay(viewModel.CurrentSettings);
            }
        };

        // Re-apply all localized strings when the user switches language at runtime
        _localeChangedHandler = (_) =>
        {
            Dispatcher.Invoke(() => ApplyLocalization());
        };
        viewModel.GetLocalizer().LocaleChanged += _localeChangedHandler;

        KeyDown += OnKeyDown;
        PreviewMouseDown += OnWindowPreviewMouseDown;
        Deactivated += OnWindowDeactivated;
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
            AboutVersionText.Text = string.Format("Version {0}", infoVersion);
        AboutRuntimeText.Text = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        AboutPlatformText.Text = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})";
    }

    private async void OnSessionsTabChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckUnsavedSettingsAsync()) { TabSettings.IsChecked = true; return; }
            SwitchToTab("Sessions");
        }
        catch (Exception ex) { Core.Logging.FileLogger.Error($"Tab switch failed: {ex.Message}"); }
    }

    private async void OnTunnelsTabChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckUnsavedSettingsAsync()) { TabSettings.IsChecked = true; return; }
            SwitchToTab("Tunnels");
        }
        catch (Exception ex) { Core.Logging.FileLogger.Error($"Tab switch failed: {ex.Message}"); }
    }

    private async void OnScheduledTabChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckUnsavedSettingsAsync()) { TabSettings.IsChecked = true; return; }
            SwitchToTab("Scheduled");
        }
        catch (Exception ex) { Core.Logging.FileLogger.Error($"Tab switch failed: {ex.Message}"); }
    }

    private void OnSettingsTabChecked(object sender, RoutedEventArgs e)
    {
        SwitchToTab("Settings");
    }

    private void OnNavigateToGatewaySettings(object sender, RoutedEventArgs e)
    {
        TabSettings.IsChecked = true;
        SwitchToTab("Settings");
        Mw_SettingsSubTabControl.SelectedItem = Mw_SettingsTabSsh;
    }

    /// <summary>
    /// Prompts the user to discard unsaved settings changes when navigating away.
    /// Returns true if navigation should proceed, false if it should be cancelled.
    /// </summary>
    private async Task<bool> CheckUnsavedSettingsAsync()
    {
        if (DataContext is not MainViewModel vm) return true;
        if (vm.SelectedTab != "Settings") return true;
        if (!vm.Settings.IsDirty) return true;

        var discard = await vm.DialogService.ShowConfirmAsync(
            vm.Localize("SettingsUnsavedWarningTitle"),
            vm.Localize("SettingsUnsavedWarning"),
            "warning");

        if (discard)
        {
            await vm.Settings.DiscardChangesAsync();
        }

        return discard;
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

    private async void OnAddToolMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || DataContext is not MainViewModel vm)
        {
            return;
        }

        await ShowAddToolPickerAsync(vm, item.Tag as string);
    }

    private async Task ShowAddToolPickerAsync(MainViewModel vm, string? group = null)
    {
        try
        {
            var dialog = new Views.Dialogs.ToolPickerDialog(
                vm.GetLocalizer(),
                ToolRegistry.All,
                group,
                vm.ServerList.SelectedServer?.RemoteServer)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.SelectedTool is null)
            {
                return;
            }

            var host = dialog.SelectedTool.IsNetworkTool
                ? dialog.SelectedHost
                : string.Empty;

            var dto = new Core.Configuration.ServerProfileDto
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = dialog.SelectedDisplayName,
                ConnectionType = dialog.SelectedTool.ToolType,
                RemoteServer = host,
                Group = string.IsNullOrWhiteSpace(group) ? null : group
            };

            var servers = await vm.ConfigManager.LoadServersAsync();
            servers.Add(dto);
            await vm.ConfigManager.SaveServersAsync(servers);

            var settings = await vm.ConfigManager.LoadSettingsAsync();
            vm.ServerList.LoadServers(servers, settings);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Add tool failed: {ex.Message}");
        }
    }

    private async void OnToolsTabChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckUnsavedSettingsAsync()) { TabSettings.IsChecked = true; return; }
            SwitchToTab("Tools");
            PopulateToolsTab();
        }
        catch (Exception ex) { Core.Logging.FileLogger.Error($"Tab switch failed: {ex.Message}"); }
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

        // Close the Command Palette when switching tabs
        if (vm.IsCommandPaletteOpen)
        {
            vm.CloseCommandPaletteCommand.Execute(null);
        }

        // Save TreeView scroll position when leaving Sessions tab
        if (vm.IsSessionsTabSelected && !string.Equals(tabName, "Sessions", StringComparison.Ordinal))
        {
            SaveTreeViewScrollPosition();
        }

        Heimdall.Core.Logging.FileLogger.Info(
            $"Navigation request: tab={tabName}, current={vm.SelectedTab}, hasSessions={vm.Connection.HasActiveSessions}");

        vm.SelectedTab = tabName;
        UpdateTabVisibility(vm);

        // Restore TreeView scroll position when returning to Sessions tab
        if (string.Equals(tabName, "Sessions", StringComparison.Ordinal))
        {
            RestoreTreeViewScrollPosition();
        }

        Heimdall.Core.Logging.FileLogger.Info(
            $"Navigation applied: tab={vm.SelectedTab}, sessionsVisible={vm.IsSessionsTabSelected}, tunnelsVisible={vm.IsTunnelsTabSelected}, scheduledVisible={vm.IsScheduledTabSelected}, settingsVisible={vm.IsSettingsTabSelected}");
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

    private void OnScheduledTaskDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && vm.SelectedScheduledTask is not null)
        {
            vm.EditScheduledTaskCommand.Execute(null);
        }
    }

    private void OnFilterClearClick(object sender, RoutedEventArgs e)
    {
        Mw_FilterBox.Text = string.Empty;
        Mw_FilterBox.Focus();
    }

    private void OnFilterBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrEmpty(Mw_FilterBox.Text);
        Mw_FilterClearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        if (DataContext is ViewModels.MainViewModel vm)
        {
            var count = vm.ServerList.FilteredCount;
            if (hasText)
            {
                Mw_FilterResultCount.Text = string.Format(vm.Localize("SearchResultCount"), count);
                Mw_FilterResultCount.Visibility = Visibility.Visible;
            }
            else
            {
                Mw_FilterResultCount.Visibility = Visibility.Collapsed;
            }
        }
    }

    // Settings search: keyword map for each sub-tab (English keywords, always match)
    private static readonly string[][] SettingsTabKeywords =
    [
        // General
        ["language", "theme", "editor", "appearance", "sessions", "sleep", "dark", "light", "langue", "apparence"],
        // Terminal
        ["terminal", "font", "color", "scheme", "powershell", "execution", "policy", "dracula", "monokai", "nord", "solarized", "police", "couleur"],
        // SSH & SFTP
        ["ssh", "sftp", "plink", "gateway", "tunnel", "anti-idle", "x11", "agent", "key", "passerelle", "cl\u00e9"],
        // RDP
        ["rdp", "remote desktop", "resolution", "nla", "multi-monitor", "clipboard", "drives", "printers", "audio", "redirect", "color depth", "bitmap", "reconnect", "bureau \u00e0 distance"],
        // Security
        ["security", "credential", "password", "keepass", "provider", "guard", "s\u00e9curit\u00e9", "mot de passe"],
        // Advanced
        ["logging", "log", "timeout", "external", "tools", "delay", "session", "journal", "d\u00e9lai", "outils"],
    ];

    private void OnSettingsSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = Mw_SettingsSearchBox.Text?.Trim() ?? "";
        var hasText = !string.IsNullOrEmpty(query);
        Mw_SettingsSearchClear.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        var tabs = new TabItem[]
        {
            Mw_SettingsTabGeneral, Mw_SettingsTabTerminal, Mw_SettingsTabSsh,
            Mw_SettingsTabRdp, Mw_SettingsTabSecurity, Mw_SettingsTabAdvanced,
        };

        if (!hasText)
        {
            // Show all tabs
            foreach (var tab in tabs)
            {
                tab.Visibility = Visibility.Visible;
            }

            Mw_SettingsSearchHint.Visibility = Visibility.Collapsed;
            return;
        }

        string queryLower = query.ToLowerInvariant();
        int matchCount = 0;
        TabItem? firstMatch = null;

        for (int i = 0; i < tabs.Length && i < SettingsTabKeywords.Length; i++)
        {
            string[] keywords = SettingsTabKeywords[i];
            // Also match against the tab header text
            string headerText = tabs[i].Header?.ToString()?.ToLowerInvariant() ?? "";
            // Explicit string types (instead of var) avoid an SDK 10.0.201 overload-resolution
            // quirk that was mis-inferring queryLower as int and routing Contains to the
            // Contains(char, StringComparison) overload.
            bool matches = keywords.Any((string k) => k.Contains(queryLower))
                           || headerText.Contains(queryLower);

            tabs[i].Visibility = matches ? Visibility.Visible : Visibility.Collapsed;

            if (matches)
            {
                matchCount++;
                firstMatch ??= tabs[i];
            }
        }

        // Auto-select the first matching tab if the current selection is hidden
        if (Mw_SettingsSubTabControl.SelectedItem is TabItem selected && selected.Visibility == Visibility.Collapsed && firstMatch is not null)
        {
            Mw_SettingsSubTabControl.SelectedItem = firstMatch;
        }

        // Show hint text
        if (DataContext is MainViewModel vm)
        {
            Mw_SettingsSearchHintText.Text = string.Format(
                vm.Localize("SettingsSearchResultCount"), matchCount, tabs.Length);
            Mw_SettingsSearchHint.Visibility = Visibility.Visible;
        }
    }

    private void OnSettingsSearchClearClick(object sender, RoutedEventArgs e)
    {
        Mw_SettingsSearchBox.Text = string.Empty;
        Mw_SettingsSearchBox.Focus();
    }

    /// <summary>
    /// Returns true when the keyboard focus is inside embedded content (terminal
    /// WebView2 or WebView2-based tool view), meaning single-modifier shortcuts
    /// should be forwarded to the content instead of being intercepted by the shell.
    /// Includes a fallback for tool sessions where WebView2 HWND focus tracking
    /// is unreliable (draw.io iframe, etc.).
    /// </summary>
    private bool IsEmbeddedContentFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;

        // Direct WebView2 focus detection (works when WPF properly tracks HWND focus)
        if (focused is Microsoft.Web.WebView2.Wpf.WebView2
            || FindAncestor<Views.EmbeddedSshView>(focused) is not null
            || FindAncestor<Views.EmbeddedVncView>(focused) is not null)
        {
            return true;
        }

        return false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        // When a terminal has focus, only intercept Ctrl+Shift combos and F-keys;
        // let single-Ctrl shortcuts (Ctrl+B/F/K) pass through to the remote session.
        // The OriginalSource check catches WebView2 AcceleratorKeyPressed events that
        // WPF's stale Keyboard.FocusedElement misses (known HwndHost focus gap).
        var terminalHasFocus = IsEmbeddedContentFocused()
            || e.OriginalSource is Microsoft.Web.WebView2.Wpf.WebView2;

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
                if (terminalHasFocus) break;
                if (vm.ServerList.SelectedServer is not null &&
                    vm.ServerList.DeleteServerCommand.CanExecute(vm.ServerList.SelectedServer))
                {
                    vm.ServerList.DeleteServerCommand.Execute(vm.ServerList.SelectedServer);
                }
                e.Handled = true;
                break;

            case Key.E when Keyboard.Modifiers == ModifierKeys.Control:
                if (terminalHasFocus) break;
                if (vm.ServerList.SelectedServer is not null &&
                    vm.ServerList.EditServerCommand.CanExecute(vm.ServerList.SelectedServer))
                {
                    vm.ServerList.EditServerCommand.Execute(vm.ServerList.SelectedServer);
                }
                e.Handled = true;
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                if (terminalHasFocus) break;
                Mw_FilterBox.Focus();
                Mw_FilterBox.SelectAll();
                e.Handled = true;
                break;

            case Key.B when Keyboard.Modifiers == ModifierKeys.Control:
                if (terminalHasFocus) break;
                ToggleSidebar();
                e.Handled = true;
                break;

            case Key.K when Keyboard.Modifiers == ModifierKeys.Control:
                if (terminalHasFocus) break;
                vm.OpenCommandPaletteCommand.Execute(null);
                BeginFocusCommandPalette();
                e.Handled = true;
                break;

            case Key.S when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                CaptureActiveSessionScreenshot(vm);
                e.Handled = true;
                break;

            case Key.N when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                _ = LaunchNetworkScannerAsync(vm);
                e.Handled = true;
                break;

            case Key.T when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                ToggleSidebarTab();
                e.Handled = true;
                break;

            case Key.O when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                if (vm.Connection.ActiveSession is { IsSplit: true } splitSession)
                {
                    vm.ToggleSplitOrientation(splitSession);
                }
                e.Handled = true;
                break;

            case Key.W when Keyboard.Modifiers == ModifierKeys.Control:
                if (terminalHasFocus) break;
                if (vm.Connection.ActiveSession is not null &&
                    vm.Connection.CloseSessionCommand.CanExecute(vm.Connection.ActiveSession))
                {
                    vm.Connection.CloseSessionCommand.Execute(vm.Connection.ActiveSession);
                }
                e.Handled = true;
                break;

            case Key.Tab when Keyboard.Modifiers == ModifierKeys.Control:
                {
                    var sessions = vm.Connection.ActiveSessions;
                    if (sessions.Count > 1 && vm.Connection.ActiveSession is not null)
                    {
                        var idx = sessions.IndexOf(vm.Connection.ActiveSession);
                        vm.Connection.ActiveSession = sessions[(idx + 1) % sessions.Count];
                    }
                    e.Handled = true;
                    break;
                }

            case Key.Tab when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                {
                    var sessions = vm.Connection.ActiveSessions;
                    if (sessions.Count > 1 && vm.Connection.ActiveSession is not null)
                    {
                        var idx = sessions.IndexOf(vm.Connection.ActiveSession);
                        vm.Connection.ActiveSession = sessions[(idx - 1 + sessions.Count) % sessions.Count];
                    }
                    e.Handled = true;
                    break;
                }

            case Key.F1:
                ShowKeyboardShortcutHelp();
                e.Handled = true;
                break;

            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Key.Escape when _uiState.IsFullscreen:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Key.Apps:
            case Key.F10 when Keyboard.Modifiers == ModifierKeys.Shift:
                if (terminalHasFocus) break;
                OpenTreeViewKeyboardContextMenu(vm);
                e.Handled = true;
                break;
        }
    }

    private void ShowKeyboardShortcutHelp()
    {
        if (DataContext is not MainViewModel vm) return;

        var shortcuts = vm.Localize("HelpShortcutsContent");
        MessageBox.Show(shortcuts, vm.Localize("HelpShortcutsTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

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
        var container = FindTreeViewItemContainer(SessionTreeView, target);
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

    /// <summary>
    /// Walks the TreeView item container hierarchy to find the container for a given data item.
    /// Required because virtualized TreeViews only materialize visible containers.
    /// </summary>
    private static TreeViewItem? FindTreeViewItemContainer(ItemsControl parent, object? item)
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

    /// <summary>
    /// Captures the active session tab content as an image and copies it to the clipboard.
    /// </summary>
    private void CaptureActiveSessionScreenshot(MainViewModel vm)
    {
        try
        {
            var session = vm.Connection.ActiveSession;
            if (session?.HostControl is not UIElement element)
            {
                return;
            }

            var bounds = new System.Windows.Size(element.RenderSize.Width, element.RenderSize.Height);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(element);
            var bitmap = new RenderTargetBitmap(
                (int)(bounds.Width * dpi.DpiScaleX),
                (int)(bounds.Height * dpi.DpiScaleY),
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);

            bitmap.Render(element);
            Clipboard.SetImage(bitmap);
            vm.StatusText = vm.Localize("ScreenshotCaptured");
        }
        catch
        {
            vm.StatusText = vm.Localize("ScreenshotFailed");
        }
    }

    private async Task LaunchNetworkScannerAsync(MainViewModel vm)
    {
        var cidr = await vm.DialogService.ShowInputAsync(
            vm.Localize("NetworkScannerTitle"),
            vm.Localize("NetworkScannerCidrPrompt"),
            "192.168.1.0/24");

        if (string.IsNullOrWhiteSpace(cidr)) return;

        vm.StatusText = string.Format(vm.Localize("NetworkScannerScanning"), cidr, 0, "...");

        try
        {
            var results = await Core.Security.NetworkScanner.ScanSubnetAsync(
                cidr,
                (done, total) => Dispatcher.InvokeAsync(() =>
                    vm.StatusText = string.Format(
                        vm.Localize("NetworkScannerScanning"), cidr, done, total)));

            if (results.Count == 0)
            {
                vm.StatusText = string.Format(vm.Localize("NetworkScannerNoHosts"), cidr);
                return;
            }

            vm.StatusText = string.Format(vm.Localize("NetworkScannerComplete"), results.Count);

            // Build a summary and offer to add discovered hosts
            var summary = string.Join("\n", results.Select(r =>
            {
                var ports = r.OpenPorts.Count > 0 ? string.Join(", ", r.OpenPorts) : "-";
                var host = r.Hostname ?? r.IpAddress;
                return $"{r.IpAddress}  {host}  [{ports}]  {r.RoundtripMs}ms";
            }));

            var addServers = await vm.DialogService.ShowConfirmAsync(
                string.Format(vm.Localize("NetworkScannerComplete"), results.Count),
                summary + "\n\n" + vm.Localize("NetworkScannerAddServer"),
                "info");

            if (addServers)
            {
                var existingServers = await vm.ConfigManager.LoadServersAsync();

                foreach (var result in results)
                {
                    var connType = result.OpenPorts.Contains(3389) ? "RDP"
                        : result.OpenPorts.Contains(22) ? "SSH"
                        : result.OpenPorts.Contains(5900) ? "VNC"
                        : "SSH";
                    var port = connType switch
                    {
                        "RDP" => 3389,
                        "VNC" => 5900,
                        _ => 22
                    };

                    existingServers.Add(new Core.Configuration.ServerProfileDto
                    {
                        Id = Guid.NewGuid().ToString(),
                        DisplayName = result.Hostname ?? result.IpAddress,
                        RemoteServer = result.IpAddress,
                        RemotePort = port,
                        ConnectionType = connType,
                        Group = "Discovered"
                    });
                }

                await vm.ConfigManager.SaveServersAsync(existingServers);
                // Reload the full configuration to refresh the TreeView
                await vm.ReloadConfigurationAsync(await vm.ConfigManager.LoadSettingsAsync());
            }
        }
        catch (Exception ex)
        {
            vm.StatusText = string.Format(vm.Localize("NetworkScannerError"), ex.Message);
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

    private TreeViewItem? _lastDropHighlight;

    private void ClearDropHighlight()
    {
        if (_lastDropHighlight is not null)
        {
            _lastDropHighlight.BorderThickness = new Thickness(0);
            _lastDropHighlight.BorderBrush = null;
            _lastDropHighlight = null;
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
            target.BorderThickness = new Thickness(1);
            target.BorderBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
            _lastDropHighlight = target;
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

        var menu = _contextMenuFactory.CreateTreeContextMenu(target, vm, this);
        menu.PlacementTarget = treeView;
        menu.Placement = PlacementMode.MousePoint;
        treeView.ContextMenu = menu;
    }

    /// <summary>
    /// Launches an auto-detected third-party tool with server context placeholders resolved.
    /// Handles elevation (UAC) for tools that require it.
    /// </summary>
    private static void LaunchDetectedTool(
        MainViewModel vm,
        Core.Configuration.ExternalToolInfo tool,
        ServerItemViewModel server)
    {
        try
        {
            // Resolve placeholders in arguments template
            var arguments = tool.Arguments
                .Replace("{Host}", SanitizeToolArgument(server.RemoteServer), StringComparison.OrdinalIgnoreCase)
                .Replace("{Port}", server.EffectivePort.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{User}", SanitizeToolArgument(server.Username), StringComparison.OrdinalIgnoreCase);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tool.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = true
            };

            if (tool.RequiresElevation)
                psi.Verb = "runas";

            System.Diagnostics.Process.Start(psi)?.Dispose();

            Core.Logging.FileLogger.Info(
                $"Detected tool launched: {tool.ProviderName}/{tool.Name} → {tool.ExecutablePath} {arguments}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined UAC prompt; not an error
            Core.Logging.FileLogger.Info($"UAC cancelled for {tool.Name}");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Detected tool launch failed: {tool.Name}", ex);
            MessageBox.Show(
                string.Format(vm.Localize("ExternalToolLaunchError"), tool.Name, ex.Message),
                vm.Localize("AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Strips shell metacharacters from a tool argument value to prevent injection.
    /// </summary>
    private static string SanitizeToolArgument(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(value, @"[;&|`$<>()!""'\r\n%^]", "");
    }

    /// <summary>
    /// Launches an external tool in a visible console window with variable placeholders resolved.
    /// </summary>
    // ── Sidebar Tab Selector (Servers / Tools) ──────────────────────────

    /// <summary>
    /// Toggles the sidebar between the Servers tab and the Tools tab.
    /// Bound to Ctrl+Shift+T. Setting <c>IsChecked=false</c> on a grouped
    /// <see cref="RadioButton"/> leaves BOTH buttons unchecked — the sibling
    /// is not auto-selected. We therefore explicitly check the target button.
    /// </summary>
    private void ToggleSidebarTab()
    {
        if (SidebarTabTools.IsChecked == true)
        {
            SidebarTabSessions.IsChecked = true;
        }
        else
        {
            SidebarTabTools.IsChecked = true;
        }
    }

    private void OnEmptyExploreToolsClick(object sender, RoutedEventArgs e)
    {
        SidebarTabTools.IsChecked = true;
    }

    // ── Sidebar Tools tab ───────────────────────────────────────────────

    private bool _sidebarToolsPopulated;
    private System.Collections.ObjectModel.ObservableCollection<ViewModels.SidebarToolCategoryViewModel>? _sidebarToolsCategories;

    /// <summary>
    /// Builds the category/tool hierarchy via
    /// <see cref="ToolsTabPopulationService.BuildSidebarToolsData"/> and attaches
    /// it to the sidebar Tools <see cref="TreeView"/>. Lazy: only invoked the
    /// first time the user switches to the Tools tab, and re-run when external
    /// tools are discovered (the External category changes).
    /// </summary>
    private void BuildSidebarToolsData()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        _sidebarToolsCategories = _toolsTabPopulation.BuildSidebarToolsData(vm);
        SidebarToolsTreeView.ItemsSource = _sidebarToolsCategories;
        _sidebarToolsPopulated = true;

        UpdateSidebarToolsContextLabel();
    }

    /// <summary>
    /// Populates the sidebar Tools tab the first time the user switches to it.
    /// Wired from the <c>SidebarTabTools</c> <see cref="RadioButton"/> Checked event.
    /// </summary>
    private void OnSidebarTabToolsChecked(object sender, RoutedEventArgs e)
    {
        if (!_sidebarToolsPopulated)
        {
            BuildSidebarToolsData();
        }
        else
        {
            // Data already built — refresh context label in case the selected
            // server changed while the Tools tab was hidden.
            UpdateSidebarToolsContextLabel();
        }

        PersistSidebarTabChoice(isTools: true);
    }

    private void OnSidebarTabSessionsChecked(object sender, RoutedEventArgs e)
    {
        PersistSidebarTabChoice(isTools: false);
    }

    /// <summary>
    /// Persists the active sidebar tab (Servers or Tools) so the choice is
    /// restored on next launch. Reuses <see cref="Heimdall.Core.Configuration.AppSettings.ShowToolsPanel"/>.
    /// Guarded by <see cref="_sidebarTabRestored"/> to avoid overwriting the saved
    /// value during XAML initialization, when the default <c>IsChecked="True"</c>
    /// on <c>SidebarTabSessions</c> would otherwise fire before the Loaded handler
    /// has restored the persisted state.
    /// </summary>
    private async void PersistSidebarTabChoice(bool isTools)
    {
        if (!_sidebarTabRestored) return;
        if (DataContext is not MainViewModel vm) return;
        if (vm.CurrentSettings is null) return;
        if (vm.CurrentSettings.ShowToolsPanel == isTools) return;

        vm.CurrentSettings.ShowToolsPanel = isTools;
        await vm.ConfigManager.MergeSettingAsync(s => s.ShowToolsPanel = isTools);
    }

    private void UpdateSidebarToolsContextLabel()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var host = ToolsTabPopulationService.GetInheritedToolTargetHost(vm);
        var hasTarget = !string.IsNullOrEmpty(host);
        var text = hasTarget
            ? vm.Localize("ToolsNetworkContextWith").Replace("{0}", host)
            : vm.Localize("ToolsNetworkContextNone");

        Mw_SidebarToolsContextText.Text = text;
        Mw_SidebarToolsContextText.ToolTip = text;
    }

    private void OnSidebarToolsFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_sidebarToolsCategories is null)
        {
            return;
        }

        var filter = Mw_SidebarToolsFilter.Text;
        var anyVisibleTool = _toolsTabPopulation.FilterSidebarTools(_sidebarToolsCategories, filter);
        var hasFilter = !string.IsNullOrWhiteSpace(filter);

        if (DataContext is MainViewModel vm)
        {
            Mw_SidebarToolsNoResults.Text = vm.Localize("ToolsNoResults");
        }
        Mw_SidebarToolsNoResults.Visibility = hasFilter && !anyVisibleTool
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSidebarToolsSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ViewModels.SidebarToolItemViewModel item)
        {
            LaunchSidebarTool(item);
        }
    }

    private void OnSidebarToolsDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SidebarToolsTreeView.SelectedItem is ViewModels.SidebarToolItemViewModel item)
        {
            LaunchSidebarTool(item);
            e.Handled = true;
        }
    }

    private async void LaunchSidebarTool(ViewModels.SidebarToolItemViewModel item)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var descriptor = ToolRegistry.All.FirstOrDefault(
            d => string.Equals(d.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            return;
        }

        try
        {
            var context = ToolsTabPopulationService.CreateInheritedToolContext(descriptor, vm);

            // Match the full-page Tools tab: make sure the session panel is visible
            // before opening the tool tab.
            TabSessions.IsChecked = true;
            SwitchToTab("Sessions");

            await vm.OpenToolTabAsync(
                descriptor.Id,
                ToolsTabPopulationService.ResolveToolTabTitle(descriptor, context, vm),
                context);
            vm.TrackRecentTool(descriptor.Id);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Sidebar tool launch failed: {descriptor.Id}", ex);
            vm.StatusText = $"Tool launch failed: {descriptor.Id} — {ex.Message}";
        }
    }

    // ── Tools Tab (dedicated full-page browser) ──────────────────────

    private bool _toolsTabPopulated;

    private void PopulateToolsTab()
    {
        if (DataContext is not MainViewModel vm) return;

        // Header localization
        Mw_ToolsTabTitle.Text = vm.Localize("ToolsTabTitle");
        Mw_ToolsTabSearch.Tag = vm.Localize("ToolsTabSearchPlaceholder");
        Mw_ToolsTabCount.Text = vm.Localize("ToolsToolCount").Replace("{0}", ToolRegistry.All.Count.ToString());

        if (_toolsTabPopulated)
        {
            RefreshToolsTabSections(vm);
            UpdateToolLaunchContextLabels();
            return;
        }
        _toolsTabPopulated = true;
        RefreshToolsTabSections(vm);
        UpdateToolLaunchContextLabels();
    }

    private void RefreshToolsTabSections(MainViewModel vm)
    {
        var visibleCount = _toolsTabPopulation.RefreshToolsTabSections(
            ToolsTabContent,
            vm,
            Mw_ToolsTabSearch.Text,
            OnToolsTabCardClickInternal,
            OnToolPinClickInternal);
        Mw_ToolsTabCount.Text = vm.Localize("ToolsToolCount").Replace("{0}", visibleCount.ToString());
    }

    private void UpdateToolLaunchContextLabels()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var host = ToolsTabPopulationService.GetInheritedToolTargetHost(vm);
        var hasTarget = !string.IsNullOrEmpty(host);
        var text = hasTarget
            ? vm.Localize("ToolsNetworkContextWith").Replace("{0}", host)
            : vm.Localize("ToolsNetworkContextNone");
        var brushKey = hasTarget ? "AccentBrush" : "TextDisabledBrush";

        Mw_ToolsTabContextText.Text = text;
        Mw_ToolsTabContextText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        Mw_ToolsTabContextText.ToolTip = text;

        // Mirror the same context line in the sidebar Tools tab.
        UpdateSidebarToolsContextLabel();
    }

    private async void OnToolPinClickInternal(string toolId)
    {
        if (DataContext is not MainViewModel vm) return;
        await vm.ToggleFavoriteToolAsync(toolId);
        RefreshToolsTabSections(vm);
    }

    private async void OnToolsTabCardClickInternal(Core.Models.ToolDescriptor descriptor)
    {
        if (DataContext is not MainViewModel vm) return;

        try
        {
            var context = ToolsTabPopulationService.CreateInheritedToolContext(descriptor, vm);

            // Switch to Servers tab first so the session panel is visible
            TabSessions.IsChecked = true;
            SwitchToTab("Sessions");

            await vm.OpenToolTabAsync(
                descriptor.Id,
                ToolsTabPopulationService.ResolveToolTabTitle(descriptor, context, vm),
                context);
            vm.TrackRecentTool(descriptor.Id);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Tool tab card launch failed: {descriptor.Id}", ex);
        }
    }

    private void OnToolsTabSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        RefreshToolsTabSections(vm);
    }

    // ── Onboarding overlay ───────────────────────────────────────────

    private void ShowOnboardingOverlay(Heimdall.Core.Configuration.AppSettings settings)
    {
        var services = ((App)Application.Current).Services
            ?? throw new InvalidOperationException("Application service provider is not initialized.");

        _onboardingVm = services.GetRequiredService<OnboardingFlowViewModel>();
        _onboardingVm.Attach(settings);
        _onboardingVm.StepCompleted += OnOnboardingStepCompleted;
        _onboardingVm.Completed += OnOnboardingCompleted;
        OnboardingOverlay.DataContext = _onboardingVm;
        _onboardingVm.Start();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            () => OnboardingNextBtn.Focus());
    }

    private void OnOnboardingStepCompleted(object? sender, int stepIndex)
    {
        switch (stepIndex)
        {
            case 0: // Step 1 done → navigate to Sessions tab
                TabSessions.IsChecked = true;
                SwitchToTab("Sessions");
                break;
            case 1: // Step 2 done → navigate to Settings tab
                TabSettings.IsChecked = true;
                SwitchToTab("Settings");
                break;
            // Step 3 (index 2): sidebar switch happens in OnOnboardingCompleted
            // once persistence has succeeded.
        }
    }

    private void OnOnboardingCompleted(object? sender, EventArgs e)
    {
        // The RadioButton Checked event routes through OnSidebarTabToolsChecked
        // which calls PersistSidebarTabChoice(true) to save ShowToolsPanel.
        SidebarTabTools.IsChecked = true;

        if (_onboardingVm is not null)
        {
            _onboardingVm.StepCompleted -= OnOnboardingStepCompleted;
            _onboardingVm.Completed -= OnOnboardingCompleted;
            _onboardingVm = null;
        }
    }

    private async void OnToolsPanelItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Core.Models.ToolDescriptor descriptor
            || DataContext is not MainViewModel vm)
            return;

        try
        {
            var context = ToolsTabPopulationService.CreateInheritedToolContext(descriptor, vm);

            await vm.OpenToolTabAsync(
                descriptor.Id,
                ToolsTabPopulationService.ResolveToolTabTitle(descriptor, context, vm),
                context);

            vm.TrackRecentTool(descriptor.Id);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Tool panel launch failed: {descriptor.Id}", ex);
            vm.StatusText = $"Tool launch failed: {descriptor.Id} — {ex.Message}";
        }
    }

    private void OnExtToolBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executables (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel vm
            && vm.Settings.SelectedExternalTool is not null)
        {
            vm.Settings.SelectedExternalTool.ExecutablePath = dialog.FileName;
            vm.Settings.IsDirty = true;
        }
    }

    private void OnExtToolWorkDirBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();

        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel vm
            && vm.Settings.SelectedExternalTool is not null)
        {
            vm.Settings.SelectedExternalTool.WorkingDirectory = dialog.FolderName;
            vm.Settings.IsDirty = true;
        }
    }

    private void OnExtToolTestClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.Settings.SelectedExternalTool is null) return;

        var tool = vm.Settings.SelectedExternalTool;
        if (string.IsNullOrWhiteSpace(tool.ExecutablePath)) return;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tool.ExecutablePath,
                Arguments = tool.Arguments,
                UseShellExecute = true
            };
            if (!string.IsNullOrWhiteSpace(tool.WorkingDirectory))
                psi.WorkingDirectory = tool.WorkingDirectory;
            if (tool.RunAsAdministrator)
                psi.Verb = "runas";
            if (tool.RunHidden)
            {
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
            }

            System.Diagnostics.Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"External tool test failed: {ex.Message}");
            MessageBox.Show(
                string.Format(vm.Localize("ExternalToolLaunchError"), tool.Name, ex.Message),
                vm.Localize("AppName"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCredProvDbBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Database files (*.kdbx;*.db;*.gpg)|*.kdbx;*.db;*.gpg|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel vm)
        {
            vm.Settings.CredentialProviderDatabase = dialog.FileName;
            vm.Settings.IsDirty = true;
        }
    }

    private static readonly (string Label, string Command)[] CredProvPresets =
    [
        ("Custom", ""),
        ("KeePassXC", "keepassxc-cli show -s -a Password \"{Database}\" \"{Title}\""),
        ("Bitwarden CLI", "bw get password \"{Title}\""),
        ("1Password CLI", "op read \"op://{Title}/password\""),
        ("pass (GPG)", "pass show \"{Title}\""),
    ];

    private void OnCredProvPresetChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Mw_SettingsCredProvPreset.SelectedIndex < 1) return; // skip "Custom"
        if (DataContext is not MainViewModel vm) return;

        var (_, command) = CredProvPresets[Mw_SettingsCredProvPreset.SelectedIndex];
        vm.Settings.CredentialProviderCommand = command;
        vm.Settings.IsDirty = true;
    }

    private void UpdateExternalToolProviderStatus(MainViewModel vm)
    {
        var service = (System.Windows.Application.Current as App)?.Services?
            .GetService(typeof(Services.ExternalToolProviderService)) as Services.ExternalToolProviderService;
        var count = service?.DetectedTools.Count ?? 0;
        Mw_SettingsExtProvStatus.Text = count > 0
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                vm.Localize("ExtToolStatusDetected"), count)
            : vm.Localize("ExtToolStatusNone");
    }

    private void OnCmdLibSyncTokenChanged(object sender, RoutedEventArgs e)
    {
        var password = Mw_SettingsCmdLibSyncToken.Password;
        if (string.IsNullOrEmpty(password)) return;

        var app = System.Windows.Application.Current as App;
        var configManager = app?.Services?
            .GetService(typeof(Core.Configuration.ConfigManager)) as Core.Configuration.ConfigManager;
        if (configManager is null) return;

        var encrypted = Core.Security.DpapiProvider.Protect(password);
        _ = configManager.MergeSettingAsync(s => s.CmdLibGitSyncToken = encrypted);
        UpdateTokenStatus(true);
    }

    private void OnCmdLibSyncTokenClear(object sender, RoutedEventArgs e)
    {
        var app = System.Windows.Application.Current as App;
        var configManager = app?.Services?
            .GetService(typeof(Core.Configuration.ConfigManager)) as Core.Configuration.ConfigManager;
        if (configManager is null) return;

        _ = configManager.MergeSettingAsync(s => s.CmdLibGitSyncToken = null);
        Mw_SettingsCmdLibSyncToken.Password = "";
        UpdateTokenStatus(false);
    }

    private void UpdateTokenStatus(bool hasToken)
    {
        if (DataContext is not MainViewModel vm) return;
        Mw_SettingsCmdLibSyncTokenStatus.Text = hasToken
            ? vm.Localize("SettingsCmdLibSyncTokenSaved") : "";
        Mw_SettingsCmdLibSyncTokenClear.Visibility = hasToken
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnCmdLibSyncTestClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var app = System.Windows.Application.Current as App;
        var gitSync = app?.Services?
            .GetService(typeof(TwinShell.Core.Interfaces.IGitSyncService))
                as TwinShell.Core.Interfaces.IGitSyncService;
        if (gitSync is null) return;

        Mw_SettingsCmdLibSyncTestBtn.IsEnabled = false;
        Mw_SettingsCmdLibSyncTestBtn.Content = "...";

        try
        {
            var result = await gitSync.TestConnectionAsync();
            Mw_SettingsCmdLibSyncTestBtn.Content = result.Success
                ? vm.Localize("SettingsCmdLibSyncTestSuccess")
                : vm.Localize("SettingsCmdLibSyncTestFailed");
        }
        catch (Exception ex)
        {
            Mw_SettingsCmdLibSyncTestBtn.Content = vm.Localize("SettingsCmdLibSyncTestFailed");
            Core.Logging.FileLogger.Warn($"[GitSync] Test connection failed: {ex.Message}");
        }
        finally
        {
            Mw_SettingsCmdLibSyncTestBtn.IsEnabled = true;
        }
    }

    private async void OnRescanExternalToolsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var app = System.Windows.Application.Current as App;
        var providerService = app?.Services?
            .GetService(typeof(Services.ExternalToolProviderService)) as Services.ExternalToolProviderService;
        var toolRegistry = app?.Services?
            .GetService(typeof(Services.ToolRegistry)) as Services.ToolRegistry;
        var configManager = app?.Services?
            .GetService(typeof(Core.Configuration.ConfigManager)) as Core.Configuration.ConfigManager;

        if (providerService is null || toolRegistry is null || configManager is null) return;

        Mw_SettingsExtProvStatus.Text = vm.Localize("ExtToolStatusScanning");
        Mw_SettingsBtnRescan.IsEnabled = false;

        // Use the ViewModel's current (unsaved) path values for scanning.
        // Paths are only persisted when the user clicks Save, not on Rescan.
        var scanSettings = await configManager.LoadSettingsAsync();
        scanSettings.SysinternalsPath = string.IsNullOrWhiteSpace(vm.Settings.SysinternalsPath) ? null : vm.Settings.SysinternalsPath;
        scanSettings.NirSoftPath = string.IsNullOrWhiteSpace(vm.Settings.NirSoftPath) ? null : vm.Settings.NirSoftPath;
        scanSettings.NanaRunPath = string.IsNullOrWhiteSpace(vm.Settings.NanaRunPath) ? null : vm.Settings.NanaRunPath;

        await Task.Run(() =>
        {
            providerService.ScanAll(scanSettings);
            toolRegistry.RegisterExternalTools(providerService.DetectedTools);
        });

        Mw_SettingsBtnRescan.IsEnabled = true;
        UpdateExternalToolProviderStatus(vm);

        // Refresh tools tab to show newly detected tools
        _toolsTabPopulated = false;
        PopulateToolsTab();
    }

    private void OnSysinternalsPathBrowseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Sysinternals",
            ShowNewFolderButton = false
        };
        if (!string.IsNullOrEmpty(vm.Settings.SysinternalsPath)
            && System.IO.Directory.Exists(vm.Settings.SysinternalsPath))
            dlg.SelectedPath = vm.Settings.SysinternalsPath;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            vm.Settings.SysinternalsPath = dlg.SelectedPath;
            vm.Settings.IsDirty = true;
        }
    }

    private void OnNirSoftPathBrowseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "NirSoft",
            ShowNewFolderButton = false
        };
        if (!string.IsNullOrEmpty(vm.Settings.NirSoftPath)
            && System.IO.Directory.Exists(vm.Settings.NirSoftPath))
            dlg.SelectedPath = vm.Settings.NirSoftPath;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            vm.Settings.NirSoftPath = dlg.SelectedPath;
            vm.Settings.IsDirty = true;
        }
    }

    private void OnNanaRunPathBrowseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "NanaRun",
            ShowNewFolderButton = false
        };
        if (!string.IsNullOrEmpty(vm.Settings.NanaRunPath)
            && System.IO.Directory.Exists(vm.Settings.NanaRunPath))
            dlg.SelectedPath = vm.Settings.NanaRunPath;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            vm.Settings.NanaRunPath = dlg.SelectedPath;
            vm.Settings.IsDirty = true;
        }
    }

    private void OnBrowseEditorPathClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = vm.Localize("BrowseEditorPathTitle"),
            Filter = vm.Localize("BrowseEditorPathFilter")
        };
        if (!string.IsNullOrEmpty(vm.Settings.ExternalEditorPath))
        {
            var dir = System.IO.Path.GetDirectoryName(vm.Settings.ExternalEditorPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }
        if (dlg.ShowDialog(this) == true)
            vm.Settings.ExternalEditorPath = dlg.FileName;
    }

    private void OnBrowsePlinkPathClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = vm.Localize("BrowsePlinkTitle"),
            Filter = vm.Localize("BrowsePlinkFilter")
        };
        if (!string.IsNullOrEmpty(vm.Settings.PlinkPath))
        {
            var dir = System.IO.Path.GetDirectoryName(vm.Settings.PlinkPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }
        if (dlg.ShowDialog(this) == true)
            vm.Settings.PlinkPath = dlg.FileName;
    }

    private void OnBrowsePuttyPathClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = vm.Localize("BrowsePuttyTitle"),
            Filter = vm.Localize("BrowsePuttyFilter")
        };
        if (!string.IsNullOrEmpty(vm.Settings.PuttyPath))
        {
            var dir = System.IO.Path.GetDirectoryName(vm.Settings.PuttyPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }
        if (dlg.ShowDialog(this) == true)
            vm.Settings.PuttyPath = dlg.FileName;
    }

    private void OnBrowseX11PathClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = vm.Localize("BrowseX11PathTitle"),
            Filter = vm.Localize("BrowseX11PathFilter")
        };
        if (!string.IsNullOrEmpty(vm.Settings.X11ServerPath))
        {
            var dir = System.IO.Path.GetDirectoryName(vm.Settings.X11ServerPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }
        if (dlg.ShowDialog(this) == true)
            vm.Settings.X11ServerPath = dlg.FileName;
    }

    private void OnBrowseSessionLogDirClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = vm.Localize("BrowseSessionLogDirTitle"),
            ShowNewFolderButton = true
        };
        if (!string.IsNullOrEmpty(vm.Settings.SessionLogDirectory)
            && System.IO.Directory.Exists(vm.Settings.SessionLogDirectory))
        {
            dlg.SelectedPath = vm.Settings.SessionLogDirectory;
        }
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            vm.Settings.SessionLogDirectory = dlg.SelectedPath;
    }

    private static void LaunchExternalTool(
        MainViewModel vm,
        Core.Configuration.ExternalToolDefinition tool,
        ServerItemViewModel server)
    {
        try
        {
            var arguments = tool.ResolveArguments(
                server.RemoteServer,
                server.EffectivePort,
                server.Username,
                serverName: server.DisplayName,
                protocol: server.ConnectionType,
                keyFile: server.SshKeyPath,
                project: server.ProjectName,
                gateway: server.GatewayName);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tool.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(tool.WorkingDirectory))
            {
                psi.WorkingDirectory = tool.WorkingDirectory;
            }

            if (tool.RunAsAdministrator)
            {
                psi.Verb = "runas";
            }

            if (tool.RunHidden)
            {
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
            }

            System.Diagnostics.Process.Start(psi)?.Dispose();

            Core.Logging.FileLogger.Info(
                $"External tool launched: {tool.ExecutablePath} {arguments}");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"External tool launch failed: {tool.Name}", ex);
            MessageBox.Show(
                string.Format(vm.Localize("ExternalToolLaunchError"), tool.Name, ex.Message),
                vm.Localize("AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
        _tabDragItem = null;

        // Do not initiate drag when clicking the close button
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;

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
            var draggedSession = _tabDragItem;
            var data = new System.Windows.DataObject("SessionTab", draggedSession);
            var result = DragDrop.DoDragDrop(SessionTabControl, data, System.Windows.DragDropEffects.Move);
            _tabDragItem = null;
            ClearTabDropHighlight();
            ContentDropZone.Visibility = Visibility.Collapsed;

            // If the drop landed outside the TabControl (no target accepted it),
            // detach the tab to a floating window
            if (result == System.Windows.DragDropEffects.None && draggedSession is not null)
            {
                DetachSessionToFloatingWindow(draggedSession);
            }
        }
    }

    private System.Windows.Controls.TabItem? _lastTabDropHighlight;

    private void ClearTabDropHighlight()
    {
        if (_lastTabDropHighlight is not null)
        {
            _lastTabDropHighlight.BorderThickness = new Thickness(0);
            _lastTabDropHighlight.BorderBrush = null;
            _lastTabDropHighlight = null;
        }
    }

    private void OnTabDragOver(object sender, System.Windows.DragEventArgs e)
    {
        ClearTabDropHighlight();

        if (e.Data.GetDataPresent("SessionTab"))
        {
            e.Effects = System.Windows.DragDropEffects.Move;

            var targetTab = FindAncestor<System.Windows.Controls.TabItem>(e.OriginalSource as DependencyObject);
            if (targetTab is not null && targetTab.DataContext != _tabDragItem)
            {
                targetTab.BorderThickness = new Thickness(2, 0, 0, 0);
                targetTab.BorderBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
                _lastTabDropHighlight = targetTab;
                ContentDropZone.Visibility = Visibility.Collapsed;
            }
            else if (targetTab is null && _tabDragItem is not null)
            {
                // Dragging over the content area — show split drop zone
                // Allow merging into already-split sessions (N-pane support)
                if (DataContext is MainViewModel vm
                    && vm.Connection.ActiveSession is not null
                    && vm.Connection.ActiveSession != _tabDragItem)
                {
                    ContentDropZone.Visibility = Visibility.Visible;
                }
            }
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnTabDrop(object sender, System.Windows.DragEventArgs e)
    {
        ClearTabDropHighlight();
        ContentDropZone.Visibility = Visibility.Collapsed;

        if (DataContext is not MainViewModel vm) return;
        if (!e.Data.GetDataPresent("SessionTab")) return;

        var draggedItem = e.Data.GetData("SessionTab") as SessionTabViewModel;
        if (draggedItem is null) return;

        // Find the drop target tab
        var dropTarget = FindAncestor<System.Windows.Controls.TabItem>(e.OriginalSource as DependencyObject);
        var targetItem = dropTarget?.DataContext as SessionTabViewModel;

        if (targetItem is not null && targetItem != draggedItem)
        {
            // Drop on tab header → reorder
            var sessions = vm.Connection.ActiveSessions;
            int oldIndex = sessions.IndexOf(draggedItem);
            int newIndex = sessions.IndexOf(targetItem);

            if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
            {
                sessions.Move(oldIndex, newIndex);
            }
        }
        else if (targetItem is null)
        {
            // Drop on content area → merge with active session as split
            var activeSession = vm.Connection.ActiveSession;
            if (activeSession is not null && activeSession != draggedItem)
            {
                // Determine orientation based on drop position relative to content area
                var pos = e.GetPosition(SessionTabControl);
                var width = SessionTabControl.ActualWidth;
                var height = SessionTabControl.ActualHeight;

                // If aspect ratio favors horizontal proximity, split horizontal
                var relY = (height > 0) ? pos.Y / height : 0.5;
                var relX = (width > 0) ? pos.X / width : 0.5;
                var distFromHEdge = Math.Min(relY, 1 - relY);
                var distFromVEdge = Math.Min(relX, 1 - relX);
                var orientation = distFromHEdge < distFromVEdge
                    ? Heimdall.Core.Models.SplitOrientation.Horizontal
                    : Heimdall.Core.Models.SplitOrientation.Vertical;

                vm.MergeExistingSession(activeSession, draggedItem.ServerId, orientation);
                e.Handled = true;
                return; // Prevent detach-to-floating-window fallback
            }
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

            current = GetParentObject(current);
        }

        return null;
    }

    private static DependencyObject? GetParentObject(DependencyObject current)
    {
        if (current is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent;
        }

        if (current is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement);
        }

        if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }

        return LogicalTreeHelper.GetParent(current);
    }

    // ── Tab detachment ─────────────────────────────────────────────────

    /// <summary>
    /// Detaches a session tab from the main window into a standalone floating window.
    /// The host control is removed from the TabControl and re-parented to the new window.
    /// </summary>
    private void DetachSessionToFloatingWindow(SessionTabViewModel session)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!vm.Connection.ActiveSessions.Contains(session)) return;

        // Detach the host control from the tab (UIElement single-parent rule)
        var hostControl = session.HostControl;
        session.HostControl = null;

        // Remove the session from the main window's collection
        vm.Connection.ActiveSessions.Remove(session);
        if (vm.Connection.ActiveSession == session)
        {
            vm.Connection.ActiveSession = vm.Connection.ActiveSessions.LastOrDefault();
        }
        vm.Connection.HasActiveSessions = vm.Connection.ActiveSessions.Count > 0;

        // Re-assign the host control so the floating window can pick it up
        session.HostControl = hostControl;

        // Spawn the floating window
        var localizer = vm.GetLocalizer();
        var floatingWindow = new Views.FloatingSessionWindow(session, localizer)
        {
            Owner = null // Independent top-level window
        };
        floatingWindow.Show();

        Heimdall.Core.Logging.FileLogger.Info(
            string.Format(localizer["LogSessionDetached"], session.Title));
    }

    /// <summary>
    /// Detaches the secondary pane from a split session into its own floating window.
    /// Legacy entry point — delegates to <see cref="DetachPaneToFloatingWindow"/>.
    /// </summary>
    private void DetachSecondaryToFloatingWindow(SessionTabViewModel session)
    {
        if (!session.IsSplit) return;
        if (session.RootContent is not Heimdall.Core.Models.SplitContainerModel rootContainer) return;

        var secondaryPane = Heimdall.Core.Models.SplitTreeHelper.FirstLeaf(rootContainer.Second);
        if (secondaryPane is not null && secondaryPane.HostControl is not null)
        {
            DetachPaneToFloatingWindow(session, secondaryPane.PaneId);
        }
    }

    /// <summary>
    /// Detaches a specific pane from the split tree into a floating window.
    /// The pane is extracted to a new tab and then detached.
    /// </summary>
    private void DetachPaneToFloatingWindow(SessionTabViewModel session, string paneId)
    {
        if (DataContext is not MainViewModel vm) return;

        var pane = Heimdall.Core.Models.SplitTreeHelper.FindPane(session.RootContent, paneId);
        if (pane is null || pane.HostControl is null) return;

        // Capture pane metadata
        var hostControl = pane.HostControl;
        var serverId = pane.ServerId;
        var originalServerId = pane.OriginalServerId;
        var connType = pane.ConnectionType;
        var title = pane.Title;
        var status = pane.Status;
        var tunnelRoute = pane.TunnelRoute;
        var envColor = pane.EnvironmentColor;

        // Detach host control and remove pane from tree
        pane.HostControl = null;
        var newRoot = Heimdall.Core.Models.SplitTreeHelper.RemovePane(session.RootContent, paneId);
        session.RootContent = newRoot ?? new Heimdall.Core.Models.SessionPaneModel();

        // Create a new independent tab and detach it
        var newTab = vm.Connection.AddSession(serverId, title, connType);
        newTab.OriginalServerId = originalServerId;
        newTab.HostControl = hostControl;
        newTab.Status = !string.IsNullOrEmpty(status) ? status : "Connected";
        newTab.TunnelRoute = tunnelRoute;
        newTab.EnvironmentColor = envColor;

        DetachSessionToFloatingWindow(newTab);
    }

    // ── Session tab context menu handlers ──────────────────────────────

    private void OnSessionTabRightClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Resolve the tab that was actually right-clicked by walking the visual tree
        var clickSource = e.OriginalSource as DependencyObject;
        var clickedTabItem = FindAncestor<TabItem>(clickSource);
        var session = clickedTabItem?.DataContext as SessionTabViewModel
                      ?? vm.Connection.ActiveSession;
        if (session is null) return;

        // Select the right-clicked tab so it becomes active
        if (clickedTabItem is not null)
        {
            vm.Connection.ActiveSession = session;
        }

        // Views with their own context menus: SFTP, local file browser, and tool DataGrids.
        // Check the visual tree source to avoid overriding them.
        if (clickSource is not null)
        {
            // Notes owns a TreeView-specific context menu in its sidebar.
            // Skip the session menu so the tool can open its own commands.
            if (FindAncestor<Views.Tools.NotesToolView>(clickSource) is not null
                && FindAncestor<System.Windows.Controls.TreeView>(clickSource) is not null)
            {
                return;
            }

            // Check for any ancestor that has its own context menu
            if (FindAncestor<Views.EmbeddedSftpView>(clickSource) is not null
                || FindAncestor<Views.LocalFileBrowserView>(clickSource) is not null
                || FindAncestor<Views.Tools.HackerSimulatorView>(clickSource) is not null)
            {
                return;
            }

            // Tool views with DataGrid context menus (NetworkCartography, PortScanner, etc.)
            var dataGrid = FindAncestor<System.Windows.Controls.DataGrid>(clickSource);
            if (dataGrid is not null && session.ConnectionType?.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase) == true)
            {
                return; // Let the tool's own ContextMenuOpening handler take over
            }

            // Tool views with TreeView context menus (e.g. NotesToolView)
            var treeView = FindAncestor<System.Windows.Controls.TreeView>(clickSource);
            if (treeView is not null && session.ConnectionType?.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase) == true)
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

        var isToolTab = session.ConnectionType?.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase) == true;

        // Close tab — "Close" for tools, "Disconnect" for connections
        var closeItem = new System.Windows.Controls.MenuItem
        {
            Header = vm.Localize(isToolTab ? "SessionCloseTab" : "SessionDisconnect")
        };
        closeItem.Click += (_, _) => vm.Connection.CloseSessionCommand.Execute(session);
        menu.Items.Add(closeItem);

        // Connection-specific actions (not shown for tools)
        if (!isToolTab)
        {
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
                var lookupId = !string.IsNullOrEmpty(session.OriginalServerId)
                    ? session.OriginalServerId
                    : session.ServerId;

                if (!string.IsNullOrEmpty(lookupId) && vm.ServerList.ConnectCommand is not null)
                {
                    var serverVm = vm.ServerList.Servers.FirstOrDefault(
                        s => string.Equals(s.Id, lookupId, StringComparison.Ordinal));
                    if (serverVm is not null)
                    {
                        vm.ServerList.ConnectCommand.Execute(serverVm);
                    }
                }
            };
            menu.Items.Add(duplicateItem);
        }

        // Detach to floating window (works for both tools and connections)
        if (!session.IsSplit)
        {
            var detachItem = new System.Windows.Controls.MenuItem { Header = vm.Localize("SessionCtxDetach") };
            detachItem.Click += (_, _) => DetachSessionToFloatingWindow(session);
            menu.Items.Add(detachItem);
        }
        else
        {
            // Detach secondary pane to its own floating window (disabled while secondary is loading)
            var detachSecondaryItem = new System.Windows.Controls.MenuItem
            {
                Header = vm.Localize("SplitDetachSecondary"),
                IsEnabled = session.SecondaryHostControl is not null
            };
            detachSecondaryItem.Click += (_, _) => DetachSecondaryToFloatingWindow(session);
            menu.Items.Add(detachSecondaryItem);
        }

        // Transcript toggle for SSH sessions
        if (session.HostControl is Views.EmbeddedSshView sshView)
        {
            var isRecording = sshView.IsTranscriptActive;
            var transcriptItem = new System.Windows.Controls.MenuItem
            {
                Header = isRecording
                    ? vm.Localize("SessionStopTranscript")
                    : vm.Localize("SessionStartTranscript")
            };
            transcriptItem.Click += (_, _) =>
            {
                if (sshView.IsTranscriptActive)
                {
                    sshView.StopTranscript();
                    vm.StatusText = vm.Localize("SessionTranscriptStopped");
                }
                else
                {
                    var logDir = vm.CurrentSettings?.SessionLogDirectory ?? @"logs\sessions";
                    if (!System.IO.Path.IsPathRooted(logDir))
                    {
                        logDir = System.IO.Path.Combine(AppContext.BaseDirectory, logDir);
                    }

                    var invalidChars = System.IO.Path.GetInvalidFileNameChars();
                    var serverName = string.Concat(
                        session.Title.Select(c => Array.IndexOf(invalidChars, c) >= 0 ? '_' : c));
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var logFile = System.IO.Path.Combine(logDir, $"transcript_{serverName}_{timestamp}.log");

                    sshView.StartTranscript(logFile);
                    vm.StatusText = string.Format(vm.Localize("SessionTranscriptStarted"), logFile);
                }
            };
            menu.Items.Add(transcriptItem);

            // Macro recording toggle
            var isMacroRecording = sshView.IsRecordingMacro;
            var macroRecordItem = new System.Windows.Controls.MenuItem
            {
                Header = isMacroRecording
                    ? vm.Localize("MacroStopRecording")
                    : vm.Localize("MacroStartRecording")
            };
            macroRecordItem.Click += async (_, _) =>
            {
                if (sshView.IsRecordingMacro)
                {
                    var entries = sshView.StopRecording();
                    if (entries.Count > 0)
                    {
                        var name = await vm.DialogService.ShowInputAsync(
                            vm.Localize("MacroNameTitle"),
                            vm.Localize("MacroNamePrompt"));

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            var macro = new Heimdall.Core.Models.TerminalMacro
                            {
                                Name = name,
                                Entries = entries
                            };
                            await Services.MacroService.SaveMacroAsync(macro);
                            vm.StatusText = string.Format(vm.Localize("MacroRecordingStopped"), name);
                        }
                    }
                }
                else
                {
                    sshView.StartRecording();
                    vm.StatusText = vm.Localize("MacroRecordingStarted");
                }
            };
            menu.Items.Add(macroRecordItem);

            // Play macro submenu
            var playMenu = new System.Windows.Controls.MenuItem
            {
                Header = vm.Localize("MacroPlaySubmenu")
            };

            var macros = Services.MacroService.LoadMacros();
            if (macros.Count == 0)
            {
                var emptyItem = new System.Windows.Controls.MenuItem
                {
                    Header = vm.Localize("MacroNoMacros"),
                    IsEnabled = false
                };
                playMenu.Items.Add(emptyItem);
            }
            else
            {
                foreach (var macro in macros)
                {
                    var macroItem = new System.Windows.Controls.MenuItem { Header = macro.Name, Tag = macro };
                    macroItem.Click += async (s, _) =>
                    {
                        if (s is System.Windows.Controls.MenuItem { Tag: Heimdall.Core.Models.TerminalMacro m })
                        {
                            vm.StatusText = string.Format(vm.Localize("MacroPlaying"), m.Name);
                            try
                            {
                                await sshView.PlayMacro(m, CancellationToken.None);
                            }
                            catch (Exception ex)
                            {
                                Heimdall.Core.Logging.FileLogger.Warn(
                                    $"Macro playback failed: {ex.Message}");
                            }
                        }
                    };
                    playMenu.Items.Add(macroItem);
                }
            }
            menu.Items.Add(playMenu);
        }

        var closeAllItem = new System.Windows.Controls.MenuItem { Header = vm.Localize("SessionCloseSession") };
        closeAllItem.Click += (_, _) => vm.Connection.CloseSessionCommand.Execute(session);
        menu.Items.Add(closeAllItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        if (!session.IsSplit)
        {
            // "Split..." submenu with orientation sub-items (mirrors "Merge with..." pattern)
            var splitMenu = new System.Windows.Controls.MenuItem { Header = vm.Localize("SplitMenu") };

            var splitH = new System.Windows.Controls.MenuItem { Header = vm.Localize("OrientationHorizontal") };
            splitH.Click += (_, _) => RequestSplitSession(session, Heimdall.Core.Models.SplitOrientation.Horizontal);
            splitMenu.Items.Add(splitH);

            var splitV = new System.Windows.Controls.MenuItem { Header = vm.Localize("OrientationVertical") };
            splitV.Click += (_, _) => RequestSplitSession(session, Heimdall.Core.Models.SplitOrientation.Vertical);
            splitMenu.Items.Add(splitV);

            menu.Items.Add(splitMenu);

            // "Merge with..." submenu — nested per session with orientation sub-items
            var otherSessions = vm.Connection.ActiveSessions
                .Where(s => s != session
                    && s.HostControl is not null)
                .ToList();

            if (otherSessions.Count > 0)
            {
                var mergeMenu = new System.Windows.Controls.MenuItem { Header = vm.Localize("SplitMergeWith") };

                foreach (var other in otherSessions)
                {
                    var sourceTab = other;
                    var sessionMenu = new System.Windows.Controls.MenuItem { Header = sourceTab.Title };

                    // Use OriginalServerId as stable lookup key (ServerId may be empty during connection)
                    var mergeId = !string.IsNullOrEmpty(sourceTab.OriginalServerId)
                        ? sourceTab.OriginalServerId
                        : sourceTab.ServerId;

                    var mergeH = new System.Windows.Controls.MenuItem { Header = vm.Localize("OrientationHorizontal") };
                    mergeH.Click += (_, _) => vm.MergeExistingSession(
                        session, mergeId, Heimdall.Core.Models.SplitOrientation.Horizontal);
                    sessionMenu.Items.Add(mergeH);

                    var mergeV = new System.Windows.Controls.MenuItem { Header = vm.Localize("OrientationVertical") };
                    mergeV.Click += (_, _) => vm.MergeExistingSession(
                        session, mergeId, Heimdall.Core.Models.SplitOrientation.Vertical);
                    sessionMenu.Items.Add(mergeV);

                    mergeMenu.Items.Add(sessionMenu);
                }

                menu.Items.Add(mergeMenu);
            }
        }
        else
        {
            var unsplit = new System.Windows.Controls.MenuItem { Header = vm.Localize("SplitUnsplit") };
            unsplit.Click += (_, _) => UnsplitSession(session);
            menu.Items.Add(unsplit);

            var swapItem = new System.Windows.Controls.MenuItem { Header = vm.Localize("SplitSwapPanes") };
            swapItem.Click += async (_, _) => await vm.SwapSplitPanesAsync(session);
            menu.Items.Add(swapItem);

            var toggleItem = new System.Windows.Controls.MenuItem
            {
                Header = vm.Localize("SplitToggleOrientation"),
                InputGestureText = "Ctrl+Shift+O"
            };
            toggleItem.Click += (_, _) => vm.ToggleSplitOrientation(session);
            menu.Items.Add(toggleItem);

            var closeSecItem = new System.Windows.Controls.MenuItem { Header = vm.Localize("SplitCloseSecondary") };
            closeSecItem.Click += (_, _) => vm.CloseSecondaryPaneCommand.Execute(session);
            menu.Items.Add(closeSecItem);
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

        // Open the Command Palette in split mode — the palette provides fuzzy search
        // which scales to any number of servers (replaces the old ContextMenu approach
        // that became unusable with 100+ servers).
        vm.OpenSplitPalette(session, orientation);
        BeginFocusCommandPalette();
    }

    /// <summary>
    /// Legacy unsplit: detaches the root's second child to a new tab.
    /// </summary>
    private void UnsplitSession(SessionTabViewModel session)
    {
        if (!session.IsSplit) return;
        if (session.RootContent is not Heimdall.Core.Models.SplitContainerModel rootContainer) return;

        var secondaryPane = Heimdall.Core.Models.SplitTreeHelper.FirstLeaf(rootContainer.Second);
        if (secondaryPane is not null)
        {
            DetachPaneToTab(session, secondaryPane.PaneId);
        }
    }

    /// <summary>
    /// Detaches a specific pane from the split tree into its own independent tab.
    /// </summary>
    private void DetachPaneToTab(SessionTabViewModel session, string paneId)
    {
        if (DataContext is not MainViewModel vm) return;

        var pane = Heimdall.Core.Models.SplitTreeHelper.FindPane(session.RootContent, paneId);
        if (pane is null) return;

        // Capture metadata
        var hostControl = pane.HostControl;
        var serverId = pane.ServerId;
        var originalServerId = pane.OriginalServerId;
        var connType = pane.ConnectionType;
        var title = pane.Title;
        var status = pane.Status;
        var tunnelRoute = pane.TunnelRoute;
        var envColor = pane.EnvironmentColor;

        // Detach host control (UIElement single-parent rule)
        pane.HostControl = null;

        // Remove pane from tree
        var newRoot = Heimdall.Core.Models.SplitTreeHelper.RemovePane(session.RootContent, paneId);
        session.RootContent = newRoot ?? new Heimdall.Core.Models.SessionPaneModel();

        // If the pane was still connecting (no host control), clean up state and abort
        if (hostControl is null)
        {
            vm.CleanupOrphanedPane(serverId);
            Core.Logging.FileLogger.Info(
                $"Detach cancelled connecting pane '{title}'.");
            return;
        }

        // Restore as independent tab with original metadata
        if (!string.IsNullOrEmpty(serverId))
        {
            var displayTitle = !string.IsNullOrEmpty(title) ? title : serverId;
            var restoredTab = vm.Connection.AddSession(serverId, displayTitle, connType);
            restoredTab.OriginalServerId = originalServerId;
            restoredTab.HostControl = hostControl;
            restoredTab.Status = !string.IsNullOrEmpty(status) ? status : "Connected";
            restoredTab.TunnelRoute = tunnelRoute;
            restoredTab.EnvironmentColor = envColor;
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
        rdpView.UpdateAspectRatio(ratioName);
    }

    /// <summary>
    /// When switching to Tunnels/Scheduled/Settings while sessions are active,
    /// the Sessions Grid must stay visible (for sessions) but TreeView hides.
    /// When returning to Sessions, TreeView restores.
    /// </summary>
    private void UpdateTabVisibility(MainViewModel vm)
    {
        var isSessions = vm.SelectedTab == "Sessions";
        var hasSessions = vm.Connection.HasActiveSessions;

        Heimdall.Core.Logging.FileLogger.Info(
            $"UpdateTabVisibility: selectedTab={vm.SelectedTab}, isSessions={isSessions}, hasSessions={hasSessions}, sidebarHidden={_uiState.IsSidebarHidden}");

        // If not on Sessions but sessions active, show sessions full-width
        if (!isSessions && hasSessions)
        {
            // Hide TreeView temporarily
            if (!_uiState.IsSidebarHidden)
            {
                _uiState.SavedSidebarWidth = SessionTreeColumn.ActualWidth;
                SessionTreeColumn.MinWidth = 0;
                SessionTreeColumn.MaxWidth = 0;
                SessionTreeColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
            }
        }
        else if (isSessions && !_uiState.IsSidebarHidden)
        {
            // Restore TreeView
            SessionTreeColumn.MinWidth = WindowUIState.MinSidebarWidth;
            SessionTreeColumn.MaxWidth = WindowUIState.MaxSidebarWidth;
            SessionTreeColumn.Width = new GridLength(_uiState.SavedSidebarWidth > 0
                ? _uiState.SavedSidebarWidth
                : WindowUIState.DefaultSidebarWidth);
            SplitterColumn.Width = GridLength.Auto;
        }
    }

    // --- Command Palette event handlers ---

    private void OnCommandPaletteOpened(object sender, EventArgs e)
    {
        UpdatePaletteModeLabel();
        BeginFocusCommandPalette();
    }

    private void UpdatePaletteModeLabel()
    {
        if (DataContext is not MainViewModel vm)
        {
            PaletteModeLabel.Visibility = Visibility.Collapsed;
            return;
        }

        System.Windows.Automation.AutomationProperties.SetName(
            PaletteInput,
            string.IsNullOrWhiteSpace(vm.PalettePlaceholder)
                ? vm.Localize("PaletteSearchPlaceholder")
                : vm.PalettePlaceholder);

        if (vm.IsPaletteInSplitMode)
        {
            PaletteModeLabel.Text = vm.Localize("PaletteModeSplit");
            PaletteModeLabel.Visibility = Visibility.Visible;
        }
        else
        {
            PaletteModeLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void BeginFocusCommandPalette()
    {
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(FocusCommandPalette));
    }

    private void FocusCommandPalette()
    {
        if (!CommandPalettePopup.IsOpen || !PaletteInput.IsVisible)
        {
            return;
        }

        if (PresentationSource.FromVisual(PaletteInput) is HwndSource source &&
            source.Handle != IntPtr.Zero)
        {
            _ = SetForegroundWindow(source.Handle);
            _ = SetActiveWindow(source.Handle);
            _ = SetFocus(source.Handle);
        }

        Keyboard.Focus(PaletteInput);
        PaletteInput.Focus();
        PaletteInput.SelectAll();
    }

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
            {
                PaletteResultsList.SelectedIndex++;
                PaletteResultsList.ScrollIntoView(PaletteResultsList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (PaletteResultsList.SelectedIndex > 0)
            {
                PaletteResultsList.SelectedIndex--;
                PaletteResultsList.ScrollIntoView(PaletteResultsList.SelectedItem);
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// Dismiss the command palette when the user clicks anywhere in the main window
    /// (outside the Popup). The Popup is a separate HWND, so clicks on the main
    /// window surface never reach it — we intercept them here instead.
    /// </summary>
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel { IsCommandPaletteOpen: true } vm)
        {
            vm.CloseCommandPaletteCommand.Execute(null);
        }
    }

    /// <summary>
    /// Dismiss the command palette when the window loses focus (e.g., the user
    /// switches to another application). StaysOpen="True" is required for
    /// WindowsFormsHost airspace compatibility, so we handle deactivation manually.
    /// </summary>
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel { IsCommandPaletteOpen: true } vm)
        {
            vm.CloseCommandPaletteCommand.Execute(null);
        }
    }

    private void OnPaletteBorderClick(object sender, MouseButtonEventArgs e)
    {
        // No-op: the Popup is a separate HWND, so clicks inside it never
        // reach the Window's PreviewMouseDown handler. Kept for XAML binding.
    }

    /// <summary>
    /// Executes the double-clicked palette item (connects or opens tool).
    /// Captures split state synchronously BEFORE the async command runs,
    /// preventing a race condition where Popup deactivation clears
    /// <c>_splitPaletteSession</c> before <c>ConnectFromPaletteAsync</c> reads it.
    /// Single click only selects (highlights) via standard ListBox behavior.
    /// </summary>
    private void OnPaletteItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (FindAncestor<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject) is not
            { DataContext: ViewModels.ServerItemViewModel item })
        {
            return;
        }

        e.Handled = true;
        vm.ExecutePaletteSelection(item);
    }

    private void OnQuickConnectButtonClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OpenCommandPaletteCommand.Execute(null);
            BeginFocusCommandPalette();
        }
    }

    // ── Quick File Server (FileShareService bridge) ──────────────────

    private async void OnShareFolderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (_fileShareService.IsSharing)
        {
            await _fileShareService.StopAsync();
            return;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        await _fileShareService.StartAsync(dialog.SelectedPath, vm.CurrentSettings);
    }

    // ── IContextMenuCallbacks ─────────────────────────────────────────
    // Window-layer callbacks surfaced to ContextMenuFactory for actions that
    // require DataContext access, modal dialog ownership, or window-local state.

    /// <inheritdoc />
    void IContextMenuCallbacks.OpenNotesForServer(
        ServerItemViewModel server,
        NoteTemplateKind templateKind)
    {
        if (DataContext is not MainViewModel vm) return;

        var context = new Core.Models.ToolContext(
            TargetHost: server.RemoteServer,
            TargetPort: server.RemotePort > 0 ? server.RemotePort : null,
            Argument: $"template:{templateKind.ToString().ToLowerInvariant()}",
            DisplayName: server.DisplayName,
            Username: server.Username,
            ProjectName: server.ProjectName,
            GroupName: server.Group,
            ConnectionType: server.ConnectionType);

        _ = vm.OpenToolTabAsync("NOTES", server.DisplayName, context);
    }

    /// <inheritdoc />
    void IContextMenuCallbacks.LaunchExternalTool(
        ServerItemViewModel server,
        Core.Configuration.ExternalToolDefinition tool)
    {
        if (DataContext is not MainViewModel vm) return;
        LaunchExternalTool(vm, tool, server);
    }

    /// <inheritdoc />
    void IContextMenuCallbacks.LaunchDetectedTool(
        ServerItemViewModel server,
        Core.Configuration.ExternalToolInfo tool)
    {
        if (DataContext is not MainViewModel vm) return;
        LaunchDetectedTool(vm, tool, server);
    }

    /// <inheritdoc />
    void IContextMenuCallbacks.AddToolFromMenu(string? group)
    {
        if (DataContext is not MainViewModel vm) return;
        _ = ShowAddToolPickerAsync(vm, group);
    }

    private void OnFileShareSharingStarted(object? sender, FileShareStartedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        Mw_ShareFolderLabel.Text = vm.Localize("ToolsStopSharing");
        Mw_SharingStatus.Text = e.BaseUrl;
        Mw_SharingStatus.Visibility = Visibility.Visible;

        try { Clipboard.SetText(e.BaseUrl); }
        catch { /* clipboard may fail in some RDP sessions */ }

        var helpMessage = string.Format(vm.Localize("ToolsSharingHelp"),
            e.FolderName, e.BaseUrl, e.WgetCommand, e.CurlCommand, e.TftpCommand);

        vm.StatusText = string.Format(vm.Localize("ToolsSharingReady"), e.BaseUrl);

        MessageBox.Show(helpMessage,
            vm.Localize("ToolsSharingHelpTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnFileShareSharingStopped(object? sender, EventArgs e)
    {
        Mw_SharingStatus.Visibility = Visibility.Collapsed;
        Mw_SharingStatus.Text = string.Empty;

        if (DataContext is MainViewModel vm)
        {
            Mw_ShareFolderLabel.Text = vm.Localize("ToolsShareFolder");
            vm.StatusText = vm.Localize("ToolsSharingStopped");
        }
    }

    private void OnFileShareFileServed(object? sender, string fileName)
    {
        Dispatcher.Invoke(() =>
        {
            if (DataContext is not MainViewModel vm) return;
            vm.StatusText = string.Format(
                vm.Localize("ToolsSharingServed"),
                fileName,
                _fileShareService.BaseUrl);
        });
    }

    private void RestoreWindowBounds(Heimdall.Core.Configuration.AppSettings settings)
    {
        if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
        {
            // Validate that the saved position is within the virtual screen area
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            bool isOnScreen =
                settings.WindowLeft + settings.WindowWidth > virtualLeft &&
                settings.WindowLeft < virtualRight &&
                settings.WindowTop + settings.WindowHeight > virtualTop &&
                settings.WindowTop < virtualBottom;

            if (isOnScreen)
            {
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
                Width = settings.WindowWidth;
                Height = settings.WindowHeight;
            }

            if (settings.WindowMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
    }

    private void SaveWindowBounds(MainViewModel vm)
    {
        // Save Normal-state bounds even when maximized
        var bounds = WindowState == WindowState.Maximized
            ? RestoreBounds
            : new System.Windows.Rect(Left, Top, Width, Height);

        _ = vm.ConfigManager.MergeSettingAsync(s =>
        {
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
            s.WindowMaximized = WindowState == WindowState.Maximized;
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>async void</c> is intentional and required: <see cref="Window.OnClosing"/>
    /// is a synchronous override, but the unsaved-settings dialog must be awaited
    /// without blocking the dispatcher (<c>.GetAwaiter().GetResult()</c> deadlocks
    /// because the dialog posts back to the UI thread). The standard WPF pattern
    /// is to cancel the close, await the dialog, then re-invoke <see cref="Window.Close"/>.
    /// The <see cref="_closeConfirmed"/> flag prevents infinite recursion on the
    /// second pass through this handler.
    /// </remarks>
    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        if (DataContext is not MainViewModel vm) return;

        SaveWindowBounds(vm);

        if (vm.Settings.EnableSessionPersistence)
        {
            var sessions = vm.GetOpenSessions();
            _ = Services.WorkspaceService.SaveAsync(sessions);
        }

        if (vm.Settings.IsDirty && !_closeConfirmed)
        {
            // Cancel this close, show the dialog asynchronously, then re-close
            // (or stay open) based on the user's choice.
            e.Cancel = true;

            var title = vm.Localize("SettingsUnsavedWarningTitle");
            var message = vm.Localize("SettingsUnsavedWarning");
            var result = await vm.DialogService.ShowSaveDiscardCancelAsync(title, message);

            if (result is null)
            {
                // User cancelled — stay open.
                return;
            }

            if (result == true)
            {
                vm.Settings.SaveCommand.Execute(null);
            }
            // false = Discard — fall through and re-close.

            _closeConfirmed = true;
            try
            {
                Close();
            }
            catch
            {
                // If the second Close() somehow gets cancelled, reset the flag so
                // the user can try again instead of being stuck in a confirmed state.
                _closeConfirmed = false;
                throw;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeServiceThemeChanged;

        // Unsubscribe from long-lived service/ViewModel events. The DataContext
        // is the same MainViewModel instance the constructor wired up; without
        // these unsubscriptions the captured-`this` lambdas would keep this
        // window alive past Close().
        if (DataContext is MainViewModel vm)
        {
            if (_connectionPropertyChangedHandler is not null)
                vm.Connection.PropertyChanged -= _connectionPropertyChangedHandler;
            if (_serverListPropertyChangedHandler is not null)
                vm.ServerList.PropertyChanged -= _serverListPropertyChangedHandler;
            if (_externalToolsChangedHandler is not null)
                vm.ToolRegistry.ExternalToolsChanged -= _externalToolsChangedHandler;
            if (_localeChangedHandler is not null)
                vm.GetLocalizer().LocaleChanged -= _localeChangedHandler;
        }

        _fileShareService.SharingStarted -= OnFileShareSharingStarted;
        _fileShareService.SharingStopped -= OnFileShareSharingStopped;
        _fileShareService.FileServed -= OnFileShareFileServed;
        _ = _fileShareService.DisposeAsync();
        base.OnClosed(e);
    }

    /// <summary>
    /// Rebuilds any code-built tool panel UI after a runtime theme swap.
    /// Most brush properties on the generated elements use
    /// <see cref="FrameworkElement.SetResourceReference(DependencyProperty, object)"/>
    /// so they update automatically; this handler is the safety net for anything
    /// that can't be expressed as a dynamic resource reference (e.g. hover-state
    /// brush toggling driven from code-behind).
    /// </summary>
    private void OnThemeServiceThemeChanged(string themeName)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var toolsTabVisible = DataContext is MainViewModel vm
                && string.Equals(vm.SelectedTab, "Tools", StringComparison.Ordinal);
            _toolsTabPopulated = false;
            if (toolsTabVisible)
            {
                PopulateToolsTab();
            }
        });
    }
}
