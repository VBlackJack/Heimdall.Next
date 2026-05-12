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

namespace Heimdall.App.ViewModels.Settings;

public sealed partial class RdpResolutionPresetItemViewModel : ObservableObject
{
    private readonly Func<string> _invalidMessageProvider;

    [ObservableProperty]
    private string _width;

    [ObservableProperty]
    private string _height;

    public RdpResolutionPresetItemViewModel(
        string width,
        string height,
        Func<string> invalidMessageProvider)
    {
        _width = width;
        _height = height;
        _invalidMessageProvider = invalidMessageProvider;
    }

    public bool IsValid => TryGetDimensions(out _, out _);

    public string? Error => IsValid ? null : _invalidMessageProvider();

    public string? ToPresetString()
    {
        return TryGetDimensions(out var width, out var height)
            ? $"{width}x{height}"
            : null;
    }

    public static RdpResolutionPresetItemViewModel FromPreset(
        string preset,
        Func<string> invalidMessageProvider)
    {
        var parts = (preset ?? string.Empty).Split(['x', 'X'], 2);
        return parts.Length == 2
            ? new RdpResolutionPresetItemViewModel(parts[0].Trim(), parts[1].Trim(), invalidMessageProvider)
            : new RdpResolutionPresetItemViewModel((preset ?? string.Empty).Trim(), string.Empty, invalidMessageProvider);
    }

    partial void OnWidthChanged(string value) => RefreshValidationState();

    partial void OnHeightChanged(string value) => RefreshValidationState();

    private void RefreshValidationState()
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Error));
    }

    private bool TryGetDimensions(out int width, out int height)
    {
        width = 0;
        height = 0;
        return int.TryParse(Width.Trim(), out width)
            && int.TryParse(Height.Trim(), out height)
            && width > 0
            && height > 0;
    }
}
