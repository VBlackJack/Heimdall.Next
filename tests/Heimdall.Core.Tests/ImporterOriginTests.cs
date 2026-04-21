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

using Heimdall.Core.Configuration;
using Heimdall.Core.Models;

namespace Heimdall.Core.Tests;

public sealed class ImporterOriginTests
{
    [Fact]
    public void RdpFileImporter_SetsOriginToImportRdp()
    {
        var dto = RdpFileImporter.Parse("full address:s:server.example.com");

        Assert.NotNull(dto);
        Assert.Equal(ProfileOrigin.ImportRdp, dto!.Origin);
    }

    [Fact]
    public void MRemoteNgImporter_SetsOriginToImportMRemoteNg()
    {
        const string xml = """
            <mrng:Connections xmlns:mrng="http://mremoteng.org" FullFileEncryption="false">
              <Node Name="Connection" Type="Connection" Hostname="ssh.example.com" Protocol="SSH2" Port="22" />
            </mrng:Connections>
            """;

        var result = MRemoteNgImporter.Parse(xml);
        var dto = Assert.Single(result.Servers);

        Assert.Equal(ProfileOrigin.ImportMRemoteNg, dto.Origin);
    }

    [Fact]
    public void MobaXtermImporter_SetsOriginToImportMobaXterm()
    {
        const string ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            Server1= #109#0%ssh.example.com%22%deploy%
            """;

        var result = MobaXtermImporter.Parse(ini);
        var dto = Assert.Single(result.Servers);

        Assert.Equal(ProfileOrigin.ImportMobaXterm, dto.Origin);
    }

    [Fact]
    public void RdcManImporter_SetsOriginToImportRdcMan()
    {
        const string rdg = """
            <RDCMan programVersion="2.7" schemaVersion="3">
              <file>
                <name>Test</name>
                <server>
                  <name>rdp.example.com</name>
                </server>
              </file>
            </RDCMan>
            """;

        var result = RdcManImporter.Parse(rdg);
        var dto = Assert.Single(result.Servers);

        Assert.Equal(ProfileOrigin.ImportRdcMan, dto.Origin);
    }
}
