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

public class OsFingerprinterTests
{
    [Theory]
    [InlineData(128, "Windows")]
    [InlineData(127, "Windows")]
    [InlineData(120, "Windows")]
    [InlineData(64, "Linux/macOS")]
    [InlineData(63, "Linux/macOS")]
    [InlineData(55, "Linux/macOS")]
    [InlineData(255, "Network Equipment")]
    [InlineData(254, "Network Equipment")]
    [InlineData(32, "Embedded/Legacy")]
    public void GuessFromTtl_KnownRanges_ReturnsExpectedOs(int ttl, string expectedOs)
    {
        var result = OsFingerprinter.GuessFromTtl(ttl);
        Assert.NotNull(result);
        Assert.Equal(expectedOs, result.OsGuess);
        Assert.Equal("TTL", result.Source);
        Assert.True(result.Confidence > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GuessFromTtl_InvalidTtl_ReturnsNull(int ttl)
    {
        Assert.Null(OsFingerprinter.GuessFromTtl(ttl));
    }

    [Fact]
    public void GuessFromBanners_SshUbuntu_ReturnsUbuntuLinux()
    {
        var services = new List<ServiceResult>
        {
            new(22, true, "SSH", "SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.6", null, 5)
        };

        var result = OsFingerprinter.GuessFromBanners(services);
        Assert.NotNull(result);
        Assert.Contains("Ubuntu", result.OsGuess);
        Assert.Equal("Banner", result.Source);
    }

    [Fact]
    public void GuessFromBanners_IisServer_ReturnsWindows()
    {
        var services = new List<ServiceResult>
        {
            new(80, true, "HTTP", "HTTP/1.1 200 OK\r\nServer: Microsoft-IIS/10.0", null, 10)
        };

        var result = OsFingerprinter.GuessFromBanners(services);
        Assert.NotNull(result);
        Assert.Contains("Windows", result.OsGuess);
    }

    [Fact]
    public void GuessFromBanners_NoBanners_ReturnsNull()
    {
        var services = new List<ServiceResult>
        {
            new(80, true, "HTTP", null, null, 10)
        };

        Assert.Null(OsFingerprinter.GuessFromBanners(services));
    }

    [Fact]
    public void Merge_BothNull_ReturnsNull()
    {
        Assert.Null(OsFingerprinter.Merge(null, null));
    }

    [Fact]
    public void Merge_OnlyTtl_ReturnsTtl()
    {
        var ttl = new OsFingerprint("Windows", "TTL", 70);
        var result = OsFingerprinter.Merge(ttl, null);
        Assert.Same(ttl, result);
    }

    [Fact]
    public void Merge_OnlyBanner_ReturnsBanner()
    {
        var banner = new OsFingerprint("Ubuntu Linux", "Banner", 85);
        var result = OsFingerprinter.Merge(null, banner);
        Assert.Same(banner, result);
    }

    [Fact]
    public void Merge_SameFamily_BoostsConfidence()
    {
        var ttl = new OsFingerprint("Windows", "TTL", 70);
        var banner = new OsFingerprint("Windows Server", "Banner", 80);

        var result = OsFingerprinter.Merge(ttl, banner);
        Assert.NotNull(result);
        Assert.Equal("Windows Server", result.OsGuess);
        Assert.Equal(90, result.Confidence); // 80 + 10
        Assert.Equal("TTL+Banner", result.Source);
    }

    [Fact]
    public void Merge_DifferentFamily_HigherConfidenceWins()
    {
        var ttl = new OsFingerprint("Windows", "TTL", 70);
        var banner = new OsFingerprint("Ubuntu Linux", "Banner", 85);

        var result = OsFingerprinter.Merge(ttl, banner);
        Assert.NotNull(result);
        Assert.Equal("Ubuntu Linux", result.OsGuess);
        Assert.Equal(85, result.Confidence);
    }

    [Fact]
    public void Merge_ConfidenceCappedAt95()
    {
        var ttl = new OsFingerprint("Linux/macOS", "TTL", 60);
        var banner = new OsFingerprint("Ubuntu Linux", "Banner", 90);

        var result = OsFingerprinter.Merge(ttl, banner);
        Assert.NotNull(result);
        Assert.Equal(95, result.Confidence); // capped
    }
}
