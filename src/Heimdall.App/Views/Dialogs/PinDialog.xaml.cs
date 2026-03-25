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

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Heimdall.App.Theming;
using Heimdall.App.ViewModels.Dialogs;

namespace Heimdall.App.Views.Dialogs;

/// <summary>
/// PIN entry dialog. Code-behind handles PasswordBox interaction
/// and monitors the ViewModel's IsVerified property to close automatically on success.
/// Localization is handled declaratively in XAML via <c>{loc:Translate}</c>.
/// </summary>
public partial class PinDialog : Window
{
    public PinDialog()
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);

        Loaded += (_, _) => PinBox.Focus();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PinDialogViewModel.IsVerified)
            && DataContext is PinDialogViewModel { IsVerified: true })
        {
            // Clear PIN from UI memory (CWE-316)
            PinBox.Clear();
            DialogResult = true;
        }
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        SubmitPin();
    }

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitPin();
            e.Handled = true;
        }
    }

    private void SubmitPin()
    {
        if (DataContext is not PinDialogViewModel vm)
        {
            return;
        }

        string pin = PinBox.Password;
        vm.VerifyPinCommand.Execute(pin);

        // Clear on every attempt (CWE-316)
        PinBox.Clear();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        PinBox.Clear();
        DialogResult = false;
    }
}
