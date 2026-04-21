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
using System.Net;
using System.Net.Sockets;
using Heimdall.Core.Network;

namespace Heimdall.App.Services;

/// <summary>
/// Contract for a single hostname resolution inside the DNS batch tool.
/// </summary>
public interface IDnsBatchResolverService
{
    Task<DnsBatchResolveResult> ResolveAsync(string hostname, CancellationToken ct);
}

/// <summary>
/// Stateless batch DNS resolver service. It resolves a single hostname and
/// maps failures to row results; caller-controlled cancellation is rethrown.
/// </summary>
public sealed class DnsBatchResolverService : IDnsBatchResolverService
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveAsync;

    public DnsBatchResolverService()
        : this(Dns.GetHostAddressesAsync)
    {
    }

    internal DnsBatchResolverService(Func<string, CancellationToken, Task<IPAddress[]>> resolveAsync)
    {
        _resolveAsync = resolveAsync ?? throw new ArgumentNullException(nameof(resolveAsync));
    }

    public async Task<DnsBatchResolveResult> ResolveAsync(string hostname, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);
        ct.ThrowIfCancellationRequested();

        var trimmedHostname = hostname.Trim();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var addresses = await _resolveAsync(trimmedHostname, ct).ConfigureAwait(false);
            stopwatch.Stop();
            return DnsBatchResolveResult.Ok(trimmedHostname, addresses, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            return DnsBatchResolveResult.Failed(trimmedHostname, (int)stopwatch.ElapsedMilliseconds, ex.SocketErrorCode.ToString());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return DnsBatchResolveResult.Failed(trimmedHostname, (int)stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }
}
