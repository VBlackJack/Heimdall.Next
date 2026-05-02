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

using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;

namespace Heimdall.App.ViewModels.Tunnels;

/// <summary>
/// Resolves the aggregate tunnel badge state for one tab by walking every
/// split-pane leaf. <see cref="TunnelInfo.IsAlive"/> is a snapshot field; the
/// badge will not reflect silent-death transitions until the next tunnel
/// open/close event or active-session change re-triggers a resolve. This
/// limitation is accepted in Phase 3.1.
/// </summary>
internal static class TunnelBadgeStateResolver
{
    public static TunnelBadgeState Resolve(
        SessionTabViewModel tab,
        ConnectionStateMachine stateMachine,
        TunnelManager tunnelManager)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(stateMachine);
        ArgumentNullException.ThrowIfNull(tunnelManager);

        var anyTunnel = false;
        var anyUnhealthy = false;

        foreach (var pane in SplitTreeHelper.EnumerateLeaves(tab.RootContent))
        {
            if (string.IsNullOrWhiteSpace(pane.ServerId))
            {
                continue;
            }

            var tunnelPort = stateMachine.GetStateData(pane.ServerId)?.TunnelLocalPort;
            if (tunnelPort is null)
            {
                continue;
            }

            anyTunnel = true;

            var tunnel = tunnelManager.GetTunnel(tunnelPort.Value);
            if (tunnel is null || !tunnel.IsAlive)
            {
                anyUnhealthy = true;
            }
        }

        if (!anyTunnel)
        {
            return TunnelBadgeState.Hidden;
        }

        return anyUnhealthy
            ? TunnelBadgeState.Unhealthy
            : TunnelBadgeState.Healthy;
    }
}
