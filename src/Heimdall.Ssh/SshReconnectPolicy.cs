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
/// Decides whether a disconnected SSH session should enter bounded auto-reconnect.
/// </summary>
public static class SshReconnectPolicy
{
    /// <summary>
    /// Returns true only for disconnect causes that are plausibly transient.
    /// </summary>
    public static bool AllowsAutoReconnect(SshSessionDisconnectInfo disconnect)
    {
        ArgumentNullException.ThrowIfNull(disconnect);

        if (disconnect.IsClean)
        {
            return false;
        }

        // Legacy process-backed sessions only expose an exit code, not an SSH
        // failure code. Preserve their previous retry behavior unless the exit
        // has been explicitly marked clean by the caller.
        return disconnect.Failure is null
            || AllowsAutoReconnect(disconnect.Failure.Code);
    }

    /// <summary>
    /// Returns true only for classified SSH failures that can reasonably clear
    /// after a bounded retry delay.
    /// </summary>
    public static bool AllowsAutoReconnect(SshFailureCode code)
    {
        return code switch
        {
            SshFailureCode.NetworkRefused
                or SshFailureCode.NetworkTimedOut
                or SshFailureCode.NetworkReset
                or SshFailureCode.NetworkUnreachable
                or SshFailureCode.SessionDisconnected
                or SshFailureCode.TunnelBroken
                or SshFailureCode.AuthTimeout
                => true,

            _ => false,
        };
    }
}
