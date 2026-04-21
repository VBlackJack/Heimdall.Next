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
using TwinShell.Core.Models;

namespace Heimdall.App.ViewModels.Dialogs;

public sealed partial class TemplateParameterInputViewModel : ObservableObject
{
    private bool _suppressAutoPrefilledReset;

    public string Name { get; }
    public string Label { get; }
    public string Type { get; }
    public bool Required { get; }
    public string? Description { get; }

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _isAutoPrefilled;

    public TemplateParameterInputViewModel(TemplateParameter source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Name = source.Name;
        Label = source.Label;
        Type = source.Type ?? "string";
        Required = source.Required;
        Description = source.Description;
        _value = source.DefaultValue ?? string.Empty;
    }

    public void ApplyPrefilledValue(string prefilled)
    {
        _suppressAutoPrefilledReset = true;
        Value = prefilled;
        IsAutoPrefilled = true;
        _suppressAutoPrefilledReset = false;
    }

    partial void OnValueChanged(string value)
    {
        if (_suppressAutoPrefilledReset)
        {
            return;
        }

        IsAutoPrefilled = false;
    }
}
