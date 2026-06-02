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

public sealed class RdpConnectWatchdogPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ShouldArm_ReturnsTrue_ForInProgressPhases(int phaseValue)
    {
        RdpConnectionPhase phase = (RdpConnectionPhase)phaseValue;

        bool actual = RdpConnectWatchdogPolicy.ShouldArm(phase);

        Assert.True(actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ShouldArm_ReturnsFalse_ForInactiveOrConnectedPhases(int phaseValue)
    {
        RdpConnectionPhase phase = (RdpConnectionPhase)phaseValue;

        bool actual = RdpConnectWatchdogPolicy.ShouldArm(phase);

        Assert.False(actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ShouldCancel_ReturnsTrue_ForInactiveOrConnectedPhases(int phaseValue)
    {
        RdpConnectionPhase phase = (RdpConnectionPhase)phaseValue;

        bool actual = RdpConnectWatchdogPolicy.ShouldCancel(phase);

        Assert.True(actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ShouldCancel_ReturnsFalse_ForInProgressPhases(int phaseValue)
    {
        RdpConnectionPhase phase = (RdpConnectionPhase)phaseValue;

        bool actual = RdpConnectWatchdogPolicy.ShouldCancel(phase);

        Assert.False(actual);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1000, 5000)]
    [InlineData(45000, 45000)]
    [InlineData(1000000, 600000)]
    public void ResolveTimeoutMs_ReturnsDisabledOrClampedValue(int configured, int expected)
    {
        int actual = RdpConnectWatchdogPolicy.ResolveTimeoutMs(configured);

        Assert.Equal(expected, actual);
    }
}
