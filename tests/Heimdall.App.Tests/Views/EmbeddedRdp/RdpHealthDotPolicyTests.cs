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

using Heimdall.App.Views.EmbeddedRdp;

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

public sealed class RdpHealthDotPolicyTests
{
    [Theory]
    [InlineData(4, 2, false)]
    [InlineData(4, 5, false)]
    [InlineData(4, 6, false)]
    public void Resolve_ReturnsHealthy_WhenPhaseIsConnected(
        int phaseValue,
        int statusValue,
        bool wasUserInitiatedDisconnect)
    {
        var phase = (RdpConnectionPhase)phaseValue;
        var status = (RdpSessionStatus)statusValue;
        var actual = RdpHealthDotPolicy.Resolve(phase, status, wasUserInitiatedDisconnect);

        Assert.Equal(RdpHealthDotState.Healthy, actual);
    }

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(3, 1, false)]
    [InlineData(0, 5, false)]
    [InlineData(1, 6, false)]
    public void Resolve_ReturnsTransitional_ForTransientConnectionStates(
        int phaseValue,
        int statusValue,
        bool wasUserInitiatedDisconnect)
    {
        var phase = (RdpConnectionPhase)phaseValue;
        var status = (RdpSessionStatus)statusValue;
        var actual = RdpHealthDotPolicy.Resolve(phase, status, wasUserInitiatedDisconnect);

        Assert.Equal(RdpHealthDotState.Transitional, actual);
    }

    [Theory]
    [InlineData(0, 3, false)]
    [InlineData(0, 3, true)]
    [InlineData(0, 4, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 2, false)]
    public void Resolve_ReturnsIdle_ForCleanOrInactiveStates(
        int phaseValue,
        int statusValue,
        bool wasUserInitiatedDisconnect)
    {
        var phase = (RdpConnectionPhase)phaseValue;
        var status = (RdpSessionStatus)statusValue;
        var actual = RdpHealthDotPolicy.Resolve(phase, status, wasUserInitiatedDisconnect);

        Assert.Equal(RdpHealthDotState.Idle, actual);
    }

    [Theory]
    [InlineData(0, 4, false)]
    [InlineData(0, 6, false)]
    [InlineData(0, 6, true)]
    public void Resolve_ReturnsFaulted_ForUnexpectedDisconnectOrError(
        int phaseValue,
        int statusValue,
        bool wasUserInitiatedDisconnect)
    {
        var phase = (RdpConnectionPhase)phaseValue;
        var status = (RdpSessionStatus)statusValue;
        var actual = RdpHealthDotPolicy.Resolve(phase, status, wasUserInitiatedDisconnect);

        Assert.Equal(RdpHealthDotState.Faulted, actual);
    }
}
