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

using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Side-by-side text comparison tool with line-based LCS diff highlighting.
/// </summary>
public partial class TextDiffView : UserControl, IDisposable
{
    private const int MaxLineCount = 10000;

    private const int AutoCompareDebounceMs = 500;

    private LocalizationManager? _localizer;
    private string _unifiedDiff = string.Empty;
    private DispatcherTimer? _autoCompareTimer;

    public TextDiffView()
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
            OriginalText.Text = context.Argument;
        }
    }

    private void ApplyLocalization()
    {
        TitleText.Text = L("ToolDiffTitle");
        OriginalLabel.Text = L("ToolDiffOriginalLabel");
        ModifiedLabel.Text = L("ToolDiffModifiedLabel");
        DiffOutputLabel.Text = L("ToolDiffOutputLabel");
        BtnCompare.Content = L("ToolDiffBtnCompare");
        BtnSwap.Content = L("ToolDiffBtnSwap");
        BtnClear.Content = L("ToolDiffBtnClear");
        BtnCopyDiff.Content = L("ToolDiffBtnCopyDiff");

        System.Windows.Automation.AutomationProperties.SetName(BtnCompare, L("ToolDiffBtnCompare"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSwap, L("ToolDiffBtnSwap"));
        System.Windows.Automation.AutomationProperties.SetName(BtnClear, L("ToolDiffBtnClear"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyDiff, L("ToolDiffBtnCopyDiff"));
        System.Windows.Automation.AutomationProperties.SetName(OriginalText, L("ToolDiffOriginalLabel"));
        System.Windows.Automation.AutomationProperties.SetName(ModifiedText, L("ToolDiffModifiedLabel"));
        System.Windows.Automation.AutomationProperties.SetName(DiffOutput, L("ToolDiffOutputLabel"));

        BtnCopyDiff.ToolTip = L("ToolBtnCopyToClipboard");

        ChkIgnoreWhitespace.Content = L("ToolDiffChkIgnoreWhitespace");
        ChkIgnoreCase.Content = L("ToolDiffChkIgnoreCase");
        ChkAutoCompare.Content = L("ToolDiffChkAutoCompare");

        System.Windows.Automation.AutomationProperties.SetName(ChkIgnoreWhitespace, L("ToolDiffChkIgnoreWhitespace"));
        System.Windows.Automation.AutomationProperties.SetName(ChkIgnoreCase, L("ToolDiffChkIgnoreCase"));
        System.Windows.Automation.AutomationProperties.SetName(ChkAutoCompare, L("ToolDiffChkAutoCompare"));
    }

    private void OnDiffOptionChanged(object sender, RoutedEventArgs e)
    {
        // Re-run comparison when options change (if there is content)
        if (!string.IsNullOrEmpty(OriginalText.Text) || !string.IsNullOrEmpty(ModifiedText.Text))
        {
            RunComparison();
        }
    }

    private void OnAutoCompareChanged(object sender, RoutedEventArgs e)
    {
        if (ChkAutoCompare.IsChecked == true)
        {
            OriginalText.TextChanged += OnAutoCompareTextChanged;
            ModifiedText.TextChanged += OnAutoCompareTextChanged;
        }
        else
        {
            OriginalText.TextChanged -= OnAutoCompareTextChanged;
            ModifiedText.TextChanged -= OnAutoCompareTextChanged;
            _autoCompareTimer?.Stop();
        }
    }

    private void OnAutoCompareTextChanged(object sender, TextChangedEventArgs e)
    {
        _autoCompareTimer?.Stop();
        _autoCompareTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AutoCompareDebounceMs)
        };
        _autoCompareTimer.Tick += (_, _) =>
        {
            _autoCompareTimer.Stop();
            RunComparison();
        };
        _autoCompareTimer.Start();
    }

    private void OnCompareClick(object sender, RoutedEventArgs e)
    {
        RunComparison();
    }

    private async void RunComparison()
    {
        var originalText = OriginalText.Text;
        var modifiedText = ModifiedText.Text;

        var originalLines = SplitLines(originalText);
        var modifiedLines = SplitLines(modifiedText);

        if (originalLines.Length > MaxLineCount || modifiedLines.Length > MaxLineCount)
        {
            StatusText.Text = string.Format(L("ToolDiffStatusTooLarge"), MaxLineCount);
            return;
        }

        var diffOps = await Task.Run(() => ComputeDiff(originalLines, modifiedLines));
        var displayItems = new List<DiffLineViewModel>();
        var unifiedBuilder = new StringBuilder();

        int addedCount = 0;
        int removedCount = 0;
        int unchangedCount = 0;
        int leftLine = 0;
        int rightLine = 0;

        unifiedBuilder.AppendLine("--- original");
        unifiedBuilder.AppendLine("+++ modified");

        foreach (var op in diffOps)
        {
            switch (op.Type)
            {
                case DiffLineType.Unchanged:
                    leftLine++;
                    rightLine++;
                    unchangedCount++;
                    displayItems.Add(new DiffLineViewModel
                    {
                        LeftLineNumber = leftLine.ToString(),
                        RightLineNumber = rightLine.ToString(),
                        Prefix = " ",
                        Text = op.Text,
                        Background = Brushes.Transparent,
                        PrefixForeground = Brushes.Gray
                    });
                    unifiedBuilder.AppendLine($" {op.Text}");
                    break;

                case DiffLineType.Removed:
                    leftLine++;
                    removedCount++;
                    displayItems.Add(new DiffLineViewModel
                    {
                        LeftLineNumber = leftLine.ToString(),
                        RightLineNumber = string.Empty,
                        Prefix = "-",
                        Text = op.Text,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 255, 0, 0)),
                        PrefixForeground = Brushes.Red
                    });
                    unifiedBuilder.AppendLine($"-{op.Text}");
                    break;

                case DiffLineType.Added:
                    rightLine++;
                    addedCount++;
                    displayItems.Add(new DiffLineViewModel
                    {
                        LeftLineNumber = string.Empty,
                        RightLineNumber = rightLine.ToString(),
                        Prefix = "+",
                        Text = op.Text,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 0, 180, 0)),
                        PrefixForeground = Brushes.Green
                    });
                    unifiedBuilder.AppendLine($"+{op.Text}");
                    break;
            }
        }

        DiffOutput.ItemsSource = displayItems;
        _unifiedDiff = unifiedBuilder.ToString();

        StatsText.Text = string.Format(
            L("ToolDiffStats"),
            addedCount,
            removedCount,
            unchangedCount);

        StatusText.Text = string.Format(L("ToolDiffStatusDone"), displayItems.Count);
    }

    private void OnSwapClick(object sender, RoutedEventArgs e)
    {
        (OriginalText.Text, ModifiedText.Text) = (ModifiedText.Text, OriginalText.Text);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        OriginalText.Text = string.Empty;
        ModifiedText.Text = string.Empty;
        DiffOutput.ItemsSource = null;
        StatsText.Text = string.Empty;
        StatusText.Text = string.Empty;
        _unifiedDiff = string.Empty;
    }

    private void OnCopyDiffClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_unifiedDiff))
        {
            Clipboard.SetText(_unifiedDiff);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    /// <summary>
    /// Splits text into lines, handling mixed line endings.
    /// </summary>
    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }

    /// <summary>
    /// Normalizes a line for comparison, applying whitespace and case options.
    /// The original text is preserved for display; only comparison uses the normalized form.
    /// </summary>
    private string NormalizeLine(string line)
    {
        var result = line;
        if (ChkIgnoreWhitespace?.IsChecked == true)
        {
            result = Regex.Replace(result.Trim(), @"\s+", " ");
        }

        if (ChkIgnoreCase?.IsChecked == true)
        {
            result = result.ToLowerInvariant();
        }

        return result;
    }

    /// <summary>
    /// Computes a line-based diff using the Longest Common Subsequence (LCS) algorithm.
    /// Comparison uses normalized lines; display preserves original text.
    /// </summary>
    private List<DiffOperation> ComputeDiff(string[] original, string[] modified)
    {
        int n = original.Length;
        int m = modified.Length;

        // Normalize lines for comparison
        var normalizedOriginal = new string[n];
        var normalizedModified = new string[m];

        for (int i = 0; i < n; i++)
        {
            normalizedOriginal[i] = NormalizeLine(original[i]);
        }

        for (int j = 0; j < m; j++)
        {
            normalizedModified[j] = NormalizeLine(modified[j]);
        }

        // Build LCS length matrix using normalized lines
        var lcs = new int[n + 1, m + 1];

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                if (string.Equals(normalizedOriginal[i - 1], normalizedModified[j - 1], StringComparison.Ordinal))
                {
                    lcs[i, j] = lcs[i - 1, j - 1] + 1;
                }
                else
                {
                    lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
                }
            }
        }

        // Backtrack to produce diff operations (using original text for display)
        var result = new List<DiffOperation>();
        int x = n;
        int y = m;

        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 &&
                string.Equals(normalizedOriginal[x - 1], normalizedModified[y - 1], StringComparison.Ordinal))
            {
                result.Add(new DiffOperation(DiffLineType.Unchanged, original[x - 1]));
                x--;
                y--;
            }
            else if (y > 0 && (x == 0 || lcs[x, y - 1] >= lcs[x - 1, y]))
            {
                result.Add(new DiffOperation(DiffLineType.Added, modified[y - 1]));
                y--;
            }
            else
            {
                result.Add(new DiffOperation(DiffLineType.Removed, original[x - 1]));
                x--;
            }
        }

        result.Reverse();
        return result;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        _autoCompareTimer?.Stop();
        _autoCompareTimer = null;
        _unifiedDiff = string.Empty;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents a single diff operation (line added, removed, or unchanged).
    /// </summary>
    private sealed record DiffOperation(DiffLineType Type, string Text);

    /// <summary>
    /// Classification of diff line operations.
    /// </summary>
    private enum DiffLineType
    {
        Unchanged,
        Added,
        Removed
    }

    /// <summary>
    /// View model for a single line in the diff output display.
    /// </summary>
    private sealed class DiffLineViewModel
    {
        public string LeftLineNumber { get; init; } = string.Empty;
        public string RightLineNumber { get; init; } = string.Empty;
        public string Prefix { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public Brush Background { get; init; } = Brushes.Transparent;
        public Brush PrefixForeground { get; init; } = Brushes.Gray;
    }
}
