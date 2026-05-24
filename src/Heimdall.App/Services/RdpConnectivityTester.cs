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
using Heimdall.Core.Security;

namespace Heimdall.App.Services;

/// <summary>
/// Lightweight RDP reachability test: DNS resolution followed by a TCP connect
/// on the target port. It deliberately does not negotiate TLS, NLA, or RDP.
/// </summary>
internal sealed class RdpConnectivityTester
{
    public async Task<RdpConnectivityTestResult> TestAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var trimmedHost = host?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedHost)
            || !InputValidator.Validate(trimmedHost, "Address"))
        {
            return RdpConnectivityTestResult.InvalidAddress();
        }

        if (!InputValidator.ValidatePortRange(port))
        {
            return RdpConnectivityTestResult.InvalidPort();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return RdpConnectivityTestResult.Cancelled();
        }

        var dnsStopwatch = Stopwatch.StartNew();
        IPAddress[] addresses;
        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            addresses = await Dns.GetHostAddressesAsync(trimmedHost, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return RdpConnectivityTestResult.Cancelled();
        }
        catch (OperationCanceledException)
        {
            return RdpConnectivityTestResult.DnsTimeout(timeout);
        }
        catch (SocketException ex)
        {
            return RdpConnectivityTestResult.DnsFailed(ex.Message);
        }

        dnsStopwatch.Stop();
        if (addresses.Length == 0)
        {
            return RdpConnectivityTestResult.DnsNoResults();
        }

        var resolvedAddress = addresses[0];
        var tcpStopwatch = Stopwatch.StartNew();
        try
        {
            using var socket = new TcpClient(resolvedAddress.AddressFamily);
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            await socket.ConnectAsync(resolvedAddress, port, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return RdpConnectivityTestResult.Cancelled();
        }
        catch (OperationCanceledException)
        {
            return RdpConnectivityTestResult.TcpTimeout(resolvedAddress.ToString(), timeout);
        }
        catch (SocketException ex)
        {
            return RdpConnectivityTestResult.TcpFailed(
                resolvedAddress.ToString(),
                ex.SocketErrorCode,
                ex.Message);
        }

        tcpStopwatch.Stop();
        return RdpConnectivityTestResult.Success(
            resolvedAddress.ToString(),
            dnsStopwatch.Elapsed,
            tcpStopwatch.Elapsed);
    }
}

internal sealed record RdpConnectivityTestResult(
    RdpConnectivityTestOutcome Outcome,
    string? ResolvedAddress,
    TimeSpan? DnsElapsed,
    TimeSpan? TcpElapsed,
    string? Detail,
    SocketError? SocketError)
{
    public static RdpConnectivityTestResult Success(string address, TimeSpan dnsElapsed, TimeSpan tcpElapsed)
        => new(RdpConnectivityTestOutcome.Success, address, dnsElapsed, tcpElapsed, null, null);

    public static RdpConnectivityTestResult InvalidAddress()
        => new(RdpConnectivityTestOutcome.InvalidAddress, null, null, null, null, null);

    public static RdpConnectivityTestResult InvalidPort()
        => new(RdpConnectivityTestOutcome.InvalidPort, null, null, null, null, null);

    public static RdpConnectivityTestResult DnsTimeout(TimeSpan timeout)
        => new(RdpConnectivityTestOutcome.DnsTimeout, null, timeout, null, null, null);

    public static RdpConnectivityTestResult DnsFailed(string detail)
        => new(RdpConnectivityTestOutcome.DnsFailed, null, null, null, detail, null);

    public static RdpConnectivityTestResult DnsNoResults()
        => new(RdpConnectivityTestOutcome.DnsNoResults, null, null, null, null, null);

    public static RdpConnectivityTestResult TcpTimeout(string address, TimeSpan timeout)
        => new(RdpConnectivityTestOutcome.TcpTimeout, address, null, timeout, null, null);

    public static RdpConnectivityTestResult TcpFailed(string address, SocketError socketError, string detail)
        => new(RdpConnectivityTestOutcome.TcpFailed, address, null, null, detail, socketError);

    public static RdpConnectivityTestResult Cancelled()
        => new(RdpConnectivityTestOutcome.Cancelled, null, null, null, null, null);
}

internal enum RdpConnectivityTestOutcome
{
    Success,
    InvalidAddress,
    InvalidPort,
    DnsTimeout,
    DnsFailed,
    DnsNoResults,
    TcpTimeout,
    TcpFailed,
    Cancelled
}
