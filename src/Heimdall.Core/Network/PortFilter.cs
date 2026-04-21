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

namespace Heimdall.Core.Network;

/// <summary>
/// Pure filter predicate shared by the open ports tool.
/// </summary>
public static class PortFilter
{
    public static bool Matches(PortEntry? entry, string? filter)
    {
        if (entry is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.LocalAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.LocalPort.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal)
            || entry.RemoteAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Protocol.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.State.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Pid.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal);
    }
}
