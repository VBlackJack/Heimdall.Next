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

using System.Net.Sockets;
using Heimdall.App.Services;
using Heimdall.App.Services.Handlers;
using Heimdall.App.Services.WinRm;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;

namespace Heimdall.App.Tests;

public sealed class WinRmHandlerPreflightTests
{
    [Fact]
    public async Task ConnectAsync_WhenPreflightFails_ReturnsFailure()
    {
        WinRmPreflight preflight = new WinRmPreflight(
            tcpProbe: DnsFailureProbeAsync);
        WinRmHandler handler = new WinRmHandler(
            new PassThroughTunnelService(),
            new ConnectionStateMachine(),
            new LocalizationManager(),
            preflight);

        ConnectionResult result = await handler.ConnectAsync(
            CreateServer(),
            new AppSettings(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Null(result.Session);
    }

    private static Task DnsFailureProbeAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken token)
    {
        throw new SocketException((int)SocketError.HostNotFound);
    }

    private static ServerProfileDto CreateServer()
        => new ServerProfileDto
        {
            Id = "winrm-preflight-test",
            DisplayName = "WinRM Preflight Test",
            ConnectionType = "WINRM",
            RemoteServer = "server01.contoso.local",
            WinRmPort = DefaultPorts.WinRmHttp,
            WinRmUseSsl = false,
            WinRmIdentityMode = WinRmIdentityMode.CurrentUser
        };

    private sealed class PassThroughTunnelService : ITunnelService
    {
        public Task<(bool Success, bool UsesTunnel, string Host, int Port, string? ErrorMessage)> SetupTunnelIfNeededAsync(
            ServerProfileDto server,
            int remotePort,
            AppSettings settings,
            CancellationToken ct)
        {
            return Task.FromResult((true, false, server.RemoteServer, remotePort, (string?)null));
        }

        public void UpdateSettings(AppSettings settings)
        {
        }

        public Heimdall.Ssh.TunnelForwardedPortFailure? GetRecentForwardedPortFailure(int localPort) => null;
    }
}
