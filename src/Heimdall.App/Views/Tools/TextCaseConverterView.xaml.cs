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
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Text case conversion tool supporting camelCase, PascalCase, snake_case,
/// kebab-case, UPPER CASE, lower case, Title Case, and CONSTANT_CASE.
/// </summary>
public partial class TextCaseConverterView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private Func<string, string>? _lastConversion;

    public TextCaseConverterView()
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

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtInput.Focus();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolTextCaseTitle");
        LblInput.Text = L("ToolTextCaseInputLabel");
        LblConversions.Text = L("ToolTextCaseConversionsLabel");
        LblOutput.Text = L("ToolTextCaseOutputLabel");
        BtnCopy.Content = L("ToolTextCaseBtnCopy");

        BtnCamelCase.Content = L("ToolTextCaseCamel");
        BtnPascalCase.Content = L("ToolTextCasePascal");
        BtnSnakeCase.Content = L("ToolTextCaseSnake");
        BtnKebabCase.Content = L("ToolTextCaseKebab");
        BtnUpperCase.Content = L("ToolTextCaseUpper");
        BtnLowerCase.Content = L("ToolTextCaseLower");
        BtnTitleCase.Content = L("ToolTextCaseTitle_Case");
        BtnConstantCase.Content = L("ToolTextCaseConstant");

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolTextCaseInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtOutput, L("ToolTextCaseOutputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolTextCaseBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCamelCase, L("ToolTextCaseCamel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPascalCase, L("ToolTextCasePascal"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSnakeCase, L("ToolTextCaseSnake"));
        System.Windows.Automation.AutomationProperties.SetName(BtnKebabCase, L("ToolTextCaseKebab"));
        System.Windows.Automation.AutomationProperties.SetName(BtnUpperCase, L("ToolTextCaseUpper"));
        System.Windows.Automation.AutomationProperties.SetName(BtnLowerCase, L("ToolTextCaseLower"));
        System.Windows.Automation.AutomationProperties.SetName(BtnTitleCase, L("ToolTextCaseTitle_Case"));
        System.Windows.Automation.AutomationProperties.SetName(BtnConstantCase, L("ToolTextCaseConstant"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));

        TxtInput.Tag = L("ToolWatermarkTextToConvert");
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        // Re-apply last conversion when input changes
        if (_lastConversion is not null)
        {
            TxtOutput.Text = _lastConversion(TxtInput.Text);
        }
    }

    private void ApplyConversion(Func<string, string> conversion)
    {
        _lastConversion = conversion;
        TxtOutput.Text = conversion(TxtInput.Text);
    }

    private void OnCamelCaseClick(object sender, RoutedEventArgs e) => ApplyConversion(ToCamelCase);
    private void OnPascalCaseClick(object sender, RoutedEventArgs e) => ApplyConversion(ToPascalCase);
    private void OnSnakeCaseClick(object sender, RoutedEventArgs e) => ApplyConversion(ToSnakeCase);
    private void OnKebabCaseClick(object sender, RoutedEventArgs e) => ApplyConversion(ToKebabCase);
    private void OnUpperCaseClick(object sender, RoutedEventArgs e) => ApplyConversion(ToUpperCase);
    private void OnLowerCaseClick(object sender, RoutedEventArgs e) => ApplyConversion(ToLowerCase);
    private void OnTitleCaseClick(object sender, RoutedEventArgs e) => ApplyConversion(ToTitleCase);
    private void OnConstantCaseClick(object sender, RoutedEventArgs e) => ApplyConversion(ToConstantCase);

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtOutput.Text))
        {
            try
            {
                Clipboard.SetText(TxtOutput.Text);
                CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Clipboard locked by another process
            }
        }
    }

    /// <summary>
    /// Splits input text into words by whitespace, underscores, hyphens,
    /// and camelCase boundaries (transitions from lowercase to uppercase).
    /// </summary>
    internal static string[] SplitWords(string input)
    {
        if (string.IsNullOrEmpty(input))
            return [];

        // Insert boundary markers before uppercase letters that follow a lowercase letter
        // or before uppercase letters followed by lowercase (e.g., "XMLParser" -> "XML|Parser")
        var withBoundaries = Regex.Replace(input, @"(?<=[a-z])(?=[A-Z])", " ");
        withBoundaries = Regex.Replace(withBoundaries, @"(?<=[A-Z])(?=[A-Z][a-z])", " ");

        // Split on whitespace, underscores, and hyphens
        var words = Regex.Split(withBoundaries, @"[\s_\-]+");

        return words.Where(w => w.Length > 0).ToArray();
    }

    internal static string ToCamelCase(string input)
    {
        var words = SplitWords(input);
        if (words.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.Append(words[0].ToLowerInvariant());
        for (var i = 1; i < words.Length; i++)
        {
            sb.Append(Capitalize(words[i]));
        }
        return sb.ToString();
    }

    internal static string ToPascalCase(string input)
    {
        var words = SplitWords(input);
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            sb.Append(Capitalize(word));
        }
        return sb.ToString();
    }

    internal static string ToSnakeCase(string input)
    {
        var words = SplitWords(input);
        return string.Join("_", words.Select(w => w.ToLowerInvariant()));
    }

    internal static string ToKebabCase(string input)
    {
        var words = SplitWords(input);
        return string.Join("-", words.Select(w => w.ToLowerInvariant()));
    }

    internal static string ToUpperCase(string input)
    {
        return input.ToUpperInvariant();
    }

    internal static string ToLowerCase(string input)
    {
        return input.ToLowerInvariant();
    }

    internal static string ToTitleCase(string input)
    {
        var words = SplitWords(input);
        return string.Join(" ", words.Select(Capitalize));
    }

    internal static string ToConstantCase(string input)
    {
        var words = SplitWords(input);
        return string.Join("_", words.Select(w => w.ToUpperInvariant()));
    }

    private static string Capitalize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..].ToLowerInvariant();
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpTEXTCASE").Replace("\\n", "\n");
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
