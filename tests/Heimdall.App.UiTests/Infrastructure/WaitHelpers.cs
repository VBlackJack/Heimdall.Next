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

using FlaUI.Core.AutomationElements;
using Xunit.Sdk;

namespace Heimdall.App.UiTests.Infrastructure;

/// <summary>
/// Polling helpers for async WPF/UIA state propagation.
/// </summary>
public static class WaitHelpers
{
    // Bumped from 2 s to 10 s to keep WPF UIA smoke tests reliable on the
    // GitHub Actions Windows runner. 2 s was enough on dev machines but the
    // CI runner regularly missed binding propagation deadlines, surfacing
    // multiple unrelated SmokeTests as flaky failures.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

    public static T WaitFor<T>(Func<T?> probe, string description, TimeSpan? timeout = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var limit = timeout ?? DefaultTimeout;
        while (sw.Elapsed < limit)
        {
            var result = probe();
            if (result is not null)
            {
                return result;
            }

            Thread.Sleep(DefaultPollInterval);
        }

        throw new XunitException($"Timed out waiting for {description} after {limit.TotalMilliseconds} ms.");
    }

    public static void WaitUntil(Func<bool> predicate, string description, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var limit = timeout ?? DefaultTimeout;
        while (sw.Elapsed < limit)
        {
            if (predicate())
            {
                return;
            }

            Thread.Sleep(DefaultPollInterval);
        }

        throw new XunitException($"Timed out waiting for {description} after {limit.TotalMilliseconds} ms.");
    }

    public static void WaitUntilVisible(AutomationElement element, string description, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        WaitUntil(() => !element.Properties.IsOffscreen.ValueOrDefault, description, timeout);
    }

    public static void WaitUntilTextEquals(Func<string> valueFactory, string expected, string description, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        WaitUntil(() => string.Equals(valueFactory(), expected, StringComparison.Ordinal), description, timeout);
    }

    public static void WaitUntilTextMatches(Func<string> valueFactory, Func<string, bool> matcher, string description, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        ArgumentNullException.ThrowIfNull(matcher);
        WaitUntil(() => matcher(valueFactory()), description, timeout);
    }
}
