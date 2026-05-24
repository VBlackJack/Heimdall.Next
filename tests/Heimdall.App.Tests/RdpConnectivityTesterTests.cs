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

namespace Heimdall.App.Tests;

public sealed class RdpConnectivityTesterTests
{
    [Fact]
    public async Task TestAsync_InvalidPortReturnsInvalidPortOutcome()
    {
        var sut = new RdpConnectivityTester();

        var result = await sut.TestAsync(
            "localhost",
            70000,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(RdpConnectivityTestOutcome.InvalidPort, result.Outcome);
    }

    [Fact]
    public async Task TestAsync_BlankHostReturnsInvalidAddressOutcome()
    {
        RdpConnectivityTester sut = new RdpConnectivityTester();

        RdpConnectivityTestResult result = await sut.TestAsync(
            "   ",
            3389,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(RdpConnectivityTestOutcome.InvalidAddress, result.Outcome);
    }

    [Fact]
    public async Task TestAsync_InvalidHostnameReturnsInvalidAddressOutcome()
    {
        var sut = new RdpConnectivityTester();

        var result = await sut.TestAsync(
            "not a valid hostname!",
            3389,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(RdpConnectivityTestOutcome.InvalidAddress, result.Outcome);
    }

    [Fact]
    public async Task TestAsync_UnreachableLocalPortReturnsTcpFailureOrTimeout()
    {
        var sut = new RdpConnectivityTester();

        var result = await sut.TestAsync(
            "127.0.0.1",
            1,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.True(
            result.Outcome is RdpConnectivityTestOutcome.TcpFailed
                or RdpConnectivityTestOutcome.TcpTimeout,
            $"Unexpected outcome: {result.Outcome}");
    }

    [Fact]
    public async Task TestAsync_CancelledTokenReturnsCancelledOutcome()
    {
        var sut = new RdpConnectivityTester();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await sut.TestAsync(
            "127.0.0.1",
            3389,
            TimeSpan.FromSeconds(5),
            cts.Token);

        Assert.Equal(RdpConnectivityTestOutcome.Cancelled, result.Outcome);
    }
}
