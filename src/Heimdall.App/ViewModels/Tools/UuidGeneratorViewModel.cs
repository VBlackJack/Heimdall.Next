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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Identifiers;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class UuidGeneratorViewModel : ObservableObject, IDisposable
{
    public const int MinBatchCount = 1;
    public const int MaxBatchCount = 100;
    public const int DefaultBatchCount = 10;

    private readonly IUuidGeneratorToolService _service;
    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _disposed;
    private Guid? _lastSingleGuid;
    private IReadOnlyList<Guid> _lastBatchGuids = [];

    [ObservableProperty] private UuidVersion _selectedVersion = UuidVersion.V4;
    [ObservableProperty] private bool _uppercase;
    [ObservableProperty] private bool _withHyphens = true;
    [ObservableProperty] private string _singleResult = string.Empty;
    [ObservableProperty] private string _batchResults = string.Empty;
    [ObservableProperty] private string _batchCountText = string.Empty;
    [ObservableProperty] private string _resultLabelText = string.Empty;

    public UuidGeneratorViewModel(IUuidGeneratorToolService? service = null)
    {
        _service = service ?? new UuidGeneratorToolService();
    }

    public void Initialize(LocalizationManager? localizer)
    {
        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
        }

        _localizer = localizer;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged += OnLocaleChanged;
        }

        BatchCountText = DefaultBatchCount.ToString(CultureInfo.InvariantCulture);
        RebuildLocalizedStrings();
        Generate();
    }

    public void MarkInitialized() => _initialized = true;

    public void OnLocaleChanged() => RebuildLocalizedStrings();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
            _localizer = null;
        }

        GC.SuppressFinalize(this);
    }

    partial void OnSelectedVersionChanged(UuidVersion value)
    {
        if (!_initialized || _disposed)
        {
            return;
        }

        RebuildLocalizedStrings();
        Generate();
        if (_lastBatchGuids.Count > 0)
        {
            GenerateBatchWithCount(_lastBatchGuids.Count);
        }
    }

    partial void OnUppercaseChanged(bool value)
    {
        if (_initialized && !_disposed)
        {
            ReformatStored();
        }
    }

    partial void OnWithHyphensChanged(bool value)
    {
        if (_initialized && !_disposed)
        {
            ReformatStored();
        }
    }

    [RelayCommand]
    private void Generate()
    {
        if (_disposed)
        {
            return;
        }

        var guid = _service.Generate(SelectedVersion);
        _lastSingleGuid = guid;
        SingleResult = _service.Format(guid, CurrentFormat);
    }

    [RelayCommand]
    private void GenerateBatch()
    {
        if (_disposed)
        {
            return;
        }

        if (!int.TryParse(BatchCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            count = MinBatchCount;
        }

        count = Math.Clamp(count, MinBatchCount, MaxBatchCount);
        BatchCountText = count.ToString(CultureInfo.InvariantCulture);
        GenerateBatchWithCount(count);
    }

    private UuidFormat CurrentFormat => new(Uppercase, WithHyphens);

    private void GenerateBatchWithCount(int count)
    {
        var guids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            guids[i] = _service.Generate(SelectedVersion);
        }

        _lastBatchGuids = guids;
        BatchResults = FormatBatch(guids, CurrentFormat);
    }

    private void ReformatStored()
    {
        if (_lastSingleGuid.HasValue)
        {
            SingleResult = _service.Format(_lastSingleGuid.Value, CurrentFormat);
        }

        if (_lastBatchGuids.Count > 0)
        {
            BatchResults = FormatBatch(_lastBatchGuids, CurrentFormat);
        }
    }

    private string FormatBatch(IReadOnlyList<Guid> guids, UuidFormat format)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < guids.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(_service.Format(guids[i], format));
        }

        return builder.ToString();
    }

    private void RebuildLocalizedStrings()
    {
        ResultLabelText = L(SelectedVersion == UuidVersion.V7 ? "ToolUuidResultLabelV7" : "ToolUuidResultLabel");
    }

    private void OnLocaleChanged(string _) => OnLocaleChanged();

    private string L(string key) => _localizer?[key] ?? key;
}
