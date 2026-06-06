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

using System.IO;
using Heimdall.Core.Logging;

namespace Heimdall.App.Services;

internal sealed class PlinkPasswordFileJanitor
{
    internal const string PasswordFilePrefix = "heimdall_ssh_pw_";
    internal const string PasswordFileSearchPattern = "heimdall_ssh_pw_*";
    internal const int DefaultMaxAgeMinutes = 60;

    private readonly Func<string> _tempDirectory;
    private readonly Func<string, IEnumerable<string>> _enumerateFiles;
    private readonly Func<string, DateTime> _getLastWriteTimeUtc;
    private readonly Action<string> _delete;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _maxAge;

    public PlinkPasswordFileJanitor(
        Func<string>? tempDirectory = null,
        Func<string, IEnumerable<string>>? enumerateFiles = null,
        Func<string, DateTime>? getLastWriteTimeUtc = null,
        Action<string>? delete = null,
        Func<DateTime>? utcNow = null,
        TimeSpan? maxAge = null)
    {
        _tempDirectory = tempDirectory ?? Path.GetTempPath;
        _enumerateFiles = enumerateFiles ?? DefaultEnumerate;
        _getLastWriteTimeUtc = getLastWriteTimeUtc ?? File.GetLastWriteTimeUtc;
        _delete = delete ?? File.Delete;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _maxAge = maxAge ?? TimeSpan.FromMinutes(DefaultMaxAgeMinutes);
    }

    public int SweepStale()
    {
        DateTime threshold = _utcNow() - _maxAge;
        string directory = _tempDirectory();

        IEnumerable<string> candidates;
        try
        {
            candidates = _enumerateFiles(directory);
        }
        catch (IOException ex)
        {
            FileLogger.Warn($"[PlinkPasswordFileJanitor] Enumerate failed: {ex.Message}");
            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            FileLogger.Warn($"[PlinkPasswordFileJanitor] Enumerate unauthorized: {ex.Message}");
            return 0;
        }

        int removed = 0;
        try
        {
            foreach (string path in candidates)
            {
                try
                {
                    if (_getLastWriteTimeUtc(path) > threshold)
                    {
                        continue;
                    }

                    _delete(path);
                    removed++;
                }
                catch (IOException ex)
                {
                    FileLogger.Warn($"[PlinkPasswordFileJanitor] Delete failed: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    FileLogger.Warn($"[PlinkPasswordFileJanitor] Delete unauthorized: {ex.Message}");
                }
            }
        }
        catch (IOException ex)
        {
            FileLogger.Warn($"[PlinkPasswordFileJanitor] Enumerate failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            FileLogger.Warn($"[PlinkPasswordFileJanitor] Enumerate unauthorized: {ex.Message}");
        }

        if (removed > 0)
        {
            FileLogger.Info($"[PlinkPasswordFileJanitor] Swept {removed} stale plink password file(s).");
        }

        return removed;
    }

    private static IEnumerable<string> DefaultEnumerate(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, PasswordFileSearchPattern)
            : Array.Empty<string>();
}
