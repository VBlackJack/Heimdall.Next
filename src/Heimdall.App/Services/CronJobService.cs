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
using System.Text;
using Heimdall.Core.CronJob;

namespace Heimdall.App.Services;

/// <summary>
/// Loads local Windows scheduled tasks for the cron job manager.
/// </summary>
public interface ICronJobService
{
    Task<IReadOnlyList<WindowsTaskEntry>> LoadWindowsTasksAsync(CancellationToken ct);
}

public sealed class CronJobService : ICronJobService
{
    private readonly Func<CancellationToken, Task<string>> _runSchtasks;

    public CronJobService()
        : this(DefaultRunSchtasksAsync)
    {
    }

    internal CronJobService(Func<CancellationToken, Task<string>> runSchtasks)
    {
        ArgumentNullException.ThrowIfNull(runSchtasks);
        _runSchtasks = runSchtasks;
    }

    public async Task<IReadOnlyList<WindowsTaskEntry>> LoadWindowsTasksAsync(CancellationToken ct)
    {
        var csv = await _runSchtasks(ct).ConfigureAwait(false);
        return SchtasksCsvParser.Parse(csv);
    }

    private static async Task<string> DefaultRunSchtasksAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = "/query /fo CSV /v",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return string.Empty;
        }

        var output = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return output;
    }
}
