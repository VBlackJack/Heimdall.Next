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
/// Immutable snapshot describing an active SSH port-forwarding tunnel.
/// </summary>
/// <param name="ServerName">Display name of the gateway server.</param>
/// <param name="LocalPort">Local port bound for forwarding.</param>
/// <param name="RemoteHost">Target host on the remote network.</param>
/// <param name="RemotePort">Target port on the remote network.</param>
/// <param name="StartedAt">UTC timestamp when the tunnel was established.</param>
/// <param name="IsAlive">Whether the underlying SSH connection is still active.</param>
public sealed record TunnelInfo(
    string ServerName,
    int LocalPort,
    string RemoteHost,
    int RemotePort,
    DateTime StartedAt,
    bool IsAlive)
{
    /// <summary>
    /// Local port for the SOCKS5 dynamic proxy, or 0 if disabled.
    /// </summary>
    public int SocksProxyPort { get; init; }

    /// <summary>
    /// Port opened on the remote server for reverse forwarding, or 0 if disabled.
    /// </summary>
    public int RemoteBindPort { get; init; }
}
