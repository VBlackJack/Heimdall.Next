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

namespace Heimdall.App;

/// <summary>
/// Main application window. All logic lives in <see cref="MainViewModel"/>.
/// Code-behind is limited to keyboard shortcut routing, TreeView interaction, and window lifecycle.
/// </summary>
public partial class MainWindow : Window
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
    private EphemeralFileServer? _fileServer;
    private readonly Services.ThemeService _themeService;

    public MainWindow(MainViewModel viewModel, Services.ThemeService themeService)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
        DataContext = viewModel;
        _themeService = themeService;
        _themeService.ThemeChanged += OnThemeServiceThemeChanged;
        ApplyLocalization();

        TabServers.Checked += OnServersTabChecked;
        TabTunnels.Checked += OnTunnelsTabChecked;
        TabScheduled.Checked += OnScheduledTabChecked;
        TabTools.Checked += OnToolsTabChecked;
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
        viewModel.ServerList.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ServerListViewModel.SelectedServer), StringComparison.Ordinal))
            {
                Dispatcher.Invoke(UpdateToolLaunchContextLabels);
            }
        };

        // Refresh Tools tab and Settings status when background scan discovers external tools
        viewModel.ToolRegistry.ExternalToolsChanged += () =>
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

        // Wire split button callback from embedded views
        viewModel.EmbeddedSessionManager.SplitRequestedCallback = OnEmbeddedSplitRequested;

        Loaded += async (_, _) =>
        {
            if (viewModel.LoadCommand.CanExecute(null))
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }

            RestoreWindowBounds(viewModel);
            PopulateAboutSection();

            // Restore the active sidebar tab (Servers / Tools) from persisted setting.
            // ShowToolsPanel is reused as a bool flag: true = Tools tab, false = Servers tab.
            if (viewModel.Settings.ShowToolsPanel)
            {
                SidebarTabTools.IsChecked = true;
            }

            // Show onboarding overlay on first launch
            if (viewModel.CurrentSettings is not null && !viewModel.CurrentSettings.OnboardingCompleted)
            {
                ShowOnboarding(viewModel);
            }
        };

        // Re-apply all localized strings when the user switches language at runtime
        viewModel.GetLocalizer().LocaleChanged += (_) =>
        {
            Dispatcher.Invoke(() => ApplyLocalization());
        };

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

    /// <summary>
    /// Sets all user-facing strings from locale resources.
    /// Called once after DataContext is assigned.
    /// </summary>
    private void ApplyLocalization()
    {
        if (DataContext is not MainViewModel vm) return;

        ApplyNavigationLocalization(vm);
        ApplyToolbarLocalization(vm);
        ApplyTunnelLocalization(vm);
        ApplyScheduledLocalization(vm);
        ApplySettingsLocalization(vm);
        ApplyAboutLocalization(vm);
        ApplyAccessibilityLocalization(vm);
        UpdateToolLaunchContextLabels();
    }

    private void ApplyNavigationLocalization(MainViewModel vm)
    {
        Mw_AppTitle.Text = vm.Localize("AppName");

        TabServers.Content = vm.Localize("NavTabServers");
        TabTunnels.Content = vm.Localize("NavTabTunnels");
        TabScheduled.Content = vm.Localize("NavTabScheduled");
        TabTools.Content = vm.Localize("NavTabTools");
        Mw_TabSettingsText.Text = vm.Localize("NavTabSettings");
        Mw_SettingsUnsavedDot.ToolTip = vm.Localize("SettingsUnsavedChanges");
        TabAbout.Content = vm.Localize("NavTabAbout");

        Mw_FilterBox.Tag = vm.Localize("SearchPlaceholder");
        Mw_FilterBox.TextChanged += OnFilterBoxTextChanged;

        FullscreenBar.ToolTip = vm.Localize("TooltipExitFullscreenEsc");

        Mw_StatusTunnelToggle.ToolTip = vm.Localize("TunnelPanelToggle");
        Mw_BroadcastLabel.Text = vm.Localize("BroadcastBadgeLabel");
        Mw_StatusBarServersLabel.Text = " " + vm.Localize("StatusBarServers") + " " + vm.Localize("StatusBarSeparator");
        Mw_StatusBarTunnelsLabel.Text = " " + vm.Localize("StatusBarTunnels");
        Mw_StatusBarShortcutHint.Text = vm.Localize("StatusBarShortcutHint");

        Mw_PaletteNoResults.Text = vm.Localize("QuickConnectNoResults");
        Mw_PaletteHints.Text = vm.Localize("QuickConnectHints");
        System.Windows.Automation.AutomationProperties.SetName(PaletteInput, vm.Localize("PaletteSearchPlaceholder"));
    }

    private void ApplyToolbarLocalization(MainViewModel vm)
    {
        ToggleSidebarButton.ToolTip = vm.Localize("TooltipHideSidebar");
        ShowSidebarButton.ToolTip = vm.Localize("TooltipShowSidebar");
        AddButton.ToolTip = vm.Localize("TooltipAddMenu");
        ExpandAllButton.ToolTip = vm.Localize("TooltipExpandAll");
        CollapseAllButton.ToolTip = vm.Localize("TooltipCollapseAll");

        Mw_AddMenuServer.Header = vm.Localize("AddMenuServer");
        Mw_AddMenuTool.Header = vm.Localize("AddMenuTool");
        Mw_AddMenuGateway.Header = vm.Localize("AddMenuGateway");
        Mw_AddMenuFolder.Header = vm.Localize("AddMenuFolder");

        Mw_ShareFolderLabel.Text = vm.Localize("ToolsShareFolder");
        Mw_QuickConnectLabel.Text = vm.Localize("QuickConnectShortcut");

        Mw_DetailGroupLabel.Text = vm.Localize("DetailLabelGroup");
        Mw_DetailEnvLabel.Text = vm.Localize("DetailLabelEnvironment");
        Mw_DetailProjectLabel.Text = vm.Localize("DetailLabelProject");
        Mw_DetailUsernameLabel.Text = vm.Localize("DetailLabelUsername");
        Mw_DetailGatewayLabel.Text = vm.Localize("DetailLabelGateway");
        Mw_DetailAuthLabel.Text = vm.Localize("DetailLabelAuth");
        Mw_DetailTagsLabel.Text = vm.Localize("DetailLabelTags");
        Mw_DetailFavoriteLabel.Text = vm.Localize("DetailLabelFavorite");
        Mw_DetailConnectBtn.Content = vm.Localize("DetailBtnConnect");
        Mw_DetailEditBtn.Content = vm.Localize("BtnEdit");
        Mw_DetailEditBtn.ToolTip = vm.Localize("TooltipEdit");
        Mw_DetailDeleteBtn.Content = vm.Localize("BtnDelete");
        Mw_DetailDeleteBtn.ToolTip = vm.Localize("TooltipDelete");
        QuickConnectButton.ToolTip = vm.Localize("QuickConnectShortcut");

        Mw_EmptyStateTitle.Text = vm.Localize("EmptyStateTitle");
        Mw_EmptyStateSubtitle.Text = vm.Localize("EmptyStateSubtitle");
        Mw_EmptyBtnAddServer.Content = vm.Localize("EmptyStateBtnAddServer");
        Mw_EmptyBtnImport.Content = vm.Localize("EmptyStateBtnImport");
        Mw_EmptyBtnImport.ToolTip = vm.Localize("TooltipImport");
        Mw_EmptyBtnExploreTools.Content = vm.Localize("EmptyStateBtnExploreTools");
        Mw_EmptySelectServer.Text = vm.Localize("EmptyStateSelectServer");
        Mw_EmptyQuickConnectHint.Text = vm.Localize("HintQuickConnect");
        Mw_EmptyStateShortcutHints.Text = vm.Localize("EmptyStateShortcutHints");
        Mw_DetailActionHints.Text = vm.Localize("DetailActionHints");
    }

    private void ApplyTunnelLocalization(MainViewModel vm)
    {
        Mw_TunnelPanelCloseAllBtn.Content = vm.Localize("TunnelsBtnCloseAll");
        Mw_TunnelPanelEmpty.Text = vm.Localize("TunnelsEmptyState");

        Mw_TunnelPanelGrid.Tag = vm.Localize("TooltipCloseTunnel");

        var tunnelHeader = vm.Localize("TunnelPanelHeader");
        var placeholderIdx = tunnelHeader.IndexOf("{0}", StringComparison.Ordinal);
        if (placeholderIdx >= 0)
        {
            Mw_TunnelPanelHeaderPrefix.Text = tunnelHeader[..placeholderIdx];
            Mw_TunnelPanelHeaderSuffix.Text = tunnelHeader[(placeholderIdx + 3)..];
        }
        else
        {
            Mw_TunnelPanelHeaderPrefix.Text = tunnelHeader;
        }

        Mw_TpColGateway.Header = vm.Localize("TunnelsColGateway");
        Mw_TpColLocal.Header = vm.Localize("TunnelPanelColLocal");
        Mw_TpColRemote.Header = vm.Localize("TunnelPanelColRemote");
        Mw_TpColPort.Header = vm.Localize("TunnelPanelColPort");

        Mw_TpCtxClose.Header = vm.Localize("TunnelCtxClose");
        Mw_TpCtxCopyPort.Header = vm.Localize("TunnelCtxCopyPort");
        Mw_TpCtxCloseAll.Header = vm.Localize("TunnelCtxCloseAll");

        Mw_TunnelsTitle.Text = vm.Localize("TunnelsSectionTitle");
        Mw_TunnelsCloseSelectedBtn.Content = vm.Localize("TunnelsBtnCloseSelected");
        Mw_TunnelsCloseAllBtn.Content = vm.Localize("TunnelsBtnCloseAll");
        Mw_TunnelsEmpty.Text = vm.Localize("TunnelsEmptyState");

        Mw_TunnelsColGateway.Header = vm.Localize("TunnelsColGateway");
        Mw_TunnelsColLocalPort.Header = vm.Localize("TunnelsColLocalPort");
        Mw_TunnelsColRemoteHost.Header = vm.Localize("TunnelsColRemoteHost");
        Mw_TunnelsColRemotePort.Header = vm.Localize("TunnelsColRemotePort");
        Mw_TunnelsColStarted.Header = vm.Localize("TunnelsColStarted");
        Mw_TunnelsManageGatewaysLink.Content = vm.Localize("TunnelsManageGatewaysLink");
    }

    private void ApplyScheduledLocalization(MainViewModel vm)
    {
        Mw_ScheduledTitle.Text = vm.Localize("ScheduledSectionTitle");
        Mw_ScheduledAddBtn.Content = vm.Localize("ScheduledBtnAdd");
        Mw_ScheduledEditBtn.Content = vm.Localize("ScheduledBtnEdit");
        Mw_ScheduledDeleteBtn.Content = vm.Localize("ScheduledBtnDelete");
        Mw_ScheduledEmpty.Text = vm.Localize("ScheduledEmptyState");
        Mw_ScheduledCreateBtn.Content = vm.Localize("ScheduledEmptyAddButton");

        Mw_ScheduledColEnabled.Header = vm.Localize("ScheduledColEnabled");
        Mw_ScheduledColServer.Header = vm.Localize("ScheduledColServer");
        Mw_ScheduledColType.Header = vm.Localize("ScheduledColType");
        Mw_ScheduledColSchedule.Header = vm.Localize("ScheduledColSchedule");
        Mw_ScheduledColLastRun.Header = vm.Localize("ScheduledColLastRun");
        Mw_ScheduledColNextRun.Header = vm.Localize("ScheduledColNextRun");
    }

    private void ApplySettingsLocalization(MainViewModel vm)
    {
        Mw_SettingsTabGeneral.Header = vm.Localize("SettingsTabGeneral");
        Mw_SettingsTabTerminal.Header = vm.Localize("SettingsTabTerminal");
        Mw_SettingsTabSsh.Header = vm.Localize("SettingsTabSshSftp");
        Mw_SettingsTabRdp.Header = vm.Localize("SettingsTabRdp");
        Mw_SettingsTabSecurity.Header = vm.Localize("SettingsTabSecurity");
        Mw_SettingsTabAdvanced.Header = vm.Localize("SettingsTabAdvanced");
        Mw_SettingsSearchBox.Tag = vm.Localize("SettingsSearchPlaceholder");

        Mw_SettingsAppearanceTitle.Text = vm.Localize("SettingsSectionAppearance");
        Mw_SettingsLanguageLabel.Text = vm.Localize("SettingsLabelLanguage");
        Mw_SettingsThemeLabel.Text = vm.Localize("SettingsLabelTheme");
        Mw_ThemeDraculaPro.Content = vm.Localize("ThemeDraculaPro");
        Mw_ThemeAlucard.Content = vm.Localize("ThemeAlucard");
        Mw_ThemeBlade.Content = vm.Localize("ThemeBlade");
        Mw_ThemeBuffy.Content = vm.Localize("ThemeBuffy");
        Mw_ThemeLincoln.Content = vm.Localize("ThemeLincoln");
        Mw_ThemeMorbius.Content = vm.Localize("ThemeMorbius");
        Mw_ThemeVanHelsing.Content = vm.Localize("ThemeVanHelsing");
        System.Windows.Automation.AutomationProperties.SetName(Mw_ThemeDraculaPro, vm.Localize("ThemeDraculaPro"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ThemeAlucard, vm.Localize("ThemeAlucard"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ThemeBlade, vm.Localize("ThemeBlade"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ThemeBuffy, vm.Localize("ThemeBuffy"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ThemeLincoln, vm.Localize("ThemeLincoln"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ThemeMorbius, vm.Localize("ThemeMorbius"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ThemeVanHelsing, vm.Localize("ThemeVanHelsing"));
        Mw_SettingsMaxSessionsLabel.Text = vm.Localize("SettingsLabelMaxEmbeddedSessions");
        Mw_SettingsPreventSleep.Content = vm.Localize("SettingsLabelPreventSleep");
        Mw_SettingsEnableSessionPersistence.Content = vm.Localize("SettingsWorkspaceRestore");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsEnableSessionPersistence, vm.Localize("SettingsWorkspaceRestore"));
        Mw_SettingsEditorPathLabel.Text = vm.Localize("SettingsLabelEditorPath");

        Mw_SettingsTerminalTitle.Text = vm.Localize("SettingsSectionTerminal");
        Mw_SettingsTerminalFontLabel.Text = vm.Localize("SettingsLabelTerminalFont");
        Mw_SettingsTerminalFontSizeLabel.Text = vm.Localize("SettingsLabelTerminalFontSize");
        Mw_SettingsTerminalColorSchemeLabel.Text = vm.Localize("SettingsLabelTerminalColorScheme");
        Mw_SchemeDefault.Content = vm.Localize("TerminalSchemeDefault");
        Mw_SchemeDracula.Content = vm.Localize("TerminalSchemeDracula");
        Mw_SchemeSolarized.Content = vm.Localize("TerminalSchemeSolarizedDark");
        Mw_SchemeMonokai.Content = vm.Localize("TerminalSchemeMonokai");
        Mw_SchemeNord.Content = vm.Localize("TerminalSchemeNord");
        Mw_SettingsPsPolicyLabel.Text = vm.Localize("SettingsPsExecutionPolicy");
        Mw_PsDefault.Content = vm.Localize("PsPolicyDefault");
        Mw_PsBypass.Content = vm.Localize("PsPolicyBypass");
        Mw_PsRemoteSigned.Content = vm.Localize("PsPolicyRemoteSigned");
        Mw_PsUnrestricted.Content = vm.Localize("PsPolicyUnrestricted");
        Mw_PsAllSigned.Content = vm.Localize("PsPolicyAllSigned");
        Mw_SettingsPsPolicyHint.Text = vm.Localize("SettingsPsExecutionPolicyHint");

        Mw_SettingsSshTitle.Text = vm.Localize("SettingsSectionSshDefaults");
        Mw_SettingsPlinkPathLabel.Text = vm.Localize("SettingsLabelPlinkPath");
        Mw_SettingsPlinkPathHint.Text = vm.Localize("SettingsPlinkPathHint");
        Mw_SettingsPuttyPathLabel.Text = vm.Localize("SettingsLabelPuttyPath");
        Mw_SettingsPuttyPathHint.Text = vm.Localize("SettingsPuttyPathHint");
        Mw_SettingsSshModeLabel.Text = vm.Localize("SettingsLabelSshMode");
        Mw_SshModeEmbedded.Content = vm.Localize("SettingsSshModeEmbedded");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SshModeEmbedded, vm.Localize("SettingsSshModeEmbedded"));
        Mw_SshModeExternal.Content = vm.Localize("SettingsSshModeExternal");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SshModeExternal, vm.Localize("SettingsSshModeExternal"));
        Mw_SettingsSshModeHint.Text = vm.Localize("SettingsSshModeHint");
        Mw_SettingsAntiIdleLabel.Text = vm.Localize("SettingsLabelAntiIdleInterval");
        Mw_SettingsSshTmoutLabel.Text = vm.Localize("SettingsLabelSshTmoutReset");
        Mw_SettingsAutoOpenSftp.Content = vm.Localize("SettingsLabelAutoOpenSftp");
        Mw_SettingsX11PathLabel.Text = vm.Localize("SettingsLabelX11ServerPath");
        Mw_SettingsX11AutoStart.Content = vm.Localize("SettingsLabelX11AutoStart");
        Mw_SettingsGatewaysTitle.Text = vm.Localize("GatewaysSectionTitle");
        Mw_SettingsGatewaysAddBtn.Content = vm.Localize("GatewaysBtnAdd");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsGatewaysAddBtn, vm.Localize("GatewaysBtnAdd"));
        Mw_SettingsGatewaysEditBtn.Content = vm.Localize("GatewaysBtnEdit");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsGatewaysEditBtn, vm.Localize("GatewaysBtnEdit"));
        Mw_SettingsGatewaysDeleteBtn.Content = vm.Localize("GatewaysBtnDelete");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsGatewaysDeleteBtn, vm.Localize("GatewaysBtnDelete"));

        Mw_ApplySshModeAll.Content = vm.Localize("SettingsApplyModeToAll");
        Mw_ApplySshModeAll.ToolTip = vm.Localize("TooltipApplyModeToAll");
        System.Windows.Automation.AutomationProperties.SetName(Mw_ApplySshModeAll, vm.Localize("SettingsApplyModeToAll"));

        Mw_SettingsRdpDefaultsTitle.Text = vm.Localize("SettingsSectionRdpDefaults");
        Mw_SettingsRdpModeLabel.Text = vm.Localize("SettingsLabelRdpMode");
        Mw_RdpModeEmbedded.Content = vm.Localize("SettingsRdpModeEmbedded");
        System.Windows.Automation.AutomationProperties.SetName(Mw_RdpModeEmbedded, vm.Localize("SettingsRdpModeEmbedded"));
        Mw_RdpModeExternal.Content = vm.Localize("SettingsRdpModeExternal");
        System.Windows.Automation.AutomationProperties.SetName(Mw_RdpModeExternal, vm.Localize("SettingsRdpModeExternal"));
        Mw_SettingsRdpModeHint.Text = vm.Localize("SettingsRdpModeHint");
        Mw_ApplyRdpModeAll.Content = vm.Localize("SettingsApplyModeToAll");
        Mw_ApplyRdpModeAll.ToolTip = vm.Localize("TooltipApplyModeToAll");
        System.Windows.Automation.AutomationProperties.SetName(Mw_ApplyRdpModeAll, vm.Localize("SettingsApplyModeToAll"));
        Mw_SettingsRdpWidthLabel.Text = vm.Localize("SettingsLabelRdpWidth");
        Mw_SettingsRdpHeightLabel.Text = vm.Localize("SettingsLabelRdpHeight");
        Mw_SettingsRdpColorDepthLabel.Text = vm.Localize("SettingsLabelRdpColorDepth");
        Mw_ColorDepth16.Content = vm.Localize("SettingsColorDepth16");
        System.Windows.Automation.AutomationProperties.SetName(Mw_ColorDepth16, vm.Localize("SettingsColorDepth16"));
        Mw_ColorDepth24.Content = vm.Localize("SettingsColorDepth24");
        System.Windows.Automation.AutomationProperties.SetName(Mw_ColorDepth24, vm.Localize("SettingsColorDepth24"));
        Mw_ColorDepth32.Content = vm.Localize("SettingsColorDepth32");
        System.Windows.Automation.AutomationProperties.SetName(Mw_ColorDepth32, vm.Localize("SettingsColorDepth32"));
        Mw_SettingsRdpAudioLabel.Text = vm.Localize("SettingsLabelRdpAudio");
        Mw_SettingsRdpAudioDisabled.Content = vm.Localize("ServerDialogAudioDisabled");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsRdpAudioDisabled, vm.Localize("ServerDialogAudioDisabled"));
        Mw_SettingsRdpAudioLocal.Content = vm.Localize("ServerDialogAudioLocal");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsRdpAudioLocal, vm.Localize("ServerDialogAudioLocal"));
        Mw_SettingsRdpAudioRemote.Content = vm.Localize("ServerDialogAudioRemote");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsRdpAudioRemote, vm.Localize("ServerDialogAudioRemote"));
        Mw_SettingsRdpNla.Content = vm.Localize("SettingsLabelRdpNla");
        Mw_SettingsRdpDynRes.Content = vm.Localize("SettingsLabelRdpDynamicRes");
        Mw_SettingsRdpMultiMon.Content = vm.Localize("SettingsLabelRdpMultiMonitor");
        Mw_SettingsRdpAutoReconnect.Content = vm.Localize("SettingsLabelRdpAutoReconnect");
        Mw_SettingsRdpBitmapCache.Content = vm.Localize("SettingsLabelRdpBitmapCache");
        Mw_SettingsRdpCompression.Content = vm.Localize("SettingsLabelRdpCompression");
        Mw_SettingsRdpRedirTitle.Text = vm.Localize("SettingsLabelRdpRedirection");
        Mw_SettingsRdpClipboard.Content = vm.Localize("ServerDialogRedirectClipboard");
        Mw_SettingsRdpDrives.Content = vm.Localize("ServerDialogRedirectDrives");
        Mw_SettingsRdpPrinters.Content = vm.Localize("ServerDialogRedirectPrinters");

        Mw_SettingsCredProviderTitle.Text = vm.Localize("SettingsSectionCredentialProvider");
        Mw_SettingsCredProviderEnabled.Content = vm.Localize("SettingsLabelCredProviderEnabled");
        Mw_SettingsCredProvPresetLabel.Text = vm.Localize("CredProvPresetLabel");
        PopulateCredProvPresets(vm);
        Mw_SettingsCredProviderCmdLabel.Text = vm.Localize("SettingsLabelCredProviderCommand");
        Mw_SettingsCredProvCmd.Tag = vm.Localize("SettingsCredProvCmdPlaceholder");
        Mw_SettingsCredProvCmdHint.Text = vm.Localize("CredProvCmdHint");
        Mw_SettingsCredProviderDbLabel.Text = vm.Localize("SettingsLabelCredProviderDatabase");
        Mw_SettingsCredProvTestBtn.Content = vm.Localize("CredProvBtnTest");
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsCredProvTestBtn, vm.Localize("CredProvBtnTest"));
        Mw_SettingsCredGuard.Content = vm.Localize("SettingsLabelCredentialGuard");

        Mw_SettingsSessionLoggingTitle.Text = vm.Localize("SettingsSectionSessionLogging");
        Mw_SettingsEnableLogging.Content = vm.Localize("SettingsLabelEnableLogging");
        Mw_SettingsSessionLoggingEnabled.Content = vm.Localize("SettingsLabelSessionLoggingEnabled");
        Mw_SettingsSessionLogDirLabel.Text = vm.Localize("SettingsLabelSessionLogDirectory");
        Mw_SettingsTimeoutsTitle.Text = vm.Localize("SettingsSectionTimeouts");
        Mw_SettingsTunnelDelayLabel.Text = vm.Localize("SettingsLabelTunnelDelay");
        Mw_SettingsRdpTimeoutLabel.Text = vm.Localize("SettingsLabelRdpTimeout");
        Mw_SettingsExtToolTimeoutLabel.Text = vm.Localize("SettingsLabelExtToolTimeout");
        // Command Library Git Sync
        Mw_SettingsCmdLibSyncTitle.Text = vm.Localize("SettingsCmdLibSyncTitle");
        Mw_SettingsCmdLibSyncEnable.Content = vm.Localize("SettingsCmdLibSyncEnable");
        Mw_SettingsCmdLibSyncLblUrl.Text = vm.Localize("SettingsCmdLibSyncUrl");
        Mw_SettingsCmdLibSyncLblBranch.Text = vm.Localize("SettingsCmdLibSyncBranch");
        Mw_SettingsCmdLibSyncLblToken.Text = vm.Localize("SettingsCmdLibSyncToken");
        Mw_SettingsCmdLibSyncLblAuthor.Text = vm.Localize("SettingsCmdLibSyncAuthor");
        Mw_SettingsCmdLibSyncOnStartup.Content = vm.Localize("SettingsCmdLibSyncOnStartup");
        Mw_SettingsCmdLibSyncAutoPush.Content = vm.Localize("SettingsCmdLibSyncAutoPush");
        Mw_SettingsCmdLibSyncTestBtn.Content = vm.Localize("SettingsCmdLibSyncTestConnection");
        Mw_SettingsCmdLibSyncTokenClear.ToolTip = vm.Localize("SettingsCmdLibSyncTokenClear");
        // Check if a token is already stored (asynchronously loaded from settings)
        _ = Task.Run(async () =>
        {
            var cfgMgr = (System.Windows.Application.Current as App)?.Services?
                .GetService(typeof(Core.Configuration.ConfigManager)) as Core.Configuration.ConfigManager;
            if (cfgMgr is null) return;
            var s = await cfgMgr.LoadSettingsAsync();
            Dispatcher.Invoke(() => UpdateTokenStatus(!string.IsNullOrEmpty(s.CmdLibGitSyncToken)));
        });

        Mw_SettingsExtProvTitle.Text = vm.Localize("SettingsSectionExternalToolProviders");
        Mw_SettingsExtProvDesc.Text = vm.Localize("SettingsExternalToolProvidersDesc");
        Mw_SettingsLblSysintPath.Text = vm.Localize("SettingsLblSysinternalsPath");
        Mw_SettingsLblNirSoftPath.Text = vm.Localize("SettingsLblNirSoftPath");
        Mw_SettingsLblNanaRunPath.Text = vm.Localize("SettingsLblNanaRunPath");
        Mw_SettingsBtnRescan.Content = vm.Localize("ExtToolBtnRescan");
        UpdateExternalToolProviderStatus(vm);
        Mw_SettingsExtToolsTitle.Text = vm.Localize("SettingsSectionExternalToolsList");
        Mw_SidebarToolsFilter.Tag = vm.Localize("ToolsPanelFilterPlaceholder");
        Mw_SettingsExtToolsAddBtn.Content = vm.Localize("BtnAdd");
        Mw_SettingsExtToolsRemoveBtn.Content = vm.Localize("BtnRemove");
        Mw_ExtToolLblName.Text = vm.Localize("ExternalToolLabelName");
        Mw_ExtToolLblPath.Text = vm.Localize("ExternalToolLabelPath");
        Mw_ExtToolLblArgs.Text = vm.Localize("ExternalToolLabelArguments");
        Mw_ExtToolLblWorkDir.Text = vm.Localize("ExternalToolLabelWorkDir");
        Mw_ExtToolChkAdmin.Content = vm.Localize("ExternalToolRunAsAdmin");
        Mw_ExtToolChkHidden.Content = vm.Localize("ExternalToolRunHidden");
        Mw_ExtToolTestBtn.Content = vm.Localize("ExtToolBtnTest");
        PopulateExtToolPlaceholderList(vm);
        UpdateExtToolPreview();

        Mw_SettingsSaveBtn.Content = vm.Localize("SettingsBtnSaveSettings");
        Mw_SettingsResetBtn.Content = vm.Localize("SettingsBtnResetDefaults");
        Mw_SettingsExportBtn.Content = vm.Localize("SettingsBtnExportServers");
        Mw_SettingsExportBtn.ToolTip = vm.Localize("TooltipExport");
        Mw_SettingsImportBtn.Content = vm.Localize("SettingsBtnImportServers");
        Mw_SettingsImportBtn.ToolTip = vm.Localize("TooltipImport");
        Mw_SettingsCitrixBtn.Content = vm.Localize("BtnImportCitrix");
        Mw_SettingsCitrixBtn.ToolTip = vm.Localize("TooltipImportCitrix");
    }

    private void ApplyAboutLocalization(MainViewModel vm)
    {
        Mw_AboutAppName.Text = vm.Localize("AppName");
        Mw_AboutTagline.Text = vm.Localize("AboutTagline");
        Mw_AboutAuthorLabel.Text = vm.Localize("AboutLabelAuthor");
        Mw_AboutLicenseLabel.Text = vm.Localize("AboutLabelLicense");
        Mw_AboutLicenseValue.Text = vm.Localize("AboutLicenseValue");
        Mw_AboutRuntimeLabel.Text = vm.Localize("AboutLabelRuntime");
        Mw_AboutPlatformLabel.Text = vm.Localize("AboutLabelPlatform");
        Mw_AboutFeaturesTitle.Text = vm.Localize("AboutLabelFeatures");
        Mw_AboutFeature1.Text = "\u2022 " + vm.Localize("AboutFeatureEmbedded");
        Mw_AboutFeature2.Text = "\u2022 " + vm.Localize("AboutFeatureDpapi");
        Mw_AboutFeature3.Text = "\u2022 " + vm.Localize("AboutFeatureTunneling");
        Mw_AboutFeature4.Text = "\u2022 " + vm.Localize("AboutFeatureSftp");
        Mw_AboutFeature5.Text = "\u2022 " + vm.Localize("AboutFeatureBilingual");
        Mw_AboutFeature6.Text = "\u2022 " + vm.Localize("AboutFeatureTheme");
    }

    private void ApplyAccessibilityLocalization(MainViewModel vm)
    {
        System.Windows.Automation.AutomationProperties.SetName(TabServers, vm.Localize("NavTabServers"));
        System.Windows.Automation.AutomationProperties.SetName(TabTunnels, vm.Localize("NavTabTunnels"));
        System.Windows.Automation.AutomationProperties.SetName(TabScheduled, vm.Localize("NavTabScheduled"));
        System.Windows.Automation.AutomationProperties.SetName(TabSettings, vm.Localize("NavTabSettings"));
        System.Windows.Automation.AutomationProperties.SetName(TabAbout, vm.Localize("NavTabAbout"));
        System.Windows.Automation.AutomationProperties.SetName(ToggleSidebarButton, vm.Localize("TooltipHideSidebar"));
        System.Windows.Automation.AutomationProperties.SetName(ShowSidebarButton, vm.Localize("TooltipShowSidebar"));
        System.Windows.Automation.AutomationProperties.SetName(ExpandAllButton, vm.Localize("TooltipExpandAll"));
        System.Windows.Automation.AutomationProperties.SetName(CollapseAllButton, vm.Localize("TooltipCollapseAll"));
        System.Windows.Automation.AutomationProperties.SetName(AddButton, vm.Localize("TooltipAddMenu"));
        System.Windows.Automation.AutomationProperties.SetName(SessionTabControl, vm.Localize("NavTabSessions"));
        System.Windows.Automation.AutomationProperties.SetName(QuickConnectButton, vm.Localize("QuickConnectShortcut"));

        // DataTemplate relay: session tab close button and busy indicator bind to ancestor Tag
        SessionTabControl.Tag = vm.Localize("A11yCloseSessionTab");
        Mw_SessionsGrid.Tag = vm.Localize("A11yOperationInProgress");

        // Server detail and empty state buttons
        System.Windows.Automation.AutomationProperties.SetName(Mw_DetailConnectBtn, vm.Localize("AccessDetailConnect"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ToolDetailOpenBtn, vm.Localize("AccessDetailOpen"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_DetailEditBtn, vm.Localize("BtnEdit"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_DetailDeleteBtn, vm.Localize("BtnDelete"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_EmptyBtnAddServer, vm.Localize("AccessEmptyAddServer"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_EmptyBtnImport, vm.Localize("AccessEmptyImport"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_EmptyBtnExploreTools, vm.Localize("AccessEmptyExploreTools"));

        // Tunnel panel icon buttons
        Mw_TunnelPanelCollapseBtn.ToolTip = vm.Localize("TooltipCollapseTunnelPanel");

        // Tunnel tab buttons
        System.Windows.Automation.AutomationProperties.SetName(Mw_TunnelsCloseSelectedBtn, vm.Localize("AccessTunnelsCloseSelected"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_TunnelsCloseAllBtn, vm.Localize("AccessTunnelsCloseAll"));

        // Manage gateways link
        System.Windows.Automation.AutomationProperties.SetName(Mw_TunnelsManageGatewaysLink, vm.Localize("TunnelsManageGatewaysLink"));

        // Scheduled task buttons
        System.Windows.Automation.AutomationProperties.SetName(Mw_ScheduledAddBtn, vm.Localize("AccessScheduledAdd"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ScheduledEditBtn, vm.Localize("AccessScheduledEdit"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ScheduledDeleteBtn, vm.Localize("AccessScheduledDelete"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_ScheduledCreateBtn, vm.Localize("AccessScheduledCreate"));

        // Settings action buttons
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsSaveBtn, vm.Localize("AccessSettingsSave"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsResetBtn, vm.Localize("AccessSettingsReset"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsExportBtn, vm.Localize("AccessSettingsExport"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsImportBtn, vm.Localize("AccessSettingsImport"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsCitrixBtn, vm.Localize("AccessSettingsCitrix"));

        // Settings gateway sub-panel buttons
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsGatewaysAddBtn, vm.Localize("AccessSettingsGatewaysAdd"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsGatewaysEditBtn, vm.Localize("AccessSettingsGatewaysEdit"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsGatewaysDeleteBtn, vm.Localize("AccessSettingsGatewaysDelete"));

        // External tools management buttons
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsExtToolsAddBtn, vm.Localize("AccessSettingsExtToolsAdd"));
        System.Windows.Automation.AutomationProperties.SetName(Mw_SettingsExtToolsRemoveBtn, vm.Localize("AccessSettingsExtToolsRemove"));
    }

    private async void OnServersTabChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckUnsavedSettingsAsync()) { TabSettings.IsChecked = true; return; }
            SwitchToTab("Servers");
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

        // Save TreeView scroll position when leaving Servers tab
        if (vm.IsServersTabSelected && !string.Equals(tabName, "Servers", StringComparison.Ordinal))
        {
            SaveTreeViewScrollPosition();
        }

        Heimdall.Core.Logging.FileLogger.Info(
            $"Navigation request: tab={tabName}, current={vm.SelectedTab}, hasSessions={vm.Connection.HasActiveSessions}");

        vm.SelectedTab = tabName;
        UpdateTabVisibility(vm);

        // Restore TreeView scroll position when returning to Servers tab
        if (string.Equals(tabName, "Servers", StringComparison.Ordinal))
        {
            RestoreTreeViewScrollPosition();
        }

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

        var queryLower = query.ToLowerInvariant();
        int matchCount = 0;
        TabItem? firstMatch = null;

        for (int i = 0; i < tabs.Length && i < SettingsTabKeywords.Length; i++)
        {
            var keywords = SettingsTabKeywords[i];
            // Also match against the tab header text
            var headerText = tabs[i].Header?.ToString()?.ToLowerInvariant() ?? "";
            bool matches = keywords.Any(k => k.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                           || headerText.Contains(queryLower, StringComparison.OrdinalIgnoreCase);

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

            case Key.Escape when _isFullscreen:
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
        if (!ServerTreeView.IsKeyboardFocusWithin)
        {
            return;
        }

        var target = ServerTreeView.SelectedItem;
        var menu = CreateTreeContextMenu(vm, target);

        // Try to position the menu at the selected item's location
        var container = FindTreeViewItemContainer(ServerTreeView, target);
        if (container is not null)
        {
            menu.PlacementTarget = container;
            menu.Placement = PlacementMode.Bottom;
        }
        else
        {
            menu.PlacementTarget = ServerTreeView;
            menu.Placement = PlacementMode.Center;
        }

        ServerTreeView.ContextMenu = menu;
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
                ServerDetailPanel.Visibility = Visibility.Collapsed;
                ToolDetailPanel.Visibility = Visibility.Visible;
                UpdateToolDetailPanel(vm, server.ConnectionType!);
            }
            else
            {
                ServerDetailPanel.Visibility = Visibility.Visible;
                ToolDetailPanel.Visibility = Visibility.Collapsed;
                Mw_DetailConnectBtn.Content = vm.Localize("DetailBtnConnect");
                Mw_DetailHostPort.Visibility = Visibility.Visible;
            }
        }
        else
        {
            vm.ServerList.SelectedServer = null;
            ServerDetailPanel.Visibility = Visibility.Collapsed;
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

        var menu = CreateTreeContextMenu(vm, target);
        menu.PlacementTarget = treeView;
        menu.Placement = PlacementMode.MousePoint;
        treeView.ContextMenu = menu;
    }

    private ContextMenu CreateTreeContextMenu(MainViewModel vm, object? target)
    {
        return target switch
        {
            ServerItemViewModel server when server.ConnectionType?.StartsWith(
                "TOOL:", StringComparison.OrdinalIgnoreCase) == true
                => CreateToolContextMenu(vm, server),
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
            server,
            inputGestureText: "Ctrl+E"));
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

        // Wake-on-LAN (only shown when MAC address is configured)
        if (Core.Security.WakeOnLan.IsValidMac(server.MacAddress))
        {
            var wolItem = new MenuItem { Header = vm.Localize("TreeCtxWakeOnLan") };
            wolItem.Click += async (_, _) =>
            {
                var sent = await Core.Security.WakeOnLan.SendAsync(server.MacAddress);
                vm.StatusText = sent
                    ? vm.Localize("WolSent")
                    : vm.Localize("WolFailed");
            };
            menu.Items.Add(wolItem);
        }

        // Notes submenu
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateNotesSubmenu(vm, server));

        // External tools submenu
        var externalToolsMenu = CreateExternalToolsMenu(vm, server);
        if (externalToolsMenu is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(externalToolsMenu);
        }

        // Detected third-party tools submenu (Sysinternals / NirSoft)
        var detectedToolsMenu = CreateDetectedToolsMenu(vm, server);
        if (detectedToolsMenu is not null)
        {
            if (externalToolsMenu is null) menu.Items.Add(new Separator());
            menu.Items.Add(detectedToolsMenu);
        }

        menu.Items.Add(new Separator());
        var deleteItem = CreateMenuItem(
            vm.Localize("TreeCtxDelete"),
            vm.ServerList.DeleteServerCommand,
            server,
            inputGestureText: "Ctrl+Del");
        deleteItem.Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush
            ?? new System.Windows.Media.SolidColorBrush(Colors.Red);
        menu.Items.Add(deleteItem);

        return menu;
    }

    private MenuItem CreateNotesSubmenu(MainViewModel vm, ServerItemViewModel server)
    {
        var submenu = new MenuItem { Header = vm.Localize("TreeCtxNotes") };

        var blankItem = new MenuItem { Header = vm.Localize("ToolNotesBtnNew") };
        blankItem.Click += (_, _) => OpenNotesForServer(vm, server, NoteTemplateKind.Blank);
        submenu.Items.Add(blankItem);

        var dailyItem = new MenuItem { Header = vm.Localize("ToolNotesBtnDaily") };
        dailyItem.Click += (_, _) => OpenNotesForServer(vm, server, NoteTemplateKind.Daily);
        submenu.Items.Add(dailyItem);

        var incidentItem = new MenuItem { Header = vm.Localize("ToolNotesBtnIncident") };
        incidentItem.Click += (_, _) => OpenNotesForServer(vm, server, NoteTemplateKind.Incident);
        submenu.Items.Add(incidentItem);

        var procedureItem = new MenuItem { Header = vm.Localize("ToolNotesBtnProcedure") };
        procedureItem.Click += (_, _) => OpenNotesForServer(vm, server, NoteTemplateKind.Procedure);
        submenu.Items.Add(procedureItem);

        return submenu;
    }

    private static void OpenNotesForServer(
        MainViewModel vm,
        ServerItemViewModel server,
        NoteTemplateKind templateKind)
    {
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

    /// <summary>
    /// Builds a context menu specific to tool entries (TOOL:*) in the TreeView.
    /// Excludes server-specific actions like Connect, Copy Hostname, Wake-on-LAN, External Tools.
    /// </summary>
    private ContextMenu CreateToolContextMenu(MainViewModel vm, ServerItemViewModel tool)
    {
        var menu = CreateContextMenu();

        // "Open in Tab" — the primary action for tools
        var openItem = new MenuItem { Header = vm.Localize("TreeCtxOpenToolInTab") };
        openItem.Click += (_, _) =>
        {
            var toolId = tool.ConnectionType!["TOOL:".Length..];
            vm.TrackRecentTool(toolId.ToUpperInvariant());
            var context = new Core.Models.ToolContext(
                TargetHost: tool.RemoteServer,
                TargetPort: tool.RemotePort > 0 ? tool.RemotePort : null,
                Argument: tool.RemoteServer);
            _ = vm.OpenToolTabAsync(toolId, tool.DisplayName, context);
        };
        menu.Items.Add(openItem);

        menu.Items.Add(new Separator());

        // Move to Project / Group (tools can be organized just like servers)
        menu.Items.Add(CreateMoveToProjectMenu(vm, tool));
        menu.Items.Add(CreateMoveToGroupMenu(vm, tool));

        menu.Items.Add(new Separator());

        // Remove from inventory
        var removeItem = CreateMenuItem(
            vm.Localize("TreeCtxRemoveTool"),
            vm.ServerList.DeleteServerCommand,
            tool);
        removeItem.Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush
            ?? new System.Windows.Media.SolidColorBrush(Colors.Red);
        menu.Items.Add(removeItem);

        return menu;
    }

    /// <summary>
    /// Builds the "External Tools" submenu for a server context menu.
    /// Returns null if no external tools are configured.
    /// </summary>
    private MenuItem? CreateExternalToolsMenu(MainViewModel vm, ServerItemViewModel server)
    {
        var tools = vm.CurrentSettings?.ExternalTools;
        if (tools is null || tools.Count == 0)
        {
            return null;
        }

        var submenu = new MenuItem
        {
            Header = vm.Localize("TreeCtxExternalTools")
        };

        foreach (var tool in tools)
        {
            var toolItem = new MenuItem
            {
                Header = tool.Name
            };

            // Capture for closure
            var capturedTool = tool;
            toolItem.Click += (_, _) => LaunchExternalTool(vm, capturedTool, server);
            submenu.Items.Add(toolItem);
        }

        return submenu;
    }

    /// <summary>
    /// Creates a submenu for auto-detected third-party tools (Sysinternals, NirSoft),
    /// grouped by provider. Only shown if at least one tool is detected.
    /// </summary>
    private MenuItem? CreateDetectedToolsMenu(MainViewModel vm, ServerItemViewModel server)
    {
        var app = System.Windows.Application.Current as App;
        var providerService = app?.Services?
            .GetService(typeof(Services.ExternalToolProviderService)) as Services.ExternalToolProviderService;

        var tools = providerService?.DetectedTools;
        if (tools is null || tools.Count == 0)
            return null;

        var submenu = new MenuItem
        {
            Header = vm.Localize("TreeCtxDetectedTools")
        };

        // Group by provider (Sysinternals, NirSoft, etc.)
        var groups = tools.GroupBy(t => t.ProviderName, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var providerMenu = new MenuItem { Header = group.Key, FontWeight = FontWeights.SemiBold };

            foreach (var tool in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var toolItem = new MenuItem { Header = tool.Name };

                if (tool.DescriptionKey is not null)
                    toolItem.ToolTip = vm.Localize(tool.DescriptionKey);

                var captured = tool;
                toolItem.Click += (_, _) => LaunchDetectedTool(vm, captured, server);
                providerMenu.Items.Add(toolItem);
            }

            submenu.Items.Add(providerMenu);
        }

        return submenu;
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
            SidebarTabServers.IsChecked = true;
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
    /// Builds the category/tool hierarchy from <see cref="ToolRegistry"/> and
    /// attaches it to the sidebar Tools <see cref="TreeView"/>. Lazy: only
    /// invoked the first time the user switches to the Tools tab, and re-run
    /// when external tools are discovered (the External category changes).
    /// </summary>
    private void BuildSidebarToolsData()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var grouped = ToolRegistry.All
            .GroupBy(d => d.Category)
            .OrderBy(g => g.Key);

        var categories = new System.Collections.ObjectModel.ObservableCollection<ViewModels.SidebarToolCategoryViewModel>();

        foreach (var group in grouped)
        {
            var brushKey = GetCategoryBrushKey(group.Key);
            var tools = new System.Collections.ObjectModel.ObservableCollection<ViewModels.SidebarToolItemViewModel>();

            var sortedTools = group
                .Select(d => new
                {
                    Descriptor = d,
                    Name = vm.Localize(d.LabelKey)
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var tool in sortedTools)
            {
                var aliases = string.Join(' ', tool.Descriptor.CommandPrefixes);
                tools.Add(new ViewModels.SidebarToolItemViewModel
                {
                    Id = tool.Descriptor.Id,
                    Name = tool.Name,
                    BrushKey = brushKey,
                    IconGeometryKey = tool.Descriptor.IconResourceKey,
                    Searchable = $"{tool.Name} {aliases}".ToLowerInvariant()
                });
            }

            var categoryName = vm.Localize(group.First().CategoryLabelKey);
            categories.Add(new ViewModels.SidebarToolCategoryViewModel
            {
                CategoryName = categoryName,
                BrushKey = brushKey,
                Tools = tools,
                VisibleCount = tools.Count,
                IsExpanded = false
            });
        }

        _sidebarToolsCategories = categories;
        SidebarToolsTreeView.ItemsSource = categories;
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
    }

    private void UpdateSidebarToolsContextLabel()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var host = GetInheritedToolTargetHost(vm);
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

        var filter = Mw_SidebarToolsFilter.Text.Trim();
        var hasFilter = !string.IsNullOrEmpty(filter);
        var filterLower = filter.ToLowerInvariant();
        var anyVisibleTool = false;

        foreach (var category in _sidebarToolsCategories)
        {
            var visibleInCategory = 0;
            foreach (var tool in category.Tools)
            {
                var matches = !hasFilter || tool.Searchable.Contains(filterLower, StringComparison.Ordinal);
                tool.IsVisible = matches;
                if (matches)
                {
                    visibleInCategory++;
                }
            }

            category.VisibleCount = hasFilter ? visibleInCategory : category.Tools.Count;
            category.IsVisible = !hasFilter || visibleInCategory > 0;
            // Auto-expand matching categories when filtering; collapse when cleared.
            category.IsExpanded = hasFilter && visibleInCategory > 0;

            if (category.IsVisible)
            {
                anyVisibleTool = true;
            }
        }

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
            var context = CreateInheritedToolContext(vm, descriptor);

            // Match the full-page Tools tab: make sure the session panel is visible
            // before opening the tool tab.
            TabServers.IsChecked = true;
            SwitchToTab("Servers");

            await vm.OpenToolTabAsync(
                descriptor.Id,
                ResolveToolTabTitle(vm, descriptor, context),
                context);
            vm.TrackRecentTool(descriptor.Id);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Sidebar tool launch failed: {descriptor.Id}", ex);
            vm.StatusText = $"Tool launch failed: {descriptor.Id} — {ex.Message}";
        }
    }

    private static string GetCategoryBrushKey(Core.Models.ToolCategory category)
        => category switch
        {
            Core.Models.ToolCategory.Network => "ToolNetworkBrush",
            Core.Models.ToolCategory.Security => "ToolSecurityBrush",
            Core.Models.ToolCategory.Encoding => "ToolEncodingBrush",
            Core.Models.ToolCategory.System => "ToolSystemBrush",
            Core.Models.ToolCategory.External => "ToolExternalBrush",
            _ => "TextSecondaryBrush"
        };

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
        ToolsTabContent.Children.Clear();
        var filter = Mw_ToolsTabSearch.Text.Trim();
        var hasFilter = !string.IsNullOrEmpty(filter);
        var matchingToolCount = 0;

        // ── Favorites section ──
        if (!hasFilter)
        {
            var favIds = vm.FavoriteToolIds;
            if (favIds.Count > 0)
            {
                AddToolsTabSectionHeader(vm.Localize("ToolsFavoritesHeader"), "WarningBrush");
                var favPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
                foreach (var favId in favIds)
                {
                    var desc = ToolRegistry.All.FirstOrDefault(d => string.Equals(d.Id, favId, StringComparison.OrdinalIgnoreCase));
                    if (desc is not null)
                        favPanel.Children.Add(CreateToolsTabCard(vm, desc));
                }
                ToolsTabContent.Children.Add(favPanel);
            }
            else
            {
                AddToolsTabSectionHeader(vm.Localize("ToolsFavoritesHeader"), "WarningBrush");
                var emptyFav = new TextBlock
                {
                    Text = vm.Localize("ToolsEmptyFavorites"),
                    FontSize = (double)FindResource("FontSizeCaption"),
                    Margin = new Thickness(4, 0, 0, 16)
                };
                emptyFav.SetResourceReference(TextBlock.ForegroundProperty, "TextDisabledBrush");
                ToolsTabContent.Children.Add(emptyFav);
            }

            // ── Recent section ──
            var recentIds = vm.RecentToolIds;
            if (recentIds.Count > 0)
            {
                AddToolsTabSectionHeader(vm.Localize("ToolsRecentHeader"), "AccentBrush");
                var recentPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
                foreach (var rid in recentIds)
                {
                    var desc = ToolRegistry.All.FirstOrDefault(d => string.Equals(d.Id, rid, StringComparison.OrdinalIgnoreCase));
                    if (desc is not null)
                        recentPanel.Children.Add(CreateToolsTabCard(vm, desc));
                }
                ToolsTabContent.Children.Add(recentPanel);
            }
        }

        // ── All tools by category ──
        if (!hasFilter)
            AddToolsTabSectionHeader(vm.Localize("ToolsAllHeader"), "TextPrimaryBrush");

        string? lastCategory = null;
        var sorted = ToolRegistry.All
            .OrderBy(d => d.Category)
            .ThenBy(d => vm.Localize(d.LabelKey), StringComparer.OrdinalIgnoreCase);

        WrapPanel? currentWrap = null;

        foreach (var descriptor in sorted)
        {
            if (hasFilter)
            {
                var label = vm.Localize(descriptor.LabelKey);
                var aliases = string.Join(" ", descriptor.CommandPrefixes);
                var descKey = descriptor.DescriptionKey ?? $"ToolDesc{descriptor.Id}";
                var descText = vm.Localize(descKey);
                var searchable = $"{label} {aliases} {descText}";
                if (!searchable.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            matchingToolCount++;

            if (!string.Equals(descriptor.CategoryLabelKey, lastCategory, StringComparison.Ordinal))
            {
                if (currentWrap is not null)
                    ToolsTabContent.Children.Add(currentWrap);

                var brushKey = GetCategoryBrushKey(descriptor.Category);
                AddToolsTabCategoryHeader(vm.Localize(descriptor.CategoryLabelKey), brushKey);
                currentWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                lastCategory = descriptor.CategoryLabelKey;
            }

            currentWrap?.Children.Add(CreateToolsTabCard(vm, descriptor));
        }
        if (currentWrap is not null)
            ToolsTabContent.Children.Add(currentWrap);

        Mw_ToolsTabCount.Text = vm.Localize("ToolsToolCount")
            .Replace("{0}", (hasFilter ? matchingToolCount : ToolRegistry.All.Count).ToString());

        if (hasFilter && matchingToolCount == 0)
        {
            var emptyState = new StackPanel
            {
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            var noResultsTitle = new TextBlock
            {
                Text = vm.Localize("ToolsNoResults"),
                FontSize = (double)FindResource("FontSizeBodyLarge"),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            noResultsTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            emptyState.Children.Add(noResultsTitle);

            var noResultsHint = new TextBlock
            {
                Text = vm.Localize("ToolsNoResultsHint"),
                FontSize = (double)FindResource("FontSizeCaption"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            noResultsHint.SetResourceReference(TextBlock.ForegroundProperty, "TextDisabledBrush");
            emptyState.Children.Add(noResultsHint);

            ToolsTabContent.Children.Add(emptyState);
        }
    }

    private void AddToolsTabSectionHeader(string text, string brushKey)
    {
        var sectionHeader = new TextBlock
        {
            Text = text,
            FontSize = (double)FindResource("FontSizeBodyLarge"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 8)
        };
        sectionHeader.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        ToolsTabContent.Children.Add(sectionHeader);
    }

    private void AddToolsTabCategoryHeader(string text, string brushKey)
    {
        var header = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4)
        };
        var accentBar = new Border
        {
            Width = 3,
            Height = 16,
            CornerRadius = new CornerRadius(1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        accentBar.SetResourceReference(Border.BackgroundProperty, brushKey);
        header.Children.Add(accentBar);

        var label = new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = (double)FindResource("FontSizeCaption"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        header.Children.Add(label);
        ToolsTabContent.Children.Add(header);
    }

    /// <summary>
    /// Creates a wider card for the Tools tab grid layout with a pin/unpin button.
    /// </summary>
    private FrameworkElement CreateToolsTabCard(MainViewModel vm, Core.Models.ToolDescriptor descriptor)
    {
        var categoryBrushKey = GetCategoryBrushKey(descriptor.Category);
        const string DefaultBorderKey = "BorderBrush";
        const string ActiveBorderKey = "AccentBrush";
        const string DefaultBackgroundKey = "CardBrush";
        const string ActiveBackgroundKey = "HighlightBrush";

        // Icon
        System.Windows.Shapes.Path? iconPath = null;
        if (descriptor.IconResourceKey is not null
            && TryFindResource(descriptor.IconResourceKey) is System.Windows.Media.Geometry geo)
        {
            iconPath = new System.Windows.Shapes.Path
            {
                Data = geo,
                Width = 20,
                Height = 20,
                Stretch = System.Windows.Media.Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconPath.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, categoryBrushKey);
        }

        var nameBlock = new TextBlock
        {
            Text = vm.Localize(descriptor.LabelKey),
            FontSize = (double)FindResource("FontSizeBody"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var descKey = descriptor.DescriptionKey ?? $"ToolDesc{descriptor.Id}";
        var descText = vm.Localize(descKey);
        var descBlock = new TextBlock
        {
            Text = descText != descKey ? descText : "",
            FontSize = (double)FindResource("FontSizeSmallCaption"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        // Pin/Unpin button
        var isFav = vm.FavoriteToolIds.Contains(descriptor.Id, StringComparer.OrdinalIgnoreCase);
        var pinBtn = new Button
        {
            Content = isFav ? "\uE735" : "\uE734",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Style = (Style)FindResource("ToolbarGhostButtonStyle"),
            Padding = new Thickness(2),
            Opacity = isFav ? 1.0 : 0.4,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 0, 0, 0),
            Tag = descriptor.Id,
            ToolTip = isFav ? vm.Localize("ToolsUnpinTooltip") : vm.Localize("ToolsPinTooltip")
        };
        pinBtn.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,
            isFav ? "WarningBrush" : "TextSecondaryBrush");
        System.Windows.Automation.AutomationProperties.SetName(pinBtn,
            isFav ? vm.Localize("A11yUnpinTool") : vm.Localize("A11yPinTool"));
        pinBtn.Click += OnToolPinClick;

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(nameBlock);
        if (!string.IsNullOrEmpty(descBlock.Text))
            textStack.Children.Add(descBlock);

        var content = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 24, 0)
        };
        if (iconPath is not null)
        {
            var iconBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(6),
                Opacity = 0.12,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = iconPath
            };
            iconBorder.SetResourceReference(Border.BackgroundProperty, categoryBrushKey);
            content.Children.Add(iconBorder);
            DockPanel.SetDock(iconBorder, Dock.Left);
        }
        content.Children.Add(textStack);

        // Use a bare template so the button has no hover chrome —
        // all visual feedback comes from the outer cardBorder.
        var btnTemplate = new ControlTemplate(typeof(Button));
        var btnPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        btnPresenter.SetValue(MarginProperty, new Thickness(10, 8, 10, 8));
        btnPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        btnPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        btnTemplate.VisualTree = btnPresenter;

        var launchButton = new Button
        {
            Content = content,
            Tag = descriptor,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = descBlock.Text.Length > 0 ? descBlock.Text : null,
            Template = btnTemplate
        };
        System.Windows.Automation.AutomationProperties.SetName(launchButton, vm.Localize(descriptor.LabelKey));
        launchButton.Click += OnToolsTabCardClick;

        var cardBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = launchButton
        };
        cardBorder.SetResourceReference(Border.BorderBrushProperty, DefaultBorderKey);
        cardBorder.SetResourceReference(Border.BackgroundProperty, DefaultBackgroundKey);

        void UpdateCardVisualState()
        {
            var isActive = launchButton.IsMouseOver
                || launchButton.IsKeyboardFocusWithin
                || pinBtn.IsMouseOver
                || pinBtn.IsKeyboardFocusWithin;

            // Resolve via SetResourceReference so the hover-state brushes track
            // runtime theme swaps without needing a manual rebuild.
            cardBorder.SetResourceReference(Border.BorderBrushProperty,
                isActive ? ActiveBorderKey : DefaultBorderKey);
            cardBorder.SetResourceReference(Border.BackgroundProperty,
                isActive ? ActiveBackgroundKey : DefaultBackgroundKey);
        }

        launchButton.MouseEnter += (_, _) => UpdateCardVisualState();
        launchButton.MouseLeave += (_, _) => UpdateCardVisualState();
        launchButton.GotKeyboardFocus += (_, _) => UpdateCardVisualState();
        launchButton.LostKeyboardFocus += (_, _) => UpdateCardVisualState();
        pinBtn.MouseEnter += (_, _) => UpdateCardVisualState();
        pinBtn.MouseLeave += (_, _) => UpdateCardVisualState();
        pinBtn.GotKeyboardFocus += (_, _) => UpdateCardVisualState();
        pinBtn.LostKeyboardFocus += (_, _) => UpdateCardVisualState();

        var card = new Grid
        {
            Width = 280,
            Margin = new Thickness(0, 0, 8, 8)
        };
        card.Children.Add(cardBorder);

        pinBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        pinBtn.VerticalAlignment = VerticalAlignment.Top;
        pinBtn.Margin = new Thickness(0, 6, 6, 0);
        card.Children.Add(pinBtn);

        return card;
    }

    private string? GetInheritedToolTargetHost(MainViewModel vm)
    {
        var host = vm.ServerList.SelectedServer?.RemoteServer;
        return string.IsNullOrWhiteSpace(host) ? null : host.Trim();
    }

    private Core.Models.ToolContext? CreateInheritedToolContext(MainViewModel vm, Core.Models.ToolDescriptor descriptor)
    {
        if (!descriptor.IsNetworkTool)
        {
            return null;
        }

        var host = GetInheritedToolTargetHost(vm);
        return host is null ? null : new Core.Models.ToolContext(TargetHost: host);
    }

    private string ResolveToolTabTitle(MainViewModel vm, Core.Models.ToolDescriptor descriptor, Core.Models.ToolContext? context)
    {
        if (context?.TargetHost is not null && descriptor.LabelWithArgKey is not null)
        {
            return string.Format(vm.Localize(descriptor.LabelWithArgKey), context.TargetHost);
        }

        return vm.Localize(descriptor.LabelKey);
    }

    private void UpdateToolLaunchContextLabels()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var host = GetInheritedToolTargetHost(vm);
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

    private async void OnToolPinClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || DataContext is not MainViewModel vm) return;
        e.Handled = true;
        var toolId = btn.Tag as string;
        if (toolId is null) return;
        await vm.ToggleFavoriteToolAsync(toolId);
        RefreshToolsTabSections(vm);
    }

    private async void OnToolsTabCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not Core.Models.ToolDescriptor descriptor
            || DataContext is not MainViewModel vm)
            return;

        try
        {
            var context = CreateInheritedToolContext(vm, descriptor);

            // Switch to Servers tab first so the session panel is visible
            TabServers.IsChecked = true;
            SwitchToTab("Servers");

            await vm.OpenToolTabAsync(descriptor.Id, ResolveToolTabTitle(vm, descriptor, context), context);
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

    private int _onboardingStep;
    private const int OnboardingStepCount = 3;

    private void ShowOnboarding(MainViewModel vm)
    {
        _onboardingStep = 0;
        OnboardingOverlay.Visibility = Visibility.Visible;
        OnboardingOverlay.KeyDown += OnOnboardingKeyDown;
        UpdateOnboardingStep(vm);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            () => OnboardingNextBtn.Focus());
    }

    private void UpdateOnboardingStep(MainViewModel vm)
    {
        // Step indicator dots
        OnboardingDots.Children.Clear();
        for (var i = 0; i < OnboardingStepCount; i++)
        {
            OnboardingDots.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = i == _onboardingStep
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("TextDisabledBrush"),
                Margin = new Thickness(4, 0, 4, 0)
            });
        }

        // Content per step
        switch (_onboardingStep)
        {
            case 0:
                OnboardingTitle.Text = vm.Localize("OnboardingStep1Title");
                OnboardingSubtitle.Text = vm.Localize("OnboardingStep1Desc");
                break;
            case 1:
                OnboardingTitle.Text = vm.Localize("OnboardingStep2Title");
                OnboardingSubtitle.Text = vm.Localize("OnboardingStep2Desc");
                break;
            case 2:
                OnboardingTitle.Text = vm.Localize("OnboardingStep3Title");
                OnboardingSubtitle.Text = vm.Localize("OnboardingStep3Desc");
                break;
        }

        OnboardingSkipBtn.Content = vm.Localize("OnboardingBtnSkip");
        System.Windows.Automation.AutomationProperties.SetName(OnboardingSkipBtn, vm.Localize("OnboardingBtnSkip"));
        var nextKey = _onboardingStep < OnboardingStepCount - 1
            ? "OnboardingBtnNext"
            : "OnboardingBtnGetStarted";
        OnboardingNextBtn.Content = vm.Localize(nextKey);
        System.Windows.Automation.AutomationProperties.SetName(OnboardingNextBtn, vm.Localize(nextKey));
    }

    private async void OnOnboardingSkip(object sender, RoutedEventArgs e)
    {
        await CompleteOnboardingAsync();
    }

    private async void OnOnboardingNext(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (_onboardingStep < OnboardingStepCount - 1)
        {
            // Perform action for the completed step
            switch (_onboardingStep)
            {
                case 0: // Step 1 done → navigate to Servers tab
                    TabServers.IsChecked = true;
                    SwitchToTab("Servers");
                    break;
                case 1: // Step 2 done → navigate to Settings tab
                    TabSettings.IsChecked = true;
                    SwitchToTab("Settings");
                    break;
            }

            _onboardingStep++;
            UpdateOnboardingStep(vm);
        }
        else
        {
            // Step 3 done → switch sidebar to the Tools tab and complete onboarding.
            // ShowToolsPanel is reused as a bool flag: true = Tools tab, false = Servers tab.
            if (vm.CurrentSettings is not null)
                vm.CurrentSettings.ShowToolsPanel = true;
            SidebarTabTools.IsChecked = true;
            await CompleteOnboardingAsync();
        }
    }

    private void OnOnboardingKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _ = CompleteOnboardingAsync();
        }
    }

    private async Task CompleteOnboardingAsync()
    {
        OnboardingOverlay.KeyDown -= OnOnboardingKeyDown;
        OnboardingOverlay.Visibility = Visibility.Collapsed;
        if (DataContext is not MainViewModel vm || vm.CurrentSettings is null) return;
        vm.CurrentSettings.OnboardingCompleted = true;
        await vm.ConfigManager.MergeSettingAsync(s => s.OnboardingCompleted = true);
    }

    private async void OnToolsPanelItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Core.Models.ToolDescriptor descriptor
            || DataContext is not MainViewModel vm)
            return;

        try
        {
            var context = CreateInheritedToolContext(vm, descriptor);

            await vm.OpenToolTabAsync(
                descriptor.Id,
                ResolveToolTabTitle(vm, descriptor, context),
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

    private void UpdateExtToolPreview()
    {
        if (DataContext is not MainViewModel vm || vm.Settings.SelectedExternalTool is null)
        {
            Mw_ExtToolPreview.Text = "";
            return;
        }

        var tool = vm.Settings.SelectedExternalTool;
        var selectedServer = vm.ServerList.SelectedServer;
        string preview;
        if (selectedServer is not null)
        {
            var def = new Core.Configuration.ExternalToolDefinition
            {
                ExecutablePath = tool.ExecutablePath,
                Arguments = tool.Arguments
            };
            var resolved = def.ResolveArguments(
                selectedServer.RemoteServer, selectedServer.EffectivePort, selectedServer.Username,
                serverName: selectedServer.DisplayName, protocol: selectedServer.ConnectionType,
                keyFile: selectedServer.SshKeyPath, project: selectedServer.ProjectName,
                gateway: selectedServer.GatewayName);
            preview = $"{tool.ExecutablePath} {resolved}";
        }
        else
        {
            preview = $"{tool.ExecutablePath} {tool.Arguments}";
        }

        Mw_ExtToolPreview.Text = preview;
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

    private void PopulateCredProvPresets(MainViewModel vm)
    {
        Mw_SettingsCredProvPreset.Items.Clear();
        foreach (var (label, _) in CredProvPresets)
        {
            Mw_SettingsCredProvPreset.Items.Add(label);
        }
        Mw_SettingsCredProvPreset.SelectedIndex = 0;
    }

    private void OnCredProvPresetChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Mw_SettingsCredProvPreset.SelectedIndex < 1) return; // skip "Custom"
        if (DataContext is not MainViewModel vm) return;

        var (_, command) = CredProvPresets[Mw_SettingsCredProvPreset.SelectedIndex];
        vm.Settings.CredentialProviderCommand = command;
        vm.Settings.IsDirty = true;
    }

    private void PopulateExtToolPlaceholderList(MainViewModel vm)
    {
        Mw_ExtToolPlaceholderList.Items.Clear();
        foreach (var (variable, descKey) in Core.Configuration.ExternalToolDefinition.SupportedPlaceholders)
        {
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 2) };
            panel.Children.Add(new TextBlock
            {
                Text = variable,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = (double)FindResource("FontSizeCaption"),
                Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = $" \u2014 {vm.Localize(descKey)}",
                FontSize = (double)FindResource("FontSizeCaption"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center
            });
            Mw_ExtToolPlaceholderList.Items.Add(panel);
        }
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
        menu.Items.Add(CreateAddToolMenuItem(vm, folder.FullPath));

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
            vm.ServerList.AddServerCommand,
            inputGestureText: "Ctrl+N"));
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

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateAddToolMenuItem(vm));

        return menu;
    }

    private MenuItem CreateAddToolMenuItem(MainViewModel vm, string? group = null)
    {
        var item = new MenuItem { Header = vm.Localize("AddMenuTool"), Tag = group };
        item.Click += OnAddToolMenuClick;
        return item;
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
        bool isEnabled = true,
        string? inputGestureText = null)
    {
        return new MenuItem
        {
            Header = header,
            Command = command,
            CommandParameter = parameter,
            IsEnabled = isEnabled,
            InputGestureText = inputGestureText ?? string.Empty
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

    /// <summary>
    /// Walks the visual tree of the given element to find a child <see cref="ScrollViewer"/>.
    /// </summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
                return sv;
            var result = FindScrollViewer(child);
            if (result is not null)
                return result;
        }
        return null;
    }

    private void SaveTreeViewScrollPosition()
    {
        var sv = FindScrollViewer(ServerTreeView);
        if (sv is not null)
        {
            _treeScrollVerticalOffset = sv.VerticalOffset;
            _treeScrollHorizontalOffset = sv.HorizontalOffset;
        }
    }

    private void RestoreTreeViewScrollPosition()
    {
        // Defer to allow the TreeView to re-render before restoring scroll position
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                var sv = FindScrollViewer(ServerTreeView);
                if (sv is not null)
                {
                    sv.ScrollToVerticalOffset(_treeScrollVerticalOffset);
                    sv.ScrollToHorizontalOffset(_treeScrollHorizontalOffset);
                }
            }));
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
    private double _treeScrollVerticalOffset;
    private double _treeScrollHorizontalOffset;

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
        if (DataContext is not ViewModels.MainViewModel vm) return;

        foreach (var folder in vm.ServerList.GroupedServers)
        {
            SetFolderExpandedRecursive(folder, expanded);
        }
    }

    private static void SetFolderExpandedRecursive(ViewModels.FolderViewModel folder, bool expanded)
    {
        folder.IsExpanded = expanded;
        foreach (var sub in folder.SubFolders)
        {
            SetFolderExpandedRecursive(sub, expanded);
        }
    }

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

    // ── Ephemeral File Server ─────────────────────────────────────────

    private void OnShareFolderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (_fileServer is { IsHttpRunning: true } or { IsTftpRunning: true })
        {
            StopFileServer();
            return;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var directory = dialog.SelectedPath;
        var settings = (DataContext as MainViewModel)?.CurrentSettings;
        _fileServer = new EphemeralFileServer
        {
            ShutdownTimeoutMs = settings?.ServerShutdownTimeoutMs ?? 2000
        };

        var httpPort = settings?.EphemeralHttpPort ?? 8080;
        var tftpPort = settings?.EphemeralTftpPort ?? 69;

        try
        {
            _fileServer.StartHttpServer(directory, httpPort);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Failed to start HTTP server: {ex.Message}");
        }

        try
        {
            _fileServer.StartTftpServer(directory, tftpPort);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Failed to start TFTP server: {ex.Message}");
        }

        if (!_fileServer.IsHttpRunning && !_fileServer.IsTftpRunning)
        {
            _fileServer.Dispose();
            _fileServer = null;
            return;
        }

        // Update UI to show Stop Sharing state
        Mw_ShareFolderLabel.Text = vm.Localize("ToolsStopSharing");

        var localIp = EphemeralFileServer.GetLocalIpAddress();
        var folderName = System.IO.Path.GetFileName(directory);
        var baseUrl = $"http://{localIp}:{httpPort}";

        Mw_SharingStatus.Text = baseUrl;
        Mw_SharingStatus.Visibility = Visibility.Visible;

        // Show a helper dialog with ready-to-use commands
        var wgetCmd = $"wget {baseUrl}/<filename>";
        var curlCmd = $"curl -O {baseUrl}/<filename>";
        var tftpCmd = $"tftp {localIp} -c get <filename>";

        // Copy the base URL to clipboard for convenience
        try { Clipboard.SetText(baseUrl); } catch { /* clipboard may fail in some RDP sessions */ }

        var helpMessage = string.Format(vm.Localize("ToolsSharingHelp"),
            folderName, baseUrl, wgetCmd, curlCmd, tftpCmd);

        vm.StatusText = string.Format(vm.Localize("ToolsSharingReady"), baseUrl);

        MessageBox.Show(helpMessage,
            vm.Localize("ToolsSharingHelpTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        _fileServer.FileServed += fileName =>
        {
            Dispatcher.Invoke(() =>
            {
                if (DataContext is MainViewModel mvm)
                    mvm.StatusText = string.Format(vm.Localize("ToolsSharingServed"), fileName, baseUrl);
            });
        };
    }

    private async void StopFileServer()
    {
        if (_fileServer is not null)
        {
            await _fileServer.DisposeAsync();
            _fileServer = null;
        }

        Mw_SharingStatus.Visibility = Visibility.Collapsed;
        Mw_SharingStatus.Text = string.Empty;

        if (DataContext is MainViewModel vm)
        {
            Mw_ShareFolderLabel.Text = vm.Localize("ToolsShareFolder");
            vm.StatusText = vm.Localize("ToolsSharingStopped");
        }
    }

    private void RestoreWindowBounds(MainViewModel vm)
    {
        var settings = vm.ConfigManager.LoadSettingsAsync().GetAwaiter().GetResult();
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
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
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

        if (vm.Settings.IsDirty)
        {
            var title = vm.Localize("SettingsUnsavedWarningTitle");
            var message = vm.Localize("SettingsUnsavedWarning");
            var result = vm.DialogService.ShowSaveDiscardCancelAsync(title, message)
                .GetAwaiter().GetResult();

            if (result is null)
            {
                e.Cancel = true;
                return;
            }

            if (result == true)
            {
                vm.Settings.SaveCommand.Execute(null);
            }
            // false = Discard — let the window close without saving
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeServiceThemeChanged;
        StopFileServer();
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
