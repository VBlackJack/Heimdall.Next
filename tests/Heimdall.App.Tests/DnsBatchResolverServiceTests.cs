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

public sealed class DnsBatchResolverServiceTests
{
    [Fact]
    public async Task ResolveAsync_Success_ReturnsOkResult()
    {
        var service = new DnsBatchResolverService((host, ct) =>
        {
            Assert.Equal("example.com", host);
            return Task.FromResult(new[] { IPAddress.Parse("192.0.2.10") });
        });

        var result = await service.ResolveAsync("example.com", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("192.0.2.10", result.Ipv4);
        Assert.Equal("OK", result.Status);
    }

    [Fact]
    public async Task ResolveAsync_TrimsHostname()
    {
        string? captured = null;
        var service = new DnsBatchResolverService((host, ct) =>
        {
            captured = host;
            return Task.FromResult(Array.Empty<IPAddress>());
        });

        await service.ResolveAsync("  example.com  ", CancellationToken.None);

        Assert.Equal("example.com", captured);
    }

    [Fact]
    public async Task ResolveAsync_EmptyHostname_Throws()
    {
        var service = new DnsBatchResolverService((_, _) => Task.FromResult(Array.Empty<IPAddress>()));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_NullHostname_Throws()
    {
        var service = new DnsBatchResolverService((_, _) => Task.FromResult(Array.Empty<IPAddress>()));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ResolveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_PreCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new DnsBatchResolverService((_, _) => Task.FromResult(Array.Empty<IPAddress>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveAsync("example.com", cts.Token));
    }

    [Fact]
    public async Task ResolveAsync_UserCancellationDuringResolve_Rethrows()
    {
        using var cts = new CancellationTokenSource();
        var service = new DnsBatchResolverService((_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Array.Empty<IPAddress>());
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveAsync("example.com", cts.Token));
    }

    [Fact]
    public async Task ResolveAsync_SocketException_ReturnsFailedWithSocketErrorCode()
    {
        var service = new DnsBatchResolverService((_, _) =>
            throw new SocketException((int)SocketError.HostNotFound));

        var result = await service.ResolveAsync("bad.example", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SocketError.HostNotFound.ToString(), result.Status);
    }

    [Fact]
    public async Task ResolveAsync_GenericException_ReturnsFailedWithMessage()
    {
        var service = new DnsBatchResolverService((_, _) =>
            throw new InvalidOperationException("boom"));

        var result = await service.ResolveAsync("bad.example", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("boom", result.Status);
    }

    [Fact]
    public async Task ResolveAsync_NullArray_TreatedAsEmptySuccess()
    {
        var service = new DnsBatchResolverService((_, _) => Task.FromResult<IPAddress[]>(null!));

        var result = await service.ResolveAsync("example.com", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(DnsBatchResolveResult.Placeholder, result.Ipv4);
        Assert.Equal(DnsBatchResolveResult.Placeholder, result.Ipv6);
    }

    [Fact]
    public async Task ResolveAsync_ElapsedMs_IsNonNegative()
    {
        var service = new DnsBatchResolverService((_, _) => Task.FromResult(Array.Empty<IPAddress>()));

        var result = await service.ResolveAsync("example.com", CancellationToken.None);

        Assert.True(result.ResolveTimeMs >= 0);
    }

    [Fact]
    public void Constructor_NullDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DnsBatchResolverService(null!));
    }
}
