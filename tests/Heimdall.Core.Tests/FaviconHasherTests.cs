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

using Heimdall.Core.Discovery;

namespace Heimdall.Core.Tests;

public class FaviconHasherTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("foo", -156908512)]
    [InlineData("bar", 1158584717)]
    [InlineData("test", -1167338989)]
    [InlineData("hello", 613153351)]
    [InlineData("hello world", 1586663183)]
    [InlineData("The quick brown fox jumps over the lazy dog", 776992547)]
    public void MurmurHash3_ReferenceVectors_ReturnExpectedSignedHash(
        string input,
        int expected)
    {
        int hash = FaviconHasher.MurmurHash3(input);

        Assert.Equal(expected, hash);
    }

    [Fact]
    public void KnownHashes_ContainsCommonManagementInterfaces()
    {
        Assert.Equal("FortiGate (Fortinet)", FaviconHasher.KnownHashes[-305179312]);
        Assert.Equal("VMware ESXi", FaviconHasher.KnownHashes[-1615998030]);
        Assert.Equal("Grafana", FaviconHasher.KnownHashes[1169437688]);
        Assert.Equal("Jenkins", FaviconHasher.KnownHashes[-1331059960]);
    }
}
