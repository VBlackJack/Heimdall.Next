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

namespace Heimdall.App.ViewModels;

public partial class SessionTabViewModel
{
    [ObservableProperty]
    private bool _isPostConnectRunning;

    [ObservableProperty]
    private string _postConnectProgressText = string.Empty;

    [ObservableProperty]
    private string _postConnectTooltip = string.Empty;

    private Action? _cancelPostConnectAction;

    public void SetPostConnectState(bool isRunning, string progressText, string tooltip, Action? cancelAction = null)
    {
        IsPostConnectRunning = isRunning;
        PostConnectProgressText = progressText;
        PostConnectTooltip = tooltip;
        _cancelPostConnectAction = cancelAction;
        CancelPostConnectCommand.NotifyCanExecuteChanged();
    }

    public void ClearPostConnectState()
    {
        SetPostConnectState(false, string.Empty, string.Empty, null);
    }

    partial void OnIsPostConnectRunningChanged(bool value)
    {
        CancelPostConnectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancelPostConnect))]
    private void CancelPostConnect()
    {
        _cancelPostConnectAction?.Invoke();
    }

    private bool CanCancelPostConnect() => IsPostConnectRunning && _cancelPostConnectAction is not null;
}
