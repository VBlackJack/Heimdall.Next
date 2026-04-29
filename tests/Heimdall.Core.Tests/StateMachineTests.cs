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

using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;

namespace Heimdall.Core.Tests;

public class ConnectionStateMachineTests
{
    private readonly ConnectionStateMachine _sm = new();

    [Fact]
    public void InitialState_IsDisconnected()
    {
        Assert.Equal(ConnectionState.Disconnected, _sm.GetState("server-1"));
    }

    [Fact]
    public void GetStateData_ReturnsNull_ForUnknownServer()
    {
        Assert.Null(_sm.GetStateData("unknown"));
    }

    [Fact]
    public void ValidTransition_Succeeds()
    {
        var result = _sm.TryTransition("server-1", ConnectionState.Initializing);

        Assert.True(result);
        Assert.Equal(ConnectionState.Initializing, _sm.GetState("server-1"));
    }

    [Fact]
    public void InvalidTransition_ReturnsFalse()
    {
        var result = _sm.TryTransition("server-1", ConnectionState.Connected);

        Assert.False(result);
        Assert.Equal(ConnectionState.Disconnected, _sm.GetState("server-1"));
    }

    [Fact]
    public void SetError_TransitionsToError()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);

        var result = _sm.SetError("server-1", "Something failed");

        Assert.True(result);
        Assert.Equal(ConnectionState.Error, _sm.GetState("server-1"));
        var data = _sm.GetStateData("server-1");
        Assert.NotNull(data);
        Assert.Equal("Something failed", data.ErrorMessage);
    }

    [Fact]
    public void SetError_ReturnsFalse_WhenTransitionInvalid()
    {
        // Error -> Error is not a valid transition; server starts at Disconnected,
        // transition to Error first, then try again.
        _sm.SetError("server-1", "first error");

        var result = _sm.SetError("server-1", "second error");

        Assert.False(result);
    }

    [Fact]
    public void Reset_ReturnsToDisconnected()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);
        _sm.TryTransition("server-1", ConnectionState.ValidatingConfig);

        _sm.Reset("server-1");

        Assert.Equal(ConnectionState.Disconnected, _sm.GetState("server-1"));
    }

    [Fact]
    public void Reset_ClearsTunnelInfoAndError()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);
        _sm.TryTransition("server-1", ConnectionState.ValidatingConfig);
        _sm.TryTransition("server-1", ConnectionState.EstablishingTunnel);
        _sm.SetTunnelInfo("server-1", 12345, 9876);
        _sm.SetError("server-1", "tunnel failed");

        _sm.Reset("server-1");

        var data = _sm.GetStateData("server-1");
        Assert.NotNull(data);
        Assert.Null(data.ErrorMessage);
        Assert.Null(data.TunnelLocalPort);
        Assert.Null(data.TunnelProcessId);
    }

    [Fact]
    public void Reset_NoOp_ForUnknownServer()
    {
        // Should not throw
        _sm.Reset("unknown-server");
    }

    [Fact]
    public void Reset_NoOp_WhenAlreadyDisconnected()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);
        _sm.TryTransition("server-1", ConnectionState.Disconnected);

        // Already disconnected, should be a no-op
        _sm.Reset("server-1");

        Assert.Equal(ConnectionState.Disconnected, _sm.GetState("server-1"));
    }

    [Fact]
    public void Remove_StopsTracking()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);

        _sm.Remove("server-1");

        Assert.Null(_sm.GetStateData("server-1"));
        Assert.Equal(ConnectionState.Disconnected, _sm.GetState("server-1"));
    }

    [Fact]
    public void StateChanged_EventFires_OnValidTransition()
    {
        string? receivedServerId = null;
        ConnectionState? receivedPrevious = null;
        ConnectionState? receivedNew = null;

        _sm.StateChanged += (id, prev, next, err) =>
        {
            receivedServerId = id;
            receivedPrevious = prev;
            receivedNew = next;
        };

        _sm.TryTransition("server-1", ConnectionState.Initializing);

        Assert.Equal("server-1", receivedServerId);
        Assert.Equal(ConnectionState.Disconnected, receivedPrevious);
        Assert.Equal(ConnectionState.Initializing, receivedNew);
    }

    [Fact]
    public void StateChanged_EventFires_OnSetError()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);

        string? receivedError = null;
        _sm.StateChanged += (id, prev, next, err) => { receivedError = err; };

        _sm.SetError("server-1", "auth failed");

        Assert.Equal("auth failed", receivedError);
    }

    [Fact]
    public void StateChanged_DoesNotFire_OnInvalidTransition()
    {
        var fired = false;
        _sm.StateChanged += (_, _, _, _) => { fired = true; };

        _sm.TryTransition("server-1", ConnectionState.Connected);

        Assert.False(fired);
    }

    [Fact]
    public void SetTunnelInfo_StoresPortAndPid()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);
        _sm.SetTunnelInfo("server-1", 55000, 1234);

        var data = _sm.GetStateData("server-1");
        Assert.NotNull(data);
        Assert.Equal(55000, data.TunnelLocalPort);
        Assert.Equal(1234, data.TunnelProcessId);
    }

    [Fact]
    public void ConnectedAt_SetWhenReachingConnected()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);
        _sm.TryTransition("server-1", ConnectionState.ValidatingConfig);
        _sm.TryTransition("server-1", ConnectionState.LaunchingRdp);
        _sm.TryTransition("server-1", ConnectionState.Connected);

        var data = _sm.GetStateData("server-1");
        Assert.NotNull(data);
        Assert.NotNull(data.ConnectedAtUtc);
    }

    [Fact]
    public void ConnectedAt_ClearedOnDisconnected()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);
        _sm.TryTransition("server-1", ConnectionState.ValidatingConfig);
        _sm.TryTransition("server-1", ConnectionState.LaunchingRdp);
        _sm.TryTransition("server-1", ConnectionState.Connected);
        _sm.TryTransition("server-1", ConnectionState.Disconnecting);
        _sm.TryTransition("server-1", ConnectionState.Disconnected);

        var data = _sm.GetStateData("server-1");
        Assert.NotNull(data);
        Assert.Null(data.ConnectedAtUtc);
    }

    [Fact]
    public void GetActiveConnections_ExcludesDisconnectedAndError()
    {
        _sm.TryTransition("active-1", ConnectionState.Initializing);
        _sm.TryTransition("active-1", ConnectionState.ValidatingConfig);
        _sm.TryTransition("active-1", ConnectionState.LaunchingRdp);
        _sm.TryTransition("active-1", ConnectionState.Connected);

        _sm.TryTransition("disconnected-1", ConnectionState.Initializing);
        _sm.TryTransition("disconnected-1", ConnectionState.Disconnected);

        _sm.SetError("error-1", "failed");

        var active = _sm.GetActiveConnections();

        Assert.Single(active);
        Assert.True(active.ContainsKey("active-1"));
    }

    [Fact]
    public void GetServersByState_ReturnsCorrectServers()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);
        _sm.TryTransition("server-2", ConnectionState.Initializing);
        _sm.TryTransition("server-3", ConnectionState.Initializing);
        _sm.TryTransition("server-3", ConnectionState.ValidatingConfig);

        var initializing = _sm.GetServersByState(ConnectionState.Initializing).ToList();

        Assert.Equal(2, initializing.Count);
        Assert.Contains("server-1", initializing);
        Assert.Contains("server-2", initializing);
    }

    [Fact]
    public void GetStateData_ReturnsSnapshot_NotLiveReference()
    {
        _sm.TryTransition("server-1", ConnectionState.Initializing);

        var snapshot = _sm.GetStateData("server-1");
        Assert.NotNull(snapshot);

        _sm.TryTransition("server-1", ConnectionState.ValidatingConfig);

        // Snapshot should still show Initializing
        Assert.Equal(ConnectionState.Initializing, snapshot.CurrentState);
        // Live state should be ValidatingConfig
        Assert.Equal(ConnectionState.ValidatingConfig, _sm.GetState("server-1"));
    }

    [Theory]
    [InlineData(ConnectionState.Disconnected, ConnectionState.Initializing, true)]
    [InlineData(ConnectionState.Disconnected, ConnectionState.Connected, false)]
    [InlineData(ConnectionState.Disconnected, ConnectionState.Error, true)]
    [InlineData(ConnectionState.Initializing, ConnectionState.ValidatingConfig, true)]
    [InlineData(ConnectionState.Initializing, ConnectionState.Error, true)]
    [InlineData(ConnectionState.Initializing, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.Initializing, ConnectionState.Connected, false)]
    [InlineData(ConnectionState.ValidatingConfig, ConnectionState.EstablishingTunnel, true)]
    [InlineData(ConnectionState.ValidatingConfig, ConnectionState.LaunchingRdp, true)]
    [InlineData(ConnectionState.ValidatingConfig, ConnectionState.LaunchingSsh, true)]
    [InlineData(ConnectionState.ValidatingConfig, ConnectionState.LaunchingSftp, true)]
    [InlineData(ConnectionState.ValidatingConfig, ConnectionState.Error, true)]
    [InlineData(ConnectionState.ValidatingConfig, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.ValidatingConfig, ConnectionState.Connected, false)]
    [InlineData(ConnectionState.EstablishingTunnel, ConnectionState.TunnelEstablished, true)]
    [InlineData(ConnectionState.EstablishingTunnel, ConnectionState.Error, true)]
    [InlineData(ConnectionState.EstablishingTunnel, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.EstablishingTunnel, ConnectionState.LaunchingRdp, false)]
    [InlineData(ConnectionState.TunnelEstablished, ConnectionState.LaunchingRdp, true)]
    [InlineData(ConnectionState.TunnelEstablished, ConnectionState.LaunchingSsh, true)]
    [InlineData(ConnectionState.TunnelEstablished, ConnectionState.LaunchingSftp, true)]
    [InlineData(ConnectionState.TunnelEstablished, ConnectionState.Error, true)]
    [InlineData(ConnectionState.TunnelEstablished, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.TunnelEstablished, ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.LaunchingRdp, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.LaunchingRdp, ConnectionState.Error, true)]
    [InlineData(ConnectionState.LaunchingRdp, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.LaunchingRdp, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.LaunchingSsh, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.LaunchingSsh, ConnectionState.Error, true)]
    [InlineData(ConnectionState.LaunchingSsh, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.LaunchingSsh, ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.LaunchingSftp, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.LaunchingSftp, ConnectionState.Error, true)]
    [InlineData(ConnectionState.LaunchingSftp, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.LaunchingSftp, ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.Connected, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.Connected, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.Connected, ConnectionState.Error, true)]
    [InlineData(ConnectionState.Connected, ConnectionState.Initializing, false)]
    [InlineData(ConnectionState.Disconnecting, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.Disconnecting, ConnectionState.Error, true)]
    [InlineData(ConnectionState.Disconnecting, ConnectionState.Connected, false)]
    [InlineData(ConnectionState.Error, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.Error, ConnectionState.Initializing, true)]
    [InlineData(ConnectionState.Error, ConnectionState.Connected, false)]
    [InlineData(ConnectionState.Error, ConnectionState.Error, false)]
    public void IsValidTransition_MatchesTable(ConnectionState from, ConnectionState to, bool expected)
    {
        Assert.Equal(expected, ConnectionStateMachine.IsValidTransition(from, to));
    }

    [Theory]
    [InlineData(ConnectionState.Disconnected, "StatusReady", "LogDisconnected", false, true, false)]
    [InlineData(ConnectionState.Initializing, "StatusConnectingProgress", "LogInitializing", false, false, true)]
    [InlineData(ConnectionState.ValidatingConfig, "StatusConnectingProgress", "LogValidating", false, false, true)]
    [InlineData(ConnectionState.EstablishingTunnel, "StatusEstablishingTunnel", "LogTunnelCreating", false, false, true)]
    [InlineData(ConnectionState.TunnelEstablished, "StatusTunnelEstablished", "LogTunnelCreated", false, true, false)]
    [InlineData(ConnectionState.LaunchingRdp, "StatusConnecting", "LogRdpLaunching", false, false, true)]
    [InlineData(ConnectionState.LaunchingSsh, "StatusLaunchingSsh", "LogSshLaunching", false, false, true)]
    [InlineData(ConnectionState.LaunchingSftp, "StatusLaunchingSftp", "LogSftpLaunching", false, false, true)]
    [InlineData(ConnectionState.Connected, "StatusConnected", "LogRdpConnection", false, true, false)]
    [InlineData(ConnectionState.Disconnecting, "StatusDisconnecting", "LogDisconnecting", false, false, true)]
    [InlineData(ConnectionState.Error, "StatusError", "LogError", false, true, false)]
    public void GetMetadata_ReturnsCorrectValues(
        ConnectionState state,
        string expectedDisplayKey,
        string expectedLogKey,
        bool expectedIsTerminal,
        bool expectedAllowsUserAction,
        bool expectedIsProgress)
    {
        var metadata = ConnectionStateMachine.GetMetadata(state);

        Assert.Equal(expectedDisplayKey, metadata.DisplayKey);
        Assert.Equal(expectedLogKey, metadata.LogKey);
        Assert.Equal(expectedIsTerminal, metadata.IsTerminal);
        Assert.Equal(expectedAllowsUserAction, metadata.AllowsUserAction);
        Assert.Equal(expectedIsProgress, metadata.IsProgress);
    }

    [Fact]
    public void FullRdpTunnelWorkflow_Succeeds()
    {
        Assert.True(_sm.TryTransition("srv", ConnectionState.Initializing));
        Assert.True(_sm.TryTransition("srv", ConnectionState.ValidatingConfig));
        Assert.True(_sm.TryTransition("srv", ConnectionState.EstablishingTunnel));
        _sm.SetTunnelInfo("srv", 55000, 4321);
        Assert.True(_sm.TryTransition("srv", ConnectionState.TunnelEstablished));
        Assert.True(_sm.TryTransition("srv", ConnectionState.LaunchingRdp));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Connected));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Disconnecting));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Disconnected));

        Assert.Equal(ConnectionState.Disconnected, _sm.GetState("srv"));
    }

    [Fact]
    public void CanTransition_LaunchingRdp_ToLaunchedExternalClient()
    {
        var sm = new ConnectionStateMachine();
        Assert.True(sm.TryTransition("server-1", ConnectionState.Initializing));
        Assert.True(sm.TryTransition("server-1", ConnectionState.ValidatingConfig));
        Assert.True(sm.TryTransition("server-1", ConnectionState.LaunchingRdp));

        var transitioned = sm.TryTransition("server-1", ConnectionState.LaunchedExternalClient);

        Assert.True(transitioned);
        Assert.Equal(ConnectionState.LaunchedExternalClient, sm.GetState("server-1"));
    }

    [Fact]
    public void LaunchedExternalClient_CanTransitionToDisconnected()
    {
        var sm = ArrangeAtLaunchedExternalClient("server-1");

        Assert.True(sm.TryTransition("server-1", ConnectionState.Disconnected));
        Assert.Equal(ConnectionState.Disconnected, sm.GetState("server-1"));
    }

    [Fact]
    public void LaunchedExternalClient_CanTransitionToError()
    {
        var sm = ArrangeAtLaunchedExternalClient("server-1");

        Assert.True(sm.TryTransition("server-1", ConnectionState.Error));
        Assert.Equal(ConnectionState.Error, sm.GetState("server-1"));
    }

    [Fact]
    public void LaunchedExternalClient_CannotTransitionBackToConnected()
    {
        var sm = ArrangeAtLaunchedExternalClient("server-1");

        Assert.False(sm.TryTransition("server-1", ConnectionState.Connected));
        Assert.Equal(ConnectionState.LaunchedExternalClient, sm.GetState("server-1"));
    }

    [Fact]
    public void LaunchedExternalClient_Metadata_UsesDedicatedStatusKey()
    {
        var metadata = ConnectionStateMachine.GetMetadata(ConnectionState.LaunchedExternalClient);

        Assert.Equal("StatusLaunchedExternalClient", metadata.DisplayKey);
        Assert.Equal("LogLaunchedExternalClient", metadata.LogKey);
        Assert.False(metadata.IsTerminal);
        Assert.True(metadata.AllowsUserAction);
        Assert.False(metadata.IsProgress);
    }

    [Fact]
    public void FullDirectSshWorkflow_Succeeds()
    {
        Assert.True(_sm.TryTransition("srv", ConnectionState.Initializing));
        Assert.True(_sm.TryTransition("srv", ConnectionState.ValidatingConfig));
        Assert.True(_sm.TryTransition("srv", ConnectionState.LaunchingSsh));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Connected));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Disconnecting));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Disconnected));
    }

    [Fact]
    public void ErrorRecoveryWorkflow_Succeeds()
    {
        _sm.TryTransition("srv", ConnectionState.Initializing);
        _sm.SetError("srv", "timeout");

        Assert.Equal(ConnectionState.Error, _sm.GetState("srv"));

        Assert.True(_sm.TryTransition("srv", ConnectionState.Disconnected));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Initializing));
    }

    // ── LaunchingLocal state transitions ────────────────────────────────

    [Fact]
    public void ValidatingConfig_CanTransitionToLaunchingLocal()
    {
        Assert.True(_sm.TryTransition("srv", ConnectionState.Initializing));
        Assert.True(_sm.TryTransition("srv", ConnectionState.ValidatingConfig));
        Assert.True(_sm.TryTransition("srv", ConnectionState.LaunchingLocal));

        Assert.Equal(ConnectionState.LaunchingLocal, _sm.GetState("srv"));
    }

    [Fact]
    public void LaunchingLocal_CanTransitionToConnected()
    {
        _sm.TryTransition("srv", ConnectionState.Initializing);
        _sm.TryTransition("srv", ConnectionState.ValidatingConfig);
        _sm.TryTransition("srv", ConnectionState.LaunchingLocal);

        Assert.True(_sm.TryTransition("srv", ConnectionState.Connected));
    }

    [Fact]
    public void LaunchingLocal_CanTransitionToError()
    {
        _sm.TryTransition("srv", ConnectionState.Initializing);
        _sm.TryTransition("srv", ConnectionState.ValidatingConfig);
        _sm.TryTransition("srv", ConnectionState.LaunchingLocal);

        Assert.True(_sm.SetError("srv", "Local shell failed"));
        Assert.Equal(ConnectionState.Error, _sm.GetState("srv"));
    }

    [Fact]
    public void LaunchingLocal_CanTransitionToDisconnecting()
    {
        _sm.TryTransition("srv", ConnectionState.Initializing);
        _sm.TryTransition("srv", ConnectionState.ValidatingConfig);
        _sm.TryTransition("srv", ConnectionState.LaunchingLocal);

        Assert.True(_sm.TryTransition("srv", ConnectionState.Disconnecting));
    }

    [Fact]
    public void LaunchingLocal_CannotTransitionToDisconnectedDirectly()
    {
        _sm.TryTransition("srv", ConnectionState.Initializing);
        _sm.TryTransition("srv", ConnectionState.ValidatingConfig);
        _sm.TryTransition("srv", ConnectionState.LaunchingLocal);

        Assert.False(_sm.TryTransition("srv", ConnectionState.Disconnected));
    }

    [Fact]
    public void FullLocalShellWorkflow_Succeeds()
    {
        Assert.True(_sm.TryTransition("srv", ConnectionState.Initializing));
        Assert.True(_sm.TryTransition("srv", ConnectionState.ValidatingConfig));
        Assert.True(_sm.TryTransition("srv", ConnectionState.LaunchingLocal));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Connected));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Disconnecting));
        Assert.True(_sm.TryTransition("srv", ConnectionState.Disconnected));
    }

    [Theory]
    [InlineData(ConnectionState.ValidatingConfig, ConnectionState.LaunchingLocal, true)]
    [InlineData(ConnectionState.LaunchingLocal, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.LaunchingLocal, ConnectionState.Error, true)]
    [InlineData(ConnectionState.LaunchingLocal, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.LaunchingLocal, ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.LaunchingLocal, ConnectionState.Initializing, false)]
    public void LaunchingLocal_TransitionTable(ConnectionState from, ConnectionState to, bool expected)
    {
        Assert.Equal(expected, ConnectionStateMachine.IsValidTransition(from, to));
    }

    [Fact]
    public void LaunchingLocal_Metadata_IsProgressState()
    {
        var metadata = ConnectionStateMachine.GetMetadata(ConnectionState.LaunchingLocal);

        Assert.Equal("StatusLaunchingLocal", metadata.DisplayKey);
        Assert.Equal("LogLocalShellLaunching", metadata.LogKey);
        Assert.False(metadata.IsTerminal);
        Assert.False(metadata.AllowsUserAction);
        Assert.True(metadata.IsProgress);
    }

    // ── All protocol launch states: full transition coverage ────────────

    [Theory]
    [InlineData(ConnectionState.LaunchingVnc)]
    [InlineData(ConnectionState.LaunchingFtp)]
    [InlineData(ConnectionState.LaunchingTelnet)]
    [InlineData(ConnectionState.LaunchingCitrix)]
    [InlineData(ConnectionState.LaunchingRdp)]
    [InlineData(ConnectionState.LaunchingSsh)]
    [InlineData(ConnectionState.LaunchingSftp)]
    [InlineData(ConnectionState.LaunchingLocal)]
    public void LaunchState_ToConnected_IsValid(ConnectionState launchState)
    {
        Assert.True(ConnectionStateMachine.IsValidTransition(launchState, ConnectionState.Connected));
    }

    [Theory]
    [InlineData(ConnectionState.LaunchingVnc)]
    [InlineData(ConnectionState.LaunchingFtp)]
    [InlineData(ConnectionState.LaunchingTelnet)]
    [InlineData(ConnectionState.LaunchingCitrix)]
    [InlineData(ConnectionState.LaunchingRdp)]
    [InlineData(ConnectionState.LaunchingSsh)]
    [InlineData(ConnectionState.LaunchingSftp)]
    [InlineData(ConnectionState.LaunchingLocal)]
    public void LaunchState_ToError_IsValid(ConnectionState launchState)
    {
        Assert.True(ConnectionStateMachine.IsValidTransition(launchState, ConnectionState.Error));
    }

    [Theory]
    [InlineData(ConnectionState.LaunchingVnc)]
    [InlineData(ConnectionState.LaunchingFtp)]
    [InlineData(ConnectionState.LaunchingTelnet)]
    [InlineData(ConnectionState.LaunchingCitrix)]
    [InlineData(ConnectionState.LaunchingRdp)]
    [InlineData(ConnectionState.LaunchingSsh)]
    [InlineData(ConnectionState.LaunchingSftp)]
    [InlineData(ConnectionState.LaunchingLocal)]
    public void LaunchState_ToDisconnecting_IsValid(ConnectionState launchState)
    {
        Assert.True(ConnectionStateMachine.IsValidTransition(launchState, ConnectionState.Disconnecting));
    }

    // ── TunnelEstablished → each protocol launch transition ─────────────

    [Theory]
    [InlineData(ConnectionState.LaunchingRdp)]
    [InlineData(ConnectionState.LaunchingSsh)]
    [InlineData(ConnectionState.LaunchingSftp)]
    [InlineData(ConnectionState.LaunchingVnc)]
    [InlineData(ConnectionState.LaunchingFtp)]
    [InlineData(ConnectionState.LaunchingTelnet)]
    [InlineData(ConnectionState.LaunchingCitrix)]
    public void TunnelEstablished_ToLaunchState_IsValid(ConnectionState launchState)
    {
        Assert.True(ConnectionStateMachine.IsValidTransition(ConnectionState.TunnelEstablished, launchState));
    }

    // ── ValidatingConfig → each protocol launch (direct, no tunnel) ─────

    [Theory]
    [InlineData(ConnectionState.LaunchingRdp)]
    [InlineData(ConnectionState.LaunchingSsh)]
    [InlineData(ConnectionState.LaunchingSftp)]
    [InlineData(ConnectionState.LaunchingVnc)]
    [InlineData(ConnectionState.LaunchingFtp)]
    [InlineData(ConnectionState.LaunchingTelnet)]
    [InlineData(ConnectionState.LaunchingCitrix)]
    [InlineData(ConnectionState.LaunchingLocal)]
    public void ValidatingConfig_ToLaunchState_IsValid(ConnectionState launchState)
    {
        Assert.True(ConnectionStateMachine.IsValidTransition(ConnectionState.ValidatingConfig, launchState));
    }

    // ── Full workflow for each protocol via tunnel ───────────────────────

    [Theory]
    [InlineData(ConnectionState.LaunchingVnc)]
    [InlineData(ConnectionState.LaunchingFtp)]
    [InlineData(ConnectionState.LaunchingTelnet)]
    [InlineData(ConnectionState.LaunchingCitrix)]
    public void FullTunnelWorkflow_Succeeds_ForAllProtocols(ConnectionState launchState)
    {
        var sm = new ConnectionStateMachine();
        var id = $"srv-{launchState}";

        Assert.True(sm.TryTransition(id, ConnectionState.Initializing));
        Assert.True(sm.TryTransition(id, ConnectionState.ValidatingConfig));
        Assert.True(sm.TryTransition(id, ConnectionState.EstablishingTunnel));
        Assert.True(sm.TryTransition(id, ConnectionState.TunnelEstablished));
        Assert.True(sm.TryTransition(id, launchState));
        Assert.True(sm.TryTransition(id, ConnectionState.Connected));
        Assert.True(sm.TryTransition(id, ConnectionState.Disconnecting));
        Assert.True(sm.TryTransition(id, ConnectionState.Disconnected));

        Assert.Equal(ConnectionState.Disconnected, sm.GetState(id));
    }

    // ── Full direct workflow for each protocol (no tunnel) ──────────────

    [Theory]
    [InlineData(ConnectionState.LaunchingVnc)]
    [InlineData(ConnectionState.LaunchingFtp)]
    [InlineData(ConnectionState.LaunchingTelnet)]
    [InlineData(ConnectionState.LaunchingCitrix)]
    public void FullDirectWorkflow_Succeeds_ForAllProtocols(ConnectionState launchState)
    {
        var sm = new ConnectionStateMachine();
        var id = $"srv-{launchState}";

        Assert.True(sm.TryTransition(id, ConnectionState.Initializing));
        Assert.True(sm.TryTransition(id, ConnectionState.ValidatingConfig));
        Assert.True(sm.TryTransition(id, launchState));
        Assert.True(sm.TryTransition(id, ConnectionState.Connected));
        Assert.True(sm.TryTransition(id, ConnectionState.Disconnecting));
        Assert.True(sm.TryTransition(id, ConnectionState.Disconnected));
    }

    // ── Concurrent servers in different states ──────────────────────────

    [Fact]
    public void ConcurrentServers_ThreeServersInDifferentStates()
    {
        var sm = new ConnectionStateMachine();

        // Server 1: Connected
        sm.TryTransition("srv-1", ConnectionState.Initializing);
        sm.TryTransition("srv-1", ConnectionState.ValidatingConfig);
        sm.TryTransition("srv-1", ConnectionState.LaunchingSsh);
        sm.TryTransition("srv-1", ConnectionState.Connected);

        // Server 2: Establishing tunnel
        sm.TryTransition("srv-2", ConnectionState.Initializing);
        sm.TryTransition("srv-2", ConnectionState.ValidatingConfig);
        sm.TryTransition("srv-2", ConnectionState.EstablishingTunnel);

        // Server 3: Error state
        sm.TryTransition("srv-3", ConnectionState.Initializing);
        sm.SetError("srv-3", "Auth failed");

        Assert.Equal(ConnectionState.Connected, sm.GetState("srv-1"));
        Assert.Equal(ConnectionState.EstablishingTunnel, sm.GetState("srv-2"));
        Assert.Equal(ConnectionState.Error, sm.GetState("srv-3"));

        // Verify active connections excludes Error and Disconnected
        var active = sm.GetActiveConnections();
        Assert.Equal(2, active.Count);
        Assert.True(active.ContainsKey("srv-1"));
        Assert.True(active.ContainsKey("srv-2"));
        Assert.False(active.ContainsKey("srv-3"));
    }

    // ── Reset after Error state ─────────────────────────────────────────

    [Fact]
    public void ResetAfterError_TransitionsToDisconnected()
    {
        var sm = new ConnectionStateMachine();
        sm.TryTransition("srv", ConnectionState.Initializing);
        sm.TryTransition("srv", ConnectionState.ValidatingConfig);
        sm.TryTransition("srv", ConnectionState.LaunchingVnc);
        sm.SetError("srv", "Connection refused");

        Assert.Equal(ConnectionState.Error, sm.GetState("srv"));

        sm.Reset("srv");

        Assert.Equal(ConnectionState.Disconnected, sm.GetState("srv"));
        var data = sm.GetStateData("srv");
        Assert.NotNull(data);
        Assert.Null(data.ErrorMessage);
    }

    [Fact]
    public void ResetAfterError_CanReconnect()
    {
        var sm = new ConnectionStateMachine();
        sm.TryTransition("srv", ConnectionState.Initializing);
        sm.SetError("srv", "timeout");
        sm.Reset("srv");

        Assert.True(sm.TryTransition("srv", ConnectionState.Initializing));
        Assert.Equal(ConnectionState.Initializing, sm.GetState("srv"));
    }

    // ── Metadata for all launch states ──────────────────────────────────

    [Theory]
    [InlineData(ConnectionState.LaunchingVnc, "StatusVncConnecting", "LogVncLaunching")]
    [InlineData(ConnectionState.LaunchingFtp, "StatusFtpConnecting", "LogFtpLaunching")]
    [InlineData(ConnectionState.LaunchingTelnet, "StatusTelnetConnecting", "LogTelnetLaunching")]
    [InlineData(ConnectionState.LaunchingCitrix, "StatusCitrixConnecting", "LogCitrixLaunching")]
    public void LaunchState_Metadata_IsProgressState(ConnectionState state, string displayKey, string logKey)
    {
        var metadata = ConnectionStateMachine.GetMetadata(state);

        Assert.Equal(displayKey, metadata.DisplayKey);
        Assert.Equal(logKey, metadata.LogKey);
        Assert.False(metadata.IsTerminal);
        Assert.False(metadata.AllowsUserAction);
        Assert.True(metadata.IsProgress);
    }

    // ── Transition table coverage for Vnc/Ftp/Telnet/Citrix ─────────────

    [Theory]
    [InlineData(ConnectionState.LaunchingVnc, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.LaunchingVnc, ConnectionState.Error, true)]
    [InlineData(ConnectionState.LaunchingVnc, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.LaunchingVnc, ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.LaunchingVnc, ConnectionState.Initializing, false)]
    [InlineData(ConnectionState.LaunchingFtp, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.LaunchingFtp, ConnectionState.Error, true)]
    [InlineData(ConnectionState.LaunchingFtp, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.LaunchingFtp, ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.LaunchingTelnet, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.LaunchingTelnet, ConnectionState.Error, true)]
    [InlineData(ConnectionState.LaunchingTelnet, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.LaunchingTelnet, ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.LaunchingCitrix, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.LaunchingCitrix, ConnectionState.Error, true)]
    [InlineData(ConnectionState.LaunchingCitrix, ConnectionState.Disconnecting, true)]
    [InlineData(ConnectionState.LaunchingCitrix, ConnectionState.Disconnected, false)]
    public void NewLaunchStates_TransitionTable(ConnectionState from, ConnectionState to, bool expected)
    {
        Assert.Equal(expected, ConnectionStateMachine.IsValidTransition(from, to));
    }

    // ── TunnelEstablished cannot transition to LaunchingLocal ────────────

    [Fact]
    public void TunnelEstablished_CannotTransitionToLaunchingLocal()
    {
        // Local shell does not use tunnels
        Assert.False(ConnectionStateMachine.IsValidTransition(
            ConnectionState.TunnelEstablished, ConnectionState.LaunchingLocal));
    }

    private static ConnectionStateMachine ArrangeAtLaunchedExternalClient(string serverId)
    {
        var sm = new ConnectionStateMachine();
        Assert.True(sm.TryTransition(serverId, ConnectionState.Initializing));
        Assert.True(sm.TryTransition(serverId, ConnectionState.ValidatingConfig));
        Assert.True(sm.TryTransition(serverId, ConnectionState.LaunchingRdp));
        Assert.True(sm.TryTransition(serverId, ConnectionState.LaunchedExternalClient));
        return sm;
    }
}

