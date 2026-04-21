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
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class UlidGeneratorViewModel : ObservableObject, IDisposable
{
    public const int MinBatchCount = 1;
    public const int MaxBatchCount = 100;
    public const int DefaultBatchCount = 10;

    private readonly IUlidGeneratorToolService _service;
    private bool _disposed;

    [ObservableProperty] private string _singleResult = string.Empty;
    [ObservableProperty] private string _batchResults = string.Empty;
    [ObservableProperty] private string _batchCountText = string.Empty;

    public UlidGeneratorViewModel(IUlidGeneratorToolService? service = null)
    {
        _service = service ?? new UlidGeneratorToolService();
    }

    public void Initialize(LocalizationManager? _)
    {
        BatchCountText = DefaultBatchCount.ToString(CultureInfo.InvariantCulture);
        Generate();
    }

    public void MarkInitialized()
    {
    }

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private void Generate()
    {
        if (_disposed)
        {
            return;
        }

        SingleResult = _service.Generate();
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

        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(_service.Generate());
        }

        BatchResults = builder.ToString();
    }
}
