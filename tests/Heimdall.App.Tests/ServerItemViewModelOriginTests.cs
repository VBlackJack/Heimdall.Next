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

using Heimdall.App.ViewModels;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed class ServerItemViewModelOriginTests
{
    [Fact]
    public void Origin_DefaultIsManual_AndIsOriginBadgeVisibleIsFalse()
    {
        var vm = new ServerItemViewModel();

        Assert.Equal(ProfileOrigin.Manual, vm.Origin);
        Assert.False(vm.IsOriginBadgeVisible);
    }

    [Fact]
    public void Origin_SetToImportOpenSsh_RaisesPropertyChangedForAllThreeDerivedProperties()
    {
        var vm = new ServerItemViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changed.Add(args.PropertyName);
            }
        };

        vm.Origin = ProfileOrigin.ImportOpenSsh;

        Assert.Contains(nameof(ServerItemViewModel.Origin), changed);
        Assert.Contains(nameof(ServerItemViewModel.OriginBadgeCode), changed);
        Assert.Contains(nameof(ServerItemViewModel.OriginDisplayName), changed);
        Assert.Contains(nameof(ServerItemViewModel.IsOriginBadgeVisible), changed);
    }

    [Theory]
    [InlineData(ProfileOrigin.Manual)]
    [InlineData(ProfileOrigin.ImportRdp)]
    [InlineData(ProfileOrigin.ImportOpenSsh)]
    [InlineData(ProfileOrigin.ImportPutty)]
    [InlineData(ProfileOrigin.ImportMRemoteNg)]
    [InlineData(ProfileOrigin.ImportMobaXterm)]
    [InlineData(ProfileOrigin.ImportRdcMan)]
    public void OriginBadgeCode_ForEachOrigin_MatchesDisplayHelper(ProfileOrigin origin)
    {
        var vm = new ServerItemViewModel
        {
            Origin = origin
        };

        Assert.Equal(ProfileOriginDisplay.GetBadgeCode(origin), vm.OriginBadgeCode);
    }
}
