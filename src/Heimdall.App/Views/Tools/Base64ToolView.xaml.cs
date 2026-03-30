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

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Base64 encoder/decoder tool supporting both text and file input modes.
/// </summary>
public partial class Base64ToolView : UserControl, IToolView
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int AsyncThresholdBytes = 100 * 1024; // 100 KB

    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _isFileMode;
    private byte[]? _fileBytes;

    public Base64ToolView()
    {
        InitializeComponent();
        InputText.PreviewKeyDown += OnInputPreviewKeyDown;
    }

    /// <summary>
    /// Initializes the view with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            InputText.Text = context.Argument;
        }
        else
        {
            InputText.Text = string.Empty;
        }

        _initialized = true;

        if (!string.IsNullOrEmpty(InputText.Text))
        {
            _ = EncodeAsync();
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            InputText.Focus();
            if (!string.IsNullOrEmpty(InputText.Text))
            {
                InputText.SelectAll();
            }
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolBase64Title");
        InputLabel.Text = L("ToolBase64InputLabel");
        OutputLabel.Text = L("ToolBase64OutputLabel");
        BtnEncode.Content = L("ToolBase64BtnEncode");
        BtnDecode.Content = L("ToolBase64BtnDecode");
        BtnCopyOutput.Content = L("ToolBase64BtnCopy");
        BtnBrowseFile.Content = L("ToolBase64BtnBrowse");
        ChkFileMode.Content = L("ToolBase64FileMode");

        System.Windows.Automation.AutomationProperties.SetName(BtnEncode, L("ToolBase64BtnEncode"));
        System.Windows.Automation.AutomationProperties.SetName(BtnDecode, L("ToolBase64BtnDecode"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyOutput, L("ToolBase64BtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnBrowseFile, L("ToolBase64BtnBrowse"));
        ChkUrlSafe.Content = L("ToolBase64UrlSafe");
        System.Windows.Automation.AutomationProperties.SetName(ChkUrlSafe, L("ToolBase64UrlSafe"));
        System.Windows.Automation.AutomationProperties.SetName(ChkFileMode, L("ToolBase64FileMode"));
        System.Windows.Automation.AutomationProperties.SetName(InputText, L("ToolBase64InputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(OutputText, L("ToolBase64OutputLabel"));

        BtnCopyOutput.ToolTip = L("ToolBtnCopyToClipboard");

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));

        InputText.Tag = L("ToolWatermarkTextToEncode");
        TxtEmptyState.Text = L("ToolBase64EmptyState");
    }

    private void OnEncodeClick(object sender, RoutedEventArgs e)
    {
        _ = EncodeAsync();
    }

    private async Task EncodeAsync()
    {
        try
        {
            byte[] data;
            if (_isFileMode && _fileBytes is not null)
            {
                data = _fileBytes;
            }
            else
            {
                data = Encoding.UTF8.GetBytes(InputText.Text);
            }

            var urlSafe = ChkUrlSafe?.IsChecked == true;

            string encoded;
            if (data.Length > AsyncThresholdBytes)
            {
                BtnEncode.IsEnabled = false;
                encoded = await Task.Run(() => EncodeBase64(data, urlSafe));
                BtnEncode.IsEnabled = true;
            }
            else
            {
                encoded = EncodeBase64(data, urlSafe);
            }

            OutputText.Text = encoded;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Visible;
            StatusText.Text = string.Format(L("ToolBase64StatusEncoded"), data.Length);
        }
        catch (Exception ex)
        {
            BtnEncode.IsEnabled = true;
            OutputText.Text = string.Empty;
            StatusText.Text = string.Format(L("ToolBase64StatusError"), ex.Message);
        }
    }

    private static string EncodeBase64(byte[] data, bool urlSafe)
    {
        var encoded = Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks);
        if (!urlSafe)
        {
            return encoded;
        }

        return encoded
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private void OnDecodeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = InputText.Text.Trim();
            if (ChkUrlSafe?.IsChecked == true)
            {
                input = input.Replace('-', '+').Replace('_', '/');
                // Restore padding
                switch (input.Length % 4)
                {
                    case 2: input += "=="; break;
                    case 3: input += "="; break;
                }
            }

            var decoded = Convert.FromBase64String(input);

            if (_isFileMode)
            {
                var dialog = new SaveFileDialog
                {
                    Title = L("ToolBase64SaveFileTitle"),
                    Filter = L("ToolBase64SaveFileFilter")
                };

                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                {
                    File.WriteAllBytes(dialog.FileName, decoded);
                    StatusText.Text = string.Format(L("ToolBase64StatusSaved"), dialog.FileName);
                }

                return;
            }

            OutputText.Text = Encoding.UTF8.GetString(decoded);
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Visible;
            StatusText.Text = string.Format(L("ToolBase64StatusDecoded"), decoded.Length);
        }
        catch (FormatException)
        {
            OutputText.Text = string.Empty;
            StatusText.Text = L("ToolBase64StatusInvalidInput");
        }
        catch (Exception ex)
        {
            OutputText.Text = string.Empty;
            StatusText.Text = string.Format(L("ToolBase64StatusError"), ex.Message);
        }
    }

    private void OnCopyOutputClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputText.Text))
        {
            try { Clipboard.SetText(OutputText.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnBrowseFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("ToolBase64OpenFileTitle"),
            Filter = L("ToolBase64OpenFileFilter")
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var fileInfo = new FileInfo(dialog.FileName);
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                StatusText.Text = L("ToolBase64ErrorFileTooLarge");
                return;
            }

            _fileBytes = File.ReadAllBytes(dialog.FileName);
            InputText.Text = string.Format(L("ToolBase64FileLoaded"), Path.GetFileName(dialog.FileName), _fileBytes.Length);
            InputText.IsReadOnly = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(L("ToolBase64StatusError"), ex.Message);
        }
    }

    private void OnFileModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        _isFileMode = ChkFileMode.IsChecked == true;
        BtnBrowseFile.Visibility = _isFileMode ? Visibility.Visible : Visibility.Collapsed;

        if (!_isFileMode)
        {
            _fileBytes = null;
            InputText.IsReadOnly = false;
            InputText.Text = string.Empty;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized)
        {
            OutputText.Text = string.Empty;
            StatusText.Text = string.Empty;
            ResultsPanel.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
        }
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            _ = EncodeAsync();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Enter)
        {
            OnDecodeClick(sender, e);
            e.Handled = true;
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpBASE64").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        InputText.PreviewKeyDown -= OnInputPreviewKeyDown;
        _fileBytes = null;
        GC.SuppressFinalize(this);
    }
}
