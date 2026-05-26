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
using Heimdall.Terminal;
using Heimdall.Terminal.ConPty;

namespace Heimdall.Terminal.Tests;

public sealed class TerminalSessionCancellationTests
{
    [Fact]
    public async Task ConPtySession_StartAsync_PreCancelledToken_Throws()
    {
        if (!OperatingSystem.IsWindows() || !ConPtySession.IsAvailable)
        {
            return;
        }

        ConPtySession session = new();
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                session.StartAsync(
                    TerminalTestHelpers.ResolvePowerShellExecutable(),
                    "-NoLogo -NoProfile -Command \"exit 0\"",
                    cancellationToken: cancellationTokenSource.Token));
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task PipeModeSession_StartAsync_PreCancelledToken_Throws()
    {
        PipeModeSession session = new();
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                session.StartAsync(
                    TerminalTestHelpers.ResolvePowerShellExecutable(),
                    "-NoLogo -NoProfile -Command \"exit 0\"",
                    cancellationToken: cancellationTokenSource.Token));
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSession_StartAsync_ExternalCancellation_AbortsConnect()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(100));

        TelnetSession session = new(IPAddress.Parse("192.0.2.1").ToString(), 65000, connectTimeoutMs: 5000);
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                session.StartAsync(
                    string.Empty,
                    string.Empty,
                    cancellationToken: cancellationTokenSource.Token));
        }
        finally
        {
            stopwatch.Stop();
            session.Dispose();
        }

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Cancellation took {stopwatch.Elapsed.TotalMilliseconds} ms.");
    }
}
