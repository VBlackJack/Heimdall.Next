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

using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Secure password generator with configurable character sets and strength indicator.
/// </summary>
public partial class PasswordGeneratorView : UserControl, IDisposable
{
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()-_=+[]{}|;:',.<>?/~`";

    private LocalizationManager? _localizer;
    private bool _initialized;

    public PasswordGeneratorView()
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

        if (context?.Argument is { } arg && int.TryParse(arg, out var length))
        {
            length = Math.Clamp(length, 8, 128);
            LengthSlider.Value = length;
        }

        _initialized = true;
        GeneratePassword();
    }

    private void ApplyLocalization()
    {
        TitleText.Text = L("ToolPwdGenTitle");
        BtnGenerate.Content = L("ToolPwdGenBtnGenerate");
        BtnCopy.Content = L("ToolPwdGenBtnCopy");
        LengthLabel.Text = L("ToolPwdGenLength");
        ChkUppercase.Content = L("ToolPwdGenUppercase");
        ChkLowercase.Content = L("ToolPwdGenLowercase");
        ChkDigits.Content = L("ToolPwdGenDigits");
        ChkSymbols.Content = L("ToolPwdGenSymbols");

        System.Windows.Automation.AutomationProperties.SetName(BtnGenerate, L("ToolPwdGenBtnGenerate"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolPwdGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(ChkUppercase, L("ToolPwdGenUppercase"));
        System.Windows.Automation.AutomationProperties.SetName(ChkLowercase, L("ToolPwdGenLowercase"));
        System.Windows.Automation.AutomationProperties.SetName(ChkDigits, L("ToolPwdGenDigits"));
        System.Windows.Automation.AutomationProperties.SetName(ChkSymbols, L("ToolPwdGenSymbols"));
        System.Windows.Automation.AutomationProperties.SetName(PasswordOutput, L("ToolPwdGenTitle"));
        System.Windows.Automation.AutomationProperties.SetName(LengthSlider, L("ToolPwdGenLength"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        GeneratePassword();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PasswordOutput.Text))
        {
            Clipboard.SetText(PasswordOutput.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnParameterChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        LengthValueText.Text = ((int)LengthSlider.Value).ToString();
        GeneratePassword();
    }

    private void GeneratePassword()
    {
        var charset = BuildCharset();
        if (charset.Length == 0)
        {
            PasswordOutput.Text = string.Empty;
            UpdateStrengthIndicator(0, 0);
            return;
        }

        var length = (int)LengthSlider.Value;
        LengthValueText.Text = length.ToString();

        var password = new StringBuilder(length);
        Span<byte> buffer = stackalloc byte[length];
        RandomNumberGenerator.Fill(buffer);

        for (var i = 0; i < length; i++)
        {
            // Uniform distribution via rejection sampling equivalent
            password.Append(charset[buffer[i] % charset.Length]);
        }

        PasswordOutput.Text = password.ToString();

        var entropyPerChar = Math.Log2(charset.Length);
        var totalEntropy = entropyPerChar * length;
        UpdateStrengthIndicator(totalEntropy, charset.Length);
    }

    private string BuildCharset()
    {
        var sb = new StringBuilder();
        if (ChkUppercase.IsChecked == true) sb.Append(UppercaseChars);
        if (ChkLowercase.IsChecked == true) sb.Append(LowercaseChars);
        if (ChkDigits.IsChecked == true) sb.Append(DigitChars);
        if (ChkSymbols.IsChecked == true) sb.Append(SymbolChars);
        return sb.ToString();
    }

    private void UpdateStrengthIndicator(double entropy, int charsetSize)
    {
        string strengthKey;
        Brush barBrush;
        double widthPercent;

        switch (entropy)
        {
            case < 40:
                strengthKey = "ToolPwdGenStrengthWeak";
                barBrush = (Brush)FindResource("ErrorBrush");
                widthPercent = 0.25;
                break;
            case < 60:
                strengthKey = "ToolPwdGenStrengthMedium";
                barBrush = (Brush)FindResource("WarningBrush");
                widthPercent = 0.50;
                break;
            case < 80:
                strengthKey = "ToolPwdGenStrengthStrong";
                barBrush = (Brush)FindResource("InfoBrush");
                widthPercent = 0.75;
                break;
            default:
                strengthKey = "ToolPwdGenStrengthVeryStrong";
                barBrush = (Brush)FindResource("SuccessBrush");
                widthPercent = 1.0;
                break;
        }

        StrengthLabel.Text = L(strengthKey);
        StrengthBar.Background = barBrush;

        // Animate width relative to parent
        StrengthBar.Width = ((FrameworkElement)StrengthBar.Parent).ActualWidth * widthPercent;
        if (StrengthBar.Width <= 0)
        {
            StrengthBar.Width = 100 * widthPercent;
        }

        EntropyText.Text = string.Format(L("ToolPwdGenEntropy"), entropy.ToString("F1"), charsetSize);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
