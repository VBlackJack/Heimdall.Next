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

using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Ssh;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;

namespace Heimdall.App.Tests;

public sealed class TunnelReuseIdentityTests
{
    [Fact]
    public async Task SetupTunnelIfNeededAsync_ReusedTunnelReturnsExistingLocalBindHost()
    {
        using var tunnelManager = new TunnelManager();
        const string gatewayId = "gw-A";
        const string remoteHost = "10.0.0.5";
        const int remotePort = 3389;
        const int localPort = 50123;
        string localBindHost = LoopbackBinding.FormatAlias(2);
        var gateway = new SshGatewayDto
        {
            Id = gatewayId,
            Host = "gateway.example.test",
            User = "ssh-user"
        };
        TunnelInfo existing = MakeTunnel(
            TunnelService.BuildGatewayChainKey([gateway]),
            remoteHost,
            remotePort) with
        {
            LocalPort = localPort,
            LocalBindHost = localBindHost
        };

        Assert.True(tunnelManager.TryRegisterExternalTunnel(existing, new TestDisposable(), () => true));
        var service = new TunnelService(
            tunnelManager,
            new HostKeyStore(),
            new HostKeyTrustService(new HostKeyStore()),
            new ConnectionStateMachine(),
            new LocalizationManager(),
            RejectingHostKeyVerifier.Instance);
        var server = new ServerProfileDto
        {
            Id = "server-1",
            RemoteServer = remoteHost,
            RemotePort = remotePort,
            SshGatewayId = gatewayId,
            UseDirectConnection = false
        };
        var settings = new AppSettings { SshGateways = [gateway] };

        var result = await service.SetupTunnelIfNeededAsync(
            server,
            remotePort,
            settings,
            CancellationToken.None,
            preferDistinctLoopback: true);

        Assert.True(result.Success);
        Assert.True(result.UsesTunnel);
        Assert.Equal(localBindHost, result.Host);
        Assert.Equal(localPort, result.Port);
    }

    [Fact]
    public void FindReusableTunnel_SameChainAndTarget_ReturnsExistingTunnel()
    {
        var existing = MakeTunnel(gatewayChainKey: "gw-A");

        var result = TunnelService.FindReusableTunnel(
            [existing],
            "gw-A",
            "10.0.0.5",
            3389,
            socksProxyPort: 0,
            remoteBindPort: 0);

        Assert.Same(existing, result);
    }

    [Fact]
    public void FindReusableTunnel_DifferentChainSameTarget_ReturnsNull()
    {
        var existing = MakeTunnel(gatewayChainKey: "gw-A");

        var result = TunnelService.FindReusableTunnel(
            [existing],
            "gw-B",
            "10.0.0.5",
            3389,
            socksProxyPort: 0,
            remoteBindPort: 0);

        Assert.Null(result);
    }

    [Fact]
    public void FindReusableTunnel_SameChainDifferentTarget_ReturnsNull()
    {
        var existing = MakeTunnel(gatewayChainKey: "gw-A");

        var result = TunnelService.FindReusableTunnel(
            [existing],
            "gw-A",
            "10.0.0.5",
            3390,
            socksProxyPort: 0,
            remoteBindPort: 0);

        Assert.Null(result);
    }

    [Fact]
    public void FindReusableTunnel_DifferentSocksProxyPort_ReturnsNull()
    {
        var existing = MakeTunnel(gatewayChainKey: "gw-A", socksProxyPort: 1080);

        var result = TunnelService.FindReusableTunnel(
            [existing],
            "gw-A",
            "10.0.0.5",
            3389,
            socksProxyPort: 1081,
            remoteBindPort: 0);

        Assert.Null(result);
    }

    [Fact]
    public void FindReusableTunnel_DifferentRemoteBindPort_ReturnsNull()
    {
        var existing = MakeTunnel(gatewayChainKey: "gw-A", remoteBindPort: 2222);

        var result = TunnelService.FindReusableTunnel(
            [existing],
            "gw-A",
            "10.0.0.5",
            3389,
            socksProxyPort: 0,
            remoteBindPort: 2223);

        Assert.Null(result);
    }

    [Fact]
    public void FindReusableTunnel_DeadTunnel_ReturnsNull()
    {
        var existing = MakeTunnel(gatewayChainKey: "gw-A", isAlive: false);

        var result = TunnelService.FindReusableTunnel(
            [existing],
            "gw-A",
            "10.0.0.5",
            3389,
            socksProxyPort: 0,
            remoteBindPort: 0);

        Assert.Null(result);
    }

    [Fact]
    public void FindReusableTunnel_EmptyChainKeyMatchesEmptyChainKey()
    {
        var existing = MakeTunnel(gatewayChainKey: string.Empty);

        var result = TunnelService.FindReusableTunnel(
            [existing],
            string.Empty,
            "10.0.0.5",
            3389,
            socksProxyPort: 0,
            remoteBindPort: 0);

        Assert.Same(existing, result);
    }

    [Fact]
    public void FindReusableTunnel_NullActiveTunnels_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TunnelService.FindReusableTunnel(
                null!,
                "gw-A",
                "10.0.0.5",
                3389,
                socksProxyPort: 0,
                remoteBindPort: 0));
    }

    [Fact]
    public void FindReusableTunnel_NullGatewayChainKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TunnelService.FindReusableTunnel(
                [],
                null!,
                "10.0.0.5",
                3389,
                socksProxyPort: 0,
                remoteBindPort: 0));
    }

    [Fact]
    public void FindReusableTunnel_NullRemoteHost_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TunnelService.FindReusableTunnel(
                [],
                "gw-A",
                null!,
                3389,
                socksProxyPort: 0,
                remoteBindPort: 0));
    }

    [Fact]
    public void BuildGatewayChainKey_EmptyChain_ReturnsEmpty()
    {
        var result = TunnelService.BuildGatewayChainKey([]);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildGatewayChainKey_SingleHop_IsStableAndVersioned()
    {
        var chain = new[] { new SshGatewayDto { Id = "gw-A" } };

        var first = TunnelService.BuildGatewayChainKey(chain);
        var second = TunnelService.BuildGatewayChainKey(chain);

        Assert.StartsWith("v1:sha256:", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildGatewayChainKey_MultiHop_IsOrderSensitiveAndSeparatorSafe()
    {
        var chain = new[]
        {
            new SshGatewayDto { Id = "foo:1" },
            new SshGatewayDto { Id = "bar" }
        };
        var sameIdsDifferentOrder = new[]
        {
            new SshGatewayDto { Id = "bar" },
            new SshGatewayDto { Id = "foo:1" }
        };
        var naiveJoinCollision = new[]
        {
            new SshGatewayDto { Id = "foo" },
            new SshGatewayDto { Id = "1|bar" }
        };

        var key = TunnelService.BuildGatewayChainKey(chain);

        Assert.NotEqual(key, TunnelService.BuildGatewayChainKey(sameIdsDifferentOrder));
        Assert.NotEqual(key, TunnelService.BuildGatewayChainKey(naiveJoinCollision));
    }

    private static TunnelInfo MakeTunnel(
        string gatewayChainKey,
        string remoteHost = "10.0.0.5",
        int remotePort = 3389,
        bool isAlive = true,
        int socksProxyPort = 0,
        int remoteBindPort = 0)
    {
        return new TunnelInfo(
            "gateway",
            50123,
            remoteHost,
            remotePort,
            DateTime.UtcNow,
            isAlive)
        {
            GatewayChainKey = gatewayChainKey,
            SocksProxyPort = socksProxyPort,
            RemoteBindPort = remoteBindPort
        };
    }

    private sealed class TestDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
