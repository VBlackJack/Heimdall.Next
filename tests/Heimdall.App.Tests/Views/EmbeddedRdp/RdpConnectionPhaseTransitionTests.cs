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

public sealed class RdpConnectionPhaseTransitionTests
{
    [Fact]
    public void RdpConnectionPhase_ValuesRemainInStepperOrder()
    {
        var expected = new[]
        {
            RdpConnectionPhase.None,
            RdpConnectionPhase.Preparing,
            RdpConnectionPhase.Connecting,
            RdpConnectionPhase.Loading,
            RdpConnectionPhase.Connected
        };

        var actual = Enum.GetValues<RdpConnectionPhase>();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    public void GetLitSegmentCount_ReturnsExpected(int phaseValue, int expected)
    {
        var phase = (RdpConnectionPhase)phaseValue;
        var actual = RdpConnectionPhasePolicy.GetLitSegmentCount(phase);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "RdpStatusPreparing")]
    [InlineData(2, "RdpStatusConnecting")]
    [InlineData(3, "RdpStatusLoading")]
    [InlineData(4, "RdpStatusConnected")]
    public void GetStatusKey_ReturnsExpected(int phaseValue, string? expected)
    {
        var phase = (RdpConnectionPhase)phaseValue;
        var actual = RdpConnectionPhasePolicy.GetStatusKey(phase);

        Assert.Equal(expected, actual);
    }
}
