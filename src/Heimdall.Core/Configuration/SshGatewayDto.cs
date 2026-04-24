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

using System.Text.Json.Serialization;

namespace Heimdall.Core.Configuration;

/// <summary>
/// Flat DTO for SSH gateway JSON deserialization.
/// The ViewModel layer converts these to ObservableObject models.
/// </summary>
public sealed class SshGatewayDto
{
    private string? _sshKeyPassphraseEncrypted;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string User { get; set; } = string.Empty;
    public string? KeyPath { get; set; }
    public string? SshPasswordEncrypted { get; set; }
    public string? SshKeyPassphraseEncrypted
    {
        get => _sshKeyPassphraseEncrypted;
        set
        {
            _sshKeyPassphraseEncrypted = value;
            HasSshKeyPassphraseEncryptedField = true;
        }
    }

    [JsonIgnore]
    public bool HasSshKeyPassphraseEncryptedField { get; private set; }

    [JsonIgnore]
    public bool UsesLegacySshCredentialMapping =>
        !HasSshKeyPassphraseEncryptedField
        && !string.IsNullOrWhiteSpace(KeyPath)
        && !string.IsNullOrWhiteSpace(SshPasswordEncrypted);

    public bool IsDefault { get; set; }
    public string? ParentGatewayId { get; set; }
    public string? HostKeyFingerprint { get; set; }
}
