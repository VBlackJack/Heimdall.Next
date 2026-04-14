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
public partial class MainWindow : Window, IContextMenuCallbacks, ISessionTabContextCallbacks
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

    private readonly TreeInteractionState _treeState = new();
    private readonly TabInteractionState _tabState = new();
    private readonly Services.ThemeService _themeService;
    private readonly ContextMenuFactory _contextMenuFactory;
    private readonly SessionTabContextMenuFactory _sessionTabContextMenuFactory;
    private readonly SessionSplitService _splitService;
    private readonly FileShareService _fileShareService;
    private readonly KeyboardShortcutService _keyboardShortcutService;
    private readonly WindowUIState _uiState = new();
    private object? _lastKeyEventSource;
    private readonly ToolsTabPopulationService _toolsTabPopulation;
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
        SessionTabContextMenuFactory sessionTabContextMenuFactory,
        SessionSplitService splitService,
        ToolsTabPopulationService toolsTabPopulation,
        FileShareService fileShareService,
        KeyboardShortcutService keyboardShortcutService)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
        DataContext = viewModel;
        _themeService = themeService;
        _contextMenuFactory = contextMenuFactory;
        _sessionTabContextMenuFactory = sessionTabContextMenuFactory;
        _splitService = splitService;
        _splitService.SplitPaletteRequested += OnSplitPaletteRequested;
        _toolsTabPopulation = toolsTabPopulation;
        _fileShareService = fileShareService;
        _keyboardShortcutService = keyboardShortcutService;
        RegisterKeyboardShortcuts();
        _fileShareService.SharingStarted += OnFileShareSharingStarted;
        _fileShareService.SharingStopped += OnFileShareSharingStopped;
        _fileShareService.FileServed += OnFileShareFileServed;
        _themeService.ThemeChanged += OnThemeServiceThemeChanged;
        viewModel.ToolsTab.SectionsInvalidated += OnToolsTabSectionsInvalidated;
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
                Dispatcher.Invoke(() => viewModel.ToolsTab.RefreshContextLabel());
            }
        };
        viewModel.ServerList.PropertyChanged += _serverListPropertyChangedHandler;

        // Refresh Tools tab and Settings status when background scan discovers external tools
        _externalToolsChangedHandler = () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                viewModel.ToolsTab.OnExternalToolsChanged();
                viewModel.Sidebar.OnExternalToolsChanged();
                UpdateExternalToolProviderStatus(viewModel);
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
            // The OneWay binding picks up the VM state change and drives both RadioButtons.
            viewModel.Sidebar.SetActiveTab(viewModel.Settings.ShowToolsPanel);
            viewModel.Sidebar.EnablePersistence();

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
    }

    /// <summary>
    /// Handles split requests from embedded view header buttons by delegating
    /// to <see cref="SessionSplitService.HandleEmbeddedSplitRequest"/>.
    /// </summary>
    private void OnEmbeddedSplitRequested(SessionTabViewModel session)
    {
        if (DataContext is not MainViewModel vm) return;
        _splitService.HandleEmbeddedSplitRequest(session, vm);
    }

    private void OnSplitPaletteRequested(object? sender, EventArgs e)
    {
        BeginFocusCommandPalette();
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
            if (DataContext is MainViewModel vm)
            {
                vm.ToolsTab.RefreshHeaderText();
                vm.ToolsTab.RefreshContextLabel();
                vm.ToolsTab.InvalidateSections();
            }
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
        if (vm.CommandPalette.IsOpen)
        {
            vm.CommandPalette.CloseCommand.Execute(null);
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
        _lastKeyEventSource = e.OriginalSource;
        if (_keyboardShortcutService.TryHandle(e))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Wires every Heimdall keyboard shortcut into the
    /// <see cref="KeyboardShortcutService"/>. Called once from the
    /// constructor after the service is resolved from DI.
    /// </summary>
    private void RegisterKeyboardShortcuts()
    {
        // ── Server list ──────────────────────────────────────────────
        // Ctrl+N: add server (always — never gated by terminal focus, matches legacy)
        _keyboardShortcutService.Register(Key.N, ModifierKeys.Control, () =>
        {
            if (GetMainVm() is { } vm)
                TryExecute(vm.ServerList.AddServerCommand);
        });

        // Delete: delete selected server (single-key, modifier-laxist, terminal-gated)
        _keyboardShortcutService.Register(Key.Delete, ModifierKeys.None, () =>
        {
            if (GetMainVm() is { } vm && vm.ServerList.SelectedServer is { } selected)
                TryExecute(vm.ServerList.DeleteServerCommand, selected);
        }, canExecute: () => !IsTerminalFocusedContext());

        // Ctrl+E: edit selected server (terminal-gated)
        _keyboardShortcutService.Register(Key.E, ModifierKeys.Control, () =>
        {
            if (GetMainVm() is { } vm && vm.ServerList.SelectedServer is { } selected)
                TryExecute(vm.ServerList.EditServerCommand, selected);
        }, canExecute: () => !IsTerminalFocusedContext());

        // ── Sidebar / filter / palette (terminal-gated) ──────────────
        // Ctrl+F: focus filter box
        _keyboardShortcutService.Register(Key.F, ModifierKeys.Control, () =>
        {
            Mw_FilterBox.Focus();
            Mw_FilterBox.SelectAll();
        }, canExecute: () => !IsTerminalFocusedContext());

        // Ctrl+B: toggle sidebar
        _keyboardShortcutService.Register(Key.B, ModifierKeys.Control,
            ToggleSidebar,
            canExecute: () => !IsTerminalFocusedContext());

        // Ctrl+K: open command palette
        _keyboardShortcutService.Register(Key.K, ModifierKeys.Control, () =>
        {
            if (GetMainVm() is { } vm)
            {
                vm.CommandPalette.OpenCommand.Execute(null);
                BeginFocusCommandPalette();
            }
        }, canExecute: () => !IsTerminalFocusedContext());

        // ── Ctrl+Shift combos (NOT terminal-gated) ───────────────────
        // Ctrl+Shift+S: screenshot active session
        _keyboardShortcutService.Register(Key.S, ModifierKeys.Control | ModifierKeys.Shift, () =>
        {
            if (GetMainVm() is { } vm)
                CaptureActiveSessionScreenshot(vm);
        });

        // Ctrl+Shift+N: launch network scanner
        _keyboardShortcutService.Register(Key.N, ModifierKeys.Control | ModifierKeys.Shift, () =>
        {
            if (GetMainVm() is { } vm)
                _ = LaunchNetworkScannerAsync(vm);
        });

        // Ctrl+Shift+T: toggle sidebar tab (Sessions ↔ Tools)
        _keyboardShortcutService.Register(Key.T, ModifierKeys.Control | ModifierKeys.Shift, () =>
        {
            if (GetMainVm() is { } vm)
                vm.Sidebar.ToggleTabCommand.Execute(null);
        });

        // Ctrl+Shift+O: toggle split orientation on the active split session
        _keyboardShortcutService.Register(Key.O, ModifierKeys.Control | ModifierKeys.Shift, () =>
        {
            if (GetMainVm() is { Connection.ActiveSession: { IsSplit: true } splitSession } vm)
                vm.ToggleSplitOrientation(splitSession);
        });

        // ── Session lifecycle ────────────────────────────────────────
        // Ctrl+W: close active session (terminal-gated)
        _keyboardShortcutService.Register(Key.W, ModifierKeys.Control, () =>
        {
            if (GetMainVm() is { Connection.ActiveSession: { } active } vm)
                TryExecute(vm.Connection.CloseSessionCommand, active);
        }, canExecute: () => !IsTerminalFocusedContext());

        // Ctrl+Tab: cycle to next session
        _keyboardShortcutService.Register(Key.Tab, ModifierKeys.Control, () =>
        {
            if (GetMainVm() is not { } vm) return;
            var sessions = vm.Connection.ActiveSessions;
            if (sessions.Count > 1 && vm.Connection.ActiveSession is not null)
            {
                var idx = sessions.IndexOf(vm.Connection.ActiveSession);
                vm.Connection.ActiveSession = sessions[(idx + 1) % sessions.Count];
            }
        });

        // Ctrl+Shift+Tab: cycle to previous session
        _keyboardShortcutService.Register(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, () =>
        {
            if (GetMainVm() is not { } vm) return;
            var sessions = vm.Connection.ActiveSessions;
            if (sessions.Count > 1 && vm.Connection.ActiveSession is not null)
            {
                var idx = sessions.IndexOf(vm.Connection.ActiveSession);
                vm.Connection.ActiveSession = sessions[(idx - 1 + sessions.Count) % sessions.Count];
            }
        });

        // ── Modifier-laxist function keys (legacy: fire regardless of modifiers) ──
        // F1: show keyboard shortcut help
        _keyboardShortcutService.Register(Key.F1, ModifierKeys.None, ShowKeyboardShortcutHelp);

        // F11: toggle fullscreen
        _keyboardShortcutService.Register(Key.F11, ModifierKeys.None, ToggleFullscreen);

        // Escape: exit fullscreen (only when fullscreen)
        _keyboardShortcutService.Register(Key.Escape, ModifierKeys.None,
            ToggleFullscreen,
            canExecute: () => _uiState.IsFullscreen);

        // ── TreeView context menu ────────────────────────────────────
        // Apps key: open context menu (terminal-gated, modifier-laxist)
        _keyboardShortcutService.Register(Key.Apps, ModifierKeys.None, () =>
        {
            if (GetMainVm() is { } vm)
                OpenTreeViewKeyboardContextMenu(vm);
        }, canExecute: () => !IsTerminalFocusedContext());

        // Shift+F10: open context menu (strict modifier, terminal-gated)
        _keyboardShortcutService.Register(Key.F10, ModifierKeys.Shift, () =>
        {
            if (GetMainVm() is { } vm)
                OpenTreeViewKeyboardContextMenu(vm);
        }, canExecute: () => !IsTerminalFocusedContext());
    }

    private MainViewModel? GetMainVm() => DataContext as MainViewModel;

    private bool IsTerminalFocusedContext()
        => KeyboardShortcutService.IsTerminalFocused(_lastKeyEventSource ?? new object(), IsEmbeddedContentFocused);

    private static void TryExecute(System.Windows.Input.ICommand? command, object? parameter = null)
    {
        if (command is not null && command.CanExecute(parameter))
        {
            command.Execute(parameter);
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

    // ── Sidebar Tab Selector (Servers / Tools) ──────────────────────────

    /// <summary>
    /// Switches the sidebar to the Tools tab. Wired from the empty-state
    /// "Explore tools" link button.
    /// </summary>
    private void OnEmptyExploreToolsClick(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.Sidebar.SetActiveTab(isTools: true);
    }

    // ── Sidebar Tools tab — view-layer glue (state lives in SidebarViewModel) ──

    private void OnSidebarTabToolsChecked(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.Sidebar.SetActiveTab(isTools: true);
    }

    private void OnSidebarTabSessionsChecked(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.Sidebar.SetActiveTab(isTools: false);
    }

    private async void OnSidebarToolsSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not ViewModels.SidebarToolItemViewModel item) return;
        if (DataContext is not MainViewModel vm) return;

        // Match the full-page Tools tab: make sure the session panel is visible
        // before opening the tool tab. Tab-strip navigation stays in the view.
        TabSessions.IsChecked = true;
        SwitchToTab("Sessions");

        await vm.Sidebar.LaunchToolAsync(item);
    }

    private async void OnSidebarToolsDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SidebarToolsTreeView.SelectedItem is not ViewModels.SidebarToolItemViewModel item) return;
        if (DataContext is not MainViewModel vm) return;

        e.Handled = true;
        TabSessions.IsChecked = true;
        SwitchToTab("Sessions");
        await vm.Sidebar.LaunchToolAsync(item);
    }

    // ── Tools Tab (dedicated full-page browser) ──────────────────────
    // State, search, filter and context labels live in ToolsTabViewModel.
    // The view keeps only: (a) the SectionsInvalidated handler that re-runs
    // the imperative ToolsTabPopulationService card rendering, (b) the two
    // card callbacks that are passed into that service, and (c) tab-strip
    // navigation on card click (which stays in the view layer).

    private void OnToolsTabSectionsInvalidated(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var visibleCount = _toolsTabPopulation.RefreshToolsTabSections(
            ToolsTabContent,
            vm,
            vm.ToolsTab.SearchText,
            OnToolsTabCardClickInternal,
            OnToolPinClickInternal);
        vm.ToolsTab.UpdateVisibleCount(visibleCount);
    }

    private void OnToolPinClickInternal(string toolId)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ToolsTab.ToggleFavoriteCommand.Execute(toolId);
        }
    }

    private void OnToolsTabCardClickInternal(Core.Models.ToolDescriptor descriptor)
    {
        if (DataContext is not MainViewModel vm) return;
        TabSessions.IsChecked = true;
        SwitchToTab("Sessions");
        vm.ToolsTab.LaunchToolCommand.Execute(descriptor);
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
        // Switch to Tools tab via the VM. The OneWay binding propagates the
        // change back to the RadioButton group and the VM persists the choice.
        if (DataContext is MainViewModel vm)
            vm.Sidebar.SetActiveTab(isTools: true);

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
        vm.ToolsTab.OnExternalToolsChanged();
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

        var menu = _sessionTabContextMenuFactory.CreateMenu(session, vm, this);
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
            string.IsNullOrWhiteSpace(vm.CommandPalette.Placeholder)
                ? vm.Localize("PaletteSearchPlaceholder")
                : vm.CommandPalette.Placeholder);

        if (vm.CommandPalette.IsInSplitMode)
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
            vm.CommandPalette.CloseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+Enter = connect in split pane
            var target = vm.CommandPalette.SelectedItem ?? vm.CommandPalette.Results.FirstOrDefault();
            _ = vm.CommandPalette.ConnectSplitFromPaletteCommand.ExecuteAsync(target);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _ = vm.CommandPalette.ConnectFromPaletteCommand.ExecuteAsync(
                vm.CommandPalette.SelectedItem ?? vm.CommandPalette.Results.FirstOrDefault());
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
        if (DataContext is MainViewModel vm && vm.CommandPalette.IsOpen)
        {
            vm.CommandPalette.CloseCommand.Execute(null);
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
        vm.CommandPalette.ExecuteSelection(item);
    }

    private void OnQuickConnectButtonClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.CommandPalette.OpenCommand.Execute(null);
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

    // ── ISessionTabContextCallbacks ───────────────────────────────────
    // OnAspectRatioClick and ToggleFullscreen stay in MainWindow (aspect
    // ratio touches the active RDP view, fullscreen touches named XAML
    // elements via MainWindow.WindowUI.cs). All split/merge/detach
    // operations delegate to SessionSplitService.

    /// <inheritdoc />
    void ISessionTabContextCallbacks.OnAspectRatioClick(object sender, RoutedEventArgs e)
        => OnAspectRatioClick(sender, e);

    /// <inheritdoc />
    void ISessionTabContextCallbacks.ToggleFullscreen()
        => ToggleFullscreen();

    /// <inheritdoc />
    void ISessionTabContextCallbacks.DetachSessionToFloatingWindow(SessionTabViewModel session)
    {
        if (DataContext is not MainViewModel vm) return;
        _splitService.DetachSessionToFloatingWindow(session, vm);
    }

    /// <inheritdoc />
    void ISessionTabContextCallbacks.DetachSecondaryToFloatingWindow(SessionTabViewModel session)
    {
        if (DataContext is not MainViewModel vm) return;
        _splitService.DetachSecondaryToFloatingWindow(session, vm);
    }

    /// <inheritdoc />
    void ISessionTabContextCallbacks.RequestSplitSession(
        SessionTabViewModel session,
        Heimdall.Core.Models.SplitOrientation orientation)
    {
        if (DataContext is not MainViewModel vm) return;
        _splitService.RequestSplitSession(session, orientation, vm);
    }

    /// <inheritdoc />
    void ISessionTabContextCallbacks.UnsplitSession(SessionTabViewModel session)
    {
        if (DataContext is not MainViewModel vm) return;
        _splitService.UnsplitSession(session, vm);
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
            vm.ToolsTab.SectionsInvalidated -= OnToolsTabSectionsInvalidated;
        }

        _fileShareService.SharingStarted -= OnFileShareSharingStarted;
        _fileShareService.SharingStopped -= OnFileShareSharingStopped;
        _fileShareService.FileServed -= OnFileShareFileServed;
        _splitService.SplitPaletteRequested -= OnSplitPaletteRequested;
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
            if (DataContext is not MainViewModel vm) return;
            if (!string.Equals(vm.SelectedTab, "Tools", StringComparison.Ordinal)) return;

            // Re-render the cards so brushes reflect the new theme.
            vm.ToolsTab.InvalidateSections();
        });
    }
}
