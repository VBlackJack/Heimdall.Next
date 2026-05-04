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

using Heimdall.App.Services;

namespace Heimdall.App.Tests.Services;

public sealed class RecentConnectionTrackerTests
{
    [Fact]
    public void GetLastProtocol_BeforeAnyRecord_ReturnsNull()
    {
        var tracker = new RecentConnectionTracker();

        Assert.Null(tracker.GetLastProtocol("host.example.com"));
    }

    [Fact]
    public void Record_ThenGetLastProtocol_ReturnsRecordedProtocol()
    {
        var tracker = new RecentConnectionTracker();

        tracker.Record("host.example.com", "RDP");

        Assert.Equal("RDP", tracker.GetLastProtocol("host.example.com"));
    }

    [Fact]
    public void GetLastProtocol_IsCaseInsensitiveOnHost()
    {
        var tracker = new RecentConnectionTracker();

        tracker.Record("Host.Example.COM", "SSH");

        Assert.Equal("SSH", tracker.GetLastProtocol("host.example.com"));
        Assert.Equal("SSH", tracker.GetLastProtocol("HOST.EXAMPLE.COM"));
    }

    [Fact]
    public void Record_NormalizesProtocolToUpperInvariant()
    {
        var tracker = new RecentConnectionTracker();

        tracker.Record("host.example.com", "rdp");

        Assert.Equal("RDP", tracker.GetLastProtocol("host.example.com"));
    }

    [Theory]
    [InlineData(null, "RDP")]
    [InlineData("", "RDP")]
    [InlineData("   ", "RDP")]
    [InlineData("host", null)]
    [InlineData("host", "")]
    [InlineData("host", "   ")]
    public void Record_IgnoresNullOrWhitespaceInputs(string? host, string? protocol)
    {
        var tracker = new RecentConnectionTracker();

        tracker.Record(host, protocol);

        Assert.Empty(tracker.GetRecents(10));
    }

    [Fact]
    public void Record_SamePairTwice_DedupesAndKeepsMostRecent()
    {
        var tracker = new RecentConnectionTracker();

        tracker.Record("host.a", "RDP");
        tracker.Record("host.b", "SSH");
        tracker.Record("host.a", "RDP"); // duplicate, should bump to top

        var recents = tracker.GetRecents(10);

        Assert.Equal(2, recents.Count);
        Assert.Equal("host.a", recents[0].Host);
        Assert.Equal("RDP", recents[0].Protocol);
        Assert.Equal("host.b", recents[1].Host);
    }

    [Fact]
    public void Record_SameHostDifferentProtocols_KeepsBothEntries()
    {
        var tracker = new RecentConnectionTracker();

        tracker.Record("host.example.com", "SSH");
        tracker.Record("host.example.com", "RDP");

        var recents = tracker.GetRecents(10);

        Assert.Equal(2, recents.Count);
        Assert.Equal("RDP", recents[0].Protocol);
        Assert.Equal("SSH", recents[1].Protocol);
        Assert.Equal("RDP", tracker.GetLastProtocol("host.example.com"));
    }

    [Fact]
    public void GetRecents_ReturnsMostRecentFirst()
    {
        var tracker = new RecentConnectionTracker();

        tracker.Record("first", "SSH");
        tracker.Record("second", "RDP");
        tracker.Record("third", "VNC");

        var recents = tracker.GetRecents(10);

        Assert.Equal("third", recents[0].Host);
        Assert.Equal("second", recents[1].Host);
        Assert.Equal("first", recents[2].Host);
    }

    [Fact]
    public void GetRecents_RespectsMaxParameter()
    {
        var tracker = new RecentConnectionTracker();

        tracker.Record("a", "SSH");
        tracker.Record("b", "SSH");
        tracker.Record("c", "SSH");

        Assert.Equal(2, tracker.GetRecents(2).Count);
        Assert.Empty(tracker.GetRecents(0));
        Assert.Empty(tracker.GetRecents(-1));
        Assert.Equal(3, tracker.GetRecents(100).Count);
    }

    [Fact]
    public void Record_BeyondCapacity_DropsOldestEntries()
    {
        var tracker = new RecentConnectionTracker();

        // The implementation caps the in-memory log at 50 entries.
        for (var i = 0; i < 60; i++)
        {
            tracker.Record($"host{i}", "SSH");
        }

        var recents = tracker.GetRecents(100);

        Assert.Equal(50, recents.Count);
        // Most recent must be present, oldest dropped.
        Assert.Equal("host59", recents[0].Host);
        Assert.Null(tracker.GetLastProtocol("host0"));
        Assert.Null(tracker.GetLastProtocol("host9"));
        Assert.Equal("SSH", tracker.GetLastProtocol("host10"));
    }
}