public class ApplicationStatusMachineTests
{
    private readonly ApplicationStatusMachine _sm = new();

    [Fact]
    public void InitialStatus_IsInitializing()
    {
        Assert.Equal(ApplicationStatus.Initializing, _sm.CurrentStatus);
    }

    [Fact]
    public void TransitionToReady_Succeeds()
    {
        var result = _sm.TryTransition(ApplicationStatus.Ready);

        Assert.True(result);
        Assert.Equal(ApplicationStatus.Ready, _sm.CurrentStatus);
    }

    [Fact]
    public void InvalidTransition_ReturnsFalse()
    {
        // Initializing -> Busy is not valid
        var result = _sm.TryTransition(ApplicationStatus.Busy);

        Assert.False(result);
        Assert.Equal(ApplicationStatus.Initializing, _sm.CurrentStatus);
    }

    [Fact]
    public void SameStateTransition_IsNoOp_ReturnsTrue()
    {
        var result = _sm.TryTransition(ApplicationStatus.Initializing);

        Assert.True(result);
        Assert.Equal(ApplicationStatus.Initializing, _sm.CurrentStatus);
    }

    [Fact]
    public void TransitionToBusy_StoresReason()
    {
        _sm.TryTransition(ApplicationStatus.Ready);

        _sm.TryTransition(ApplicationStatus.Busy, "Loading servers");

        Assert.Equal("Loading servers", _sm.BusyReason);
    }

