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

public enum KnownHostsDiagnosticLevel
{
    Info,
    Warning
}

public enum KnownHostsDiagnosticCode
{
    HashedEntryNotSupported,
    CertAuthorityNotSupported,
    RevokedEntryNotSupported,
    UnsupportedHostPattern,
    UnsupportedKeyType,
    MalformedLine,
    DuplicateFingerprintInSourceMerged,
    IntraFileFingerprintConflict
}

/// <summary>
/// Structured parser or preview diagnostic emitted while reading known_hosts.
/// </summary>
public sealed record KnownHostsImportDiagnostic(
    KnownHostsDiagnosticLevel Level,
    int SourceLineNumber,
    KnownHostsDiagnosticCode Code,
    string? Context = null);
