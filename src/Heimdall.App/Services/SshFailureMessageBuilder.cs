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
using Heimdall.Core.Localization;

namespace Heimdall.App.Services;

internal static class SshFailureMessageBuilder
{
    public static string HostKeyMismatch(
        LocalizationManager localizer,
        string storedFingerprint,
        string presentedFingerprint)
    {
        string message = localizer[SshLocalizationKeys.ErrorHostKeyMismatch];
        if (string.Equals(message, SshLocalizationKeys.ErrorHostKeyMismatch, StringComparison.Ordinal))
        {
            message = "SSH host key mismatch \u2014 possible MITM. Stored fingerprint differs from server-presented fingerprint.";
        }

        string detail = localizer.Format(
            SshLocalizationKeys.ErrorHostKeyMismatchDetail,
            storedFingerprint,
            presentedFingerprint);
        if (string.Equals(detail, SshLocalizationKeys.ErrorHostKeyMismatchDetail, StringComparison.Ordinal))
        {
            detail = $"Stored: {storedFingerprint}. Presented: {presentedFingerprint}.";
        }

        return $"{message} {detail}";
    }

    public static string HostKeyUnavailable(LocalizationManager localizer)
    {
        string message = localizer[SshLocalizationKeys.ErrorSshHostKeyUnavailable];
        return string.Equals(message, SshLocalizationKeys.ErrorSshHostKeyUnavailable, StringComparison.Ordinal)
            ? "Heimdall could not verify the gateway host key. Refusing to fall back to plink's local cache."
            : message;
    }

    public static string Cancelled(LocalizationManager localizer)
    {
        string message = localizer[SshLocalizationKeys.ErrorSshCancelled];
        return string.Equals(message, SshLocalizationKeys.ErrorSshCancelled, StringComparison.Ordinal)
            ? "Connection was cancelled."
            : message;
    }
}