    [Fact]
    public void TransitionToError_StoresMessage()
    {
        _sm.TryTransition(ApplicationStatus.Ready);

        _sm.TryTransition(ApplicationStatus.Error, "Config corrupted");

        Assert.Equal("Config corrupted", _sm.ErrorMessage);
    }

    [Fact]
    public void TransitionToReady_ClearsBusyAndError()
    {
        _sm.TryTransition(ApplicationStatus.Ready);
        _sm.TryTransition(ApplicationStatus.Busy, "working");
        _sm.TryTransition(ApplicationStatus.Ready);

        Assert.Null(_sm.BusyReason);
        Assert.Null(_sm.ErrorMessage);
    }

    [Fact]
    public void BeginOperation_TransitionsToBusy()
    {
        _sm.TryTransition(ApplicationStatus.Ready);

        using var op = _sm.BeginOperation("test operation");

        Assert.Equal(ApplicationStatus.Busy, _sm.CurrentStatus);
    }

    [Fact]
    public void DisposeOperation_ReturnsToReady()
    {
        _sm.TryTransition(ApplicationStatus.Ready);

        var op = _sm.BeginOperation("test");
        op.Dispose();

        Assert.Equal(ApplicationStatus.Ready, _sm.CurrentStatus);
    }

    [Fact]
    public void DisposeOperation_Idempotent()
    {
        _sm.TryTransition(ApplicationStatus.Ready);

        var op = _sm.BeginOperation("test");
        op.Dispose();
        op.Dispose(); // Should not throw or corrupt state

        Assert.Equal(ApplicationStatus.Ready, _sm.CurrentStatus);
    }

