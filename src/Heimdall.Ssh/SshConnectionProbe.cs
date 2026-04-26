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

using System.Net.Sockets;
using System.Text;

namespace Heimdall.Ssh;

/// <summary>
/// Lightweight SSH reachability probe. Opens TCP and reads the protocol banner
/// only; it does not authenticate or trigger host-key verification.
/// </summary>
public static class SshConnectionProbe
{
    private const int MaxBannerBytes = 512;

    public sealed record ProbeResult(
        bool Success,
        string? Banner,
        SshFailureCode? FailureCode,
        string? Message);

    public static async Task<ProbeResult> ProbeAsync(
        string host,
        int port,
        int timeoutMs,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, linkedCts.Token).ConfigureAwait(false);

            await using var stream = client.GetStream();
            var banner = await ReadBannerAsync(stream, linkedCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(banner))
            {
                return new ProbeResult(
                    false,
                    banner,
                    SshFailureCode.ProtocolError,
                    "Server reached but did not send an SSH banner.");
            }

            var trimmed = banner.Trim();
            if (!trimmed.StartsWith("SSH-", StringComparison.Ordinal))
            {
                return new ProbeResult(
                    false,
                    trimmed,
                    SshFailureCode.ProtocolError,
                    "Server reached but is not an SSH server.");
            }

            return new ProbeResult(true, trimmed, null, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ProbeResult(
                false,
                null,
                SshFailureCode.NetworkTimedOut,
                "Connection timed out.");
        }
        catch (SocketException ex)
        {
            return ClassifySocketException(ex);
        }
    }

    private static async Task<string?> ReadBannerAsync(
        NetworkStream stream,
        CancellationToken ct)
    {
        var buffer = new byte[MaxBannerBytes];
        var read = 0;

        while (read < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(read, 1),
                    ct)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            read += bytesRead;
            if (buffer[read - 1] == '\n')
            {
                break;
            }
        }

        return read == 0 ? null : Encoding.ASCII.GetString(buffer, 0, read);
    }

    private static ProbeResult ClassifySocketException(SocketException ex)
    {
        return ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => new ProbeResult(
                false,
                null,
                SshFailureCode.NetworkRefused,
                "Connection refused."),
            SocketError.TimedOut => new ProbeResult(
                false,
                null,
                SshFailureCode.NetworkTimedOut,
                "Connection timed out."),
            SocketError.HostNotFound
                or SocketError.HostUnreachable
                or SocketError.NetworkUnreachable => new ProbeResult(
                    false,
                    null,
                    SshFailureCode.NetworkUnreachable,
                    ex.Message),
            SocketError.ConnectionReset => new ProbeResult(
                false,
                null,
                SshFailureCode.NetworkReset,
                "Connection reset."),
            _ => new ProbeResult(
                false,
                null,
                SshFailureCode.Unknown,
                ex.Message)
        };
    }
}
