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
/// Generic text input dialog with title, prompt, text box, and OK/Cancel buttons.
/// </summary>
public partial class InputDialog : Window
{
    /// <summary>
    /// Gets or sets the prompt text displayed above the input field.
    /// </summary>
    public string Prompt
    {
        get => PromptText.Text;
        set => PromptText.Text = value;
    }

    /// <summary>
    /// Gets or sets the text entered in the input field.
    /// </summary>
    public string InputText
    {
        get => InputTextBox.Text;
        set => InputTextBox.Text = value;
    }

    public InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
