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
using System.Windows.Controls;
using System.Windows.Media;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// URL encoder/decoder tool with support for full URL encoding
/// (Uri.EscapeDataString) and component encoding (Uri.EscapeUriString).
/// </summary>
public partial class UrlEncoderView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _updatingFromCode;

    public UrlEncoderView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the tool with optional context and localization.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // Pre-fill with a sensible default; context overrides if provided
        var initialText = "https://example.com/path?key=hello world&lang=en";

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            initialText = context.Argument;
        }

        _initialized = true;

        // Setting TxtDecoded.Text triggers OnDecodedTextChanged which auto-encodes
        TxtDecoded.Text = initialText;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtDecoded.Focus();
            TxtDecoded.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolUrlEncTitle");
        LblDecoded.Text = L("ToolUrlEncDecodedLabel");
        LblEncoded.Text = L("ToolUrlEncEncodedLabel");
        BtnCopyDecoded.Content = L("ToolUrlEncBtnCopy");
        BtnCopyEncoded.Content = L("ToolUrlEncBtnCopy");
        LblComponentEncoding.Text = L("ToolUrlEncComponentMode");

        System.Windows.Automation.AutomationProperties.SetName(BtnCopyDecoded, L("ToolUrlEncBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyEncoded, L("ToolUrlEncBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(TxtDecoded, L("ToolUrlEncDecodedLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtEncoded, L("ToolUrlEncEncodedLabel"));

        BtnCopyDecoded.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyEncoded.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnDecodedTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _updatingFromCode) return;

        _updatingFromCode = true;
        try
        {
            var input = TxtDecoded.Text;
            if (string.IsNullOrEmpty(input))
            {
                TxtEncoded.Text = string.Empty;
                return;
            }

            TxtEncoded.Text = ChkComponentEncoding.IsChecked == true
                ? Uri.EscapeDataString(input)
                : EscapeUrlPreservingStructure(input);
            ResetBorderState(TxtEncoded);
        }
        finally
        {
            _updatingFromCode = false;
        }
    }

    private void OnEncodedTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _updatingFromCode) return;

        _updatingFromCode = true;
        try
        {
            var input = TxtEncoded.Text;
            if (string.IsNullOrEmpty(input))
            {
                TxtDecoded.Text = string.Empty;
                ResetBorderState(TxtDecoded);
                return;
            }

            try
            {
                TxtDecoded.Text = Uri.UnescapeDataString(input);
                ResetBorderState(TxtDecoded);
            }
            catch (UriFormatException)
            {
                TxtDecoded.Text = string.Empty;
                TxtDecoded.BorderBrush = (Brush)FindResource("ErrorBrush");
            }
        }
        finally
        {
            _updatingFromCode = false;
        }
    }

    private void OnCopyDecodedClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtDecoded.Text))
        {
            Clipboard.SetText(TxtDecoded.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopyEncodedClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtEncoded.Text))
        {
            Clipboard.SetText(TxtEncoded.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    /// <summary>
    /// Encodes a full URL while preserving structural characters (:, /, ?, #, &amp;, =, @).
    /// Only non-ASCII and unsafe characters are percent-encoded.
    /// </summary>
    private static string EscapeUrlPreservingStructure(string url)
    {
        // Split by structural characters, encode each segment, then rejoin
        var result = new System.Text.StringBuilder(url.Length * 2);
        var segment = new System.Text.StringBuilder();

        foreach (var ch in url)
        {
            if (IsUrlStructuralChar(ch))
            {
                if (segment.Length > 0)
                {
                    result.Append(Uri.EscapeDataString(segment.ToString()));
                    segment.Clear();
                }

                result.Append(ch);
            }
            else
            {
                segment.Append(ch);
            }
        }

        if (segment.Length > 0)
        {
            result.Append(Uri.EscapeDataString(segment.ToString()));
        }

        return result.ToString();
    }

    private static bool IsUrlStructuralChar(char c)
    {
        return c is ':' or '/' or '?' or '#' or '&' or '=' or '@' or '%';
    }

    /// <summary>
    /// Resets a TextBox border to the default theme brush after a successful operation.
    /// </summary>
    private void ResetBorderState(System.Windows.Controls.TextBox textBox)
    {
        textBox.BorderBrush = (Brush)FindResource("BorderBrush");
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        // Reserved for future resource cleanup.
    }
}
