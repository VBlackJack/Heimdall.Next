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

namespace Heimdall.Core.Ssh;

public interface IHostKeyTrustService
{
    HostKeyEntry? GetEntry(string host, int port);

    HostKeyEntry? GetEffectiveEntry(string host, int port);

    IReadOnlyList<(string HostPort, HostKeyEntry Entry)> GetAllEntries();

    HostKeyVerifyResult Verify(string host, int port, string presentedFingerprint, string algorithm);

    void Trust(string host, int port, string fingerprint, string algorithm, HostKeySource source, string? publicKeyBase64 = null);

    void TrustForSession(string host, int port, string fingerprint, string algorithm, string? publicKeyBase64 = null);

    void Import(string host, int port, string fingerprint, string algorithm, DateTimeOffset importedAt, string? publicKeyBase64 = null);

    bool Remove(string host, int port);

    event Action<string, HostKeyEntry>? EntryTrusted;

    event Action<string>? EntryRemoved;

    event Action<string, HostKeyEntry, HostKeyEntry>? EntryReplaced;
}
