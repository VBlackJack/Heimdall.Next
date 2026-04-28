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

namespace Heimdall.App.Views.EmbeddedRdp;

/// <summary>
/// High-level embedded RDP session status used to drive localized visible labels
/// while preserving invariant codes for existing view-model consumers.
/// </summary>
internal enum RdpSessionStatus
{
    Preparing,
    Connecting,
    Connected,
    Disconnecting,
    Disconnected,
    Reconnecting,
    Error
}

internal static class RdpSessionStatusKeys
{
    /// <summary>
    /// Returns the i18n key for a given status.
    /// </summary>
    public static string GetKey(RdpSessionStatus status) => status switch
    {
        RdpSessionStatus.Preparing => "RdpStatusPreparing",
        RdpSessionStatus.Connecting => "RdpStatusConnecting",
        RdpSessionStatus.Connected => "RdpStatusConnected",
        RdpSessionStatus.Disconnecting => "RdpStatusDisconnecting",
        RdpSessionStatus.Disconnected => "RdpStatusDisconnected",
        RdpSessionStatus.Reconnecting => "RdpStatusReconnecting",
        RdpSessionStatus.Error => "RdpStatusError",
        _ => "RdpStatusError"
    };

    /// <summary>
    /// Returns the stable code used by converters and pane state matching.
    /// </summary>
    public static string GetInvariantCode(RdpSessionStatus status) => status switch
    {
        RdpSessionStatus.Preparing => "Preparing",
        RdpSessionStatus.Connecting => "Connecting",
        RdpSessionStatus.Connected => "Connected",
        RdpSessionStatus.Disconnecting => "Disconnecting",
        RdpSessionStatus.Disconnected => "Disconnected",
        RdpSessionStatus.Reconnecting => "Reconnecting",
        RdpSessionStatus.Error => "Error",
        _ => "Error"
    };
}
