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

using Heimdall.App.Views.Tools;

namespace Heimdall.App.Tests;

/// <summary>
/// Guards shared network presets used across multiple tools.
/// </summary>
public class NetworkToolPresetsTests
{
    [Fact]
    public void DnsServers_FirstEntry_RemainsSystemDefault()
    {
        Assert.NotEmpty(NetworkToolPresets.DnsServers);
        Assert.Equal("System", NetworkToolPresets.DnsServers[0].Label);
        Assert.Null(NetworkToolPresets.DnsServers[0].Address);
    }

    [Fact]
    public void SnmpPresets_ContainDefaultCommunityAndOid()
    {
        Assert.Contains(NetworkToolPresets.SnmpDefaultCommunity, NetworkToolPresets.SnmpCommonCommunities);
        Assert.StartsWith("1.3.6.1.", NetworkToolPresets.SnmpDefaultOid);
    }

    [Fact]
    public void TlsQuickPorts_AreIncludedInExtendedProfile_AndRemainLabelled()
    {
        Assert.NotEmpty(NetworkToolPresets.TlsQuickScanPorts);
        Assert.NotEmpty(NetworkToolPresets.TlsExtendedScanPorts);

        foreach (var port in NetworkToolPresets.TlsQuickScanPorts)
        {
            Assert.Contains(port, NetworkToolPresets.TlsExtendedScanPorts);
            Assert.NotEqual("TLS", NetworkToolPresets.GetTlsServiceLabel(port));
        }
    }
}
