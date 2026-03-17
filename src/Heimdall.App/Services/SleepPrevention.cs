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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Heimdall.App.Services;

/// <summary>
/// Prevents Windows from entering sleep/standby while embedded sessions are active.
/// Uses <c>SetThreadExecutionState</c> with a reference-counted session model so that
/// sleep prevention remains active as long as at least one session is running.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SleepPrevention
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private static int _activeSessionCount;

    /// <summary>
    /// Signals that an embedded session has started. When the first session is
    /// registered, sleep prevention is enabled.
    /// </summary>
    public static void SessionStarted()
    {
        if (Interlocked.Increment(ref _activeSessionCount) == 1)
        {
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            Core.Logging.FileLogger.Info("Sleep prevention enabled (sessions active)");
        }
    }

    /// <summary>
    /// Signals that an embedded session has ended. When the last session is
    /// unregistered, sleep prevention is cleared.
    /// </summary>
    public static void SessionEnded()
    {
        if (Interlocked.Decrement(ref _activeSessionCount) <= 0)
        {
            Interlocked.Exchange(ref _activeSessionCount, 0);
            SetThreadExecutionState(ES_CONTINUOUS);
            Core.Logging.FileLogger.Info("Sleep prevention cleared (no sessions)");
        }
    }

    /// <summary>
    /// Unconditionally clears sleep prevention regardless of session count.
    /// Called during application shutdown.
    /// </summary>
    public static void ForceRelease()
    {
        Interlocked.Exchange(ref _activeSessionCount, 0);
        SetThreadExecutionState(ES_CONTINUOUS);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
