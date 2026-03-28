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
using System.Security.Cryptography;
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
public partial class UuidGeneratorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private const int MaxBatchCount = 100;
    private const int MinBatchCount = 1;

    public UuidGeneratorView()
    {
        InitializeComponent();
        TxtCount.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateBatchClick(s, e); };
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

        // Set ComboBox item labels after localizer is available
        if (CmbVersion.Items[0] is ComboBoxItem v4Item)
        {
            v4Item.Content = L("ToolUuidVersionV4");
        }

        if (CmbVersion.Items[1] is ComboBoxItem v7Item)
        {
            v7Item.Content = L("ToolUuidVersionV7");
        }

        System.Windows.Automation.AutomationProperties.SetName(CmbVersion, L("ToolUuidVersionLabel"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e) => GenerateSingle();
    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtResult.Text, sender as Button);

    private void OnVersionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update the result label to reflect the selected version
        if (_localizer is null) return;

        var version = GetSelectedVersion();
        LblSingleResult.Text = version == 7
            ? L("ToolUuidResultLabelV7")
            : L("ToolUuidResultLabel");

        GenerateSingle();

        if (!string.IsNullOrEmpty(TxtBatchResults.Text))
        {
            var lines = TxtBatchResults.Text.Split(Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                GenerateBatch(lines.Length);
            }
        }
    }

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        // Guard: fired during InitializeComponent before all controls exist
        if (TxtResult is null || TxtBatchResults is null) return;

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
        TxtResult.Text = FormatUuid(GenerateUuid());
    }

    private void GenerateBatch(int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.AppendLine();
            sb.Append(FormatUuid(GenerateUuid()));
        }

        TxtBatchResults.Text = sb.ToString();
    }

    private Guid GenerateUuid()
    {
        return GetSelectedVersion() == 7 ? GenerateUuidV7() : Guid.NewGuid();
    }

    private int GetSelectedVersion()
    {
        if (CmbVersion?.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            int.TryParse(tagStr, out var version))
        {
            return version;
        }

        return 4;
    }

    /// <summary>
    /// Generates a UUID v7 (RFC 9562) with a Unix timestamp in the upper 48 bits,
    /// version bits set to 0111, variant bits set to 10xx, and cryptographic random fill.
    /// </summary>
    private static Guid GenerateUuidV7()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        // Unix timestamp in milliseconds (48 bits)
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;

        // Version: set upper nibble of byte 6 to 0111 (7)
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // Variant: set upper 2 bits of byte 8 to 10
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
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
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Clipboard locked by another process
            }
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpUUID").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
