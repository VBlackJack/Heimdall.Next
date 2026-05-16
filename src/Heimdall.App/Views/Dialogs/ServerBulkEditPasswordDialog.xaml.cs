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
using Heimdall.App.ViewModels;

namespace Heimdall.App.Views.Dialogs;

/// <summary>
/// Minimal modal dialog for bulk password editing. Uses two <see cref="PasswordBox"/>
/// controls (password + confirmation) since <c>PasswordBox</c> does not support
/// data binding for security reasons.
/// </summary>
public partial class ServerBulkEditPasswordDialog : Window
{
    public ServerBulkEditPasswordDialog()
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PasswordInput.Focus();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ServerBulkEditPasswordViewModel vm)
        {
            vm.Password = PasswordInput.Password;
        }
    }

    private void OnConfirmPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ServerBulkEditPasswordViewModel vm)
        {
            vm.ConfirmPassword = ConfirmPasswordInput.Password;
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ServerBulkEditPasswordViewModel { IsApplyEnabled: true, ResolvedPassword: not null })
        {
            return;
        }

        DialogResult = true;
    }
}
