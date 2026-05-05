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

using Heimdall.App.Localization;
using Heimdall.Core.Ssh;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

internal sealed record PlinkHostKeyDecision(
    bool ShouldProceed,
    string? Fingerprint,
    SshFailureCode? FailureCode,
    string? FailureMessageKey,
    string? StoredFingerprint,
    string? PresentedFingerprint);

internal static class PlinkHostKeyDecider
{
    /// <summary>
    /// Resolves the host-key fingerprint Heimdall will pass to plink via
    /// -hostkey. The method proceeds only when Heimdall has a trusted
    /// fingerprint; otherwise it returns a structured fail-closed decision.
    /// </summary>
    internal static async Task<PlinkHostKeyDecision> DecideAsync(
        string host,
        int port,
        string? username,
        string plinkPath,
        int probeTimeoutMs,
        string? storedFingerprint,
        IPlinkHostKeyProbe probe,
        IHostKeyVerifier verifier,
        IHostKeyTrustService trustService,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(plinkPath);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(trustService);
        ct.ThrowIfCancellationRequested();

        var normalizedStored = string.IsNullOrWhiteSpace(storedFingerprint)
            ? null
            : storedFingerprint;
        var presentation = await probe.ProbeAsync(
                plinkPath,
                host,
                port,
                username,
                probeTimeoutMs,
                ct)
            .ConfigureAwait(false);

        if (normalizedStored is not null)
        {
            return await DecideWithStoredFingerprintAsync(
                    host,
                    port,
                    normalizedStored,
                    presentation,
                    verifier,
                    trustService,
                    ct)
                .ConfigureAwait(false);
        }

        return await DecideFirstUseAsync(
                host,
                port,
                presentation,
                verifier,
                trustService,
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<PlinkHostKeyDecision> DecideWithStoredFingerprintAsync(
        string host,
        int port,
        string storedFingerprint,
        PlinkHostKeyPresentation? presentation,
        IHostKeyVerifier verifier,
        IHostKeyTrustService trustService,
        CancellationToken ct)
    {
        if (presentation is null)
        {
            Core.Logging.FileLogger.Warn(
                $"Plink host-key probe returned no presentation for {host}:{port}; proceeding with stored Heimdall fingerprint.");
            return Proceed(storedFingerprint);
        }

        if (string.Equals(presentation.Fingerprint, storedFingerprint, StringComparison.Ordinal))
        {
            return Proceed(storedFingerprint);
        }

        var decision = await verifier.VerifyAsync(
                host,
                port,
                presentation.Algorithm,
                presentation.Fingerprint,
                storedFingerprint,
                ct)
            .ConfigureAwait(false);

        return ApplyVerifierDecision(
            host,
            port,
            presentation,
            storedFingerprint,
            decision,
            trustService);
    }

    private static async Task<PlinkHostKeyDecision> DecideFirstUseAsync(
        string host,
        int port,
        PlinkHostKeyPresentation? presentation,
        IHostKeyVerifier verifier,
        IHostKeyTrustService trustService,
        CancellationToken ct)
    {
        if (presentation is null)
        {
            return Reject(
                SshFailureCode.HostKeyUnavailable,
                SshLocalizationKeys.ErrorSshHostKeyUnavailable,
                null,
                null);
        }

        var decision = await verifier.VerifyAsync(
                host,
                port,
                presentation.Algorithm,
                presentation.Fingerprint,
                storedFingerprint: null,
                ct)
            .ConfigureAwait(false);

        return ApplyVerifierDecision(
            host,
            port,
            presentation,
            storedFingerprint: null,
            decision,
            trustService);
    }

    private static PlinkHostKeyDecision ApplyVerifierDecision(
        string host,
        int port,
        PlinkHostKeyPresentation presentation,
        string? storedFingerprint,
        HostKeyDecision decision,
        IHostKeyTrustService trustService)
    {
        if (decision == HostKeyDecision.Accept)
        {
            trustService.Trust(
                host,
                port,
                presentation.Fingerprint,
                presentation.Algorithm,
                HostKeySource.UserConfirmed);
            return Proceed(presentation.Fingerprint, storedFingerprint, presentation.Fingerprint);
        }

        if (decision == HostKeyDecision.TrustOnce)
        {
            trustService.TrustForSession(
                host,
                port,
                presentation.Fingerprint,
                presentation.Algorithm);
            return Proceed(presentation.Fingerprint, storedFingerprint, presentation.Fingerprint);
        }

        return storedFingerprint is null
            ? Reject(
                SshFailureCode.Cancelled,
                SshLocalizationKeys.ErrorSshCancelled,
                null,
                presentation.Fingerprint)
            : Reject(
                SshFailureCode.HostKeyMismatch,
                SshLocalizationKeys.ErrorHostKeyMismatch,
                storedFingerprint,
                presentation.Fingerprint);
    }

    private static PlinkHostKeyDecision Proceed(
        string fingerprint,
        string? storedFingerprint = null,
        string? presentedFingerprint = null)
    {
        return new PlinkHostKeyDecision(
            ShouldProceed: true,
            fingerprint,
            FailureCode: null,
            FailureMessageKey: null,
            storedFingerprint,
            presentedFingerprint);
    }

    private static PlinkHostKeyDecision Reject(
        SshFailureCode failureCode,
        string failureMessageKey,
        string? storedFingerprint,
        string? presentedFingerprint)
    {
        return new PlinkHostKeyDecision(
            ShouldProceed: false,
            Fingerprint: null,
            failureCode,
            failureMessageKey,
            storedFingerprint,
            presentedFingerprint);
    }
}
