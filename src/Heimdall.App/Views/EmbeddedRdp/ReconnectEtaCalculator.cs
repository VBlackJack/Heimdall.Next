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

namespace Heimdall.App.Views.EmbeddedRdp;

internal static class ReconnectEtaCalculator
{
    public static int? EstimateSeconds(IReadOnlyList<DateTime> attemptTimestampsUtc, DateTime nowUtc)
    {
        if (attemptTimestampsUtc.Count < 2)
        {
            return null;
        }

        var previousTimestamp = attemptTimestampsUtc[^2];
        var lastTimestamp = attemptTimestampsUtc[^1];
        var retryInterval = lastTimestamp - previousTimestamp;
        if (retryInterval <= TimeSpan.Zero)
        {
            return 0;
        }

        var elapsedSinceLastAttempt = nowUtc - lastTimestamp;
        if (elapsedSinceLastAttempt >= retryInterval)
        {
            return 0;
        }

        var remaining = retryInterval - elapsedSinceLastAttempt;
        return Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
    }
}
