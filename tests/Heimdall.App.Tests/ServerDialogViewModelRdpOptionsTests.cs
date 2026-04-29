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

using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

public sealed class ServerDialogViewModelRdpOptionsTests
{
    [Fact]
    public void Default_values_for_external_mode_fields_are_false()
    {
        var dto = new ServerProfileDto();

        Assert.False(dto.RdpAdminMode);
        Assert.False(dto.RdpFullScreen);
    }

    [Fact]
    public void RdpAdminMode_and_RdpFullScreen_round_trip()
    {
        var vm = new ServerDialogViewModel
        {
            DisplayName = "Server",
            RemoteServer = "server.example.com",
            ConnectionType = "RDP",
            RdpAdminMode = true,
            RdpFullScreen = true
        };

        var dto = vm.ToDto();

        Assert.True(dto.RdpAdminMode);
        Assert.True(dto.RdpFullScreen);

        var vm2 = ServerDialogViewModel.FromDto(dto);

        Assert.True(vm2.RdpAdminMode);
        Assert.True(vm2.RdpFullScreen);
    }

    [Theory]
    [InlineData(0x000)]
    [InlineData(0x001)]
    [InlineData(0x100)]
    [InlineData(0x001 | 0x008 | 0x080)]
    [InlineData(0x001 | 0x002 | 0x004 | 0x008 | 0x020 | 0x080 | 0x100)]
    public void Performance_flags_round_trip_through_bool_properties(int flags)
    {
        var dto = new ServerProfileDto { RdpPerformanceFlags = flags };

        var vm = ServerDialogViewModel.FromDto(dto);

        Assert.Equal((flags & 0x001) != 0, vm.RdpPerfDisableWallpaper);
        Assert.Equal((flags & 0x002) != 0, vm.RdpPerfDisableDrag);
        Assert.Equal((flags & 0x004) != 0, vm.RdpPerfDisableAnimations);
        Assert.Equal((flags & 0x008) != 0, vm.RdpPerfDisableThemes);
        Assert.Equal((flags & 0x020) != 0, vm.RdpPerfDisableCursorShadow);
        Assert.Equal((flags & 0x080) != 0, vm.RdpPerfEnableFontSmoothing);
        Assert.Equal((flags & 0x100) != 0, vm.RdpPerfEnableComposition);

        var roundTripped = vm.ToDto();
        Assert.Equal(flags, roundTripped.RdpPerformanceFlags);
    }

    [Fact]
    public void Mutating_perf_bool_updates_the_DTO_bitmask()
    {
        var vm = ServerDialogViewModel.FromDto(new ServerProfileDto { RdpPerformanceFlags = 0 });

        vm.RdpPerfDisableThemes = true;

        var dto = vm.ToDto();
        Assert.Equal(0x008, dto.RdpPerformanceFlags);
    }

    [Fact]
    public void Mutating_performance_flags_updates_bool_properties()
    {
        var vm = ServerDialogViewModel.FromDto(new ServerProfileDto { RdpPerformanceFlags = 0 });

        vm.RdpPerformanceFlags = 0x001 | 0x080;

        Assert.True(vm.RdpPerfDisableWallpaper);
        Assert.True(vm.RdpPerfEnableFontSmoothing);
        Assert.False(vm.RdpPerfDisableThemes);
    }
}
