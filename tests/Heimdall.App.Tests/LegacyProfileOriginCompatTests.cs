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

using System.IO;
using System.Text;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed class LegacyProfileOriginCompatTests
{
    [Fact]
    public async Task LoadLegacyServersJson_WithoutOriginField_AllProfilesDefaultToManual()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "heimdall-b63-legacy", Guid.NewGuid().ToString("N"));

        try
        {
            var configManager = new ConfigManager(rootPath);
            await configManager.InitializeAsync();

            var serversPath = Path.Combine(rootPath, "config", "servers.json");
            await File.WriteAllTextAsync(
                serversPath,
                """
                [
                  {
                    "Id": "legacy-1",
                    "DisplayName": "Legacy SSH",
                    "RemoteServer": "ssh.example.com",
                    "ConnectionType": "SSH"
                  },
                  {
                    "Id": "legacy-2",
                    "DisplayName": "Legacy RDP",
                    "RemoteServer": "rdp.example.com",
                    "ConnectionType": "RDP"
                  }
                ]
                """,
                new UTF8Encoding(false));

            var servers = await configManager.LoadServersAsync();

            Assert.Equal(2, servers.Count);
            Assert.All(servers, server => Assert.Equal(ProfileOrigin.Manual, server.Origin));
        }
        finally
        {
            try
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
