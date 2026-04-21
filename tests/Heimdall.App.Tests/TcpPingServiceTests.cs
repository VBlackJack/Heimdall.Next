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

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Heimdall.App.Services;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public sealed class TcpPingServiceTests
{
    [Fact]
    public async Task ProbeAsync_HappyPath_ReturnsSuccessfulProbe()
    {
        var service = new TcpPingService(Returns(42.0));

        var result = await service.ProbeAsync(new TcpPingProbeRequest("example.com", 443, 2, 5000), CancellationToken.None);

        Assert.Equal(TcpPingProbeStatus.Success, result.Status);
        Assert.Equal(42.0, result.LatencyMs);
        Assert.Equal(2, result.Seq);
        Assert.Equal("example.com", result.Host);
        Assert.Equal(443, result.Port);
    }

    [Fact]
    public async Task ProbeAsync_SocketException_ReturnsFailedResult()
    {
        var service = new TcpPingService(Throws(new SocketException((int)SocketError.ConnectionRefused)));

        var result = await service.ProbeAsync(new TcpPingProbeRequest("example.com", 443, 1, 5000), CancellationToken.None);

        Assert.Equal(TcpPingProbeStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task ProbeAsync_GenericException_ReturnsFailedResult()
    {
        var service = new TcpPingService(Throws(new InvalidOperationException("oops")));

        var result = await service.ProbeAsync(new TcpPingProbeRequest("example.com", 443, 1, 5000), CancellationToken.None);

        Assert.Equal(TcpPingProbeStatus.Failed, result.Status);
        Assert.Equal("oops", result.ErrorMessage);
    }

    [Fact]
    public async Task ProbeAsync_InnerExceptionMessage_TakesPrecedence()
    {
        var service = new TcpPingService(Throws(new Exception("outer", new Exception("inner"))));

        var result = await service.ProbeAsync(new TcpPingProbeRequest("example.com", 443, 1, 5000), CancellationToken.None);

        Assert.Equal("inner", result.ErrorMessage);
    }

    [Fact]
    public async Task ProbeAsync_InternalTimeout_ReturnsFailedTimeout()
    {
        var service = new TcpPingService(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return 0.0;
        });

        var result = await service.ProbeAsync(new TcpPingProbeRequest("example.com", 443, 1, 50), CancellationToken.None);

        Assert.Equal(TcpPingProbeStatus.Failed, result.Status);
        Assert.Equal("Timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task ProbeAsync_PreCancelledUserToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new TcpPingService(Returns(1.0));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ProbeAsync(new TcpPingProbeRequest("example.com", 443, 1, 5000), cts.Token));
    }

    [Fact]
    public async Task ProbeAsync_UserCancelMidProbe_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var service = new TcpPingService(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return 0.0;
        });

        var task = service.ProbeAsync(new TcpPingProbeRequest("example.com", 443, 1, 5000), cts.Token);
        cts.CancelAfter(20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task ProbeAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = new TcpPingService(Returns(1.0));

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ProbeAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Ctor_NullDelegate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TcpPingService(null!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProbeAsync_EchoesRequestValues_OnBothPaths(bool success)
    {
        var service = success
            ? new TcpPingService(Returns(12.0))
            : new TcpPingService(Throws(new InvalidOperationException("boom")));

        var result = await service.ProbeAsync(new TcpPingProbeRequest("host", 8443, 7, 1000), CancellationToken.None);

        Assert.Equal(7, result.Seq);
        Assert.Equal("host", result.Host);
        Assert.Equal(8443, result.Port);
    }

    [Fact]
    public async Task PublicCtor_SmokeAgainstUnusedPort_ReturnsFailedQuickly()
    {
        var unusedPort = GetUnusedTcpPort();
        var service = new TcpPingService();
        var stopwatch = Stopwatch.StartNew();

        var result = await service.ProbeAsync(new TcpPingProbeRequest("127.0.0.1", unusedPort, 1, 250), CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(TcpPingProbeStatus.Failed, result.Status);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000);
    }

    [Fact]
    public async Task ProbeAsync_NullMessageFromException_IsNormalizedByFailedFactory()
    {
        var service = new TcpPingService(Throws(new Exception(null as string)));

        var result = await service.ProbeAsync(new TcpPingProbeRequest("host", 443, 1, 1000), CancellationToken.None);

        Assert.Equal(TcpPingProbeStatus.Failed, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    private static Func<string, int, CancellationToken, Task<double>> Returns(double latencyMs)
        => (_, _, _) => Task.FromResult(latencyMs);

    private static Func<string, int, CancellationToken, Task<double>> Throws(Exception ex)
        => (_, _, _) => Task.FromException<double>(ex);

    private static int GetUnusedTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
