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
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Real-time regex pattern tester with match listing and group captures.
/// </summary>
public partial class RegexTesterView : UserControl, IDisposable
{
    private const int MaxDisplayedMatches = 500;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    private LocalizationManager? _localizer;
    private bool _initialized;
    private DispatcherTimer? _debounceTimer;

    public RegexTesterView()
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

        _debounceTimer = new DispatcherTimer
        {
            Interval = DebounceDelay
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ExecuteMatch();
        };

        if (!string.IsNullOrEmpty(context?.Argument))
        {
            PatternText.Text = context.Argument;
        }

        _initialized = true;
    }

    private void ApplyLocalization()
    {
        TitleText.Text = L("ToolRegexTitle");
        PatternLabel.Text = L("ToolRegexPatternLabel");
        TestTextLabel.Text = L("ToolRegexTestTextLabel");
        MatchesLabel.Text = L("ToolRegexMatchesLabel");
        BtnCopyMatches.Content = L("ToolRegexBtnCopy");
        ChkIgnoreCase.Content = L("ToolRegexIgnoreCase");
        ChkMultiline.Content = L("ToolRegexMultiline");
        ChkSingleline.Content = L("ToolRegexSingleline");
        MatchCountText.Text = string.Empty;
        StatusText.Text = string.Empty;

        System.Windows.Automation.AutomationProperties.SetName(BtnCopyMatches, L("ToolRegexBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(ChkIgnoreCase, L("ToolRegexIgnoreCase"));
        System.Windows.Automation.AutomationProperties.SetName(ChkMultiline, L("ToolRegexMultiline"));
        System.Windows.Automation.AutomationProperties.SetName(ChkSingleline, L("ToolRegexSingleline"));
        System.Windows.Automation.AutomationProperties.SetName(PatternText, L("ToolRegexPatternLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TestText, L("ToolRegexTestTextLabel"));
        System.Windows.Automation.AutomationProperties.SetName(MatchesList, L("ToolRegexMatchesLabel"));

        BtnCopyMatches.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnPatternTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized) ScheduleMatch();
    }

    private void OnTestTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized) ScheduleMatch();
    }

    private void OnFlagChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized) ScheduleMatch();
    }

    private void ScheduleMatch()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void ExecuteMatch()
    {
        MatchesList.Items.Clear();
        MatchCountText.Text = string.Empty;

        var pattern = PatternText.Text;
        var testText = TestText.Text;

        if (string.IsNullOrEmpty(pattern))
        {
            StatusText.Text = string.Empty;
            return;
        }

        RegexOptions options = RegexOptions.None;
        if (ChkIgnoreCase.IsChecked == true) options |= RegexOptions.IgnoreCase;
        if (ChkMultiline.IsChecked == true) options |= RegexOptions.Multiline;
        if (ChkSingleline.IsChecked == true) options |= RegexOptions.Singleline;

        Regex regex;
        try
        {
            regex = new Regex(pattern, options, RegexTimeout);
            StatusText.Text = L("ToolRegexStatusValid");
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = string.Format(L("ToolRegexStatusInvalid"), ex.Message);
            return;
        }

        if (string.IsNullOrEmpty(testText))
        {
            return;
        }

        try
        {
            var matches = regex.Matches(testText);
            int totalCount = matches.Count;
            MatchCountText.Text = string.Format(L("ToolRegexMatchCount"), totalCount);

            int displayCount = Math.Min(totalCount, MaxDisplayedMatches);
            for (int i = 0; i < displayCount; i++)
            {
                var match = matches[i];
                var sb = new StringBuilder();
                sb.Append(string.Format(L("ToolRegexMatchEntry"), i, match.Index, match.Value));

                for (int g = 1; g < match.Groups.Count; g++)
                {
                    var group = match.Groups[g];
                    sb.Append(string.Format(L("ToolRegexGroupEntry"), g, group.Value));
                }

                MatchesList.Items.Add(new ListBoxItem
                {
                    Content = sb.ToString(),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 13
                });
            }

            if (totalCount > MaxDisplayedMatches)
            {
                MatchesList.Items.Add(new ListBoxItem
                {
                    Content = string.Format(L("ToolRegexMatchesTruncated"), MaxDisplayedMatches, totalCount),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                    FontStyle = FontStyles.Italic,
                    FontSize = 13
                });
            }
        }
        catch (RegexMatchTimeoutException)
        {
            StatusText.Text = L("ToolRegexStatusTimeout");
        }
    }

    private void OnCopyMatchesClick(object sender, RoutedEventArgs e)
    {
        if (MatchesList.Items.Count == 0) return;

        var sb = new StringBuilder();
        foreach (ListBoxItem item in MatchesList.Items)
        {
            sb.AppendLine(item.Content?.ToString());
        }

        Clipboard.SetText(sb.ToString());
        CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        _debounceTimer?.Stop();
        _debounceTimer = null;
        GC.SuppressFinalize(this);
    }
}
