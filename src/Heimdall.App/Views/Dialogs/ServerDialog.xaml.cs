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
using Heimdall.Core.Localization;
using Microsoft.Win32;

namespace Heimdall.App.Views.Dialogs;

/// <summary>
/// Server add/edit dialog. Code-behind is limited to PasswordBox handling,
/// file browse, DialogResult assignment, and localization.
/// </summary>
public partial class ServerDialog : Window
{
    private readonly LocalizationManager? _localizer;

    public ServerDialog(LocalizationManager localizer)
    {
        _localizer = localizer;
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
        ApplyLocalization();
    }

    public ServerDialog()
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ServerDialogViewModel vm)
        {
            return;
        }

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
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
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
        DlgSrv_TabTunneling.Header = _localizer["ServerDialogTabTunneling"];
        DlgSrv_TabAuthentication.Header = _localizer["ServerDialogTabAuthentication"];
        DlgSrv_TabOptions.Header = _localizer["ServerDialogTabOptions"];
        DlgSrv_TabInfo.Header = _localizer["ServerDialogTabInfo"];

        // Connection basics
        DlgSrv_ConnectionBasicsTitle.Text = _localizer["ServerDialogConnectionBasics"];
        DlgSrv_ConnectionBasicsDesc.Text = _localizer["ServerDialogConnectionBasicsDesc"];
        DlgSrv_DisplayNameLabel.Text = _localizer["ServerDialogLabelDisplayName"] + " *";
        DlgSrv_ConnectionTypeLabel.Text = _localizer["ServerDialogLabelConnectionType"] + " *";
        DlgSrv_ServerLabel.Text = _localizer["ServerDialogLabelServer"] + " *";

        // Gateway routing
        DlgSrv_GatewayRoutingTitle.Text = _localizer["ServerDialogGatewayRouting"];
        DlgSrv_GatewayRoutingDesc.Text = _localizer["ServerDialogGatewayRoutingDesc"];
        DlgSrv_DirectConnectCb.Content = _localizer["ServerDialogDirectConnect"];
        DlgSrv_TestConnectionBtn.Content = _localizer["ServerDialogTestConnection"];

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

        // Authentication tab
        DlgSrv_RdpCredentialsTitle.Text = _localizer["ServerDialogRdpCredentials"];
        DlgSrv_RdpCredentialsDesc.Text = _localizer["ServerDialogRdpCredentialsDesc"];
        DlgSrv_RdpUsernameLabel.Text = _localizer["ServerDialogLabelUsername"];
        DlgSrv_RdpPasswordLabel.Text = _localizer["ServerDialogLabelPassword"];
        DlgSrv_SshCredentialsTitle.Text = _localizer["ServerDialogSshCredentials"];
        DlgSrv_SshCredentialsDesc.Text = _localizer["ServerDialogSshCredentialsDesc"];
        DlgSrv_SshUsernameLabel.Text = _localizer["ServerDialogLabelUsername"];
        DlgSrv_SshKeyLabel.Text = _localizer["ServerDialogLabelSshKey"];
        DlgSrv_BrowseBtn.Content = _localizer["ServerDialogBtnBrowse"];
        DlgSrv_PassphraseLabel.Text = _localizer["ServerDialogLabelPassphrase"];
        DlgSrv_SshAuthHint.Text = _localizer["ServerDialogSshAuthHint"];
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

        // SSH options
        DlgSrv_SshOptionsTitle.Text = _localizer["ServerDialogSshOptions"];
        DlgSrv_SshOptionsDesc.Text = _localizer["ServerDialogSshOptionsDesc"];
        DlgSrv_SshModeLabel.Text = _localizer["ServerDialogLabelSshMode"];
        DlgSrv_SshModeEmbedded.Content = _localizer["ServerDialogModeEmbedded"];
        DlgSrv_SshModeExternal.Content = _localizer["ServerDialogModeExternal"];
        DlgSrv_SshCompressionCb.Content = _localizer["ServerDialogSshCompression"];
        DlgSrv_SshAgentFwdCb.Content = _localizer["ServerDialogSshAgentForward"];
        DlgSrv_SshX11Cb.Content = _localizer["ServerDialogSshX11"];

        // Local shell
        DlgSrv_LocalShellTitle.Text = _localizer["ServerDialogLocalShell"];
        DlgSrv_LocalShellDesc.Text = _localizer["ServerDialogLocalShellDesc"];
        DlgSrv_ExecutableLabel.Text = _localizer["ServerDialogLabelExecutable"];
        DlgSrv_ArgumentsLabel.Text = _localizer["ServerDialogLabelArguments"];
        DlgSrv_WorkingDirLabel.Text = _localizer["ServerDialogLabelWorkingDir"];
        DlgSrv_RunAsAdminCb.Content = _localizer["ServerDialogRunAsAdmin"];

        // Citrix
        DlgSrv_CitrixTitle.Text = _localizer["ServerDialogCitrixWorkspace"];
        DlgSrv_CitrixDesc.Text = _localizer["ServerDialogCitrixWorkspaceDesc"];
        DlgSrv_StoreFrontLabel.Text = _localizer["ServerDialogLabelStoreFrontUrl"];
        DlgSrv_AppNameLabel.Text = _localizer["ServerDialogLabelAppName"];
        DlgSrv_IcaFileLabel.Text = _localizer["ServerDialogLabelIcaFilePath"];
        DlgSrv_CitrixHint.Text = _localizer["ServerDialogCitrixHint"];
        DlgSrv_SeamlessCb.Content = _localizer["ServerDialogCitrixSeamless"];
        DlgSrv_SsoCb.Content = _localizer["ServerDialogCitrixSso"];

        // FTP (Authentication tab)
        DlgSrv_FtpCredentialsTitle.Text = _localizer["ServerDialogFtpCredentials"];
        DlgSrv_FtpCredentialsDesc.Text = _localizer["ServerDialogFtpCredentialsDesc"];
        DlgSrv_FtpUsernameLabel.Text = _localizer["ServerDialogFtpUsername"];
        DlgSrv_FtpPasswordLabel.Text = _localizer["ServerDialogFtpPassword"];

        // VNC (Authentication tab)
        DlgSrv_VncCredentialsTitle.Text = _localizer["ServerDialogVncCredentials"];
        DlgSrv_VncCredentialsDesc.Text = _localizer["ServerDialogVncCredentialsDesc"];
        DlgSrv_VncPasswordLabel.Text = _localizer["ServerDialogVncPassword"];

        // Telnet (Authentication tab)
        DlgSrv_TelnetCredentialsTitle.Text = _localizer["ServerDialogTelnetCredentials"];
        DlgSrv_TelnetCredentialsDesc.Text = _localizer["ServerDialogTelnetCredentialsDesc"];
        DlgSrv_TelnetUsernameLabel.Text = _localizer["ServerDialogLabelUsername"];
        DlgSrv_TelnetPasswordLabel.Text = _localizer["ServerDialogLabelPassword"];

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
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_CancelBtn, _localizer["ServerDialogBtnCancel"]);
        System.Windows.Automation.AutomationProperties.SetName(DlgSrv_SaveBtn, _localizer["ServerDialogBtnSave"]);

        // Accessibility: automation names for PasswordBox controls
        System.Windows.Automation.AutomationProperties.SetName(RdpPasswordBox, _localizer["ServerDialogLabelPassword"]);
        System.Windows.Automation.AutomationProperties.SetName(SshPasswordBox, _localizer["ServerDialogLabelPassphrase"]);
        System.Windows.Automation.AutomationProperties.SetName(FtpPasswordBox, _localizer["ServerDialogFtpPassword"]);
        System.Windows.Automation.AutomationProperties.SetName(VncPasswordBox, _localizer["ServerDialogVncPassword"]);
        System.Windows.Automation.AutomationProperties.SetName(TelnetPasswordBox, _localizer["ServerDialogLabelPassword"]);
    }
}
