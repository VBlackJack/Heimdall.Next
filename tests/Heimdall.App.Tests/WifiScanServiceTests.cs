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

namespace Heimdall.App.Tests;

public sealed class WifiScanServiceTests
{
    [Fact]
    public void Constructor_NullRunner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WifiScanService(null!));
    }

    [Fact]
    public async Task ScanAsync_ParsesAndSortsBySignalDescending()
    {
        var service = new WifiScanService(() => Task.FromResult("""
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : 40%
        Channel : 1
SSID 2 : GuestWifi
    Authentication : Open
    Encryption : None
    BSSID 1 : 11:22:33:44:55:66
        Signal : 80%
        Channel : 6
"""));

        var results = await service.ScanAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("GuestWifi", results[0].Ssid);
        Assert.Equal(80, results[0].SignalValue);
    }

    [Fact]
    public async Task ScanAsync_NullRunnerOutput_ReturnsEmpty()
    {
        var service = new WifiScanService(() => Task.FromResult<string>(null!));

        var results = await service.ScanAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanAsync_RunnerException_Propagates()
    {
        var service = new WifiScanService(() => throw new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ScanAsync());

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task ScanAsync_EmptyRunnerOutput_ReturnsEmpty()
    {
        var service = new WifiScanService(() => Task.FromResult(string.Empty));

        var results = await service.ScanAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanAsync_WhitespaceRunnerOutput_ReturnsEmpty()
    {
        var service = new WifiScanService(() => Task.FromResult(" \r\n\t "));

        var results = await service.ScanAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanAsync_RunnerIsInvokedOnce()
    {
        var calls = 0;
        var service = new WifiScanService(() =>
        {
            calls++;
            return Task.FromResult("""
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : 40%
        Channel : 1
""");
        });

        await service.ScanAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ScanAsync_PreservesDuplicateBssidsReturnedByParser()
    {
        var service = new WifiScanService(() => Task.FromResult("""
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : 40%
        Channel : 1
SSID 2 : GuestWifi
    Authentication : Open
    Encryption : None
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : 30%
        Channel : 6
"""));

        var results = await service.ScanAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("aa:bb:cc:dd:ee:ff", results[0].Bssid);
        Assert.Equal("aa:bb:cc:dd:ee:ff", results[1].Bssid);
    }
}
