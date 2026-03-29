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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Side-by-side text comparison tool with line-based LCS diff highlighting.
/// </summary>
public partial class TextDiffView : UserControl, IToolView
{
    private const int MaxLineCount = 10000;

    private const int AutoCompareDebounceMs = 500;

    private LocalizationManager? _localizer;
    private string _unifiedDiff = string.Empty;
    private DispatcherTimer? _autoCompareTimer;

    public TextDiffView()
    {
        InitializeComponent();
        OriginalText.PreviewKeyDown += OnDiffInputPreviewKeyDown;
        ModifiedText.PreviewKeyDown += OnDiffInputPreviewKeyDown;
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

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            OriginalText.Focus();
            if (!string.IsNullOrEmpty(OriginalText.Text))
            {
                OriginalText.SelectAll();
            }
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolDiffTitle");
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

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));

        OriginalText.Tag = L("ToolWatermarkOriginalText");
        ModifiedText.Tag = L("ToolWatermarkModifiedText");
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
        if (_autoCompareTimer == null)
        {
            _autoCompareTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AutoCompareDebounceMs)
            };
            _autoCompareTimer.Tick += (_, _) =>
            {
                _autoCompareTimer.Stop();
                RunComparison();
            };
        }
        _autoCompareTimer.Start();
    }

    private void OnCompareClick(object sender, RoutedEventArgs e)
    {
        RunComparison();
    }

    private void OnDiffInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            RunComparison();
            e.Handled = true;
        }
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

        var ignoreWhitespace = ChkIgnoreWhitespace?.IsChecked == true;
        var ignoreCase = ChkIgnoreCase?.IsChecked == true;
        var diffOps = await Task.Run(() => ComputeDiff(originalLines, modifiedLines, ignoreWhitespace, ignoreCase));
        var displayItems = new List<DiffLineViewModel>();
        var unifiedBuilder = new StringBuilder();

        int addedCount = 0;
        int removedCount = 0;
        int unchangedCount = 0;
        int leftLine = 0;
        int rightLine = 0;

        var errorBrush = FindResource("ErrorBrush") as SolidColorBrush;
        var removedBg = errorBrush is not null
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, errorBrush.Color.R, errorBrush.Color.G, errorBrush.Color.B))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 255, 0, 0));
        var removedFg = errorBrush ?? Brushes.Red;

        var successBrush = FindResource("SuccessBrush") as SolidColorBrush;
        var addedBg = successBrush is not null
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, successBrush.Color.R, successBrush.Color.G, successBrush.Color.B))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 0, 180, 0));
        var addedFg = successBrush ?? Brushes.Green;

        unifiedBuilder.AppendLine(L("ToolDiffOriginalHeader"));
        unifiedBuilder.AppendLine(L("ToolDiffModifiedHeader"));

        // Build word-level highlight brushes (stronger alpha than line background)
        var removedWordBg = errorBrush is not null
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(96, errorBrush.Color.R, errorBrush.Color.G, errorBrush.Color.B))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(96, 255, 0, 0));
        var addedWordBg = successBrush is not null
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(96, successBrush.Color.R, successBrush.Color.G, successBrush.Color.B))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(96, 0, 180, 0));

        // Pair consecutive removed+added lines as modifications for word-level diff
        int idx = 0;
        while (idx < diffOps.Count)
        {
            var op = diffOps[idx];

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
                        Inlines = [new Run(op.Text)],
                        Background = Brushes.Transparent,
                        PrefixForeground = Brushes.Gray
                    });
                    unifiedBuilder.AppendLine($" {op.Text}");
                    idx++;
                    break;

                case DiffLineType.Removed:
                    // Check if next op is Added (forming a modification pair)
                    if (idx + 1 < diffOps.Count && diffOps[idx + 1].Type == DiffLineType.Added)
                    {
                        var removedOp = op;
                        var addedOp = diffOps[idx + 1];

                        var (oldInlines, newInlines) = BuildWordDiffInlines(
                            removedOp.Text, addedOp.Text, removedWordBg, addedWordBg);

                        leftLine++;
                        removedCount++;
                        displayItems.Add(new DiffLineViewModel
                        {
                            LeftLineNumber = leftLine.ToString(),
                            RightLineNumber = string.Empty,
                            Prefix = "-",
                            Inlines = oldInlines,
                            Background = removedBg,
                            PrefixForeground = removedFg
                        });
                        unifiedBuilder.AppendLine($"-{removedOp.Text}");

                        rightLine++;
                        addedCount++;
                        displayItems.Add(new DiffLineViewModel
                        {
                            LeftLineNumber = string.Empty,
                            RightLineNumber = rightLine.ToString(),
                            Prefix = "+",
                            Inlines = newInlines,
                            Background = addedBg,
                            PrefixForeground = addedFg
                        });
                        unifiedBuilder.AppendLine($"+{addedOp.Text}");

                        idx += 2;
                    }
                    else
                    {
                        leftLine++;
                        removedCount++;
                        displayItems.Add(new DiffLineViewModel
                        {
                            LeftLineNumber = leftLine.ToString(),
                            RightLineNumber = string.Empty,
                            Prefix = "-",
                            Inlines = [new Run(op.Text)],
                            Background = removedBg,
                            PrefixForeground = removedFg
                        });
                        unifiedBuilder.AppendLine($"-{op.Text}");
                        idx++;
                    }
                    break;

                case DiffLineType.Added:
                    rightLine++;
                    addedCount++;
                    displayItems.Add(new DiffLineViewModel
                    {
                        LeftLineNumber = string.Empty,
                        RightLineNumber = rightLine.ToString(),
                        Prefix = "+",
                        Inlines = [new Run(op.Text)],
                        Background = addedBg,
                        PrefixForeground = addedFg
                    });
                    unifiedBuilder.AppendLine($"+{op.Text}");
                    idx++;
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
            try { Clipboard.SetText(_unifiedDiff); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
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
    private static string NormalizeLine(string line, bool ignoreWhitespace, bool ignoreCase)
    {
        var result = line;
        if (ignoreWhitespace)
        {
            result = Regex.Replace(result.Trim(), @"\s+", " ");
        }

        if (ignoreCase)
        {
            result = result.ToLowerInvariant();
        }

        return result;
    }

    /// <summary>
    /// Computes a line-based diff using the Longest Common Subsequence (LCS) algorithm.
    /// Comparison uses normalized lines; display preserves original text.
    /// </summary>
    private static List<DiffOperation> ComputeDiff(string[] original, string[] modified, bool ignoreWhitespace, bool ignoreCase)
    {
        int n = original.Length;
        int m = modified.Length;

        // Normalize lines for comparison
        var normalizedOriginal = new string[n];
        var normalizedModified = new string[m];

        for (int i = 0; i < n; i++)
        {
            normalizedOriginal[i] = NormalizeLine(original[i], ignoreWhitespace, ignoreCase);
        }

        for (int j = 0; j < m; j++)
        {
            normalizedModified[j] = NormalizeLine(modified[j], ignoreWhitespace, ignoreCase);
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

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpDIFF").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
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
    /// Uses a collection of <see cref="Inline"/> elements for word-level diff highlighting.
    /// </summary>
    private sealed class DiffLineViewModel
    {
        public string LeftLineNumber { get; init; } = string.Empty;
        public string RightLineNumber { get; init; } = string.Empty;
        public string Prefix { get; init; } = string.Empty;
        public List<Inline> Inlines { get; init; } = [];
        public Brush Background { get; init; } = Brushes.Transparent;
        public Brush PrefixForeground { get; init; } = Brushes.Gray;
    }

    /// <summary>
    /// Computes word-level LCS between two lines and returns inline collections
    /// with changed words highlighted using stronger background colors.
    /// </summary>
    private static (List<Inline> oldInlines, List<Inline> newInlines) BuildWordDiffInlines(
        string oldLine, string newLine, Brush removedWordBg, Brush addedWordBg)
    {
        var oldTokens = TokenizeLine(oldLine);
        var newTokens = TokenizeLine(newLine);

        var lcsResult = ComputeWordLcs(oldTokens, newTokens);

        var oldInlines = BuildInlinesFromDiff(oldTokens, lcsResult.oldChanged, removedWordBg);
        var newInlines = BuildInlinesFromDiff(newTokens, lcsResult.newChanged, addedWordBg);

        return (oldInlines, newInlines);
    }

    /// <summary>
    /// Splits a line into tokens preserving whitespace as separate tokens.
    /// </summary>
    private static List<string> TokenizeLine(string line)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                int start = i;
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                tokens.Add(line[start..i]);
            }
            else
            {
                int start = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
                tokens.Add(line[start..i]);
            }
        }
        return tokens;
    }

    /// <summary>
    /// Computes word-level LCS and returns boolean arrays indicating which tokens changed.
    /// </summary>
    private static (bool[] oldChanged, bool[] newChanged) ComputeWordLcs(
        List<string> oldTokens, List<string> newTokens)
    {
        int n = oldTokens.Count;
        int m = newTokens.Count;
        var dp = new int[n + 1, m + 1];

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                if (string.Equals(oldTokens[i - 1], newTokens[j - 1], StringComparison.Ordinal))
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to find which tokens are in the LCS
        var oldInLcs = new bool[n];
        var newInLcs = new bool[m];
        int x = n, y = m;
        while (x > 0 && y > 0)
        {
            if (string.Equals(oldTokens[x - 1], newTokens[y - 1], StringComparison.Ordinal))
            {
                oldInLcs[x - 1] = true;
                newInLcs[y - 1] = true;
                x--;
                y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        // Invert: changed = NOT in LCS
        var oldChanged = new bool[n];
        var newChanged = new bool[m];
        for (int i = 0; i < n; i++) oldChanged[i] = !oldInLcs[i];
        for (int j = 0; j < m; j++) newChanged[j] = !newInLcs[j];

        return (oldChanged, newChanged);
    }

    /// <summary>
    /// Builds a list of <see cref="Inline"/> elements from tokens and their changed status.
    /// Changed tokens receive a highlighted background.
    /// </summary>
    private static List<Inline> BuildInlinesFromDiff(List<string> tokens, bool[] changed, Brush highlightBrush)
    {
        var inlines = new List<Inline>();
        if (tokens.Count == 0) return inlines;

        var sb = new StringBuilder();
        bool currentChanged = changed[0];

        for (int i = 0; i < tokens.Count; i++)
        {
            if (changed[i] != currentChanged)
            {
                // Flush accumulated text
                var run = new Run(sb.ToString());
                if (currentChanged) run.Background = highlightBrush;
                inlines.Add(run);
                sb.Clear();
                currentChanged = changed[i];
            }
            sb.Append(tokens[i]);
        }

        // Flush remaining
        if (sb.Length > 0)
        {
            var run = new Run(sb.ToString());
            if (currentChanged) run.Background = highlightBrush;
            inlines.Add(run);
        }

        return inlines;
    }
}
