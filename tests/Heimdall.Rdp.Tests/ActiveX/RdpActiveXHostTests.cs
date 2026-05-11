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

using Heimdall.Rdp.ActiveX;

namespace Heimdall.Rdp.Tests.ActiveX;

public sealed class RdpActiveXHostTests
{
    [Fact]
    public void StripScrollbarBits_RemovesOnlyNativeScrollbarStyles()
    {
        const long style = unchecked((long)0x8000_0000_0030_1234UL);
        const long expected = unchecked((long)0x8000_0000_0000_1234UL);

        var actual = RdpActiveXHost.StripScrollbarBits(style);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StripScrollbarBits_LeavesStyleWithoutScrollbarBitsUnchanged()
    {
        const long style = 0x0000_0000_0000_1234L;

        var actual = RdpActiveXHost.StripScrollbarBits(style);

        Assert.Equal(style, actual);
    }

    [Fact]
    public void PostConnectStripTimer_BeginStartsTicksAndStopsAfterMaxDuration()
    {
        var clock = new FakeClock();
        var timers = new List<FakeStripTimer>();
        var stripCount = 0;
        var logs = new List<string>();
        var timer = new RdpPostConnectStripTimer(
            () =>
            {
                var fake = new FakeStripTimer();
                timers.Add(fake);
                return fake;
            },
            clock,
            () => stripCount++,
            logs.Add,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(750));

        timer.Begin("test");

        Assert.True(timer.IsRunning);
        Assert.Single(timers);
        Assert.True(timers[0].Started);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        timers[0].RaiseTick();
        clock.Advance(TimeSpan.FromMilliseconds(250));
        timers[0].RaiseTick();
        clock.Advance(TimeSpan.FromMilliseconds(250));
        timers[0].RaiseTick();

        Assert.Equal(3, stripCount);
        Assert.False(timer.IsRunning);
        Assert.True(timers[0].Stopped);
        Assert.True(timers[0].Disposed);
        Assert.Contains(logs, log => log.Contains("started", StringComparison.Ordinal));
        Assert.Contains(logs, log => log.Contains("max-duration", StringComparison.Ordinal));
    }

    [Fact]
    public void PostConnectStripTimer_DisposeStopsTimerCleanly()
    {
        var clock = new FakeClock();
        var fake = new FakeStripTimer();
        var timer = new RdpPostConnectStripTimer(
            () => fake,
            clock,
            () => { },
            _ => { },
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(750));

        timer.Begin("test");
        timer.Dispose();

        Assert.False(timer.IsRunning);
        Assert.True(fake.Stopped);
        Assert.True(fake.Disposed);
    }

    [Fact]
    public void PostConnectStripTimer_BeginTwiceDisposesPreviousTimer()
    {
        var clock = new FakeClock();
        var timers = new List<FakeStripTimer>();
        var timer = new RdpPostConnectStripTimer(
            () =>
            {
                var fake = new FakeStripTimer();
                timers.Add(fake);
                return fake;
            },
            clock,
            () => { },
            _ => { },
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(750));

        timer.Begin("first");
        timer.Begin("second");

        Assert.True(timer.IsRunning);
        Assert.Equal(2, timers.Count);
        Assert.True(timers[0].Stopped);
        Assert.True(timers[0].Disposed);
        Assert.True(timers[1].Started);
        Assert.False(timers[1].Disposed);
    }

    private sealed class FakeClock : IRdpPostConnectStripTimerClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-05-11T00:00:00Z");

        public void Advance(TimeSpan elapsed)
        {
            UtcNow += elapsed;
        }
    }

    private sealed class FakeStripTimer : IRdpStripTimer
    {
        public event EventHandler? Tick;

        public TimeSpan Interval { get; set; }

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
            Started = true;
            Stopped = false;
        }

        public void Stop()
        {
            Stopped = true;
        }

        public void Dispose()
        {
            Disposed = true;
        }

        public void RaiseTick()
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}
