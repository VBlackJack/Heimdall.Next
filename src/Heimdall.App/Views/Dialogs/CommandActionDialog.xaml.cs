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
/// Command action add/edit dialog. Code-behind handles localization,
/// validation triggering, and DialogResult assignment.
/// </summary>
public partial class CommandActionDialog : Window
{
    public CommandActionDialog()
    {
        InitializeComponent();
        WindowThemeHelper.ApplyCurrentTheme(this);

        Loaded += (_, _) =>
        {
            if (DataContext is not CommandActionDialogViewModel { Localizer: not null } vm)
            {
                TxtTitle.Focus();
                return;
            }

            // Buttons
            CancelBtn.Content = vm.Localizer["BtnCancel"];
            SaveBtn.Content = vm.Localizer["BtnSave"];
            System.Windows.Automation.AutomationProperties.SetName(CancelBtn, vm.Localizer["BtnCancel"]);
            System.Windows.Automation.AutomationProperties.SetName(SaveBtn, vm.Localizer["BtnSave"]);

            // Form labels
            LblTitle.Text = vm.Localizer["ToolCmdLibDialogLblTitle"];
            LblCategory.Text = vm.Localizer["ToolCmdLibDialogLblCategory"];
            LblPlatform.Text = vm.Localizer["ToolCmdLibDialogLblPlatform"];
            LblRisk.Text = vm.Localizer["ToolCmdLibDialogLblRisk"];
            LblDescription.Text = vm.Localizer["ToolCmdLibDialogLblDescription"];
            LblTags.Text = vm.Localizer["ToolCmdLibDialogLblTags"];
            LblNotes.Text = vm.Localizer["ToolCmdLibDialogLblNotes"];

            // Template section headers
            LblWindowsSection.Text = vm.Localizer["ToolCmdLibDialogSectionWindows"];
            LblLinuxSection.Text = vm.Localizer["ToolCmdLibDialogSectionLinux"];
            LblWinPattern.Text = vm.Localizer["ToolCmdLibDialogLblPattern"];
            LblLinuxPattern.Text = vm.Localizer["ToolCmdLibDialogLblPattern"];
            LblWinName.Text = vm.Localizer["ToolCmdLibDialogLblTemplateName"];
            LblLinuxName.Text = vm.Localizer["ToolCmdLibDialogLblTemplateName"];
            LblWinParamName.Text = vm.Localizer["ToolCmdLibDialogParamName"];
            LblWinParamLabel.Text = vm.Localizer["ToolCmdLibDialogParamLabel"];
            LblWinParamType.Text = vm.Localizer["ToolCmdLibDialogParamType"];
            LblLinuxParamName.Text = vm.Localizer["ToolCmdLibDialogParamName"];
            LblLinuxParamLabel.Text = vm.Localizer["ToolCmdLibDialogParamLabel"];
            LblLinuxParamType.Text = vm.Localizer["ToolCmdLibDialogParamType"];

            // Parameter buttons
            BtnAddWinParam.Content = vm.Localizer["ToolCmdLibDialogBtnAddParam"];
            BtnAddLinuxParam.Content = vm.Localizer["ToolCmdLibDialogBtnAddParam"];

            // Risk combo localization
            RiskInfo.Content = vm.Localizer["ToolCmdLibRiskInfo"];
            RiskRun.Content = vm.Localizer["ToolCmdLibRiskRun"];
            RiskDangerous.Content = vm.Localizer["ToolCmdLibRiskDangerous"];

            // Accessibility
            System.Windows.Automation.AutomationProperties.SetName(TxtTitle, vm.Localizer["ToolCmdLibDialogLblTitle"]);
            System.Windows.Automation.AutomationProperties.SetName(CmbCategory, vm.Localizer["ToolCmdLibDialogLblCategory"]);
            System.Windows.Automation.AutomationProperties.SetName(CmbPlatform, vm.Localizer["ToolCmdLibDialogLblPlatform"]);
            System.Windows.Automation.AutomationProperties.SetName(CmbRisk, vm.Localizer["ToolCmdLibDialogLblRisk"]);

            // Show/hide template sections based on platform
            UpdateTemplateSections(vm.Platform);
            CmbPlatform.SelectionChanged += (_, _) =>
            {
                if (DataContext is CommandActionDialogViewModel v)
                    UpdateTemplateSections(v.Platform);
            };

            TxtTitle.Focus();
        };
    }

    private void UpdateTemplateSections(TwinShell.Core.Enums.Platform platform)
    {
        WindowsSection.Visibility = platform is TwinShell.Core.Enums.Platform.Windows
            or TwinShell.Core.Enums.Platform.Both
            ? Visibility.Visible : Visibility.Collapsed;

        LinuxSection.Visibility = platform is TwinShell.Core.Enums.Platform.Linux
            or TwinShell.Core.Enums.Platform.Both
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CommandActionDialogViewModel vm) return;

        vm.ValidateCommand.Execute(null);

        if (vm.ValidationError is null)
        {
            DialogResult = true;
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DialogResult == true) return;
        if (DataContext is not CommandActionDialogViewModel { IsDirty: true } vm) return;

        var title = vm.Localizer?["DialogUnsavedWarningTitle"] ?? "Unsaved Changes";
        var message = vm.Localizer?["DialogUnsavedWarning"]
            ?? "You have unsaved changes. Discard them and close?";
        var yes = vm.Localizer?["BtnYes"] ?? "Yes";
        var no = vm.Localizer?["BtnNo"] ?? "No";

        var discard = MessageDialog.ShowConfirm(this, title, message, "warning", yes, no);
        if (!discard)
        {
            e.Cancel = true;
        }
    }
}
