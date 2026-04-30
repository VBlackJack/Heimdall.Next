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

internal enum RdpHealthDotState
{
    Idle,
    Healthy,
    Transitional,
    Faulted
}

internal static class RdpHealthDotPolicy
{
    public static RdpHealthDotState Resolve(
        RdpConnectionPhase phase,
        RdpSessionStatus status,
        bool wasUserInitiatedDisconnect)
    {
        if (phase == RdpConnectionPhase.Connected)
        {
            return RdpHealthDotState.Healthy;
        }

        if (phase is RdpConnectionPhase.Preparing
            or RdpConnectionPhase.Connecting
            or RdpConnectionPhase.Loading)
        {
            return RdpHealthDotState.Transitional;
        }

        return status switch
        {
            RdpSessionStatus.Reconnecting => RdpHealthDotState.Transitional,
            RdpSessionStatus.Disconnecting => RdpHealthDotState.Idle,
            RdpSessionStatus.Disconnected when wasUserInitiatedDisconnect => RdpHealthDotState.Idle,
            RdpSessionStatus.Disconnected => RdpHealthDotState.Faulted,
            RdpSessionStatus.Error => RdpHealthDotState.Faulted,
            _ => RdpHealthDotState.Idle
        };
    }
}
