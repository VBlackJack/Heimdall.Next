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

using System.Net;
using System.Net.Sockets;
using Heimdall.Core.Network;
using Heimdall.Core.Security;

namespace Heimdall.App.Services;

/// <summary>
/// Contract for a single Wake-on-LAN magic packet send attempt.
/// </summary>
public interface IWakeOnLanService
{
    Task<WakeOnLanResult> SendAsync(WakeOnLanRequest request, CancellationToken ct);
}

/// <summary>
/// Stateless Wake-on-LAN sender. Validation is defensive and failures are
/// returned as error results instead of bubbling to the UI.
/// </summary>
public sealed class WakeOnLanService : IWakeOnLanService
{
    public const int DefaultPort = 9;
    public const int SendTimeoutMs = 2000;

    private readonly Func<byte[], IPAddress, int, CancellationToken, Task> _sendAsync;

    public WakeOnLanService()
        : this(DefaultSendAsync)
    {
    }

    internal WakeOnLanService(Func<byte[], IPAddress, int, CancellationToken, Task> sendAsync)
    {
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
    }

    public async Task<WakeOnLanResult> SendAsync(WakeOnLanRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ct.IsCancellationRequested)
        {
            return WakeOnLanResult.Error(string.Empty, request.BroadcastAddress ?? string.Empty, DefaultPort, "ToolWolErrorSocket", "Operation canceled");
        }

        var port = InputValidator.ValidatePortRange(request.Port) ? request.Port : DefaultPort;

        if (!MacAddressParser.TryParse(request.MacAddress, out var macBytes))
        {
            return WakeOnLanResult.Error(string.Empty, request.BroadcastAddress, port, "ToolWolErrorInvalidMac");
        }

        if (!MacAddressParser.TryNormalize(request.MacAddress, out var formattedMac))
        {
            return WakeOnLanResult.Error(string.Empty, request.BroadcastAddress, port, "ToolWolErrorInvalidMac");
        }

        if (!IPAddress.TryParse((request.BroadcastAddress ?? string.Empty).Trim(), out var broadcastAddress))
        {
            return WakeOnLanResult.Error(
                formattedMac,
                request.BroadcastAddress ?? string.Empty,
                port,
                "ToolWolErrorInvalidBroadcast");
        }

        var broadcastText = broadcastAddress.ToString();
        var packet = MagicPacketBuilder.Build(macBytes);

        try
        {
            using var timeoutCts = new CancellationTokenSource(SendTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await _sendAsync(packet, broadcastAddress, port, linkedCts.Token).ConfigureAwait(false);
            return WakeOnLanResult.Sent(formattedMac, broadcastText, port);
        }
        catch (SocketException ex)
        {
            return WakeOnLanResult.Error(formattedMac, broadcastText, port, "ToolWolErrorSocket", ex.Message);
        }
        catch (Exception ex)
        {
            return WakeOnLanResult.Error(formattedMac, broadcastText, port, "ToolWolErrorSocket", ex.Message);
        }
    }

    private static async Task DefaultSendAsync(byte[] packet, IPAddress address, int port, CancellationToken ct)
    {
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        var endpoint = new IPEndPoint(address, port);
        await client.SendAsync(packet, endpoint, ct).ConfigureAwait(false);
    }
}
