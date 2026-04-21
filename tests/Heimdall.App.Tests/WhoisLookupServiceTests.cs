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

using System.Net.Sockets;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public sealed class WhoisLookupServiceTests
{
    [Fact]
    public async Task LookupAsync_WithoutGateway_UsesDirectPath()
    {
        var service = BuildService(
            gateway: null,
            directQuery: (domain, ct) => Task.FromResult("raw whois text"),
            tunnelQuery: (_, _, _) => throw new Xunit.Sdk.XunitException("tunnelQuery should not be invoked"));

        var result = await service.LookupAsync(new WhoisLookupRequest("example.com"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("raw whois text", result.Output);
    }

    [Fact]
    public async Task LookupAsync_WithGateway_UsesTunnelPath()
    {
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump.example.com", Port = 22 };
        var service = BuildService(
            gateway,
            directQuery: (_, _) => throw new Xunit.Sdk.XunitException("directQuery should not be invoked"),
            tunnelQuery: (gw, domain, ct) =>
            {
                Assert.Same(gateway, gw);
                return Task.FromResult("tunnel output");
            });

        var result = await service.LookupAsync(new WhoisLookupRequest("example.com"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("tunnel output", result.Output);
    }

    [Fact]
    public async Task LookupAsync_TrimsDomainBeforeDispatch()
    {
        string? capturedDomain = null;
        var service = BuildService(
            gateway: null,
            directQuery: (domain, ct) =>
            {
                capturedDomain = domain;
                return Task.FromResult("ok");
            },
            tunnelQuery: (_, _, _) => Task.FromResult(string.Empty));

        await service.LookupAsync(new WhoisLookupRequest("  example.com  "), CancellationToken.None);

        Assert.Equal("example.com", capturedDomain);
    }

    [Fact]
    public async Task SetGateway_SwitchesPathOnSubsequentCall()
    {
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump", Port = 22 };
        var directCalls = 0;
        var tunnelCalls = 0;
        var service = BuildService(
            gateway: null,
            directQuery: (_, _) =>
            {
                directCalls++;
                return Task.FromResult("direct");
            },
            tunnelQuery: (_, _, _) =>
            {
                tunnelCalls++;
                return Task.FromResult("tunnel");
            });

        await service.LookupAsync(new WhoisLookupRequest("example.com"), CancellationToken.None);
        service.SetGateway(gateway);
        await service.LookupAsync(new WhoisLookupRequest("example.com"), CancellationToken.None);
        service.SetGateway(null);
        await service.LookupAsync(new WhoisLookupRequest("example.com"), CancellationToken.None);

        Assert.Equal(2, directCalls);
        Assert.Equal(1, tunnelCalls);
    }

    [Fact]
    public async Task LookupAsync_DirectPathThrowsSocketException_ReturnsFailedError()
    {
        var service = BuildService(
            gateway: null,
            directQuery: (_, _) => throw new SocketException((int)SocketError.HostNotFound),
            tunnelQuery: (_, _, _) => Task.FromResult(string.Empty));

        var result = await service.LookupAsync(new WhoisLookupRequest("invalid.example"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ToolWhoisErrorFailed", result.ErrorKey);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorArg));
    }

    [Fact]
    public async Task LookupAsync_TunnelPathThrowsInvalidOperationException_ReturnsFailedError()
    {
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump", Port = 22 };
        var service = BuildService(
            gateway,
            directQuery: (_, _) => Task.FromResult(string.Empty),
            tunnelQuery: (_, _, _) => throw new InvalidOperationException("preflight failed"));

        var result = await service.LookupAsync(new WhoisLookupRequest("example.com"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ToolWhoisErrorFailed", result.ErrorKey);
        Assert.Equal("preflight failed", result.ErrorArg);
    }

    [Fact]
    public async Task LookupAsync_OperationCancelledDuringQuery_ReturnsTimeoutError()
    {
        using var cts = new CancellationTokenSource();
        var service = BuildService(
            gateway: null,
            directQuery: (_, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(string.Empty);
            },
            tunnelQuery: (_, _, _) => Task.FromResult(string.Empty));

        var result = await service.LookupAsync(new WhoisLookupRequest("example.com"), cts.Token);

        Assert.False(result.Success);
        Assert.Equal("ToolWhoisErrorTimeout", result.ErrorKey);
        Assert.Null(result.ErrorArg);
    }

    [Fact]
    public async Task LookupAsync_PreCancelledToken_ThrowsSynchronously()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = BuildService(
            gateway: null,
            directQuery: (_, _) => throw new Xunit.Sdk.XunitException("directQuery should not be invoked"),
            tunnelQuery: (_, _, _) => throw new Xunit.Sdk.XunitException("tunnelQuery should not be invoked"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LookupAsync(new WhoisLookupRequest("example.com"), cts.Token));
    }

    [Fact]
    public async Task LookupAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = BuildService(
            gateway: null,
            directQuery: (_, _) => Task.FromResult(string.Empty),
            tunnelQuery: (_, _, _) => Task.FromResult(string.Empty));

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.LookupAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LookupAsync_InvalidDomain_ThrowsArgumentException(string invalidDomain)
    {
        var service = BuildService(
            gateway: null,
            directQuery: (_, _) => Task.FromResult(string.Empty),
            tunnelQuery: (_, _, _) => Task.FromResult(string.Empty));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.LookupAsync(new WhoisLookupRequest(invalidDomain), CancellationToken.None));

        Assert.Equal("request", ex.ParamName);
    }

    [Fact]
    public async Task LookupAsync_ElapsedMsIsNonNegative()
    {
        var service = BuildService(
            gateway: null,
            directQuery: (_, _) => Task.FromResult("ok"),
            tunnelQuery: (_, _, _) => Task.FromResult(string.Empty));

        var result = await service.LookupAsync(new WhoisLookupRequest("example.com"), CancellationToken.None);

        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task LookupAsync_NullOutputFromDelegate_IsNormalizedToEmptyString()
    {
        var service = BuildService(
            gateway: null,
            directQuery: (_, _) => Task.FromResult<string>(null!),
            tunnelQuery: (_, _, _) => Task.FromResult(string.Empty));

        var result = await service.LookupAsync(new WhoisLookupRequest("example.com"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    private static WhoisLookupService BuildService(
        SshGatewayDto? gateway,
        Func<string, CancellationToken, Task<string>> directQuery,
        Func<SshGatewayDto, string, CancellationToken, Task<string>> tunnelQuery)
        => new(gateway, directQuery, tunnelQuery);
}
