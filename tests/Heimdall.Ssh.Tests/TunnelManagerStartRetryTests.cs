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

namespace Heimdall.Ssh.Tests;

public class TunnelManagerStartRetryTests
{
    [Fact]
    public void ExecuteStartWithRetry_SucceedsOnFirstAttempt_DoesNotSleep()
    {
        var attempts = 0;
        var sleeps = 0;

        TunnelManager.ExecuteStartWithRetry(
            () => attempts++,
            "test port",
            maxAttempts: 3,
            retryDelay: TimeSpan.Zero,
            sleep: _ => sleeps++);

        Assert.Equal(1, attempts);
        Assert.Equal(0, sleeps);
    }

    [Fact]
    public void ExecuteStartWithRetry_AddressAlreadyInUseThenSuccess_RetriesOnce()
    {
        var attempts = 0;
        var sleeps = 0;

        TunnelManager.ExecuteStartWithRetry(
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new SocketException((int)SocketError.AddressAlreadyInUse);
                }
            },
            "test port",
            maxAttempts: 3,
            retryDelay: TimeSpan.FromMilliseconds(50),
            sleep: delay =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(50), delay);
                sleeps++;
            });

        Assert.Equal(2, attempts);
        Assert.Equal(1, sleeps);
    }

    [Fact]
    public void ExecuteStartWithRetry_AddressAlreadyInUseEveryAttempt_PropagatesFinalException()
    {
        var attempts = 0;
        var sleeps = 0;
        var exceptions = new[]
        {
            new SocketException((int)SocketError.AddressAlreadyInUse),
            new SocketException((int)SocketError.AddressAlreadyInUse),
            new SocketException((int)SocketError.AddressAlreadyInUse)
        };

        var thrown = Assert.Throws<SocketException>(() =>
            TunnelManager.ExecuteStartWithRetry(
                () => throw exceptions[attempts++],
                "test port",
                maxAttempts: 3,
                retryDelay: TimeSpan.Zero,
                sleep: _ => sleeps++));

        Assert.Same(exceptions[2], thrown);
        Assert.Equal(3, attempts);
        Assert.Equal(2, sleeps);
    }

    [Fact]
    public void ExecuteStartWithRetry_DifferentSocketError_DoesNotRetry()
    {
        var attempts = 0;
        var sleeps = 0;
        var expected = new SocketException((int)SocketError.ConnectionRefused);

        var thrown = Assert.Throws<SocketException>(() =>
            TunnelManager.ExecuteStartWithRetry(
                () =>
                {
                    attempts++;
                    throw expected;
                },
                "test port",
                maxAttempts: 3,
                retryDelay: TimeSpan.Zero,
                sleep: _ => sleeps++));

        Assert.Same(expected, thrown);
        Assert.Equal(1, attempts);
        Assert.Equal(0, sleeps);
    }

    [Fact]
    public void ExecuteStartWithRetry_UnrelatedException_DoesNotRetry()
    {
        var attempts = 0;
        var sleeps = 0;
        var expected = new InvalidOperationException("not a bind failure");

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            TunnelManager.ExecuteStartWithRetry(
                () =>
                {
                    attempts++;
                    throw expected;
                },
                "test port",
                maxAttempts: 3,
                retryDelay: TimeSpan.Zero,
                sleep: _ => sleeps++));

        Assert.Same(expected, thrown);
        Assert.Equal(1, attempts);
        Assert.Equal(0, sleeps);
    }

    [Fact]
    public void ExecuteStartWithRetry_MaxAttemptsOne_DoesNotRetry()
    {
        var attempts = 0;
        var sleeps = 0;
        var expected = new SocketException((int)SocketError.AddressAlreadyInUse);

        var thrown = Assert.Throws<SocketException>(() =>
            TunnelManager.ExecuteStartWithRetry(
                () =>
                {
                    attempts++;
                    throw expected;
                },
                "test port",
                maxAttempts: 1,
                retryDelay: TimeSpan.Zero,
                sleep: _ => sleeps++));

        Assert.Same(expected, thrown);
        Assert.Equal(1, attempts);
        Assert.Equal(0, sleeps);
    }

    [Fact(Timeout = 1000)]
    public Task ExecuteStartWithRetry_PortHeldByTcpListener_PropagatesAddressAlreadyInUse()
    {
        using var holder = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        holder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        holder.Listen(1);
        var occupiedPort = ((IPEndPoint)holder.LocalEndPoint!).Port;
        var attempts = 0;
        var sleeps = 0;

        var thrown = Assert.Throws<SocketException>(() =>
            TunnelManager.ExecuteStartWithRetry(
                () =>
                {
                    attempts++;
                    using var contender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    contender.Bind(new IPEndPoint(IPAddress.Loopback, occupiedPort));
                    contender.Listen(1);
                },
                $"local port {occupiedPort}",
                maxAttempts: 3,
                retryDelay: TimeSpan.Zero,
                sleep: _ => sleeps++));

        Assert.Equal(SocketError.AddressAlreadyInUse, thrown.SocketErrorCode);
        Assert.Equal(3, attempts);
        Assert.Equal(2, sleeps);
        return Task.CompletedTask;
    }
}
