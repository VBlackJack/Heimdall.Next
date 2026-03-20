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
using Microsoft.Win32;

namespace Heimdall.App.Views.Dialogs;

/// <summary>
/// SSH gateway add/edit dialog. Code-behind is limited to PasswordBox handling,
/// file browse, and DialogResult assignment.
/// </summary>
public partial class GatewayDialog : Window
{
    public GatewayDialog()
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);

        Loaded += (_, _) =>
        {
            if (DataContext is GatewayDialogViewModel { Localizer: not null } vm)
            {
                CancelBtn.Content = vm.Localizer["BtnCancel"];
                SaveBtn.Content = vm.Localizer["BtnSave"];
                System.Windows.Automation.AutomationProperties.SetName(CancelBtn, vm.Localizer["BtnCancel"]);
                System.Windows.Automation.AutomationProperties.SetName(SaveBtn, vm.Localizer["BtnSave"]);

                // Form labels
                LblName.Text = vm.Localizer["GatewayDialogLabelName"];
                LblHost.Text = vm.Localizer["GatewayDialogLabelHost"];
                LblPort.Text = vm.Localizer["GatewayDialogLabelPort"];
                LblUsername.Text = vm.Localizer["GatewayDialogLabelUsername"];
                LblKeyPath.Text = vm.Localizer["GatewayDialogLabelKeyPath"];
                LblPassword.Text = vm.Localizer["GatewayDialogLabelPassword"];
                LblParentGateway.Text = vm.Localizer["GatewayDialogLabelParentGateway"];
                LblHostKeyFingerprint.Text = vm.Localizer["GatewayDialogLabelHostKeyFingerprint"];
                BtnBrowse.Content = vm.Localizer["GatewayDialogBtnBrowse"];
                System.Windows.Automation.AutomationProperties.SetName(BtnBrowse, vm.Localizer["GatewayDialogBtnBrowse"]);

                // Input field accessibility
                System.Windows.Automation.AutomationProperties.SetName(TxtName, vm.Localizer["GatewayDialogLabelName"]);
                System.Windows.Automation.AutomationProperties.SetName(TxtHost, vm.Localizer["GatewayDialogLabelHost"]);
                System.Windows.Automation.AutomationProperties.SetName(TxtPort, vm.Localizer["GatewayDialogLabelPort"]);
                System.Windows.Automation.AutomationProperties.SetName(TxtUsername, vm.Localizer["GatewayDialogLabelUsername"]);
                System.Windows.Automation.AutomationProperties.SetName(TxtKeyPath, vm.Localizer["GatewayDialogLabelKeyPath"]);
                System.Windows.Automation.AutomationProperties.SetName(PasswordBox, vm.Localizer["GatewayDialogLabelPassword"]);
                System.Windows.Automation.AutomationProperties.SetName(CmbParentGateway, vm.Localizer["GatewayDialogLabelParentGateway"]);
                System.Windows.Automation.AutomationProperties.SetName(TxtHostKeyFingerprint, vm.Localizer["GatewayDialogLabelHostKeyFingerprint"]);
            }
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GatewayDialogViewModel vm)
        {
            return;
        }

        // Transfer PasswordBox value (WPF PasswordBox cannot be databound)
        vm.Password = PasswordBox.Password;

        vm.ValidateCommand.Execute(null);

        if (vm.ValidationError is null)
        {
            DialogResult = true;

            // Clear password from UI memory (CWE-316)
            PasswordBox.Clear();
        }
    }

    private void OnBrowseKeyClick(object sender, RoutedEventArgs e)
    {
        var localizer = (DataContext as GatewayDialogViewModel)?.Localizer;
        var dialog = new OpenFileDialog
        {
            Title = localizer?["GatewayDialogSelectSshKey"] ?? "Select SSH Key",
            Filter = localizer?["ServerDialogBrowseKeyFilter"] ?? "PPK Files (*.ppk)|*.ppk|PEM Files (*.pem)|*.pem|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true && DataContext is GatewayDialogViewModel vm)
        {
            vm.KeyPath = dialog.FileName;
        }
    }
}
