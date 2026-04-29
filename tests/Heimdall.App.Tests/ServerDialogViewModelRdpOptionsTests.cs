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
}
