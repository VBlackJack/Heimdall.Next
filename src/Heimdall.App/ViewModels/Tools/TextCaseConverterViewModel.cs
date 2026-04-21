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
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Codecs;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class TextCaseConverterViewModel : ObservableObject, IDisposable
{
    private readonly ITextCaseConverterService _service;
    private LocalizationManager? _localizer;
    private bool _disposed;

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _outputText = string.Empty;
    [ObservableProperty] private TextCaseStyle? _selectedStyle;
    [ObservableProperty] private bool _hasResult;

    public TextCaseConverterViewModel(ITextCaseConverterService? service = null)
    {
        _service = service ?? new TextCaseConverterService();
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

    [RelayCommand]
    private void Convert(TextCaseStyle style)
    {
        SelectedStyle = style;
        OutputText = _service.Convert(InputText, style);
        HasResult = true;
    }

    partial void OnInputTextChanged(string value)
    {
        if (SelectedStyle is null)
        {
            return;
        }

        OutputText = _service.Convert(value, SelectedStyle.Value);
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
