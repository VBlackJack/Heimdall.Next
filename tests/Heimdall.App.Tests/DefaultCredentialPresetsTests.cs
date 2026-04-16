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
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

/// <summary>
/// Guards the preset catalog used by the default credential scanner.
/// </summary>
public class DefaultCredentialPresetsTests
{
    [Fact]
    public void ServicePorts_KeepCoreNetworkMappings()
    {
        Assert.Equal("SSH", DefaultCredentialPresets.ServicePorts[DefaultPorts.Ssh]);
        Assert.Equal("Telnet", DefaultCredentialPresets.ServicePorts[DefaultPorts.Telnet]);
        Assert.Equal("FTP", DefaultCredentialPresets.ServicePorts[DefaultPorts.Ftp]);
        Assert.Equal("SNMP", DefaultCredentialPresets.ServicePorts[161]);
    }

    [Fact]
    public void CredentialsByService_KeepExpectedSnmpAndHttpEntries()
    {
        Assert.Contains(("public", string.Empty), DefaultCredentialPresets.CredentialsByService["SNMP"]);
        Assert.Contains(("admin", "admin"), DefaultCredentialPresets.CredentialsByService["HTTP"]);
        Assert.Contains(("root", "root"), DefaultCredentialPresets.CredentialsByService["SSH"]);
    }
}
