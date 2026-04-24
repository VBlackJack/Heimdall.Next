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

namespace Heimdall.Ssh;

/// <summary>
/// Raised when an SSH host key is explicitly rejected by the verifier.
/// </summary>
public sealed class HostKeyRejectedException : Exception
{
    public HostKeyRejectedException(
        string host,
        int port,
        string algorithm,
        string presentedFingerprint,
        string? storedFingerprint)
        : base(CreateMessage(host, port, presentedFingerprint, storedFingerprint))
    {
        Host = host;
        Port = port;
        Algorithm = algorithm;
        PresentedFingerprint = presentedFingerprint;
        StoredFingerprint = storedFingerprint;
    }

    public string Host { get; }

    public int Port { get; }

    public string Algorithm { get; }

    public string PresentedFingerprint { get; }

    public string? StoredFingerprint { get; }

    public bool IsMismatch => !string.IsNullOrWhiteSpace(StoredFingerprint);

    private static string CreateMessage(
        string host,
        int port,
        string presentedFingerprint,
        string? storedFingerprint)
    {
        return storedFingerprint is null
            ? $"SSH host key was rejected for {host}:{port}. Presented fingerprint: {presentedFingerprint}."
            : $"SSH host key mismatch for {host}:{port}. Stored fingerprint {storedFingerprint} differs from presented fingerprint {presentedFingerprint}.";
    }
}
