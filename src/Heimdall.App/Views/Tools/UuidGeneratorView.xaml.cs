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

using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// UUID v4 generator tool with single and batch generation modes.
/// Supports uppercase and hyphenless formatting options.
/// </summary>
public partial class UuidGeneratorView : UserControl, IDisposable
{
    private LocalizationManager? _localizer;
    private const int MaxBatchCount = 100;
    private const int MinBatchCount = 1;

    public UuidGeneratorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the tool with localization and optional context.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();
        GenerateSingle();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolUuidTitle");
        LblSingleResult.Text = L("ToolUuidResultLabel");
        BtnGenerate.Content = L("ToolUuidBtnGenerate");
        BtnCopy.Content = L("ToolUuidBtnCopy");
        ChkUppercase.Content = L("ToolUuidUppercase");
        ChkHyphens.Content = L("ToolUuidHyphens");
        LblBatch.Text = L("ToolUuidBatchLabel");
        LblCount.Text = L("ToolUuidCountLabel");
        BtnGenerateBatch.Content = L("ToolUuidBtnGenerateBatch");
        BtnCopyBatch.Content = L("ToolUuidBtnCopyBatch");

        System.Windows.Automation.AutomationProperties.SetName(BtnGenerate, L("ToolUuidBtnGenerate"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolUuidBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnGenerateBatch, L("ToolUuidBtnGenerateBatch"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyBatch, L("ToolUuidBtnCopyBatch"));
        System.Windows.Automation.AutomationProperties.SetName(TxtCount, L("ToolUuidCountLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtResult, L("ToolUuidResult"));
        System.Windows.Automation.AutomationProperties.SetName(ChkUppercase, L("ToolUuidUppercase"));
        System.Windows.Automation.AutomationProperties.SetName(ChkHyphens, L("ToolUuidHyphens"));
        System.Windows.Automation.AutomationProperties.SetName(TxtBatchResults, L("ToolUuidBatchResults"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyBatch.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e) => GenerateSingle();
    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtResult.Text, sender as Button);

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        // Re-format current single result if present
        if (!string.IsNullOrEmpty(TxtResult.Text))
        {
            GenerateSingle();
        }

        // Re-format batch results if present
        if (!string.IsNullOrEmpty(TxtBatchResults.Text))
        {
            var lines = TxtBatchResults.Text.Split(Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries);
            var count = lines.Length;
            if (count > 0)
            {
                GenerateBatch(count);
            }
        }
    }

    private void OnGenerateBatchClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtCount.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var count))
        {
            count = MinBatchCount;
        }

        count = Math.Clamp(count, MinBatchCount, MaxBatchCount);
        TxtCount.Text = count.ToString(CultureInfo.InvariantCulture);
        GenerateBatch(count);
    }

    private void OnCopyBatchClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtBatchResults.Text, sender as Button);

    private void GenerateSingle()
    {
        TxtResult.Text = FormatUuid(Guid.NewGuid());
    }

    private void GenerateBatch(int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.AppendLine();
            sb.Append(FormatUuid(Guid.NewGuid()));
        }

        TxtBatchResults.Text = sb.ToString();
    }

    private string FormatUuid(Guid guid)
    {
        var uppercase = ChkUppercase?.IsChecked == true;
        var withHyphens = ChkHyphens?.IsChecked == true;

        var format = withHyphens ? "D" : "N";
        var result = guid.ToString(format);

        return uppercase ? result.ToUpperInvariant() : result;
    }

    private static void CopyToClipboard(string? text, Button? btn)
    {
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                Clipboard.SetText(text);
                CopyFeedbackHelper.ShowCopyFeedback(btn);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"UuidGenerator clipboard copy failed: {ex.Message}");
            }
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
