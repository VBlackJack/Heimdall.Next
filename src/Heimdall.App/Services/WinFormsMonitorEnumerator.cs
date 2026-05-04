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

using Heimdall.Core.Logging;
using WinForms = System.Windows.Forms;

namespace Heimdall.App.Services;

/// <summary>
/// Monitor enumerator backed by <see cref="WinForms.Screen.AllScreens"/>.
/// </summary>
public sealed class WinFormsMonitorEnumerator : IMonitorEnumerator
{
    /// <inheritdoc />
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        try
        {
            return WinForms.Screen.AllScreens
                .Select((screen, index) => new MonitorInfo(
                    index,
                    screen.Bounds.Width,
                    screen.Bounds.Height,
                    screen.Primary,
                    screen.DeviceName))
                .ToArray();
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"WinForms monitor enumeration fallback: {ex.Message}");
            return [];
        }
    }
}
