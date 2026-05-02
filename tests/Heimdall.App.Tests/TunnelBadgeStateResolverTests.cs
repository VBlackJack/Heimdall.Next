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
using Heimdall.App.ViewModels.Tunnels;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;

namespace Heimdall.App.Tests;

public sealed class TunnelBadgeStateResolverTests
{
    [Fact]
    public void Resolve_TabWithEmptyRootContent_ReturnsHidden()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = new SessionTabViewModel();

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Hidden, result);
    }

    [Fact]
    public void Resolve_TabWithPaneEmptyServerId_ReturnsHidden()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = CreateTab(new SessionPaneModel { ServerId = "" });

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Hidden, result);
    }

    [Fact]
    public void Resolve_TabWithPaneServerIdNoTunnel_ReturnsHidden()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = CreateTab(new SessionPaneModel { ServerId = "server-1" });

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Hidden, result);
    }

    [Fact]
    public void Resolve_TabWithSinglePaneHealthyTunnel_ReturnsHealthy()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = CreateTab(new SessionPaneModel { ServerId = "server-1" });
        RegisterTunnel(stateMachine, tunnelManager, "server-1", 50101, isAlive: true);

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Healthy, result);
    }

    [Fact]
    public void Resolve_TabWithSinglePaneUnhealthyTunnel_ReturnsUnhealthy()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = CreateTab(new SessionPaneModel { ServerId = "server-1" });
        RegisterTunnel(stateMachine, tunnelManager, "server-1", 50101, isAlive: false);

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Unhealthy, result);
    }

    [Fact]
    public void Resolve_TabWithSinglePanePortRegisteredButTunnelMissing_ReturnsUnhealthy()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = CreateTab(new SessionPaneModel { ServerId = "server-1" });
        stateMachine.SetTunnelInfo("server-1", 50101, processId: 1234);

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Unhealthy, result);
    }

    [Fact]
    public void Resolve_TabWithSplitTreeAllHealthy_ReturnsHealthy()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = CreateTab(CreateSplitTree("server-1", "server-2"));
        RegisterTunnel(stateMachine, tunnelManager, "server-1", 50101, isAlive: true);
        RegisterTunnel(stateMachine, tunnelManager, "server-2", 50102, isAlive: true);

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Healthy, result);
    }

    [Fact]
    public void Resolve_TabWithSplitTreeMixedHealth_ReturnsUnhealthy()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = CreateTab(CreateSplitTree("server-1", "server-2"));
        RegisterTunnel(stateMachine, tunnelManager, "server-1", 50101, isAlive: true);
        RegisterTunnel(stateMachine, tunnelManager, "server-2", 50102, isAlive: false);

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Unhealthy, result);
    }

    [Fact]
    public void Resolve_TabWithSplitTreeNoneHaveTunnels_ReturnsHidden()
    {
        using var tunnelManager = new TunnelManager();
        var stateMachine = new ConnectionStateMachine();
        var tab = CreateTab(CreateSplitTree("server-1", "server-2"));

        var result = TunnelBadgeStateResolver.Resolve(tab, stateMachine, tunnelManager);

        Assert.Equal(TunnelBadgeState.Hidden, result);
    }

    private static SessionTabViewModel CreateTab(ISplitContent root)
    {
        return new SessionTabViewModel
        {
            RootContent = root
        };
    }

    private static SplitContainerModel CreateSplitTree(string firstServerId, string secondServerId)
    {
        return new SplitContainerModel
        {
            First = new SessionPaneModel { ServerId = firstServerId },
            Second = new SessionPaneModel { ServerId = secondServerId },
            Orientation = SplitOrientation.Vertical
        };
    }

    private static void RegisterTunnel(
        ConnectionStateMachine stateMachine,
        TunnelManager tunnelManager,
        string serverId,
        int localPort,
        bool isAlive)
    {
        stateMachine.SetTunnelInfo(serverId, localPort, processId: 1234);
        var info = new TunnelInfo("gateway", localPort, "target.internal", 3389, DateTime.UtcNow, isAlive);

        Assert.True(tunnelManager.TryRegisterExternalTunnel(info, new TestDisposable(), () => isAlive));
    }

    private sealed class TestDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
