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

namespace Heimdall.App.Views.Dialogs;

/// <summary>
/// Themed message dialog that replaces native <see cref="MessageBox"/>
/// to maintain visual consistency with the Dark/Light theme system.
/// </summary>
public partial class MessageDialog : Window
{
    public bool Result { get; private set; }

    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows a themed information or error message with a single OK button.
    /// </summary>
    public static void ShowMessage(Window? owner, string title, string message, string severity = "info", string primaryLabel = "OK")
    {
        var dialog = new MessageDialog { Owner = owner };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.BtnPrimary.Content = primaryLabel;
        System.Windows.Automation.AutomationProperties.SetName(dialog.BtnPrimary, primaryLabel);

        ApplySeverityStyle(dialog, severity);

        dialog.ShowDialog();
    }

    /// <summary>
    /// Shows a themed confirmation dialog with Yes/No buttons.
    /// Returns true if the user clicked the primary (Yes) button.
    /// </summary>
    public static bool ShowConfirm(
        Window? owner,
        string title,
        string message,
        string severity = "info",
        string primaryLabel = "Yes",
        string secondaryLabel = "No")
    {
        var dialog = new MessageDialog { Owner = owner };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.BtnPrimary.Content = primaryLabel;
        dialog.BtnSecondary.Content = secondaryLabel;
        dialog.BtnSecondary.Visibility = Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(dialog.BtnPrimary, primaryLabel);
        System.Windows.Automation.AutomationProperties.SetName(dialog.BtnSecondary, secondaryLabel);

        ApplySeverityStyle(dialog, severity);

        dialog.ShowDialog();
        return dialog.Result;
    }

    private static void ApplySeverityStyle(MessageDialog dialog, string severity)
    {
        // Icon glyph + color from Segoe MDL2 Assets
        var (icon, brushKey) = severity switch
        {
            "error" => ("\uEA39", "ErrorBrush"),       // ErrorBadge
            "warning" or "danger" => ("\uE7BA", "WarningBrush"), // Warning
            "success" => ("\uE73E", "SuccessBrush"),    // CheckMark
            _ => ("\uE946", "InfoBrush")                // Info
        };

        dialog.IconText.Text = icon;
        if (dialog.TryFindResource(brushKey) is System.Windows.Media.Brush brush)
        {
            dialog.IconText.Foreground = brush;
        }
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        Result = true;
        DialogResult = true;
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        Result = false;
        DialogResult = false;
    }
}