    [Fact]
    public void MultipleOperations_StaysBusy_UntilAllComplete()
    {
        _sm.TryTransition(ApplicationStatus.Ready);

        var op1 = _sm.BeginOperation("op1");
        var op2 = _sm.BeginOperation("op2");

        op1.Dispose();
        Assert.Equal(ApplicationStatus.Busy, _sm.CurrentStatus);

        op2.Dispose();
        Assert.Equal(ApplicationStatus.Ready, _sm.CurrentStatus);
    }

    [Fact]
    public void BeginOperation_Succeeds_WhenInitializing()
    {
        // Status is Initializing — allowed since startup needs tracked operations
        using var op = _sm.BeginOperation();
        Assert.NotNull(op);
    }

    [Fact]
    public void BeginOperation_Throws_WhenShutdown()
    {
        _sm.TryTransition(ApplicationStatus.Ready);
        _sm.TryTransition(ApplicationStatus.Shutdown);

        Assert.Throws<InvalidOperationException>(() => _sm.BeginOperation());
    }

    [Fact]
    public void AllowsUserAction_OnlyWhenReady()
    {
        Assert.False(_sm.AllowsUserAction); // Initializing

        _sm.TryTransition(ApplicationStatus.Ready);
        Assert.True(_sm.AllowsUserAction);

        _sm.TryTransition(ApplicationStatus.Busy);
        Assert.False(_sm.AllowsUserAction);
    }

