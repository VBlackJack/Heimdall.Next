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

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Heimdall.App.ViewModels;

namespace Heimdall.App;

/// <summary>
/// Partial class containing all UI localization methods for <see cref="MainWindow"/>.
/// Owns the <c>Apply*Localization</c> family that pushes localized strings onto
/// named XAML elements at startup and whenever the active locale changes, along
/// with the helpers that exist solely to feed those methods (credential
/// provider presets, external tool placeholder list, external tool preview).
/// </summary>
public partial class MainWindow
{
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

        TabSessions.Content = vm.Localize("NavTabSessions");
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
        Mw_StatusBarServersLabel.Text = " " + vm.Localize("StatusBarSessions") + " " + vm.Localize("StatusBarSeparator");
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

        Mw_AddMenuSession.Header = vm.Localize("AddMenuSession");
        Mw_AddMenuTool.Header = vm.Localize("AddMenuTool");
        Mw_AddMenuGateway.Header = vm.Localize("AddMenuGateway");
        Mw_AddMenuFolder.Header = vm.Localize("AddMenuFolder");

        Mw_ShareFolderLabel.Text = vm.Localize(_fileShareService.IsSharing ? "ToolsStopSharing" : "ToolsShareFolder");
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
        Mw_EmptyBtnAddSession.Content = vm.Localize("EmptyStateBtnAddSession");
        Mw_EmptyBtnImport.Content = vm.Localize("EmptyStateBtnImport");
        Mw_EmptyBtnImport.ToolTip = vm.Localize("TooltipImport");
        Mw_EmptyBtnExploreTools.Content = vm.Localize("EmptyStateBtnExploreTools");
        Mw_EmptySelectSession.Text = vm.Localize("EmptyStateSelectSession");
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
        System.Windows.Automation.AutomationProperties.SetName(TabSessions, vm.Localize("NavTabSessions"));
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
        System.Windows.Automation.AutomationProperties.SetName(Mw_EmptyBtnAddSession, vm.Localize("AccessEmptyAddServer"));
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

    private void PopulateCredProvPresets(MainViewModel vm)
    {
        Mw_SettingsCredProvPreset.Items.Clear();
        foreach (var (label, _) in CredProvPresets)
        {
            Mw_SettingsCredProvPreset.Items.Add(label);
        }
        Mw_SettingsCredProvPreset.SelectedIndex = 0;
    }

    private void PopulateExtToolPlaceholderList(MainViewModel vm)
    {
        Mw_ExtToolPlaceholderList.Items.Clear();
        foreach (var (variable, descKey) in Core.Configuration.ExternalToolDefinition.SupportedPlaceholders)
        {
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 2) };
            var variableLabel = new TextBlock
            {
                Text = variable,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = (double)FindResource("FontSizeCaption"),
                VerticalAlignment = VerticalAlignment.Center
            };
            variableLabel.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            panel.Children.Add(variableLabel);

            var descLabel = new TextBlock
            {
                Text = $" \u2014 {vm.Localize(descKey)}",
                FontSize = (double)FindResource("FontSizeCaption"),
                Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center
            };
            descLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(descLabel);

            Mw_ExtToolPlaceholderList.Items.Add(panel);
        }
    }
}
