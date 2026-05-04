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

namespace Heimdall.Rdp.Display;

/// <summary>
/// Normalizes persisted RDP monitor selections against the current host monitor count.
/// </summary>
public static class RdpSelectedMonitorValidator
{
    /// <summary>
    /// Drops invalid monitor indices while preserving the user's configured order.
    /// An empty result means "use all monitors".
    /// </summary>
    public static int[] Validate(
        IReadOnlyCollection<int>? selectedMonitorIndices,
        int availableMonitorCount,
        Action<string>? warn = null)
    {
        if (selectedMonitorIndices is null || selectedMonitorIndices.Count == 0)
        {
            return [];
        }

        if (availableMonitorCount <= 0)
        {
            warn?.Invoke("No local monitors were detected; falling back to all monitors.");
            return [];
        }

        var result = new List<int>(selectedMonitorIndices.Count);
        var seen = new HashSet<int>();

        foreach (var index in selectedMonitorIndices)
        {
            if (index < 0 || index >= availableMonitorCount)
            {
                warn?.Invoke(
                    $"Ignoring out-of-range selected monitor index {index}; available monitor count is {availableMonitorCount}.");
                continue;
            }

            if (seen.Add(index))
            {
                result.Add(index);
            }
        }

        if (result.Count == 0)
        {
            warn?.Invoke("No selected monitor indices are valid on this host; falling back to all monitors.");
        }

        return [.. result];
    }
}
