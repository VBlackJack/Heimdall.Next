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

using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.App.Services;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class IpConverterViewModel : ObservableObject, IDisposable
{
    private readonly IIpConverterToolService _service;
    private LocalizationManager? _localizer;
    private bool _disposed;

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _dottedText = string.Empty;
    [ObservableProperty] private string _decimalText = string.Empty;
    [ObservableProperty] private string _hexText = string.Empty;
    [ObservableProperty] private string _binaryText = string.Empty;
    [ObservableProperty] private string _mappedIpv6Text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyState))]
    private bool _hasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyState))]
    private bool _hasResult;

    public bool IsEmptyState => !HasError && !HasResult;

    public IpConverterViewModel(IIpConverterToolService? service = null)
    {
        _service = service ?? new IpConverterToolService();
    }

    public void Initialize(LocalizationManager? localizer)
    {
        if (ReferenceEquals(_localizer, localizer))
        {
            return;
        }

        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
        }

        _localizer = localizer;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged += OnLocaleChanged;
        }
    }

    public void Reset()
    {
        InputText = string.Empty;
        ClearResultFields();
        HasError = false;
        HasResult = false;
    }

    public void PrefillInput(string? targetHost)
    {
        if (string.IsNullOrWhiteSpace(targetHost))
        {
            return;
        }

        InputText = targetHost;
    }

    partial void OnInputTextChanged(string value)
    {
        Convert(value);
    }

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

    private void Convert(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ClearResultFields();
            HasError = false;
            HasResult = false;
            return;
        }

        if (_service.TryConvert(trimmed, out var result))
        {
            DottedText = result.Dotted;
            DecimalText = result.Decimal;
            HexText = result.Hex;
            BinaryText = result.Binary;
            MappedIpv6Text = result.MappedIpv6;
            HasError = false;
            HasResult = true;
            return;
        }

        ClearResultFields();
        HasError = true;
        HasResult = false;
    }

    private void ClearResultFields()
    {
        DottedText = string.Empty;
        DecimalText = string.Empty;
        HexText = string.Empty;
        BinaryText = string.Empty;
        MappedIpv6Text = string.Empty;
    }

    private void OnLocaleChanged(string _)
    {
    }
}
