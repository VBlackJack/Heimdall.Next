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

using System.Net;
using System.Net.Sockets;
using Heimdall.App.Services;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public sealed class WakeOnLanServiceTests
{
    [Fact]
    public async Task SendAsync_ValidInput_UsesNormalizedMacAndEndpoint()
    {
        byte[]? packet = null;
        IPAddress? address = null;
        var port = 0;
        using var cts = new CancellationTokenSource();
        var service = new WakeOnLanService((bytes, ip, p, _) =>
        {
            packet = bytes;
            address = ip;
            port = p;
            return Task.CompletedTask;
        });

        var result = await service.SendAsync(new WakeOnLanRequest("aa-bb-cc-dd-ee-ff", "255.255.255.255", 9), cts.Token);

        Assert.True(result.Success);
        Assert.Equal("AA:BB:CC:DD:EE:FF", result.MacAddress);
        Assert.Equal(IPAddress.Broadcast, address);
        Assert.Equal(9, port);
        Assert.NotNull(packet);
        Assert.Equal(MagicPacketBuilder.PacketLength, packet!.Length);
    }

    [Fact]
    public async Task SendAsync_InvalidMac_ReturnsError()
    {
        var service = new WakeOnLanService((_, _, _, _) => Task.CompletedTask);

        var result = await service.SendAsync(new WakeOnLanRequest("bad", "255.255.255.255", 9), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ToolWolErrorInvalidMac", result.ErrorKey);
    }

    [Fact]
    public async Task SendAsync_InvalidBroadcast_ReturnsError()
    {
        var service = new WakeOnLanService((_, _, _, _) => Task.CompletedTask);

        var result = await service.SendAsync(new WakeOnLanRequest("AA:BB:CC:DD:EE:FF", "not-an-ip", 9), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ToolWolErrorInvalidBroadcast", result.ErrorKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public async Task SendAsync_InvalidPort_FallsBackToDefault(int inputPort)
    {
        var seenPort = -1;
        var service = new WakeOnLanService((_, _, port, _) =>
        {
            seenPort = port;
            return Task.CompletedTask;
        });

        var result = await service.SendAsync(new WakeOnLanRequest("AA:BB:CC:DD:EE:FF", "255.255.255.255", inputPort), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(WakeOnLanService.DefaultPort, seenPort);
        Assert.Equal(WakeOnLanService.DefaultPort, result.Port);
    }

    [Fact]
    public async Task SendAsync_SocketException_ReturnsSocketError()
    {
        var service = new WakeOnLanService((_, _, _, _) => throw new SocketException((int)SocketError.ConnectionRefused));

        var result = await service.SendAsync(new WakeOnLanRequest("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ToolWolErrorSocket", result.ErrorKey);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorArg));
    }

    [Fact]
    public async Task SendAsync_GenericException_ReturnsSocketError()
    {
        var service = new WakeOnLanService((_, _, _, _) => throw new InvalidOperationException("boom"));

        var result = await service.SendAsync(new WakeOnLanRequest("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ToolWolErrorSocket", result.ErrorKey);
        Assert.Equal("boom", result.ErrorArg);
    }

    [Fact]
    public async Task SendAsync_OperationCanceledException_ReturnsSocketError()
    {
        var service = new WakeOnLanService((_, _, _, _) => throw new OperationCanceledException("cancelled"));

        var result = await service.SendAsync(new WakeOnLanRequest("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ToolWolErrorSocket", result.ErrorKey);
    }

    [Fact]
    public async Task SendAsync_NullRequest_Throws()
    {
        var service = new WakeOnLanService((_, _, _, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SendAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WakeOnLanService(null!));
    }
}
