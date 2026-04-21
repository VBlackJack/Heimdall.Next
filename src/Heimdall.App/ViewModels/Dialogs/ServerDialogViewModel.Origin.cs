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
using Heimdall.App.ViewModels;
using Heimdall.Core.Models;

namespace Heimdall.App.ViewModels.Dialogs;

public partial class ServerDialogViewModel
{
    [ObservableProperty]
    private ProfileOrigin _origin = ProfileOrigin.Manual;

    public string OriginDisplayName => ProfileOriginDisplay.GetDisplayName(Origin, Localizer);

    public bool IsOriginBadgeVisible => Origin != ProfileOrigin.Manual;

    partial void OnOriginChanged(ProfileOrigin value)
    {
        OnPropertyChanged(nameof(OriginDisplayName));
        OnPropertyChanged(nameof(IsOriginBadgeVisible));
    }
}
