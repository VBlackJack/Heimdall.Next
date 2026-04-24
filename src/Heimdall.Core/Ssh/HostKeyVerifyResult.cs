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

/// <summary>
/// Verification result returned by host-key trust checks.
/// </summary>
/// <param name="Trusted">Whether the host key should be accepted.</param>
/// <param name="FirstUse">Whether this is the first time seeing this host.</param>
/// <param name="Fingerprint">SHA256 fingerprint of the presented host key.</param>
/// <param name="StoredFingerprint">Previously stored fingerprint, if any.</param>
public sealed record HostKeyVerifyResult(
    bool Trusted,
    bool FirstUse,
    string Fingerprint,
    string? StoredFingerprint);
