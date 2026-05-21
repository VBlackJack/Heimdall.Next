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
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed class ServerDialogWinRmTests
{
    [Fact]
    public void SelectProtocol_WinRm_DefaultsToHttpPort()
    {
        ServerDialogViewModel vm = new ServerDialogViewModel();

        vm.SelectProtocolCommand.Execute("WINRM");

        Assert.Equal("WINRM", vm.ConnectionType);
        Assert.Equal(DefaultPorts.WinRmHttp, vm.WinRmPort);
        Assert.Equal(DefaultPorts.WinRmHttp, vm.EndpointPort);
    }

    [Fact]
    public void WinRmUseSsl_TogglesDefaultPorts()
    {
        ServerDialogViewModel vm = new ServerDialogViewModel { ConnectionType = "WINRM" };

        vm.WinRmUseSsl = true;

        Assert.Equal(DefaultPorts.WinRmHttps, vm.WinRmPort);
        Assert.Equal(DefaultPorts.WinRmHttps, vm.EndpointPort);

        vm.WinRmUseSsl = false;

        Assert.Equal(DefaultPorts.WinRmHttp, vm.WinRmPort);
        Assert.Equal(DefaultPorts.WinRmHttp, vm.EndpointPort);
    }

    [Fact]
    public void WinRmUseSsl_PreservesCustomPort()
    {
        ServerDialogViewModel vm = new ServerDialogViewModel
        {
            ConnectionType = "WINRM",
            WinRmPort = 12345
        };

        vm.WinRmUseSsl = true;
        Assert.Equal(12345, vm.WinRmPort);

        vm.WinRmUseSsl = false;
        Assert.Equal(12345, vm.WinRmPort);
    }

    [Fact]
    public void ToDto_FromDto_PreservesWinRmFields()
    {
        ServerDialogViewModel vm = new ServerDialogViewModel
        {
            DisplayName = "WinRM",
            RemoteServer = "server01.contoso.test",
            ConnectionType = "WINRM",
            WinRmPort = DefaultPorts.WinRmHttps,
            WinRmUseSsl = true,
            WinRmIdentityMode = WinRmIdentityMode.Credential,
            WinRmUsername = @"CONTOSO\admin",
            ExistingWinRmPasswordEncrypted = "encrypted-password"
        };

        ServerProfileDto dto = vm.ToDto();

        Assert.Equal("WINRM", dto.ConnectionType);
        Assert.Equal(DefaultPorts.WinRmHttps, dto.WinRmPort);
        Assert.True(dto.WinRmUseSsl);
        Assert.Equal(WinRmIdentityMode.Credential, dto.WinRmIdentityMode);
        Assert.Equal(@"CONTOSO\admin", dto.WinRmUsername);
        Assert.Equal("encrypted-password", dto.WinRmPasswordEncrypted);
        Assert.Null(dto.SshGatewayId);
        Assert.True(dto.UseDirectConnection);

        ServerDialogViewModel roundTripped = ServerDialogViewModel.FromDto(dto);

        Assert.Equal(DefaultPorts.WinRmHttps, roundTripped.WinRmPort);
        Assert.True(roundTripped.WinRmUseSsl);
        Assert.True(roundTripped.IsWinRmCredentialIdentity);
        Assert.Equal(@"CONTOSO\admin", roundTripped.WinRmUsername);
        Assert.Equal("encrypted-password", roundTripped.ExistingWinRmPasswordEncrypted);
    }

    [Fact]
    public void FromDto_WinRmMissingPort_UsesSslAwareDefault()
    {
        ServerDialogViewModel vm = ServerDialogViewModel.FromDto(new ServerProfileDto
        {
            ConnectionType = "WINRM",
            WinRmPort = 0,
            WinRmUseSsl = true
        });

        Assert.Equal(DefaultPorts.WinRmHttps, vm.WinRmPort);
    }
}
