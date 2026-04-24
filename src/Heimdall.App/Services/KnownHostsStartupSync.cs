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

using Heimdall.Core.Configuration;
using Heimdall.Core.Logging;
using Heimdall.Core.Ssh;
using SshKnownHostsImporter = Heimdall.Core.Ssh.KnownHostsImporter;

namespace Heimdall.App.Services;

/// <summary>
/// Runs the optional known_hosts startup import without blocking WPF startup.
/// </summary>
public sealed class KnownHostsStartupSync
{
    private readonly Func<string?, KnownHostsImportReport> _importFile;
    private readonly Action<Action> _schedule;

    public KnownHostsStartupSync(IHostKeyTrustService trustService)
        : this(path => new SshKnownHostsImporter(trustService).ImportFile(path), action => _ = Task.Run(action))
    {
    }

    public KnownHostsStartupSync(
        Func<string?, KnownHostsImportReport> importFile,
        Action<Action> schedule)
    {
        _importFile = importFile;
        _schedule = schedule;
    }

    public bool StartIfEnabled(AppSettings settings, string? knownHostsPath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.SyncKnownHostsAtStartup)
        {
            return false;
        }

        _schedule(() => RunImport(knownHostsPath));
        return true;
    }

    private void RunImport(string? knownHostsPath)
    {
        try
        {
            var report = _importFile(knownHostsPath);
            FileLogger.Info(
                $"known_hosts startup sync completed: imported={report.Imported}, matched={report.Matched}, conflicts={report.Conflicts.Count}");
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"known_hosts startup sync failed: {ex.Message}");
        }
    }
}
