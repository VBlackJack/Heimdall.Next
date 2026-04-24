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

using Heimdall.App.Services.Import;
using Heimdall.Core.Ssh;
using Heimdall.Ssh;
using KnownHostsImporter = Heimdall.App.Services.Import.KnownHostsImporter;

namespace Heimdall.App.Tests;

public sealed class KnownHostsImporterRaceTests
{
    [Fact]
    public async Task ImportSelectedAsync_LateConflict_CountedInSkippedConflict_AndNoTrustCall()
    {
        var config = new InMemoryConfigManager();
        var store = new HostKeyStore();
        var importer = new KnownHostsImporter(config, store);

        var candidate = new KnownHostsImportCandidate
        {
            Host = "race-host",
            Port = 22,
            Fingerprint = HostKeyFormats.ComputeSha256Fingerprint([0x01, 0x02, 0x03]),
            SourceLineNumber = 1
        };

        config.BeforeMergeTrustedHostKeys = () =>
            config.SetTrustedHostKey(
                HostKeyFormats.MakeKey(candidate.Host, candidate.Port),
                HostKeyFormats.ComputeSha256Fingerprint([0x09, 0x09, 0x09]));

        var outcome = await importer.ImportSelectedAsync([candidate]);

        Assert.Equal(0, outcome.Imported);
        Assert.Equal(0, outcome.SkippedExisting);
        Assert.Equal(1, outcome.SkippedConflict);
        Assert.Null(store.GetFingerprint(candidate.Host, candidate.Port));
    }
}
