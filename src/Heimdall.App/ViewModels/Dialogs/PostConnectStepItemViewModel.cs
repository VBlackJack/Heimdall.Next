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
using Heimdall.Core.Models;

namespace Heimdall.App.ViewModels.Dialogs;

public sealed partial class PostConnectStepItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private string? _commandLibraryId;

    [ObservableProperty]
    private Dictionary<string, string>? _commandLibraryParams;

    [ObservableProperty]
    private int _delayMs = 150;

    [ObservableProperty]
    private PostConnectFailurePolicy _onFailure = PostConnectFailurePolicy.Continue;

    [ObservableProperty]
    private string? _linkedActionTitle;

    [ObservableProperty]
    private bool _isBroken;

    public bool IsLinked => !string.IsNullOrWhiteSpace(CommandLibraryId);

    public string DisplayInput => IsLinked
        ? LinkedActionTitle ?? CommandLibraryId ?? string.Empty
        : Input;

    partial void OnInputChanged(string value) => OnPropertyChanged(nameof(DisplayInput));

    partial void OnCommandLibraryIdChanged(string? value)
    {
        OnPropertyChanged(nameof(IsLinked));
        OnPropertyChanged(nameof(DisplayInput));
    }

    partial void OnLinkedActionTitleChanged(string? value) => OnPropertyChanged(nameof(DisplayInput));

    public PostConnectStep ToModel()
    {
        return new PostConnectStep
        {
            Id = Id,
            Enabled = Enabled,
            Input = Input,
            CommandLibraryId = CommandLibraryId,
            CommandLibraryParams = CommandLibraryParams is null ? null : new Dictionary<string, string>(CommandLibraryParams, StringComparer.Ordinal),
            DelayMs = DelayMs,
            OnFailure = OnFailure
        };
    }

    public static PostConnectStepItemViewModel FromModel(PostConnectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new PostConnectStepItemViewModel
        {
            Id = step.Id,
            Enabled = step.Enabled,
            Input = step.Input,
            CommandLibraryId = step.CommandLibraryId,
            CommandLibraryParams = step.CommandLibraryParams is null ? null : new Dictionary<string, string>(step.CommandLibraryParams, StringComparer.Ordinal),
            DelayMs = step.DelayMs,
            OnFailure = step.OnFailure
        };
    }
}
