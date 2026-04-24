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

using System.Threading;
using Renci.SshNet;

namespace Heimdall.Ssh.Tests;

public sealed class ServerHealthMonitorTests
{
    [Fact]
    public void ParseCpuUsage_ParsesTopIdleOutput()
    {
        const string topOutput = "%Cpu(s):  1.3 us,  0.5 sy,  0.0 ni, 98.2 id,  0.0 wa,  0.0 hi,  0.0 si,  0.0 st";

        var cpuUsage = ServerHealthMonitor.ParseCpuUsage(topOutput);

        Assert.Equal(1.8, cpuUsage, 1);
    }

    [Fact]
    public void ParseMemory_ParsesFreeOutput()
    {
        const string freeOutput = "Mem:          15896        8340        1234         456        6322        6789";

        ServerHealthMonitor.ParseMemory(freeOutput, out var total, out var used, out var free);

        Assert.Equal(15896, total);
        Assert.Equal(8340, used);
        Assert.Equal(1234, free);
    }

    [Fact]
    public void ParseDisk_ParsesDfOutput()
    {
        const string dfOutput = "/dev/sda1       120G   45G   69G  40% /";

        ServerHealthMonitor.ParseDisk(dfOutput, out var used, out var total, out var percent);

        Assert.Equal("45G", used);
        Assert.Equal("120G", total);
        Assert.Equal(40, percent);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingCommandsPromptly()
    {
        using var client = new ConnectedTestSshClient();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new BlockingHealthCommandRunner();
        var monitor = new ServerHealthMonitor(_ => runner);

        try
        {
            await monitor.StartAsync(client, cancellationSource.Token);
            await runner.Started.WaitAsync(TimeSpan.FromSeconds(2));

            cancellationSource.Cancel();

            await monitor.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(runner.CallCount > 0);
            Assert.True(runner.CancellationCount > 0);
        }
        finally
        {
            await monitor.StopAsync();
            monitor.Dispose();
        }
    }

    private sealed class BlockingHealthCommandRunner : IHealthCommandRunner
    {
        private readonly TaskCompletionSource<object?> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;
        private int _cancellationCount;

        public Task Started => _started.Task;
        public int CallCount => Volatile.Read(ref _callCount);
        public int CancellationCount => Volatile.Read(ref _cancellationCount);

        public async Task<string> RunAsync(string command, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            _started.TrySetResult(null);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return string.Empty;
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancellationCount);
                throw;
            }
        }
    }

    private sealed class ConnectedTestSshClient : SshClient
    {
        public ConnectedTestSshClient()
            : base(new ConnectionInfo("example.com", 22, "tester", new NoneAuthenticationMethod("tester")))
        {
        }

        public override bool IsConnected => true;
    }
}