    [Fact]
    public void StatusChanged_EventFires()
    {
        ApplicationStatus? receivedPrev = null;
        ApplicationStatus? receivedNew = null;

        _sm.StatusChanged += (prev, next) =>
        {
            receivedPrev = prev;
            receivedNew = next;
        };

        _sm.TryTransition(ApplicationStatus.Ready);

        Assert.Equal(ApplicationStatus.Initializing, receivedPrev);
        Assert.Equal(ApplicationStatus.Ready, receivedNew);
    }

    [Fact]
    public void StatusChanged_DoesNotFire_OnSameState()
    {
        var fired = false;
        _sm.StatusChanged += (_, _) => { fired = true; };

        _sm.TryTransition(ApplicationStatus.Initializing);

        Assert.False(fired);
    }

    [Fact]
    public void StatusChanged_DoesNotFire_OnInvalidTransition()
    {
        var fired = false;
        _sm.StatusChanged += (_, _) => { fired = true; };

        _sm.TryTransition(ApplicationStatus.Shutdown); // Invalid from Initializing

        Assert.False(fired);
    }

    [Fact]
    public void PreviousStatus_TracksLastTransition()
    {
        _sm.TryTransition(ApplicationStatus.Ready);
        _sm.TryTransition(ApplicationStatus.Busy);

        Assert.Equal(ApplicationStatus.Ready, _sm.PreviousStatus);
    }

