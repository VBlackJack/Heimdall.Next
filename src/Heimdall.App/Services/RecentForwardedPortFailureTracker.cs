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

using System;
using System.Collections.Concurrent;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Caches the most recent <see cref="TunnelForwardedPortFailure"/> per tunnel
/// local port. A failure is only returned while it is recent enough to
/// plausibly explain a session drop on that port, so a stale failure cannot be
/// attributed to a later, unrelated disconnect.
/// </summary>
internal sealed class RecentForwardedPortFailureTracker
{
    /// <summary>
    /// How long a recorded failure stays attributable to a disconnect. The
    /// forwarded-port exception and the dependent session drop are observed
    /// within the same second; this window only absorbs scheduling jitter.
    /// </summary>
    private static readonly TimeSpan Retention = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<int, TunnelForwardedPortFailure> _failures =
        new ConcurrentDictionary<int, TunnelForwardedPortFailure>();

    private readonly Func<DateTimeOffset> _clock;

    public RecentForwardedPortFailureTracker()
        : this(static () => DateTimeOffset.UtcNow)
    {
    }

    internal RecentForwardedPortFailureTracker(Func<DateTimeOffset> clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Records (or overwrites) the latest failure for its local port.</summary>
    public void Record(TunnelForwardedPortFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _failures[failure.LocalPort] = failure;
    }

    /// <summary>Drops any failure recorded for the given local port.</summary>
    public void Clear(int localPort) => _failures.TryRemove(localPort, out _);

    /// <summary>
    /// Returns the recorded failure for the local port while it is still within
    /// the retention window; otherwise <c>null</c>.
    /// </summary>
    public TunnelForwardedPortFailure? GetRecent(int localPort)
    {
        if (_failures.TryGetValue(localPort, out TunnelForwardedPortFailure? failure)
            && _clock() - failure.OccurredAtUtc <= Retention)
        {
            return failure;
        }

        return null;
    }
}
