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
using Heimdall.Core.Configuration;
using System.IO;

namespace Heimdall.App.Tests;

public sealed class ExternalToolProviderServiceTests
{
    [Fact]
    public void NanaRunProvider_DetectsSupportedExecutablesInCustomDirectory()
    {
        using var tempDir = new TempDirectory();
        var minSudoPath = Path.Combine(tempDir.Path, "MinSudo.exe");
        var synthRdpDir = Directory.CreateDirectory(Path.Combine(tempDir.Path, "SynthRdp"));
        var synthRdpPath = Path.Combine(synthRdpDir.FullName, "SynthRdp.exe");

        File.WriteAllBytes(minSudoPath, []);
        File.WriteAllBytes(synthRdpPath, []);

        var provider = new NanaRunToolProvider();

        var tools = provider.Scan([tempDir.Path]);

        Assert.Contains(tools, t => t.Id == "MINSUDO" && t.ExecutablePath == minSudoPath);
        Assert.Contains(tools, t => t.Id == "SYNTHRDP" && t.ExecutablePath == synthRdpPath);
    }

    [Fact]
    public void ScanAll_UsesProviderSpecificCustomPaths()
    {
        using var tempDir = new TempDirectory();
        var minSudoPath = Path.Combine(tempDir.Path, "MinSudo.exe");
        File.WriteAllBytes(minSudoPath, []);

        var service = new ExternalToolProviderService();

        service.ScanAll(new AppSettings
        {
            NirSoftPath = tempDir.Path,
            NanaRunPath = null,
        });

        Assert.DoesNotContain(
            service.DetectedTools,
            t => t.ProviderName == "NanaRun" && t.ExecutablePath == minSudoPath);

        service.ScanAll(new AppSettings
        {
            NanaRunPath = tempDir.Path,
        });

        Assert.Contains(
            service.DetectedTools,
            t => t.ProviderName == "NanaRun" && t.ExecutablePath == minSudoPath);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"heimdall-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test temp data.
            }
        }
    }
}
