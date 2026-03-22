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

using System.IO;
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
/// Log file viewer with tail mode, regex filtering, and auto-scroll.
/// Replaces <c>Get-Content -Tail -Wait</c> for environments where
/// PowerShell execution is restricted.
/// </summary>
public partial class LogViewerView : UserControl, IToolView
{
    private const int MaxDisplayLines = 10000;
    private const int TailIntervalMs = 500;
    private const int EncodingIndexUtf8 = 0;
    private const int EncodingIndexAscii = 1;
    private const int EncodingIndexUtf16 = 2;
    private const int EncodingIndexWindows1252 = 3;
    private const int Windows1252CodePage = 1252;

    private LocalizationManager? _localizer;
    private string? _filePath;
    private FileStream? _fileStream;
    private long _lastPosition;
    private DispatcherTimer? _tailTimer;
    private Encoding _encoding = Encoding.UTF8;
    private Regex? _filterRegex;
    private bool _autoScroll = true;
    private int _totalLineCount;
    private int _displayedLineCount;
    private Brush? _highlightBrush;

    public LogViewerView()
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
    }

    private void ApplyLocalization()
    {
        TitleText.Text = L("ToolLogViewTitle");
        BtnBrowse.Content = L("ToolLogViewBtnBrowse");
        BtnTail.Content = L("ToolLogViewBtnTail");
        BtnApplyFilter.Content = L("ToolLogViewBtnFilter");
        BtnClearFilter.Content = L("ToolLogViewBtnClear");
        ChkCaseSensitive.Content = L("ToolLogViewCaseSensitive");
        EncodingLabel.Text = L("ToolLogViewEncoding");
        FilePathInput.Tag = L("ToolLogViewFilePathHint");

        // Accessibility
        System.Windows.Automation.AutomationProperties.SetName(BtnBrowse, L("ToolLogViewBtnBrowse"));
        System.Windows.Automation.AutomationProperties.SetName(BtnTail, L("ToolLogViewBtnTail"));
        System.Windows.Automation.AutomationProperties.SetName(BtnApplyFilter, L("ToolLogViewBtnFilter"));
        System.Windows.Automation.AutomationProperties.SetName(BtnClearFilter, L("ToolLogViewBtnClear"));
        System.Windows.Automation.AutomationProperties.SetName(FilePathInput, L("ToolLogViewFilePathHint"));
        System.Windows.Automation.AutomationProperties.SetName(FilterInput, L("ToolLogViewBtnFilter"));
        System.Windows.Automation.AutomationProperties.SetName(EncodingCombo, L("ToolLogViewEncoding"));
        System.Windows.Automation.AutomationProperties.SetName(LogViewer, L("ToolLogViewTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = L("ToolLogViewFileFilter"),
            Title = L("ToolLogViewBtnBrowse")
        };

        if (dialog.ShowDialog() == true)
        {
            FilePathInput.Text = dialog.FileName;
            LoadFile(dialog.FileName);
        }
    }

    private void OnFilePathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var path = FilePathInput.Text.Trim();
            if (!string.IsNullOrEmpty(path))
            {
                LoadFile(path);
            }
        }
    }

    private void LoadFile(string filePath)
    {
        StopTail();

        if (!File.Exists(filePath))
        {
            MessageBox.Show(
                L("ToolLogViewErrorFileNotFound"),
                L("ToolLogViewTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _filePath = filePath;
        _totalLineCount = 0;
        _displayedLineCount = 0;
        LogDocument.Blocks.Clear();

        try
        {
            _fileStream?.Dispose();
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(_fileStream, _encoding, leaveOpen: true);
            var content = reader.ReadToEnd();
            _lastPosition = _fileStream.Position;

            AppendLines(content);
            UpdateStats();

            // Start tail if toggle is checked
            if (BtnTail.IsChecked == true)
            {
                StartTail();
            }
        }
        catch (IOException ex)
        {
            MessageBox.Show(
                string.Format(L("ToolLogViewErrorOpen"), ex.Message),
                L("ToolLogViewTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnTailChecked(object sender, RoutedEventArgs e)
    {
        if (_fileStream is not null && _filePath is not null)
        {
            StartTail();
        }
    }

    private void OnTailUnchecked(object sender, RoutedEventArgs e)
    {
        StopTail();
    }

    private void StartTail()
    {
        if (_tailTimer is not null) return;

        _autoScroll = true;
        _tailTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TailIntervalMs) };
        _tailTimer.Tick += (_, _) => ReadNewContent();
        _tailTimer.Start();

        TailStatusText.Text = L("ToolLogViewTailActive");
        TailStatusText.Visibility = Visibility.Visible;
    }

    private void StopTail()
    {
        _tailTimer?.Stop();
        _tailTimer = null;

        TailStatusText.Visibility = Visibility.Collapsed;
    }

    private void ReadNewContent()
    {
        if (_fileStream is null || _filePath is null) return;

        try
        {
            var fileInfo = new FileInfo(_filePath);
            var currentLength = fileInfo.Length;

            if (currentLength == _lastPosition) return;

            if (currentLength < _lastPosition)
            {
                // File was truncated; re-read from start
                _lastPosition = 0;
                _totalLineCount = 0;
                _displayedLineCount = 0;
                LogDocument.Blocks.Clear();
            }

            _fileStream.Position = _lastPosition;
            using var reader = new StreamReader(_fileStream, _encoding, leaveOpen: true);
            var newContent = reader.ReadToEnd();
            _lastPosition = _fileStream.Position;

            if (!string.IsNullOrEmpty(newContent))
            {
                AppendLines(newContent);
                UpdateStats();
            }
        }
        catch (IOException)
        {
            // File may be temporarily locked; skip this tick
        }
    }

    private void AppendLines(string content)
    {
        var lines = content.Split('\n');
        _highlightBrush ??= TryFindResource("AccentBrush") as Brush ?? Brushes.Yellow;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            _totalLineCount++;

            if (_filterRegex is not null && !_filterRegex.IsMatch(line))
                continue;

            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 1 };

            if (_filterRegex is not null)
            {
                HighlightMatches(para, line, _filterRegex);
            }
            else
            {
                para.Inlines.Add(new Run(line));
            }

            LogDocument.Blocks.Add(para);
            _displayedLineCount++;
        }

        TrimToMaxLines();

        if (_autoScroll)
        {
            LogViewer.ScrollToEnd();
        }
    }

    private void HighlightMatches(Paragraph para, string line, Regex regex)
    {
        var lastIndex = 0;
        foreach (Match match in regex.Matches(line))
        {
            if (match.Index > lastIndex)
            {
                para.Inlines.Add(new Run(line[lastIndex..match.Index]));
            }

            var highlighted = new Run(match.Value)
            {
                Background = _highlightBrush
            };
            para.Inlines.Add(highlighted);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < line.Length)
        {
            para.Inlines.Add(new Run(line[lastIndex..]));
        }
    }

    private void TrimToMaxLines()
    {
        while (LogDocument.Blocks.Count > MaxDisplayLines)
        {
            LogDocument.Blocks.Remove(LogDocument.Blocks.FirstBlock);
        }
    }

    private void OnApplyFilterClick(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
    }

    private void OnClearFilterClick(object sender, RoutedEventArgs e)
    {
        FilterInput.Text = string.Empty;
        _filterRegex = null;
        ReloadWithFilter();
    }

    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var pattern = FilterInput.Text.Trim();
        if (string.IsNullOrEmpty(pattern))
        {
            _filterRegex = null;
        }
        else
        {
            try
            {
                var options = RegexOptions.Compiled;
                if (ChkCaseSensitive.IsChecked != true)
                    options |= RegexOptions.IgnoreCase;

                _filterRegex = new Regex(pattern, options);
            }
            catch (RegexParseException)
            {
                MessageBox.Show(
                    L("ToolLogViewErrorInvalidRegex"),
                    L("ToolLogViewTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        ReloadWithFilter();
    }

    /// <summary>
    /// Reloads the file content with the current filter applied.
    /// </summary>
    private void ReloadWithFilter()
    {
        if (_filePath is null || _fileStream is null) return;

        _displayedLineCount = 0;
        _totalLineCount = 0;
        LogDocument.Blocks.Clear();

        try
        {
            _fileStream.Position = 0;
            using var reader = new StreamReader(_fileStream, _encoding, leaveOpen: true);
            var content = reader.ReadToEnd();
            _lastPosition = _fileStream.Position;

            AppendLines(content);
            UpdateStats();
        }
        catch (IOException)
        {
            // File access error during reload
        }
    }

    private void OnEncodingChanged(object sender, SelectionChangedEventArgs e)
    {
        _encoding = EncodingCombo.SelectedIndex switch
        {
            EncodingIndexAscii => Encoding.ASCII,
            EncodingIndexUtf16 => Encoding.Unicode,
            EncodingIndexWindows1252 => Encoding.GetEncoding(Windows1252CodePage),
            _ => Encoding.UTF8
        };

        // Reload with new encoding if a file is open
        if (_filePath is not null)
        {
            LoadFile(_filePath);
        }
    }

    private void UpdateStats()
    {
        if (_filePath is null) return;

        try
        {
            var fileInfo = new FileInfo(_filePath);
            var sizeKb = fileInfo.Length / 1024.0;
            var lastWrite = fileInfo.LastWriteTime;

            StatsText.Text = string.Format(
                L("ToolLogViewStats"),
                _displayedLineCount,
                _totalLineCount,
                sizeKb.ToString("F1"),
                lastWrite.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (IOException)
        {
            // File info may be temporarily unavailable
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpLOGVIEW");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        StopTail();
        _fileStream?.Dispose();
        _fileStream = null;
        GC.SuppressFinalize(this);
    }
}
