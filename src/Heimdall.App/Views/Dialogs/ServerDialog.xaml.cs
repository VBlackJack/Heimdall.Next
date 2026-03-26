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
using Heimdall.App.Theming;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Microsoft.Win32;

namespace Heimdall.App.Views.Dialogs;

/// <summary>
/// Server add/edit dialog. Code-behind is limited to PasswordBox handling,
/// file browse, DialogResult assignment, localization, and advanced mode persistence.
/// </summary>
public partial class ServerDialog : Window
{
    private readonly LocalizationManager? _localizer;
    private readonly ConfigManager? _configManager;

    public ServerDialog(LocalizationManager localizer, ConfigManager configManager)
    {
        _localizer = localizer;
        _configManager = configManager;
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
        LoadAdvancedModePreference();
        ApplyLocalization();
        RegisterTabFallback();
    }

    public ServerDialog(LocalizationManager localizer)
    {
        _localizer = localizer;
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
        ApplyLocalization();
        RegisterTabFallback();
    }

    public ServerDialog()
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
        RegisterTabFallback();
    }

    // ------------------------------------------------------------------
    // Advanced mode persistence
    // ------------------------------------------------------------------

    private void LoadAdvancedModePreference()
    {
        if (_configManager is null || DataContext is not ServerDialogViewModel vm)
        {
            Loaded += OnLoadedApplyAdvancedMode;
            return;
        }

        var settings = _configManager.LoadSettingsAsync().GetAwaiter().GetResult();
        vm.IsAdvancedMode = settings.ServerDialogAdvancedMode;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoadedApplyAdvancedMode(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedApplyAdvancedMode;

        if (_configManager is null || DataContext is not ServerDialogViewModel vm) return;

        var settings = _configManager.LoadSettingsAsync().GetAwaiter().GetResult();
        vm.IsAdvancedMode = settings.ServerDialogAdvancedMode;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ServerDialogViewModel.IsAdvancedMode)) return;
        if (_configManager is null || DataContext is not ServerDialogViewModel vm) return;

        _ = _configManager.MergeSettingAsync(s => s.ServerDialogAdvancedMode = vm.IsAdvancedMode);
    }

    // ------------------------------------------------------------------
    // Tab fallback
    // ------------------------------------------------------------------

    private void RegisterTabFallback()
    {
        DlgSrv_TabTunneling.IsEnabledChanged += OnTabEnabledChanged;
        DlgSrv_TabAuthentication.IsEnabledChanged += OnTabEnabledChanged;
    }

    private void OnTabEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TabItem tab) return;

        if (e.NewValue is false)
        {
            tab.ToolTip = _localizer?["ServerDialogTabDisabledHint"];
            if (tab.IsSelected)
            {
                MainTabControl.SelectedItem = DlgSrv_TabConnection;
            }
        }
        else
        {
            tab.ToolTip = null;
        }
    }

    // ------------------------------------------------------------------
    // Save
    // ------------------------------------------------------------------

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ServerDialogViewModel vm)
        {
            return;
        }

        // Block save while still on the protocol selection step
        if (!vm.ShowFormFields)
        {
            return;
        }

        // Recompose performance flags bitmask from individual checkboxes (TS_PERF_* constants)
        var flags = 0;
        if (DlgSrv_PerfDisableWallpaperCb.IsChecked == true) flags |= 0x01;
        if (DlgSrv_PerfDisableDragCb.IsChecked == true) flags |= 0x02;
        if (DlgSrv_PerfDisableAnimationsCb.IsChecked == true) flags |= 0x04;
        if (DlgSrv_PerfDisableThemesCb.IsChecked == true) flags |= 0x08;
        if (DlgSrv_PerfDisableCursorShadowCb.IsChecked == true) flags |= 0x20;
        if (DlgSrv_PerfEnableFontSmoothingCb.IsChecked == true) flags |= 0x80;
        if (DlgSrv_PerfEnableCompositionCb.IsChecked == true) flags |= 0x100;
        vm.RdpPerformanceFlags = flags;

        // Transfer PasswordBox values if they exist in the visual tree
        var sshPwBox = FindName("SshPasswordBox") as System.Windows.Controls.PasswordBox;
        var rdpPwBox = FindName("RdpPasswordBox") as System.Windows.Controls.PasswordBox;
        var vncPwBox = FindName("VncPasswordBox") as System.Windows.Controls.PasswordBox;
        var ftpPwBox = FindName("FtpPasswordBox") as System.Windows.Controls.PasswordBox;
        var telnetPwBox = FindName("TelnetPasswordBox") as System.Windows.Controls.PasswordBox;

        if (sshPwBox is not null) vm.SshPassword = sshPwBox.Password;
        if (rdpPwBox is not null) vm.RdpPassword = rdpPwBox.Password;
        if (vncPwBox is not null) vm.VncPassword = vncPwBox.Password;
        if (ftpPwBox is not null) vm.FtpPassword = ftpPwBox.Password;
        if (telnetPwBox is not null) vm.TelnetPassword = telnetPwBox.Password;

        vm.ValidateCommand.Execute(null);

        if (vm.ValidationError is null)
        {
            DialogResult = true;

            // Clear passwords from UI memory (CWE-316)
            sshPwBox?.Clear();
            rdpPwBox?.Clear();
            vncPwBox?.Clear();
            ftpPwBox?.Clear();
            telnetPwBox?.Clear();
        }
        else
        {
            FocusFirstInvalidField(vm);
        }
    }

    private void FocusFirstInvalidField(ServerDialogViewModel vm)
    {
        if (vm.FirstInvalidField is null) return;

        System.Windows.UIElement? target = null;

        // Temporarily suppress advanced-mode persistence during focus management
        vm.PropertyChanged -= OnViewModelPropertyChanged;

        switch (vm.FirstInvalidField)
        {
            case nameof(ServerDialogViewModel.DisplayName):
                target = DlgSrv_DisplayNameBox;
                break;
            case nameof(ServerDialogViewModel.RemoteServer):
                target = DlgSrv_RemoteServerBox;
                break;
            case "EndpointPort":
                target = DlgSrv_EndpointPortBox;
                break;
            case nameof(ServerDialogViewModel.LocalPort):
                vm.IsAdvancedMode = true;
                MainTabControl.SelectedItem = DlgSrv_TabTunneling;
                target = DlgSrv_LocalPortBox;
                break;
            case nameof(ServerDialogViewModel.RdpAudioMode):
                vm.IsAdvancedMode = true;
                MainTabControl.SelectedItem = DlgSrv_TabOptions;
                target = DlgSrv_RdpAudioModeCombo;
                break;
            case nameof(ServerDialogViewModel.RdpColorDepth):
                vm.IsAdvancedMode = true;
                MainTabControl.SelectedItem = DlgSrv_TabOptions;
                target = DlgSrv_RdpColorDepthCombo;
                break;
        }

        vm.PropertyChanged += OnViewModelPropertyChanged;

        if (target is not null)
        {
            // Deferred focus: let layout complete after tab/panel changes
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => target.Focus()));
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Dirty guard: warn when closing with unsaved changes (skip when Save was clicked)
        if (DialogResult != true
            && DataContext is ServerDialogViewModel { IsDirty: true } dirtyVm)
        {
            var title = dirtyVm.Localizer?["DialogUnsavedWarningTitle"]
                ?? _localizer?["DialogUnsavedWarningTitle"]
                ?? "Unsaved Changes";
            var message = dirtyVm.Localizer?["DialogUnsavedWarning"]
                ?? _localizer?["DialogUnsavedWarning"]
                ?? "You have unsaved changes. Discard them and close?";
            var yes = dirtyVm.Localizer?["BtnYes"] ?? _localizer?["BtnYes"] ?? "Yes";
            var no = dirtyVm.Localizer?["BtnNo"] ?? _localizer?["BtnNo"] ?? "No";

            var discard = MessageDialog.ShowConfirm(this, title, message, "warning", yes, no);
            if (!discard)
            {
                e.Cancel = true;
                return;
            }
        }

        // Detach property changed handler
        if (DataContext is ServerDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Clear all password boxes from UI memory on close (CWE-316)
        (FindName("SshPasswordBox") as System.Windows.Controls.PasswordBox)?.Clear();
        (FindName("RdpPasswordBox") as System.Windows.Controls.PasswordBox)?.Clear();
        (FindName("VncPasswordBox") as System.Windows.Controls.PasswordBox)?.Clear();
        (FindName("FtpPasswordBox") as System.Windows.Controls.PasswordBox)?.Clear();
        (FindName("TelnetPasswordBox") as System.Windows.Controls.PasswordBox)?.Clear();
    }

    private void OnBrowseSshKeyClick(object sender, RoutedEventArgs e)
    {
        if (_localizer is null)
        {
            ShowBrowseKeyDialog("Select SSH Key",
                "PPK Files (*.ppk)|*.ppk|PEM Files (*.pem)|*.pem|All Files (*.*)|*.*");
            return;
        }

        ShowBrowseKeyDialog(
            _localizer["ServerDialogBrowseKeyTitle"],
            _localizer["ServerDialogBrowseKeyFilter"]);
    }

    private void ShowBrowseKeyDialog(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true && DataContext is ServerDialogViewModel vm)
        {
            vm.SshKeyPath = dialog.FileName;
        }
    }

    // ------------------------------------------------------------------
    // Localization
    // ------------------------------------------------------------------

    private void ApplyLocalization()
    {
        if (_localizer is null)
        {
            return;
        }

        // Tab headers
        DlgSrv_TabConnection.Header = _localizer["ServerDialogTabConnection"];
        DlgSrv_TabTunnelingText.Text = _localizer["ServerDialogTabTunneling"];
        DlgSrv_TabAuthentication.Header = _localizer["ServerDialogTabAuthentication"];
        DlgSrv_TabOptionsText.Text = _localizer["ServerDialogTabOptions"];
        DlgSrv_TabInfo.Header = _localizer["ServerDialogTabInfo"];

        // Protocol selector (Step 1)
        DlgSrv_ProtocolSelectorTitle.Text = _localizer["ServerDialogProtocolSelectorTitle"];
        DlgSrv_ProtocolSelectorDesc.Text = _localizer["ServerDialogProtocolSelectorDesc"];
        DlgSrv_ProtoRdpName.Text = _localizer["ServerDialogProtocolRdpName"];
        DlgSrv_ProtoRdpDesc.Text = _localizer["ServerDialogProtocolRdpDesc"];
        DlgSrv_ProtoSshName.Text = _localizer["ServerDialogProtocolSshName"];
        DlgSrv_ProtoSshDesc.Text = _localizer["ServerDialogProtocolSshDesc"];
        DlgSrv_ProtoSftpName.Text = _localizer["ServerDialogProtocolSftpName"];
        DlgSrv_ProtoSftpDesc.Text = _localizer["ServerDialogProtocolSftpDesc"];
        DlgSrv_ProtoVncName.Text = _localizer["ServerDialogProtocolVncName"];
        DlgSrv_ProtoVncDesc.Text = _localizer["ServerDialogProtocolVncDesc"];
        DlgSrv_ProtoTelnetName.Text = _localizer["ServerDialogProtocolTelnetName"];
        DlgSrv_ProtoTelnetDesc.Text = _localizer["ServerDialogProtocolTelnetDesc"];
        DlgSrv_ProtoFtpName.Text = _localizer["ServerDialogProtocolFtpName"];
        DlgSrv_ProtoFtpDesc.Text = _localizer["ServerDialogProtocolFtpDesc"];
        DlgSrv_ProtoCitrixName.Text = _localizer["ServerDialogProtocolCitrixName"];
        DlgSrv_ProtoCitrixDesc.Text = _localizer["ServerDialogProtocolCitrixDesc"];
        DlgSrv_ProtoLocalName.Text = _localizer["ServerDialogProtocolLocalName"];
        DlgSrv_ProtoLocalDesc.Text = _localizer["ServerDialogProtocolLocalDesc"];

        // Protocol badge (edit mode)
        DlgSrv_ProtocolBadgeLabel.Text = _localizer["ServerDialogProtocolBadge"];

        // Back button (add mode)
        DlgSrv_BackBtn.Content = _localizer["ServerDialogBtnBack"];

        // Connection basics (essential section)
        DlgSrv_ConnectionBasicsTitle.Text = _localizer["ServerDialogConnectionBasics"];
        DlgSrv_ConnectionBasicsDesc.Text = _localizer["ServerDialogConnectionBasicsDesc"];
        DlgSrv_DisplayNameLabel.Text = _localizer["ServerDialogLabelDisplayName"] + " *";
        DlgSrv_ServerLabel.Text = _localizer["ServerDialogLabelServer"] + " *";

        // Project + Gateway routing (essential section)
        DlgSrv_ProjectLabel.Text = _localizer["ServerDialogLabelProject"];
        DlgSrv_GatewayRoutingTitle.Text = _localizer["ServerDialogGatewayRouting"];
        DlgSrv_GatewayRoutingDesc.Text = _localizer["ServerDialogGatewayRoutingDesc"];
        DlgSrv_DirectConnectCb.Content = _localizer["ServerDialogDirectConnect"];

        // Advanced toggle
        DlgSrv_AdvancedToggle.Content = _localizer["ServerDialogAdvancedSettings"];
        System.Windows.Automation.AutomationProperties.SetName(
            DlgSrv_AdvancedToggle, _localizer["ServerDialogAdvancedSettings"]);

        // Connection path diagram
        DlgSrv_ConnectionPathTitle.Text = _localizer["ServerDialogConnectionPath"];
        DlgSrv_DirectYourPc.Text = _localizer["ServerDialogNodeYourPc"];
        DlgSrv_DirectDestination.Text = _localizer["ServerDialogNodeDestination"];
        DlgSrv_GatewayYourPc.Text = _localizer["ServerDialogNodeYourPc"];
        DlgSrv_GatewayNode.Text = _localizer["ServerDialogNodeGateway"];
        DlgSrv_GatewayDestination.Text = _localizer["ServerDialogNodeDestination"];

        // Tunneling tab
        DlgSrv_TunnelingTitle.Text = _localizer["ServerDialogTunnelingTitle"];
        DlgSrv_TunnelingDesc.Text = _localizer["ServerDialogTunnelingDesc"];
        DlgSrv_TunnelingHint.Text = _localizer["ServerDialogTunnelingHint"];
        DlgSrv_DirectConnTitle.Text = _localizer["ServerDialogDirectConnectionTitle"];
        DlgSrv_DirectConnDesc.Text = _localizer["ServerDialogDirectConnectionDesc"];
        DlgSrv_LocalTunnelPortTitle.Text = _localizer["ServerDialogLocalTunnelPort"];
        DlgSrv_LocalTunnelPortDesc.Text = _localizer["ServerDialogLocalTunnelPortDesc"];
        DlgSrv_AutoTunnelPortCb.Content = _localizer["ServerDialogAutoTunnelPort"];
        DlgSrv_ManualPortLabel.Text = _localizer["ServerDialogManualLocalPort"];
        DlgSrv_TunnelMappingTitle.Text = _localizer["ServerDialogTunnelMapping"];
        DlgSrv_ChainLabel.Text = _localizer["ServerDialogTunnelLabelChain"];
        DlgSrv_LocalEndpointLabel.Text = _localizer["ServerDialogTunnelLabelLocal"];
        DlgSrv_RemoteDestLabel.Text = _localizer["ServerDialogTunnelLabelRemote"];

        // Basic authentication section (always visible, per-protocol)
        DlgSrv_BasicRdpCredentialsTitle.Text = _localizer["ServerDialogRdpCredentials"];
        DlgSrv_BasicRdpCredentialsDesc.Text = _localizer["ServerDialogRdpCredentialsDesc"];
        DlgSrv_BasicRdpUsernameLabel.Text = _localizer["ServerDialogLabelUsername"];
        DlgSrv_BasicRdpPasswordLabel.Text = _localizer["ServerDialogLabelPassword"];
        DlgSrv_BasicSshCredentialsTitle.Text = _localizer["ServerDialogSshCredentials"];
        DlgSrv_BasicSshCredentialsDesc.Text = _localizer["ServerDialogSshCredentialsDesc"];
        DlgSrv_BasicSshUsernameLabel.Text = _localizer["ServerDialogLabelUsername"];
        DlgSrv_BasicSshKeyLabel.Text = _localizer["ServerDialogLabelSshKey"];
        DlgSrv_BasicBrowseBtn.Content = _localizer["ServerDialogBtnBrowse"];
        DlgSrv_BasicPassphraseLabel.Text = _localizer["ServerDialogLabelPassphrase"];
        DlgSrv_BasicSshAuthHint.Text = _localizer["ServerDialogSshAuthHint"];
        DlgSrv_BasicVncCredentialsTitle.Text = _localizer["ServerDialogVncCredentials"];
        DlgSrv_BasicVncCredentialsDesc.Text = _localizer["ServerDialogVncCredentialsDesc"];
        DlgSrv_BasicVncPasswordLabel.Text = _localizer["ServerDialogVncPassword"];
        DlgSrv_BasicFtpCredentialsTitle.Text = _localizer["ServerDialogFtpCredentials"];
        DlgSrv_BasicFtpCredentialsDesc.Text = _localizer["ServerDialogFtpCredentialsDesc"];
        DlgSrv_BasicFtpUsernameLabel.Text = _localizer["ServerDialogFtpUsername"];
        DlgSrv_BasicFtpPasswordLabel.Text = _localizer["ServerDialogFtpPassword"];
        DlgSrv_BasicTelnetCredentialsTitle.Text = _localizer["ServerDialogTelnetCredentials"];
        DlgSrv_BasicTelnetCredentialsDesc.Text = _localizer["ServerDialogTelnetCredentialsDesc"];
        DlgSrv_BasicTelnetUsernameLabel.Text = _localizer["ServerDialogLabelUsername"];
        DlgSrv_BasicTelnetPasswordLabel.Text = _localizer["ServerDialogLabelPassword"];
        DlgSrv_BasicLocalShellTitle.Text = _localizer["ServerDialogLocalShell"];
        DlgSrv_BasicLocalShellDesc.Text = _localizer["ServerDialogLocalShellDesc"];
        DlgSrv_BasicExecutableLabel.Text = _localizer["ServerDialogLabelExecutable"];
        DlgSrv_BasicShellPowershell.Content = _localizer["LocalShellPowershell"];
        DlgSrv_BasicShellPwsh.Content = _localizer["LocalShellPwsh"];
        DlgSrv_BasicShellCmd.Content = _localizer["LocalShellCmd"];
        DlgSrv_BasicShellBash.Content = _localizer["LocalShellBash"];
        DlgSrv_BasicShellWsl.Content = _localizer["LocalShellWsl"];
        DlgSrv_BasicArgumentsLabel.Text = _localizer["ServerDialogLabelArguments"];
        DlgSrv_BasicCitrixTitle.Text = _localizer["ServerDialogCitrixWorkspace"];
        DlgSrv_BasicCitrixDesc.Text = _localizer["ServerDialogCitrixWorkspaceDesc"];
        DlgSrv_BasicStoreFrontLabel.Text = _localizer["ServerDialogLabelStoreFrontUrl"];
        DlgSrv_BasicAppNameLabel.Text = _localizer["ServerDialogLabelAppName"];

        // Gateway authentication tab (advanced, only when gateway is active)
        DlgSrv_GatewayAuthTitle.Text = _localizer["ServerDialogGatewayAuth"];
        DlgSrv_GatewayAuthDesc.Text = _localizer["ServerDialogGatewayAuthDesc"];

        // RDP options
        DlgSrv_RdpOptionsTitle.Text = _localizer["ServerDialogRdpOptions"];
        DlgSrv_RdpOptionsDesc.Text = _localizer["ServerDialogRdpOptionsDesc"];
        DlgSrv_SessionModeLabel.Text = _localizer["ServerDialogLabelSessionMode"];
        DlgSrv_RdpModeEmbedded.Content = _localizer["ServerDialogModeEmbedded"];
        DlgSrv_RdpModeExternal.Content = _localizer["ServerDialogModeExternal"];
        DlgSrv_AspectStretch.Content = _localizer["ServerDialogAspectStretch"];
        DlgSrv_AspectPreserve.Content = _localizer["ServerDialogAspectPreserve"];
        DlgSrv_AspectRatioLabel.Text = _localizer["ServerDialogLabelAspectRatio"];
        DlgSrv_AudioModeLabel.Text = _localizer["ServerDialogLabelAudioMode"];
        DlgSrv_ColorDepthLabel.Text = _localizer["ServerDialogLabelColorDepth"];

        // Audio mode ComboBoxItems (Tag-based, Content is display text)
        DlgSrv_AudioDisabled.Content = _localizer["ServerDialogAudioDisabled"];
        DlgSrv_AudioLocal.Content = _localizer["ServerDialogAudioLocal"];
        DlgSrv_AudioRemote.Content = _localizer["ServerDialogAudioRemote"];

        // Color depth ComboBoxItems (Tag-based, Content is display text)
        DlgSrv_Color16.Content = _localizer["ServerDialogColor16"];
        DlgSrv_Color24.Content = _localizer["ServerDialogColor24"];
        DlgSrv_Color32.Content = _localizer["ServerDialogColor32"];

        // RDP checkboxes
        DlgSrv_NlaCb.Content = _localizer["ServerDialogNla"];
        DlgSrv_MultiMonitorCb.Content = _localizer["ServerDialogMultiMonitor"];
        DlgSrv_DynamicResCb.Content = _localizer["ServerDialogDynamicResolution"];
        DlgSrv_AudioCaptureCb.Content = _localizer["ServerDialogAudioCapture"];

        // Device redirection
        DlgSrv_DeviceRedirExpander.Header = _localizer["ServerDialogDeviceRedirection"];
        DlgSrv_RedirClipboardCb.Content = _localizer["ServerDialogRedirectClipboard"];
        DlgSrv_RedirDrivesCb.Content = _localizer["ServerDialogRedirectDrives"];
        DlgSrv_RedirPrintersCb.Content = _localizer["ServerDialogRedirectPrinters"];
        DlgSrv_RedirComPortsCb.Content = _localizer["ServerDialogRedirectComPorts"];
        DlgSrv_RedirSmartCardsCb.Content = _localizer["ServerDialogRedirectSmartCards"];
        DlgSrv_RedirWebcamCb.Content = _localizer["ServerDialogRedirectWebcam"];
        DlgSrv_RedirUsbCb.Content = _localizer["ServerDialogRedirectUsb"];

        // Advanced RDP behavior
        DlgSrv_AdvancedRdpExpander.Header = _localizer["ServerDialogAdvancedRdp"];
        DlgSrv_GlobalDefaultsCb.Content = _localizer["ServerDialogGlobalDefaults"];
        DlgSrv_AntiIdleCb.Content = _localizer["ServerDialogAntiIdle"];
        DlgSrv_BitmapCacheCb.Content = _localizer["ServerDialogBitmapCaching"];
        DlgSrv_RdpCompressionCb.Content = _localizer["ServerDialogRdpCompressionCb"];
        DlgSrv_AutoReconnectCb.Content = _localizer["ServerDialogAutoReconnect"];

        // RDP Experience
        DlgSrv_RdpExperienceExpander.Header = _localizer["RdpPerformanceTitle"];
        DlgSrv_PerfDisableWallpaperCb.Content = _localizer["RdpPerfDisableWallpaper"];
        DlgSrv_PerfDisableThemesCb.Content = _localizer["RdpPerfDisableThemes"];
        DlgSrv_PerfDisableAnimationsCb.Content = _localizer["RdpPerfDisableAnimations"];
        DlgSrv_PerfDisableDragCb.Content = _localizer["RdpPerfDisableDrag"];
        DlgSrv_PerfDisableCursorShadowCb.Content = _localizer["RdpPerfDisableCursorShadow"];
        DlgSrv_PerfEnableFontSmoothingCb.Content = _localizer["RdpPerfEnableFontSmoothing"];
        DlgSrv_PerfEnableCompositionCb.Content = _localizer["RdpPerfEnableComposition"];
        DlgSrv_DisableUdpCb.Content = _localizer["RdpDisableUdp"];

        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_PerfDisableWallpaperCb, _localizer["RdpPerfDisableWallpaper"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_PerfDisableThemesCb, _localizer["RdpPerfDisableThemes"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_PerfDisableAnimationsCb, _localizer["RdpPerfDisableAnimations"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_PerfDisableDragCb, _localizer["RdpPerfDisableDrag"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_PerfDisableCursorShadowCb, _localizer["RdpPerfDisableCursorShadow"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_PerfEnableFontSmoothingCb, _localizer["RdpPerfEnableFontSmoothing"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_PerfEnableCompositionCb, _localizer["RdpPerfEnableComposition"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_DisableUdpCb, _localizer["RdpDisableUdp"]);

        // Decompose bitmask into individual checkboxes
        if (DataContext is ViewModels.Dialogs.ServerDialogViewModel vm)
        {
            DlgSrv_PerfDisableWallpaperCb.IsChecked = (vm.RdpPerformanceFlags & 0x01) != 0;
            DlgSrv_PerfDisableDragCb.IsChecked = (vm.RdpPerformanceFlags & 0x02) != 0;
            DlgSrv_PerfDisableAnimationsCb.IsChecked = (vm.RdpPerformanceFlags & 0x04) != 0;
            DlgSrv_PerfDisableThemesCb.IsChecked = (vm.RdpPerformanceFlags & 0x08) != 0;
            DlgSrv_PerfDisableCursorShadowCb.IsChecked = (vm.RdpPerformanceFlags & 0x20) != 0;
            DlgSrv_PerfEnableFontSmoothingCb.IsChecked = (vm.RdpPerformanceFlags & 0x80) != 0;
            DlgSrv_PerfEnableCompositionCb.IsChecked = (vm.RdpPerformanceFlags & 0x100) != 0;
        }

        // SSH options
        DlgSrv_SshOptionsTitle.Text = _localizer["ServerDialogSshOptions"];
        DlgSrv_SshOptionsDesc.Text = _localizer["ServerDialogSshOptionsDesc"];
        DlgSrv_SshModeLabel.Text = _localizer["ServerDialogLabelSshMode"];
        DlgSrv_SshModeEmbedded.Content = _localizer["ServerDialogModeEmbedded"];
        DlgSrv_SshModeExternal.Content = _localizer["ServerDialogModeExternal"];
        DlgSrv_SshCompressionCb.Content = _localizer["ServerDialogSshCompression"];
        DlgSrv_SshAgentFwdCb.Content = _localizer["ServerDialogSshAgentForward"];
        DlgSrv_SshX11Cb.Content = _localizer["ServerDialogSshX11"];

        // Local shell advanced options
        DlgSrv_LocalShellTitle.Text = _localizer["ServerDialogLocalShellAdvanced"];
        DlgSrv_LocalShellDesc.Text = _localizer["ServerDialogLocalShellAdvancedDesc"];
        DlgSrv_WorkingDirLabel.Text = _localizer["ServerDialogLabelWorkingDir"];
        DlgSrv_ElevationModeLabel.Text = _localizer["ServerDialogElevationMode"];
        PopulateElevationModeCombo();

        // Citrix advanced options
        DlgSrv_CitrixTitle.Text = _localizer["ServerDialogCitrixAdvanced"];
        DlgSrv_CitrixDesc.Text = _localizer["ServerDialogCitrixAdvancedDesc"];
        DlgSrv_IcaFileLabel.Text = _localizer["ServerDialogLabelIcaFilePath"];
        DlgSrv_CitrixHint.Text = _localizer["ServerDialogCitrixHint"];
        DlgSrv_SeamlessCb.Content = _localizer["ServerDialogCitrixSeamless"];
        DlgSrv_SsoCb.Content = _localizer["ServerDialogCitrixSso"];

        // (FTP, VNC, Telnet credentials moved to basic auth section above)

        // FTP options
        DlgSrv_FtpOptionsTitle.Text = _localizer["ServerDialogFtpOptions"];
        DlgSrv_FtpOptionsDesc.Text = _localizer["ServerDialogFtpOptionsDesc"];
        DlgSrv_FtpPassiveCb.Content = _localizer["ServerDialogFtpPassiveMode"];
        DlgSrv_FtpSslCb.Content = _localizer["ServerDialogFtpSsl"];

        // VNC options
        DlgSrv_VncOptionsTitle.Text = _localizer["ServerDialogVncOptions"];
        DlgSrv_VncOptionsDesc.Text = _localizer["ServerDialogVncOptionsDesc"];
        DlgSrv_VncViewOnlyCb.Content = _localizer["ServerDialogVncViewOnly"];

        // Telnet options
        DlgSrv_TelnetOptionsTitle.Text = _localizer["ServerDialogTelnetOptions"];
        DlgSrv_TelnetOptionsDesc.Text = _localizer["ServerDialogTelnetOptionsDesc"];
        DlgSrv_TelnetSecurityWarning.Text = _localizer["ServerDialogTelnetSecurityWarning"];

        // Organization
        DlgSrv_OrgTitle.Text = _localizer["ServerDialogOrganization"];
        DlgSrv_OrgDesc.Text = _localizer["ServerDialogOrganizationDesc"];
        DlgSrv_FolderLabel.Text = _localizer["ServerDialogLabelFolder"];
        DlgSrv_EnvironmentLabel.Text = _localizer["ServerDialogLabelEnvironment"];
        DlgSrv_FavoriteLabel.Text = _localizer["ServerDialogLabelFavorite"];
        DlgSrv_MarkFavoriteCb.Content = _localizer["ServerDialogMarkFavorite"];
        DlgSrv_EnvNone.Content = _localizer["ServerDialogEnvNone"];
        DlgSrv_EnvProduction.Content = _localizer["ServerDialogEnvProduction"];
        DlgSrv_EnvStaging.Content = _localizer["ServerDialogEnvStaging"];
        DlgSrv_EnvLab.Content = _localizer["ServerDialogEnvLab"];
        DlgSrv_EnvPersonal.Content = _localizer["ServerDialogEnvPersonal"];

        // Metadata
        DlgSrv_MetadataTitle.Text = _localizer["ServerDialogMetadata"];
        DlgSrv_MetadataDesc.Text = _localizer["ServerDialogMetadataDesc"];
        DlgSrv_TagsLabel.Text = _localizer["ServerDialogLabelTags"];
        DlgSrv_MacAddressLabel.Text = _localizer["ServerDialogLabelMacAddress"];

        // Action buttons
        DlgSrv_CancelBtn.Content = _localizer["ServerDialogBtnCancel"];
        DlgSrv_SaveBtn.Content = _localizer["ServerDialogBtnSave"];
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_BackBtn, _localizer["ServerDialogBtnBack"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_CancelBtn, _localizer["ServerDialogBtnCancel"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_SaveBtn, _localizer["ServerDialogBtnSave"]);

        // Accessibility: protocol card automation names
        System.Windows.Automation.AutomationProperties.SetName(ProtocolCard_Rdp, _localizer["ServerDialogProtocolRdpName"]);
        System.Windows.Automation.AutomationProperties.SetName(ProtocolCard_Ssh, _localizer["ServerDialogProtocolSshName"]);
        System.Windows.Automation.AutomationProperties.SetName(ProtocolCard_Sftp, _localizer["ServerDialogProtocolSftpName"]);
        System.Windows.Automation.AutomationProperties.SetName(ProtocolCard_Vnc, _localizer["ServerDialogProtocolVncName"]);
        System.Windows.Automation.AutomationProperties.SetName(ProtocolCard_Telnet, _localizer["ServerDialogProtocolTelnetName"]);
        System.Windows.Automation.AutomationProperties.SetName(ProtocolCard_Ftp, _localizer["ServerDialogProtocolFtpName"]);
        System.Windows.Automation.AutomationProperties.SetName(ProtocolCard_Citrix, _localizer["ServerDialogProtocolCitrixName"]);
        System.Windows.Automation.AutomationProperties.SetName(ProtocolCard_Local, _localizer["ServerDialogProtocolLocalName"]);

        // Accessibility: automation names for PasswordBox controls (basic auth section)
        System.Windows.Automation.AutomationProperties.SetName(RdpPasswordBox, _localizer["ServerDialogLabelPassword"]);
        System.Windows.Automation.AutomationProperties.SetName(SshPasswordBox, _localizer["ServerDialogLabelPassphrase"]);
        System.Windows.Automation.AutomationProperties.SetName(FtpPasswordBox, _localizer["ServerDialogFtpPassword"]);
        System.Windows.Automation.AutomationProperties.SetName(VncPasswordBox, _localizer["ServerDialogVncPassword"]);
        System.Windows.Automation.AutomationProperties.SetName(TelnetPasswordBox, _localizer["ServerDialogLabelPassword"]);

        // Accessibility: automation names for basic auth browse button
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_BasicBrowseBtn, _localizer["ServerDialogBtnBrowse"]);
    }

    private void PopulateElevationModeCombo()
    {
        if (DataContext is not ServerDialogViewModel vm || _localizer is null) return;

        DlgSrv_ElevationModeCmb.Items.Clear();
        DlgSrv_ElevationModeCmb.Items.Add(new System.Windows.Controls.ComboBoxItem
        {
            Content = _localizer["ElevationModeNone"],
            Tag = Core.Models.ElevationMode.None
        });
        DlgSrv_ElevationModeCmb.Items.Add(new System.Windows.Controls.ComboBoxItem
        {
            Content = _localizer["ElevationModeAuto"],
            Tag = Core.Models.ElevationMode.Auto
        });
        DlgSrv_ElevationModeCmb.Items.Add(new System.Windows.Controls.ComboBoxItem
        {
            Content = _localizer["ElevationModeGsudo"],
            Tag = Core.Models.ElevationMode.Gsudo
        });
        DlgSrv_ElevationModeCmb.Items.Add(new System.Windows.Controls.ComboBoxItem
        {
            Content = _localizer["ElevationModeRunas"],
            Tag = Core.Models.ElevationMode.Runas
        });

        // Select current value
        for (int i = 0; i < DlgSrv_ElevationModeCmb.Items.Count; i++)
        {
            if (DlgSrv_ElevationModeCmb.Items[i] is System.Windows.Controls.ComboBoxItem item
                && item.Tag is Core.Models.ElevationMode mode && mode == vm.ElevationMode)
            {
                DlgSrv_ElevationModeCmb.SelectedIndex = i;
                break;
            }
        }

        DlgSrv_ElevationModeCmb.SelectionChanged += (_, _) =>
        {
            if (DlgSrv_ElevationModeCmb.SelectedItem is System.Windows.Controls.ComboBoxItem selected
                && selected.Tag is Core.Models.ElevationMode selectedMode)
            {
                vm.ElevationMode = selectedMode;
            }
        };

        System.Windows.Automation.AutomationProperties.SetName(
            DlgSrv_ElevationModeCmb, _localizer["ServerDialogElevationMode"]);
    }
}
