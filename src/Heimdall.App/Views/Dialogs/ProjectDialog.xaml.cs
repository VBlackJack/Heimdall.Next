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

namespace Heimdall.App.Views.Dialogs;

/// <summary>
/// Project add/edit dialog. Code-behind is limited to validation and DialogResult assignment.
/// </summary>
public partial class ProjectDialog : Window
{
    public ProjectDialog()
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);

        Loaded += (_, _) =>
        {
            if (DataContext is ProjectDialogViewModel { Localizer: not null } vm)
            {
                CancelBtn.Content = vm.Localizer["BtnCancel"];
                SaveBtn.Content = vm.Localizer["BtnSave"];
                System.Windows.Automation.AutomationProperties.SetName(CancelBtn, vm.Localizer["BtnCancel"]);
                System.Windows.Automation.AutomationProperties.SetName(SaveBtn, vm.Localizer["BtnSave"]);

                // Form labels
                LblName.Text = vm.Localizer["ProjectDialogLabelName"];
                LblDescription.Text = vm.Localizer["ProjectDialogLabelDescription"];
                LblColor.Text = vm.Localizer["ProjectDialogLabelColor"];
                LblSshDefaults.Text = vm.Localizer["ProjectDialogLabelSshDefaults"];
                LblDefaultSshUsername.Text = vm.Localizer["ProjectDialogLabelDefaultSshUsername"];
                LblDefaultSshKeyPath.Text = vm.Localizer["ProjectDialogLabelDefaultSshKeyPath"];

                // Input field accessibility
                System.Windows.Automation.AutomationProperties.SetName(TxtName, vm.Localizer["ProjectDialogLabelName"]);
                System.Windows.Automation.AutomationProperties.SetName(TxtDescription, vm.Localizer["ProjectDialogLabelDescription"]);
                System.Windows.Automation.AutomationProperties.SetName(TxtDefaultSshUsername, vm.Localizer["ProjectDialogLabelDefaultSshUsername"]);
                System.Windows.Automation.AutomationProperties.SetName(TxtDefaultSshKeyPath, vm.Localizer["ProjectDialogLabelDefaultSshKeyPath"]);

                // Color swatch accessibility + visible tooltips for colorblind users
                foreach (var (rb, key) in new (System.Windows.Controls.RadioButton, string)[]
                {
                    (RbColorBlue, "ProjectDialogColorBlue"),
                    (RbColorGreen, "ProjectDialogColorGreen"),
                    (RbColorRed, "ProjectDialogColorRed"),
                    (RbColorAmber, "ProjectDialogColorAmber"),
                    (RbColorPurple, "ProjectDialogColorPurple"),
                    (RbColorPink, "ProjectDialogColorPink"),
                    (RbColorCyan, "ProjectDialogColorCyan"),
                    (RbColorOrange, "ProjectDialogColorOrange"),
                })
                {
                    var label = vm.Localizer[key];
                    System.Windows.Automation.AutomationProperties.SetName(rb, label);
                    rb.ToolTip = label;
                    rb.Checked += (_, _) => SelectedColorLabel.Text = label;
                    if (rb.IsChecked == true)
                    {
                        SelectedColorLabel.Text = label;
                    }
                }
            }
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectDialogViewModel vm)
        {
            return;
        }

        vm.ValidateCommand.Execute(null);

        if (vm.ValidationError is null)
        {
            DialogResult = true;
        }
    }
}
