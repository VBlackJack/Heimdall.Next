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

public sealed partial class UrlEncoderViewModel : ObservableObject, IDisposable
{
    private readonly IUrlEncoderToolService _service;
    private LocalizationManager? _localizer;
    private bool _suppressPropagation;
    private bool _initialized;
    private bool _disposed;

    [ObservableProperty] private string _decodedText = string.Empty;
    [ObservableProperty] private string _encodedText = string.Empty;
    [ObservableProperty] private bool _componentMode;
    [ObservableProperty] private bool _hasDecodeError;

    public UrlEncoderViewModel(IUrlEncoderToolService? service = null)
    {
        _service = service ?? new UrlEncoderToolService();
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

    public void MarkInitialized() => _initialized = true;

    public void Reset()
    {
        _suppressPropagation = true;
        try
        {
            DecodedText = string.Empty;
            EncodedText = string.Empty;
            HasDecodeError = false;
        }
        finally
        {
            _suppressPropagation = false;
        }

        _initialized = false;
    }

    public void PrefillDecoded(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            MarkInitialized();
            return;
        }

        _initialized = true;
        DecodedText = argument;
    }

    partial void OnDecodedTextChanged(string value)
    {
        if (_suppressPropagation || !_initialized)
        {
            return;
        }

        _suppressPropagation = true;
        try
        {
            EncodedText = string.IsNullOrEmpty(value)
                ? string.Empty
                : _service.Encode(value, ComponentMode);
            HasDecodeError = false;
        }
        finally
        {
            _suppressPropagation = false;
        }
    }

    partial void OnEncodedTextChanged(string value)
    {
        if (_suppressPropagation || !_initialized)
        {
            return;
        }

        _suppressPropagation = true;
        try
        {
            if (string.IsNullOrEmpty(value))
            {
                DecodedText = string.Empty;
                HasDecodeError = false;
                return;
            }

            try
            {
                DecodedText = _service.Decode(value);
                HasDecodeError = false;
            }
            catch (UriFormatException)
            {
                DecodedText = string.Empty;
                HasDecodeError = true;
            }
        }
        finally
        {
            _suppressPropagation = false;
        }
    }

    partial void OnComponentModeChanged(bool value)
    {
        if (!_initialized || string.IsNullOrEmpty(DecodedText))
        {
            return;
        }

        _suppressPropagation = true;
        try
        {
            EncodedText = _service.Encode(DecodedText, value);
            HasDecodeError = false;
        }
        finally
        {
            _suppressPropagation = false;
        }
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

    private void OnLocaleChanged(string _)
    {
    }
}