    [Fact]
    public void Shutdown_IsTerminal()
    {
        _sm.TryTransition(ApplicationStatus.Ready);
        _sm.TryTransition(ApplicationStatus.Shutdown);

        Assert.Equal(ApplicationStatus.Shutdown, _sm.CurrentStatus);

        // No transitions allowed from Shutdown
        Assert.False(_sm.TryTransition(ApplicationStatus.Ready));
        Assert.False(_sm.TryTransition(ApplicationStatus.Error));
    }

    [Fact]
    public void ErrorRecovery_TransitionsToReady()
    {
        _sm.TryTransition(ApplicationStatus.Ready);
        _sm.TryTransition(ApplicationStatus.Error, "disk full");

        Assert.True(_sm.AllowsUserAction); // Error allows user action

        _sm.TryTransition(ApplicationStatus.Ready);
        Assert.Equal(ApplicationStatus.Ready, _sm.CurrentStatus);
    }

    [Theory]
    [InlineData(ApplicationStatus.Initializing, ApplicationStatus.Ready, true)]
    [InlineData(ApplicationStatus.Initializing, ApplicationStatus.Error, true)]
    [InlineData(ApplicationStatus.Initializing, ApplicationStatus.Busy, false)]
    [InlineData(ApplicationStatus.Initializing, ApplicationStatus.Shutdown, false)]
    [InlineData(ApplicationStatus.Ready, ApplicationStatus.Busy, true)]
    [InlineData(ApplicationStatus.Ready, ApplicationStatus.Shutdown, true)]
    [InlineData(ApplicationStatus.Ready, ApplicationStatus.Error, true)]
    [InlineData(ApplicationStatus.Ready, ApplicationStatus.Initializing, false)]
    [InlineData(ApplicationStatus.Busy, ApplicationStatus.Ready, true)]
    [InlineData(ApplicationStatus.Busy, ApplicationStatus.Error, true)]
    [InlineData(ApplicationStatus.Busy, ApplicationStatus.Shutdown, false)]
    [InlineData(ApplicationStatus.Error, ApplicationStatus.Ready, true)]
    [InlineData(ApplicationStatus.Error, ApplicationStatus.Shutdown, true)]
    [InlineData(ApplicationStatus.Error, ApplicationStatus.Busy, false)]
    [InlineData(ApplicationStatus.Shutdown, ApplicationStatus.Ready, false)]
    [InlineData(ApplicationStatus.Shutdown, ApplicationStatus.Error, false)]
    public void IsValidTransition_MatchesTable(ApplicationStatus from, ApplicationStatus to, bool expected)
    {
        Assert.Equal(expected, ApplicationStatusMachine.IsValidTransition(from, to));
    }

    [Theory]
    [InlineData(ApplicationStatus.Initializing, "AppStatusInitializing", false, false)]
    [InlineData(ApplicationStatus.Ready, "StatusReady", true, false)]
    [InlineData(ApplicationStatus.Busy, "AppStatusBusy", false, false)]
    [InlineData(ApplicationStatus.Error, "AppStatusError", true, false)]
    [InlineData(ApplicationStatus.Shutdown, "AppStatusShutdown", false, true)]
    public void GetMetadata_ReturnsCorrectValues(
        ApplicationStatus status,
        string expectedDisplayKey,
        bool expectedAllowsUserAction,
        bool expectedIsTerminal)
    {
        var metadata = ApplicationStatusMachine.GetMetadata(status);

        Assert.Equal(expectedDisplayKey, metadata.DisplayKey);
        Assert.Equal(expectedAllowsUserAction, metadata.AllowsUserAction);
        Assert.Equal(expectedIsTerminal, metadata.IsTerminal);
    }
}
