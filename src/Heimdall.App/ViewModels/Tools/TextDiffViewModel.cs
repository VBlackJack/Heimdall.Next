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

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using Heimdall.Core.Matching;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class TextDiffViewModel : ObservableObject, IDisposable
{
    public const int AutoCompareDebounceMs = 500;

    private readonly ITextDiffToolService _service;
    private readonly DispatcherTimer _debounceTimer;
    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _disposed;
    private DiffStatus _lastStatus = DiffStatus.Success;
    private IReadOnlyList<DiffLine> _rawLines = [];
    private int _addedCount;
    private int _removedCount;
    private int _unchangedCount;
    private int _lastEffectiveMaxLineCount = DiffEngine.DefaultMaxLineCount;

    [ObservableProperty] private string _originalText = string.Empty;
    [ObservableProperty] private string _modifiedText = string.Empty;
    [ObservableProperty] private bool _ignoreWhitespace;
    [ObservableProperty] private bool _ignoreCase;
    [ObservableProperty] private bool _autoCompare;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _statsText = string.Empty;
    [ObservableProperty] private string _unifiedDiffText = string.Empty;

    public ObservableCollection<DiffLineDisplay> DiffLines { get; } = [];

    public TextDiffViewModel(ITextDiffToolService? service = null)
    {
        _service = service ?? new TextDiffToolService();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoCompareDebounceMs) };
        _debounceTimer.Tick += OnDebounceTick;
    }

    public void Initialize(LocalizationManager? localizer)
    {
        if (!ReferenceEquals(_localizer, localizer))
        {
            if (_localizer is not null)
            {
                _localizer.LocaleChanged -= OnLocaleChangedInternal;
            }

            _localizer = localizer;
            if (_localizer is not null)
            {
                _localizer.LocaleChanged += OnLocaleChangedInternal;
            }
        }

        OnLocaleChanged();
    }

    public void ApplyPrefill(string? argument)
    {
        if (!string.IsNullOrEmpty(argument))
        {
            OriginalText = argument;
        }
    }

    public void MarkInitialized() => _initialized = true;

    public void OnLocaleChanged()
    {
        RebuildStatsText();
        RebuildStatusText();
        RebuildUnifiedDiffText();
    }

    [RelayCommand(CanExecute = nameof(CanDiff))]
    private async Task DiffAsync()
    {
        await RunDiffAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void Swap()
    {
        (OriginalText, ModifiedText) = (ModifiedText, OriginalText);
    }

    [RelayCommand]
    private void Clear()
    {
        OriginalText = string.Empty;
        ModifiedText = string.Empty;
        _debounceTimer.Stop();
        DiffLines.Clear();
        _rawLines = [];
        _addedCount = 0;
        _removedCount = 0;
        _unchangedCount = 0;
        _lastStatus = DiffStatus.Success;
        _lastEffectiveMaxLineCount = DiffEngine.DefaultMaxLineCount;
        HasResults = false;
        RebuildStatsText();
        RebuildStatusText();
        RebuildUnifiedDiffText();
    }

    partial void OnOriginalTextChanged(string value)
    {
        ScheduleAutoCompare();
    }

    partial void OnModifiedTextChanged(string value)
    {
        ScheduleAutoCompare();
    }

    partial void OnIgnoreWhitespaceChanged(bool value)
    {
        if (_initialized && HasAnyInput)
        {
            _ = RunDiffAsync();
        }
    }

    partial void OnIgnoreCaseChanged(bool value)
    {
        if (_initialized && HasAnyInput)
        {
            _ = RunDiffAsync();
        }
    }

    partial void OnAutoCompareChanged(bool value)
    {
        if (!value)
        {
            _debounceTimer.Stop();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        DiffCommand.NotifyCanExecuteChanged();
        RebuildStatusText();
    }

    internal void FlushPendingDiff()
    {
        _debounceTimer.Stop();
        if (_disposed || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ApplyComputationResult(ComputeDiffState());
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceTick;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChangedInternal;
            _localizer = null;
        }

        GC.SuppressFinalize(this);
    }

    private bool HasAnyInput => !string.IsNullOrEmpty(OriginalText) || !string.IsNullOrEmpty(ModifiedText);

    private bool CanDiff() => !_disposed && !IsBusy;

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _ = RunDiffAsync();
    }

    private void ScheduleAutoCompare()
    {
        if (!_initialized || !AutoCompare)
        {
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task RunDiffAsync()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var state = await Task.Run(ComputeDiffState);
            ApplyComputationResult(state);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private DiffComputationResult ComputeDiffState()
    {
        var options = new DiffOptions(IgnoreWhitespace, IgnoreCase, null);
        var effectiveMaxLineCount = options.MaxLineCount ?? DiffEngine.DefaultMaxLineCount;
        var result = _service.Diff(OriginalText, ModifiedText, options);
        var displayLines = result.Status == DiffStatus.Success
            ? BuildDisplayItems(result.Lines)
            : [];

        return new DiffComputationResult(result, displayLines, effectiveMaxLineCount);
    }

    private List<DiffLineDisplay> BuildDisplayItems(IReadOnlyList<DiffLine> lines)
    {
        var displayLines = new List<DiffLineDisplay>();
        var leftLine = 0;
        var rightLine = 0;
        var index = 0;

        while (index < lines.Count)
        {
            var line = lines[index];
            switch (line.Kind)
            {
                case DiffLineKind.Unchanged:
                    leftLine++;
                    rightLine++;
                    displayLines.Add(new DiffLineDisplay(
                        leftLine.ToString(CultureInfo.InvariantCulture),
                        rightLine.ToString(CultureInfo.InvariantCulture),
                        " ",
                        DiffLineKind.Unchanged,
                        CreatePlainSegments(line.Text)));
                    index++;
                    break;

                case DiffLineKind.Removed when index + 1 < lines.Count && lines[index + 1].Kind == DiffLineKind.Added:
                    var added = lines[index + 1];
                    var wordDiff = _service.WordDiff(line.Text, added.Text);
                    leftLine++;
                    displayLines.Add(new DiffLineDisplay(
                        leftLine.ToString(CultureInfo.InvariantCulture),
                        string.Empty,
                        "-",
                        DiffLineKind.Removed,
                        wordDiff.OldSegments));
                    rightLine++;
                    displayLines.Add(new DiffLineDisplay(
                        string.Empty,
                        rightLine.ToString(CultureInfo.InvariantCulture),
                        "+",
                        DiffLineKind.Added,
                        wordDiff.NewSegments));
                    index += 2;
                    break;

                case DiffLineKind.Removed:
                    leftLine++;
                    displayLines.Add(new DiffLineDisplay(
                        leftLine.ToString(CultureInfo.InvariantCulture),
                        string.Empty,
                        "-",
                        DiffLineKind.Removed,
                        CreatePlainSegments(line.Text)));
                    index++;
                    break;

                case DiffLineKind.Added:
                    rightLine++;
                    displayLines.Add(new DiffLineDisplay(
                        string.Empty,
                        rightLine.ToString(CultureInfo.InvariantCulture),
                        "+",
                        DiffLineKind.Added,
                        CreatePlainSegments(line.Text)));
                    index++;
                    break;
            }
        }

        return displayLines;
    }

    private void ApplyComputationResult(DiffComputationResult computationResult)
    {
        _lastStatus = computationResult.Result.Status;
        _lastEffectiveMaxLineCount = computationResult.EffectiveMaxLineCount;
        _rawLines = computationResult.Result.Lines;
        _addedCount = computationResult.Result.AddedCount;
        _removedCount = computationResult.Result.RemovedCount;
        _unchangedCount = computationResult.Result.UnchangedCount;

        DiffLines.Clear();
        if (computationResult.Result.Status == DiffStatus.Success)
        {
            foreach (var line in computationResult.DisplayLines)
            {
                DiffLines.Add(line);
            }

            HasResults = DiffLines.Count > 0;
        }
        else
        {
            HasResults = false;
        }

        RebuildStatsText();
        RebuildStatusText();
        RebuildUnifiedDiffText();
    }

    private void RebuildStatsText()
    {
        StatsText = HasResults
            ? string.Format(CultureInfo.CurrentCulture, L("ToolDiffStats"), _addedCount, _removedCount, _unchangedCount)
            : string.Empty;
    }

    private void RebuildStatusText()
    {
        if (IsBusy)
        {
            StatusText = L("ToolTextDiffComparing");
            return;
        }

        if (_lastStatus == DiffStatus.InputTooLarge)
        {
            StatusText = string.Format(CultureInfo.CurrentCulture, L("ToolDiffStatusTooLarge"), _lastEffectiveMaxLineCount);
            return;
        }

        StatusText = HasResults
            ? string.Format(CultureInfo.CurrentCulture, L("ToolDiffStatusDone"), DiffLines.Count)
            : string.Empty;
    }

    private void RebuildUnifiedDiffText()
    {
        if (_lastStatus != DiffStatus.Success || _rawLines.Count == 0)
        {
            UnifiedDiffText = string.Empty;
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(L("ToolDiffOriginalHeader"));
        builder.AppendLine(L("ToolDiffModifiedHeader"));
        foreach (var line in _rawLines)
        {
            var prefix = line.Kind switch
            {
                DiffLineKind.Unchanged => " ",
                DiffLineKind.Removed => "-",
                DiffLineKind.Added => "+",
                _ => string.Empty,
            };
            builder.Append(prefix).AppendLine(line.Text);
        }

        UnifiedDiffText = builder.ToString();
    }

    private void OnLocaleChangedInternal(string _)
    {
        OnLocaleChanged();
    }

    private string L(string key) => _localizer?[key] ?? key;

    private static IReadOnlyList<WordSegment> CreatePlainSegments(string text)
        => string.IsNullOrEmpty(text) ? [] : [new WordSegment(text, false)];

    private sealed record DiffComputationResult(
        TextDiffResult Result,
        IReadOnlyList<DiffLineDisplay> DisplayLines,
        int EffectiveMaxLineCount);
}
