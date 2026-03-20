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

using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// JSON formatter tool supporting pretty-print and minification.
/// </summary>
public partial class JsonFormatterView : UserControl, IDisposable
{
    private const long MaxInputSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int AsyncThresholdBytes = 100 * 1024; // 100 KB

    private LocalizationManager? _localizer;
    private bool _initialized;

    public JsonFormatterView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the view with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        if (!string.IsNullOrEmpty(context?.Argument))
        {
            InputText.Text = context.Argument;
        }

        _initialized = true;
    }

    private void ApplyLocalization()
    {
        TitleText.Text = L("ToolJsonTitle");
        InputLabel.Text = L("ToolJsonInputLabel");
        OutputLabel.Text = L("ToolJsonOutputLabel");
        BtnPrettify.Content = L("ToolJsonBtnPrettify");
        BtnMinify.Content = L("ToolJsonBtnMinify");
        BtnCopyOutput.Content = L("ToolJsonBtnCopy");

        System.Windows.Automation.AutomationProperties.SetName(BtnPrettify, L("ToolJsonBtnPrettify"));
        System.Windows.Automation.AutomationProperties.SetName(BtnMinify, L("ToolJsonBtnMinify"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyOutput, L("ToolJsonBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(InputText, L("ToolJsonInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(OutputText, L("ToolJsonOutputLabel"));

        BtnCopyOutput.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnPrettifyClick(object sender, RoutedEventArgs e)
    {
        _ = FormatJsonAsync(writeIndented: true);
    }

    private void OnMinifyClick(object sender, RoutedEventArgs e)
    {
        _ = FormatJsonAsync(writeIndented: false);
    }

    private async Task FormatJsonAsync(bool writeIndented)
    {
        var input = InputText.Text;

        if (string.IsNullOrWhiteSpace(input))
        {
            OutputText.Text = string.Empty;
            StatusText.Text = string.Empty;
            return;
        }

        long inputSize = System.Text.Encoding.UTF8.GetByteCount(input);
        if (inputSize > MaxInputSizeBytes)
        {
            OutputText.Text = string.Empty;
            StatusText.Text = L("ToolJsonErrorInputTooLarge");
            return;
        }

        try
        {
            string result;
            if (inputSize > AsyncThresholdBytes)
            {
                BtnPrettify.IsEnabled = false;
                BtnMinify.IsEnabled = false;
                StatusText.Text = L("ToolJsonStatusProcessing");

                result = await Task.Run(() => FormatJsonCore(input, writeIndented));

                BtnPrettify.IsEnabled = true;
                BtnMinify.IsEnabled = true;
            }
            else
            {
                result = FormatJsonCore(input, writeIndented);
            }

            OutputText.Text = result;

            var statusKey = writeIndented ? "ToolJsonStatusPrettified" : "ToolJsonStatusMinified";
            StatusText.Text = string.Format(L(statusKey), result.Length);
        }
        catch (JsonException ex)
        {
            BtnPrettify.IsEnabled = true;
            BtnMinify.IsEnabled = true;
            OutputText.Text = string.Empty;
            StatusText.Text = string.Format(L("ToolJsonStatusError"), ex.Message);
        }
    }

    private static string FormatJsonCore(string input, bool writeIndented)
    {
        using var doc = JsonDocument.Parse(input);
        var options = new JsonWriterOptions
        {
            Indented = writeIndented,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            doc.RootElement.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private void OnCopyOutputClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputText.Text))
        {
            Clipboard.SetText(OutputText.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized)
        {
            OutputText.Text = string.Empty;
            StatusText.Text = string.Empty;
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
