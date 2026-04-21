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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class PortFilterTests
{
    private static readonly PortEntry Entry = new("TCP", "127.0.0.1", 443, "203.0.113.10", 51515, "ESTABLISHED", 1234, "nginx");

    [Fact]
    public void Matches_NullEntry_ReturnsFalse()
    {
        Assert.False(PortFilter.Matches(null, "tcp"));
    }

    [Fact]
    public void Matches_EmptyFilter_ReturnsTrue()
    {
        Assert.True(PortFilter.Matches(Entry, string.Empty));
    }

    [Theory]
    [InlineData("nginx")]
    [InlineData("127.0.0.1")]
    [InlineData("443")]
    [InlineData("203.0.113.10")]
    [InlineData("TCP")]
    [InlineData("ESTABLISHED")]
    [InlineData("1234")]
    public void Matches_MatchingField_ReturnsTrue(string filter)
    {
        Assert.True(PortFilter.Matches(Entry, filter));
    }

    [Fact]
    public void Matches_NoMatch_ReturnsFalse()
    {
        Assert.False(PortFilter.Matches(Entry, "powershell"));
    }
}
