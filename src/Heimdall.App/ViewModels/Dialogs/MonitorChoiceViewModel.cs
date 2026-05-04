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

namespace Heimdall.App.ViewModels.Dialogs;

/// <summary>
/// Selectable local monitor shown by the RDP multi-monitor picker.
/// </summary>
public sealed partial class MonitorChoiceViewModel : ObservableObject
{
    public MonitorChoiceViewModel(
        int index,
        int width,
        int height,
        bool isPrimary,
        string deviceName,
        string label,
        bool isSelected)
    {
        Index = index;
        Width = width;
        Height = height;
        IsPrimary = isPrimary;
        DeviceName = deviceName;
        Label = label;
        _isSelected = isSelected;
    }

    public int Index { get; }

    public int Width { get; }

    public int Height { get; }

    public bool IsPrimary { get; }

    public string DeviceName { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;
}
