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
/// Server add/edit dialog. Code-behind is limited to PasswordBox handling,
/// file browse, and DialogResult assignment.
/// </summary>
public partial class ServerDialog : Window
{
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

        // Transfer PasswordBox values (WPF PasswordBox cannot be databound)
        vm.SshPassword = SshPasswordBox.Password;
        vm.RdpPassword = RdpPasswordBox.Password;

        vm.ValidateCommand.Execute(null);

        if (vm.ValidationError is null)
        {
            DialogResult = true;

            // Clear passwords from UI memory (CWE-316)
            SshPasswordBox.Clear();
            RdpPasswordBox.Clear();
        }
    }

    private void OnBrowseSshKeyClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select SSH Key",
            Filter = "PPK Files (*.ppk)|*.ppk|PEM Files (*.pem)|*.pem|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true && DataContext is ServerDialogViewModel vm)
        {
            vm.SshKeyPath = dialog.FileName;
        }
    }
}
