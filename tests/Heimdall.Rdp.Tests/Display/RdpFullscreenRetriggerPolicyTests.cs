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

using Heimdall.Rdp.Display;

namespace Heimdall.Rdp.Tests.Display;

public sealed class RdpFullscreenRetriggerPolicyTests
{
    [Fact]
    public void ShouldRetrigger_TrueWhenConnectedAndStabilized()
    {
        var result = RdpFullscreenRetriggerPolicy.ShouldRetrigger(
            isConnected: true,
            sinceConnected: TimeSpan.FromSeconds(15),
            stabilizationWindow: TimeSpan.FromSeconds(10));

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetrigger_FalseWhenConnectedButStillStabilizing()
    {
        var result = RdpFullscreenRetriggerPolicy.ShouldRetrigger(
            isConnected: true,
            sinceConnected: TimeSpan.FromSeconds(3),
            stabilizationWindow: TimeSpan.FromSeconds(10));

        Assert.False(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(30)]
    public void ShouldRetrigger_FalseWhenNotConnected(int secondsSinceConnected)
    {
        var result = RdpFullscreenRetriggerPolicy.ShouldRetrigger(
            isConnected: false,
            sinceConnected: TimeSpan.FromSeconds(secondsSinceConnected),
            stabilizationWindow: TimeSpan.FromSeconds(10));

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetrigger_TrueAtExactStabilizationBoundary()
    {
        var result = RdpFullscreenRetriggerPolicy.ShouldRetrigger(
            isConnected: true,
            sinceConnected: TimeSpan.FromSeconds(10),
            stabilizationWindow: TimeSpan.FromSeconds(10));

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetrigger_FalseWhenSinceConnectedIsNegative()
    {
        var result = RdpFullscreenRetriggerPolicy.ShouldRetrigger(
            isConnected: true,
            sinceConnected: TimeSpan.FromSeconds(-1),
            stabilizationWindow: TimeSpan.FromSeconds(10));

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetrigger_TrueWhenStabilizationWindowIsZero()
    {
        var result = RdpFullscreenRetriggerPolicy.ShouldRetrigger(
            isConnected: true,
            sinceConnected: TimeSpan.Zero,
            stabilizationWindow: TimeSpan.Zero);

        Assert.True(result);
    }
}
