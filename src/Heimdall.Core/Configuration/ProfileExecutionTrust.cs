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

namespace Heimdall.Core.Configuration;

/// <summary>
/// Evaluates whether a profile carries local-execution payload that must be explicitly trusted.
/// </summary>
public static class ProfileExecutionTrust
{
    /// <summary>
    /// Returns true when a LOCAL profile carries a custom local shell executable or arguments.
    /// </summary>
    public static bool CarriesLocalExecutionPayload(ServerProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return string.Equals(profile.ConnectionType, "LOCAL", StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(profile.LocalShellExecutable)
                || !string.IsNullOrWhiteSpace(profile.LocalShellArguments));
    }

    /// <summary>
    /// Returns true when a profile carries local-execution payload that has not been confirmed.
    /// </summary>
    public static bool RequiresExecutionConfirmation(ServerProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return CarriesLocalExecutionPayload(profile) && !profile.ExecutionConfirmed;
    }
}
