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

using System.Globalization;
using System.Text.RegularExpressions;
using Heimdall.Core.Logging;
using Renci.SshNet;

namespace Heimdall.Ssh;

/// <summary>
/// Holds a snapshot of server resource usage collected via SSH commands.
/// </summary>
public sealed record ServerHealthData(
    double CpuPercent,
    long MemTotalMb,
    long MemUsedMb,
    long MemFreeMb,
    string DiskUsed,
    string DiskTotal,
    int DiskPercent);

internal interface IHealthCommandRunner
{
    Task<string> RunAsync(string command, CancellationToken cancellationToken);
}

internal sealed class SshHealthCommandRunner(SshClient client) : IHealthCommandRunner
{
    public async Task<string> RunAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var sshCommand = client.CreateCommand(command);
        var asyncResult = sshCommand.BeginExecute();
        using var cancellationRegistration = cancellationToken.Register(static state =>
        {
            try
            {
                ((SshCommand)state!).CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // Command already disposed during shutdown.
            }
            catch (InvalidOperationException)
            {
                // Command already completed or never fully started.
            }
        }, sshCommand);

        var result = await Task<string>.Factory.FromAsync(
                asyncResult,
                sshCommand.EndExecute)
            .ConfigureAwait(false);

        return sshCommand.ExitStatus == 0 ? result : string.Empty;
    }
}

/// <summary>
/// Polls an SSH server for CPU, RAM, and disk usage at a configurable interval.
/// Uses the existing <see cref="SshClient"/> from an active shell session to run
/// lightweight monitoring commands on a multiplexed channel.
/// </summary>
public sealed class ServerHealthMonitor : IDisposable
{
    private static readonly int DefaultPollIntervalSeconds = 15;
    private const string CpuCommandText = "top -b -n 1 | head -5";
    private const string MemoryCommandText = "free -m | grep Mem";
    private const string DiskCommandText = "df -h / | tail -1";

