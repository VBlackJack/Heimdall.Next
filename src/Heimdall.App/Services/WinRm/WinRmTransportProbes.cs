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

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Heimdall.App.Services.WinRm;

internal static class WinRmTransportProbes
{
    public static async Task DefaultTcpProbeAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using TcpClient tcpClient = new TcpClient();
        await ConnectAsync(tcpClient, host, port, timeout, ct).ConfigureAwait(false);
    }

    public static async Task DefaultTlsProbeAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using TcpClient tcpClient = new TcpClient();
        await ConnectAsync(tcpClient, host, port, timeout, ct).ConfigureAwait(false);

        using SslStream sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            ValidateServerCertificate);
        SslClientAuthenticationOptions options = new SslClientAuthenticationOptions
        {
            TargetHost = host,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };

        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);
        try
        {
            await sslStream.AuthenticateAsClientAsync(options, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && linkedCts.IsCancellationRequested)
        {
            throw new TimeoutException($"TLS handshake timed out for {host}:{port}.");
        }
    }

    private static async Task ConnectAsync(
        TcpClient tcpClient,
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);
        try
        {
            await tcpClient.ConnectAsync(host, port, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && linkedCts.IsCancellationRequested)
        {
            throw new TimeoutException($"TCP connection timed out for {host}:{port}.");
        }
    }

    private static bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            Core.Logging.FileLogger.Warn($"WinRM TLS certificate validation failed: {sslPolicyErrors}");
        }

        return sslPolicyErrors == SslPolicyErrors.None;
    }
}
