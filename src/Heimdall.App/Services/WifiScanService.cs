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
using Heimdall.Core.Network;

namespace Heimdall.App.Services;

/// <summary>
/// Contract for scanning visible Wi-Fi networks.
/// </summary>
public interface IWifiScanService
{
    Task<IReadOnlyList<WifiEntry>> ScanAsync();
}

/// <summary>
/// Stateless wrapper over <c>netsh wlan show networks mode=bssid</c>.
/// </summary>
public sealed class WifiScanService : IWifiScanService
{
    public const int ProcessTimeoutMs = 10000;

    private readonly Func<Task<string>> _runNetshAsync;

    public WifiScanService()
        : this(DefaultRunNetshAsync)
    {
    }

    internal WifiScanService(Func<Task<string>> runNetshAsync)
    {
        ArgumentNullException.ThrowIfNull(runNetshAsync);
        _runNetshAsync = runNetshAsync;
    }

    public async Task<IReadOnlyList<WifiEntry>> ScanAsync()
    {
        var output = await _runNetshAsync().ConfigureAwait(false) ?? string.Empty;
        return NetshWifiParser.Parse(output)
            .OrderByDescending(entry => entry.SignalValue)
            .ToList();
    }

    private static Task<string> DefaultRunNetshAsync()
    {
        return Task.Run(async () =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show networks mode=bssid",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start netsh process.");

            var outputTask = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(ProcessTimeoutMs))
            {
                try
                {
                    proc.Kill();
                }
                catch
                {
                    // Preserve current best-effort kill behavior.
                }
            }

            return await outputTask.ConfigureAwait(false);
        });
    }
}
