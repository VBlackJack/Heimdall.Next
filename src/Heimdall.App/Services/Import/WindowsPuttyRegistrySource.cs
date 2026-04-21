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

using Heimdall.Core.Ssh;
using Microsoft.Win32;

namespace Heimdall.App.Services.Import;

/// <summary>
/// Reads PuTTY sessions from HKCU in strict read-only mode.
/// </summary>
public sealed class WindowsPuttyRegistrySource : IPuttySessionRegistrySource
{
    private const string SessionsRegistryPath = @"Software\SimonTatham\PuTTY\Sessions";

    public Task<IReadOnlyList<RawPuttySession>> ReadSessionsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var root = Registry.CurrentUser.OpenSubKey(SessionsRegistryPath, writable: false);
        if (root is null)
        {
            return Task.FromResult<IReadOnlyList<RawPuttySession>>([]);
        }

        var sessions = new List<RawPuttySession>();
        foreach (var subKeyName in root.GetSubKeyNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            using var subKey = root.OpenSubKey(subKeyName, writable: false);
            if (subKey is null)
            {
                continue;
            }

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var valueName in subKey.GetValueNames())
            {
                values[valueName] = subKey.GetValue(valueName);
            }

            sessions.Add(new RawPuttySession(subKeyName, values));
        }

        return Task.FromResult<IReadOnlyList<RawPuttySession>>(sessions);
    }
}