    private static readonly Regex CpuIdleRegex = new(
        @"%?Cpu.*?:\s.*?(\d+[\.,]\d+)\s*id",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CpuUsageRegex = new(
        @"%?Cpu.*?:\s*(\d+[\.,]\d+)\s*us",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MemRegex = new(
        @"Mem:\s+(\d+)\s+(\d+)\s+(\d+)",
        RegexOptions.Compiled);

    private static readonly Regex DiskRegex = new(
        @"(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\d+)%",
        RegexOptions.Compiled);

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private readonly Func<SshClient, IHealthCommandRunner> _commandRunnerFactory;
    private bool _disposed;

    /// <summary>Raised on the thread pool when new health data is available.</summary>
    public event Action<ServerHealthData>? HealthUpdated;

    public ServerHealthMonitor()
        : this(static client => new SshHealthCommandRunner(client))
    {
    }

    internal ServerHealthMonitor(Func<SshClient, IHealthCommandRunner> commandRunnerFactory)
    {
        _commandRunnerFactory = commandRunnerFactory
            ?? throw new ArgumentNullException(nameof(commandRunnerFactory));
    }

    /// <summary>
    /// Starts polling the remote server for health metrics.
    /// </summary>
    /// <param name="client">An already-connected SSH client.</param>
    /// <param name="cancellationToken">External cancellation support.</param>
    public Task StartAsync(SshClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cts is not null)
        {
            throw new InvalidOperationException("Health monitor is already running.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollTask = PollLoopAsync(client, _commandRunnerFactory(client), _cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>Stops the polling loop.</summary>
    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        try
        {
            _cts.Cancel();
            if (_pollTask is not null)
            {
                await _pollTask.WaitAsync(TimeSpan.FromMilliseconds(500))
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected from task cancellation
        }
        catch (TimeoutException)
        {
            // Expected from WaitAsync timeout
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _pollTask = null;
        }
    }

    /// <summary>Stops the polling loop synchronously (best-effort for Dispose).</summary>
    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        try
        {
            _cts.Cancel();
            _pollTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // Expected from task cancellation
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _pollTask = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private async Task PollLoopAsync(
        SshClient client,
        IHealthCommandRunner commandRunner,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                {
                    FileLogger.Warn("ServerHealthMonitor: SSH client disconnected, stopping poll loop");
                    break;
                }

                var data = await CollectHealthDataAsync(commandRunner, ct).ConfigureAwait(false);
                if (data is not null)
                {
                    HealthUpdated?.Invoke(data);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                FileLogger.Warn($"ServerHealthMonitor poll error: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(DefaultPollIntervalSeconds), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task<ServerHealthData?> CollectHealthDataAsync(
        IHealthCommandRunner commandRunner,
        CancellationToken cancellationToken)
    {
        double cpuPercent = 0;
        long memTotal = 0, memUsed = 0, memFree = 0;
        string diskUsed = "?", diskTotal = "?";
        int diskPercent = 0;

        var cpuTask = RunHealthCommandAsync(
            commandRunner,
            CpuCommandText,
            "CPU",
            cancellationToken);
        var memoryTask = RunHealthCommandAsync(
            commandRunner,
            MemoryCommandText,
            "RAM",
            cancellationToken);
        var diskTask = RunHealthCommandAsync(
            commandRunner,
            DiskCommandText,
            "disk",
            cancellationToken);

        var results = await Task.WhenAll(cpuTask, memoryTask, diskTask).ConfigureAwait(false);
        var cpuResult = results[0];
        var memResult = results[1];
        var diskResult = results[2];

        if (!string.IsNullOrWhiteSpace(cpuResult))
        {
            cpuPercent = ParseCpuUsage(cpuResult);
        }

        if (!string.IsNullOrWhiteSpace(memResult))
        {
            ParseMemory(memResult, out memTotal, out memUsed, out memFree);
        }

        if (!string.IsNullOrWhiteSpace(diskResult))
        {
            ParseDisk(diskResult, out diskUsed, out diskTotal, out diskPercent);
        }

        return new ServerHealthData(cpuPercent, memTotal, memUsed, memFree, diskUsed, diskTotal, diskPercent);
    }

    private static async Task<string> RunHealthCommandAsync(
        IHealthCommandRunner commandRunner,
        string commandText,
        string metricName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await commandRunner.RunAsync(commandText, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"ServerHealthMonitor {metricName} command failed: {ex.Message}");
            return string.Empty;
        }
    }

    internal static double ParseCpuUsage(string topOutput)
    {
        // Try to extract idle percentage and compute usage = 100 - idle
        var idleMatch = CpuIdleRegex.Match(topOutput);
        if (idleMatch.Success)
        {
            var idleStr = idleMatch.Groups[1].Value.Replace(',', '.');
            if (double.TryParse(idleStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var idle))
            {
                return Math.Round(Math.Max(0, 100.0 - idle), 1);
            }
        }

        // Fallback: extract user percentage directly
        var usMatch = CpuUsageRegex.Match(topOutput);
        if (usMatch.Success)
        {
            var usStr = usMatch.Groups[1].Value.Replace(',', '.');
            if (double.TryParse(usStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var us))
            {
                return Math.Round(us, 1);
            }
        }

        return 0;
    }

    internal static void ParseMemory(string freeOutput, out long total, out long used, out long free)
    {
        total = 0;
        used = 0;
        free = 0;

        var match = MemRegex.Match(freeOutput);
        if (match.Success)
        {
            long.TryParse(match.Groups[1].Value, out total);
            long.TryParse(match.Groups[2].Value, out used);
            long.TryParse(match.Groups[3].Value, out free);
        }
    }

    internal static void ParseDisk(string dfOutput, out string used, out string total, out int percent)
    {
        used = "?";
        total = "?";
        percent = 0;

        var match = DiskRegex.Match(dfOutput);
        if (match.Success)
        {
            total = match.Groups[2].Value;
            used = match.Groups[3].Value;
            int.TryParse(match.Groups[5].Value, out percent);
        }
    }
}
