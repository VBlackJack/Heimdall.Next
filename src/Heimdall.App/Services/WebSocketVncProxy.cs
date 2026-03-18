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
using System.Net.WebSockets;

namespace Heimdall.App.Services;

/// <summary>
/// Lightweight WebSocket-to-TCP proxy that bridges a noVNC WebSocket client
/// to a raw VNC (RFB) TCP server. Listens on a random local port, accepting
/// a single WebSocket connection and bidirectionally piping binary frames
/// to/from the VNC server.
/// </summary>
public sealed class WebSocketVncProxy : IDisposable
{
    private const int BufferSize = 65536;

    private readonly string _vncHost;
    private readonly int _vncPort;
    private readonly HttpListener _httpListener;
    private readonly CancellationTokenSource _cts = new();
    private TcpClient? _tcpClient;
    private bool _disposed;

    /// <summary>
    /// The local port on which the WebSocket proxy is listening.
    /// noVNC connects to <c>ws://localhost:{ListenPort}</c>.
    /// </summary>
    public int ListenPort { get; }

    public WebSocketVncProxy(string vncHost, int vncPort)
    {
        _vncHost = vncHost;
        _vncPort = vncPort;

        // Find a free local port by binding to port 0
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        ListenPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://127.0.0.1:{ListenPort}/");
    }

    /// <summary>
    /// Starts the WebSocket listener and begins accepting connections.
    /// Returns immediately; the proxy runs on background tasks.
    /// </summary>
    public void Start()
    {
        _httpListener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await _httpListener.GetContextAsync().ConfigureAwait(false);

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null)
                    .ConfigureAwait(false);

                // Handle one connection at a time (VNC is single-client)
                await HandleConnectionAsync(wsContext.WebSocket, ct).ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
            // HttpListener was disposed during shutdown.
        }
        catch (HttpListenerException)
        {
            // Listener stopped.
        }
    }

    private async Task HandleConnectionAsync(WebSocket ws, CancellationToken ct)
    {
        TcpClient? tcp = null;
        NetworkStream? tcpStream = null;

        try
        {
            tcp = new TcpClient();
            await tcp.ConnectAsync(_vncHost, _vncPort, ct).ConfigureAwait(false);
            tcpStream = tcp.GetStream();
            _tcpClient = tcp;

            var wsToTcp = RelayWebSocketToTcpAsync(ws, tcpStream, ct);
            var tcpToWs = RelayTcpToWebSocketAsync(tcpStream, ws, ct);

            await Task.WhenAny(wsToTcp, tcpToWs).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or WebSocketException)
        {
            // Connection ended or was cancelled.
        }
        finally
        {
            _tcpClient = null;
            tcpStream?.Dispose();
            tcp?.Dispose();

            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort close.
                }
            }

            ws.Dispose();
        }
    }

    /// <summary>
    /// Reads binary WebSocket frames from noVNC and writes them to the VNC TCP stream.
    /// </summary>
    private static async Task RelayWebSocketToTcpAsync(
        WebSocket ws, NetworkStream tcpStream, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.Count > 0)
            {
                await tcpStream.WriteAsync(buffer.AsMemory(0, result.Count), ct)
                    .ConfigureAwait(false);
                await tcpStream.FlushAsync(ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads raw TCP data from the VNC server and sends it as binary WebSocket frames.
    /// </summary>
    private static async Task RelayTcpToWebSocketAsync(
        NetworkStream tcpStream, WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            int bytesRead = await tcpStream.ReadAsync(buffer, ct).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                break;
            }

            await ws.SendAsync(
                new ArraySegment<byte>(buffer, 0, bytesRead),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cts.Cancel();

        try
        {
            _httpListener.Stop();
            _httpListener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }

        _tcpClient?.Dispose();
        _cts.Dispose();
    }
}
