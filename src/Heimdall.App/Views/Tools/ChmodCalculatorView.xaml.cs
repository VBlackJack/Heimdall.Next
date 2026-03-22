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

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Interactive chmod permission calculator with bidirectional octal/checkbox sync.
/// </summary>
public partial class ChmodCalculatorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _updatingFromCode;

    public ChmodCalculatorView()
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

        _initialized = true;

        if (!string.IsNullOrEmpty(context?.Argument) && IsValidOctal(context.Argument))
        {
            OctalInput.Text = context.Argument;
            UpdateCommandPreview(context.Argument);
        }
        else
        {
            ApplyOctalToCheckboxes("755");
            OctalInput.Text = "755";
            UpdateCommandPreview("755");
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            OctalInput.Focus();
            OctalInput.SelectAll();
        });
    }

    private static readonly Regex SymbolicClausePattern = new(
        @"^([ugoa]+)([\+\-=])([rwx]*)$",
        RegexOptions.Compiled);

    private void ApplyLocalization()
    {
        TitleText.Text = L("ToolChmodTitle");
        HeaderRead.Text = L("ToolChmodRead");
        HeaderWrite.Text = L("ToolChmodWrite");
        HeaderExecute.Text = L("ToolChmodExecute");
        LabelOwner.Text = L("ToolChmodOwner");
        LabelGroup.Text = L("ToolChmodGroup");
        LabelOthers.Text = L("ToolChmodOthers");
        OctalLabel.Text = L("ToolChmodOctal");
        SymbolicLabel.Text = L("ToolChmodSymbolic");
        BtnCopyOctal.Content = L("ToolChmodBtnCopyOctal");
        BtnCopySymbolic.Content = L("ToolChmodBtnCopySymbolic");
        PresetsLabel.Text = L("ToolChmodPresets");
        SymbolicInputLabel.Text = L("ToolChmodSymbolicInput");
        CommandPreviewLabel.Text = L("ToolChmodCommandPreview");
        BtnCopyCommand.Content = L("ToolChmodBtnCopyCommand");

        System.Windows.Automation.AutomationProperties.SetName(BtnCopyOctal, L("ToolChmodBtnCopyOctal"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopySymbolic, L("ToolChmodBtnCopySymbolic"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyCommand, L("ToolChmodBtnCopyCommand"));
        System.Windows.Automation.AutomationProperties.SetName(OctalInput, L("ToolChmodOctal"));
        System.Windows.Automation.AutomationProperties.SetName(SymbolicInput, L("ToolChmodSymbolicInput"));

        System.Windows.Automation.AutomationProperties.SetName(ChkOwnerR, $"{L("ToolChmodOwner")} {L("ToolChmodRead")}");
        System.Windows.Automation.AutomationProperties.SetName(ChkOwnerW, $"{L("ToolChmodOwner")} {L("ToolChmodWrite")}");
        System.Windows.Automation.AutomationProperties.SetName(ChkOwnerX, $"{L("ToolChmodOwner")} {L("ToolChmodExecute")}");
        System.Windows.Automation.AutomationProperties.SetName(ChkGroupR, $"{L("ToolChmodGroup")} {L("ToolChmodRead")}");
        System.Windows.Automation.AutomationProperties.SetName(ChkGroupW, $"{L("ToolChmodGroup")} {L("ToolChmodWrite")}");
        System.Windows.Automation.AutomationProperties.SetName(ChkGroupX, $"{L("ToolChmodGroup")} {L("ToolChmodExecute")}");
        System.Windows.Automation.AutomationProperties.SetName(ChkOthersR, $"{L("ToolChmodOthers")} {L("ToolChmodRead")}");
        System.Windows.Automation.AutomationProperties.SetName(ChkOthersW, $"{L("ToolChmodOthers")} {L("ToolChmodWrite")}");
        System.Windows.Automation.AutomationProperties.SetName(ChkOthersX, $"{L("ToolChmodOthers")} {L("ToolChmodExecute")}");

        BtnCopyOctal.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopySymbolic.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyCommand.ToolTip = L("ToolBtnCopyToClipboard");

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void OnPermissionChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _updatingFromCode) return;

        _updatingFromCode = true;
        try
        {
            var octal = CalculateOctalFromCheckboxes();
            OctalInput.Text = octal;
            UpdateSymbolicDisplay(octal);
            UpdateCommandPreview(octal);
        }
        finally
        {
            _updatingFromCode = false;
        }
    }

    private void OnOctalTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _updatingFromCode) return;

        var text = OctalInput.Text.Trim();
        if (!IsValidOctal(text)) return;

        _updatingFromCode = true;
        try
        {
            ApplyOctalToCheckboxes(text);
            UpdateSymbolicDisplay(text);
            UpdateCommandPreview(text);
        }
        finally
        {
            _updatingFromCode = false;
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string preset)
        {
            _updatingFromCode = true;
            try
            {
                OctalInput.Text = preset;
                ApplyOctalToCheckboxes(preset);
                UpdateSymbolicDisplay(preset);
                UpdateCommandPreview(preset);
            }
            finally
            {
                _updatingFromCode = false;
            }
        }
    }

    private void OnCopyOctalClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OctalInput.Text))
        {
            Clipboard.SetText(OctalInput.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopySymbolicClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SymbolicDisplay.Text))
        {
            Clipboard.SetText(SymbolicDisplay.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopyCommandClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CommandPreviewText.Text))
        {
            Clipboard.SetText(CommandPreviewText.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnSymbolicInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        if (!_initialized || _updatingFromCode) return;

        var text = SymbolicInput.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            SymbolicInputError.Visibility = Visibility.Collapsed;
            return;
        }

        if (TryParseSymbolicNotation(text, out var octal))
        {
            SymbolicInputError.Visibility = Visibility.Collapsed;
            _updatingFromCode = true;
            try
            {
                OctalInput.Text = octal;
                ApplyOctalToCheckboxes(octal);
                UpdateSymbolicDisplay(octal);
                UpdateCommandPreview(octal, text);
            }
            finally
            {
                _updatingFromCode = false;
            }
        }
        else
        {
            SymbolicInputError.Text = L("ToolChmodErrorInvalidSymbolic");
            SymbolicInputError.Visibility = Visibility.Visible;
        }
    }

    private void UpdateCommandPreview(string octal, string? symbolicNotation = null)
    {
        var mode = !string.IsNullOrEmpty(symbolicNotation) ? symbolicNotation : octal;
        CommandPreviewText.Text = $"chmod {mode} filename";
    }

    /// <summary>
    /// Parses symbolic chmod notation (e.g. "u+x,g-w,o=r") and returns the resulting octal string.
    /// Applies operations against a clean "000" base state to avoid progressive corruption.
    /// </summary>
    private bool TryParseSymbolicNotation(string input, out string octal)
    {
        octal = string.Empty;

        // Start from a clean base (000) to avoid progressive state corruption
        var owner = 0;
        var group = 0;
        var others = 0;

        var clauses = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (clauses.Length == 0) return false;

        foreach (var clause in clauses)
        {
            var match = SymbolicClausePattern.Match(clause.Trim());
            if (!match.Success) return false;

            var who = match.Groups[1].Value;
            var op = match.Groups[2].Value[0];
            var perms = match.Groups[3].Value;

            var permBits = 0;
            if (perms.Contains('r')) permBits |= 4;
            if (perms.Contains('w')) permBits |= 2;
            if (perms.Contains('x')) permBits |= 1;

            var applyToOwner = who.Contains('u') || who.Contains('a');
            var applyToGroup = who.Contains('g') || who.Contains('a');
            var applyToOthers = who.Contains('o') || who.Contains('a');

            if (applyToOwner) owner = ApplyOperation(owner, op, permBits);
            if (applyToGroup) group = ApplyOperation(group, op, permBits);
            if (applyToOthers) others = ApplyOperation(others, op, permBits);
        }

        octal = $"{owner}{group}{others}";
        return true;
    }

    private static int ApplyOperation(int current, char op, int bits)
    {
        return op switch
        {
            '+' => current | bits,
            '-' => current & ~bits,
            '=' => bits,
            _ => current
        };
    }

    private string CalculateOctalFromCheckboxes()
    {
        var owner = GetDigit(ChkOwnerR, ChkOwnerW, ChkOwnerX);
        var group = GetDigit(ChkGroupR, ChkGroupW, ChkGroupX);
        var others = GetDigit(ChkOthersR, ChkOthersW, ChkOthersX);
        return $"{owner}{group}{others}";
    }

    private static int GetDigit(System.Windows.Controls.CheckBox r, System.Windows.Controls.CheckBox w, System.Windows.Controls.CheckBox x)
    {
        var val = 0;
        if (r.IsChecked == true) val += 4;
        if (w.IsChecked == true) val += 2;
        if (x.IsChecked == true) val += 1;
        return val;
    }

    private void ApplyOctalToCheckboxes(string octal)
    {
        if (octal.Length != 3) return;

        var owner = octal[0] - '0';
        var group = octal[1] - '0';
        var others = octal[2] - '0';

        SetCheckboxesFromDigit(owner, ChkOwnerR, ChkOwnerW, ChkOwnerX);
        SetCheckboxesFromDigit(group, ChkGroupR, ChkGroupW, ChkGroupX);
        SetCheckboxesFromDigit(others, ChkOthersR, ChkOthersW, ChkOthersX);
    }

    private static void SetCheckboxesFromDigit(int digit, System.Windows.Controls.CheckBox r, System.Windows.Controls.CheckBox w, System.Windows.Controls.CheckBox x)
    {
        r.IsChecked = (digit & 4) != 0;
        w.IsChecked = (digit & 2) != 0;
        x.IsChecked = (digit & 1) != 0;
    }

    private void UpdateSymbolicDisplay(string octal)
    {
        if (octal.Length != 3)
        {
            SymbolicDisplay.Text = string.Empty;
            return;
        }

        var symbolic = new char[9];
        for (var i = 0; i < 3; i++)
        {
            var digit = octal[i] - '0';
            var offset = i * 3;
            symbolic[offset] = (digit & 4) != 0 ? 'r' : '-';
            symbolic[offset + 1] = (digit & 2) != 0 ? 'w' : '-';
            symbolic[offset + 2] = (digit & 1) != 0 ? 'x' : '-';
        }

        SymbolicDisplay.Text = new string(symbolic);
    }

    private static bool IsValidOctal(string text)
    {
        if (text.Length != 3) return false;
        foreach (var c in text)
        {
            if (c < '0' || c > '7') return false;
        }
        return true;
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpCHMOD");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
