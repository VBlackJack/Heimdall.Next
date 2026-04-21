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

using System.Diagnostics;
using System.Net.Sockets;
using Heimdall.Core.Network;

namespace Heimdall.App.Services;

/// <summary>
/// Measures TCP connection latency to a host:port by opening a
/// <see cref="TcpClient"/> and timing the handshake. Stateless, safe to call
/// repeatedly from the VM loop.
/// </summary>
public interface ITcpPingService
{
    Task<TcpPingProbeResult> ProbeAsync(TcpPingProbeRequest request, CancellationToken ct);
}

public sealed class TcpPingService : ITcpPingService
{
    private readonly Func<string, int, CancellationToken, Task<double>> _connectAsync;

    public TcpPingService()
        : this(DefaultConnectAsync)
    {
    }

    internal TcpPingService(Func<string, int, CancellationToken, Task<double>> connectAsync)
    {
        ArgumentNullException.ThrowIfNull(connectAsync);
        _connectAsync = connectAsync;
    }

    public async Task<TcpPingProbeResult> ProbeAsync(TcpPingProbeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        using var timeoutCts = new CancellationTokenSource();
        timeoutCts.CancelAfter(request.TimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var latencyMs = await _connectAsync(request.Host, request.Port, linked.Token).ConfigureAwait(false);
            return TcpPingProbeResult.Ok(request.Seq, request.Host, request.Port, latencyMs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return TcpPingProbeResult.Failed(request.Seq, request.Host, request.Port, "Timeout");
        }
        catch (Exception ex)
        {
            var reason = ex.InnerException?.Message ?? ex.Message;
            return TcpPingProbeResult.Failed(request.Seq, request.Host, request.Port, reason);
        }
    }

    private static async Task<double> DefaultConnectAsync(string host, int port, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }
}
