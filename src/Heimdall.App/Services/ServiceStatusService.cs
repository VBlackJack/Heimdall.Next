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

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Heimdall.Core.SystemInfo;

namespace Heimdall.App.Services;

/// <summary>
/// Loads local Windows services and launches elevated PowerShell actions.
/// </summary>
public interface IServiceStatusService
{
    Task<IReadOnlyList<ServiceEntry>> LoadAsync(CancellationToken ct);
    void StartService(string serviceName);
    void StopService(string serviceName);
    void RestartService(string serviceName);
}

public sealed class ServiceStatusService : IServiceStatusService
{
    private readonly Func<CancellationToken, Task<string>> _loadCsvAsync;
    private readonly Func<ProcessStartInfo, Process?> _launchProcess;

    public ServiceStatusService()
        : this(DefaultLoadCsvAsync, Process.Start)
    {
    }

    internal ServiceStatusService(
        Func<CancellationToken, Task<string>> loadCsvAsync,
        Func<ProcessStartInfo, Process?> launchProcess)
    {
        ArgumentNullException.ThrowIfNull(loadCsvAsync);
        ArgumentNullException.ThrowIfNull(launchProcess);
        _loadCsvAsync = loadCsvAsync;
        _launchProcess = launchProcess;
    }

    public async Task<IReadOnlyList<ServiceEntry>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var csv = await _loadCsvAsync(ct).ConfigureAwait(false) ?? string.Empty;
        return PowershellServiceCsvParser.Parse(csv);
    }

    public void StartService(string serviceName) => ExecuteServiceAction("Start-Service", serviceName);

    public void StopService(string serviceName) => ExecuteServiceAction("Stop-Service", serviceName);

    public void RestartService(string serviceName) => ExecuteServiceAction("Restart-Service", serviceName);

    private void ExecuteServiceAction(string command, string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var safeName = serviceName.Trim().Replace("'", "''", StringComparison.Ordinal);
        var script = $"{command} '{safeName}'";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        try
        {
            var process = _launchProcess(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -EncodedCommand {encoded}",
                Verb = "runas",
                UseShellExecute = true,
            });

            process?.Dispose();
        }
        catch (Win32Exception)
        {
            // Preserve current behavior: UAC declined or unavailable.
        }
    }

    private static async Task<string> DefaultLoadCsvAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"Get-Service | Select-Object Name,DisplayName,Status,StartType | ConvertTo-Csv -NoTypeInformation\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        var output = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return output;
    }
}
