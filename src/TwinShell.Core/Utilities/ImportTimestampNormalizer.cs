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

namespace TwinShell.Core.Utilities;

public static class ImportTimestampNormalizer
{
    /// <summary>
    /// Clock-skew tolerance absorbing legitimately fast clocks.
    /// </summary>
    public static readonly TimeSpan DefaultFutureSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Clamps a candidate timestamp to nowUtc when it lies more than skew in the future.
    /// </summary>
    public static DateTime ClampFutureToNow(DateTime candidate, DateTime nowUtc, TimeSpan skew, out bool wasClamped)
    {
        DateTime upperBound = nowUtc + skew;
        if (candidate > upperBound)
        {
            wasClamped = true;
            return nowUtc;
        }

        wasClamped = false;
        return candidate;
    }
}
