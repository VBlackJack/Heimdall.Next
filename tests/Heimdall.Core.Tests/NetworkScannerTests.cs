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

using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

public class NetworkScannerTests
{
    [Fact]
    public void ParseCidr_ValidCidr_ReturnsNetworkAndPrefix()
    {
        (string Network, int PrefixLength) result = NetworkScanner.ParseCidr("192.168.1.0/24");

        Assert.Equal("192.168.1.0", result.Network);
        Assert.Equal(24, result.PrefixLength);
    }

    [Fact]
    public void ParseCidr_SingleIp_ReturnsSlash32()
    {
        (string Network, int PrefixLength) result = NetworkScanner.ParseCidr("192.168.1.1");

        Assert.Equal("192.168.1.1", result.Network);
        Assert.Equal(32, result.PrefixLength);
    }

    [Theory]
    [InlineData("192.168.1.0/8")]
    [InlineData("10.0.0.0/33")]
    [InlineData("not-an-ip")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fe80::/24")]
    [InlineData("not-an-ip/24")]
    public void ParseCidr_InvalidInput_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => NetworkScanner.ParseCidr(input));
    }

    [Fact]
    public void GenerateAddresses_Slash24_ReturnsUsableHosts()
    {
        List<string> addresses = NetworkScanner.GenerateAddresses("192.168.1.0", 24);

        Assert.Equal(254, addresses.Count);
        Assert.Equal("192.168.1.1", addresses[0]);
        Assert.Equal("192.168.1.254", addresses[^1]);
    }

    [Fact]
    public void GenerateAddresses_NonNetworkAlignedSlash24_NormalizesToNetworkBase()
    {
        List<string> addresses = NetworkScanner.GenerateAddresses("192.168.1.5", 24);

        Assert.Equal(254, addresses.Count);
        Assert.Equal("192.168.1.1", addresses[0]);
        Assert.Equal("192.168.1.254", addresses[^1]);
    }

    [Fact]
    public void GenerateAddresses_Slash32_ReturnsSingleHost()
    {
        List<string> addresses = NetworkScanner.GenerateAddresses("10.0.0.7", 32);

        Assert.Equal(["10.0.0.7"], addresses);
    }

    [Fact]
    public void GenerateAddresses_NonNetworkAlignedSlash31_NormalizesToEvenBase()
    {
        List<string> addresses = NetworkScanner.GenerateAddresses("192.168.1.5", 31);

        Assert.Equal(["192.168.1.4", "192.168.1.5"], addresses);
    }

    [Fact]
    public void GenerateAddresses_HighSlash24_DoesNotWrapAround()
    {
        List<string> addresses = NetworkScanner.GenerateAddresses("255.255.255.250", 24);

        Assert.Equal(254, addresses.Count);
        Assert.All(addresses, address => Assert.StartsWith("255.255.255.", address));
        Assert.DoesNotContain(addresses, address => address.StartsWith("0.0.0.", StringComparison.Ordinal));
    }
}
