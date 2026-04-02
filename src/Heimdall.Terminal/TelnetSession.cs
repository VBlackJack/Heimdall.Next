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

namespace Heimdall.Terminal;

/// <summary>
/// Raw TCP terminal session with minimal Telnet IAC negotiation.
/// Implements <see cref="ITerminalSession"/> so it can be rendered in
/// the same WebView2 + xterm.js infrastructure used by SSH pipe mode.
/// </summary>
public class TelnetSession : ITerminalSession
{
    private const byte IAC = 255;
    private const byte WILL = 251;
    private const byte WONT = 252;
    private const byte DO = 253;
    private const byte DONT = 254;
    private const byte SB = 250;
    private const byte SE = 240;
    private const byte NAWS = 31;

    private readonly string _host;
    private readonly int _port;
    private readonly int _connectTimeoutMs;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private Task? _readLoop;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _nawsNegotiated;
    private int _columns;
    private int _rows;

    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<int>? ProcessExited;

    public bool IsRunning => _client?.Connected == true && !_disposed;
    public int? ProcessId => null;
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    public TelnetSession(string host, int port, int connectTimeoutMs = 15000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _host = host;
        _port = port > 0 ? port : 23;
        _connectTimeoutMs = connectTimeoutMs;
    }

    /// <summary>
    /// Opens a TCP connection to the remote Telnet host and starts the read loop.
    /// The <paramref name="executable"/> and <paramref name="arguments"/> parameters
    /// are ignored — host and port are provided via the constructor.
    /// </summary>
    public async Task StartAsync(
        string executable, string arguments,
        int columns = 80, int rows = 24,
        string? workingDirectory = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TelnetSession));
        if (_client is not null) throw new InvalidOperationException("Session already started.");

        _columns = columns;
        _rows = rows;
        _cts = new CancellationTokenSource();

        _client = new TcpClient();
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        connectTimeout.CancelAfter(TimeSpan.FromMilliseconds(_connectTimeoutMs));
        await _client.ConnectAsync(_host, _port, connectTimeout.Token).ConfigureAwait(false);
        _stream = _client.GetStream();

        _readLoop = Task.Run(() => ReadLoop(_cts.Token), _cts.Token);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed || _stream is null) return;
        try
        {
            _stream.Write(data);
            _stream.Flush();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn($"[TelnetSession] Write: {ex.Message}");
        }
    }

    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Sends a Telnet NAWS subnegotiation if the remote side accepted NAWS.
    /// Otherwise this is a no-op — raw TCP has no resize signaling.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        _columns = columns;
        _rows = rows;

        if (!_nawsNegotiated || _stream is null || _disposed) return;

        SendNawsSubnegotiation(columns, rows);
    }

    public void Kill()
    {
        if (_disposed) return;
        try
        {
            _stream?.Close();
            _client?.Close();
        }
        catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TelnetSession] Kill: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        Kill();

        try { _stream?.Dispose(); } catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TelnetSession] Dispose stream: {ex.Message}"); }
        try { _client?.Dispose(); } catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TelnetSession] Dispose client: {ex.Message}"); }
        _stream = null;
        _client = null;
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Background read loop that receives data from the TCP stream,
    /// strips Telnet IAC sequences, and raises <see cref="DataReceived"/>
    /// for application data.
    /// </summary>
    private async Task ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var bytesRead = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (bytesRead <= 0) break;

                ProcessIncoming(buffer.AsSpan(0, bytesRead));
            }
        }
        catch (OperationCanceledException) { /* Expected on session dispose */ }
        catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TelnetSession] ReadLoop: {ex.Message}"); }

        ProcessExited?.Invoke(0);
    }

    /// <summary>
    /// Parses incoming data, handling Telnet IAC commands inline and
    /// forwarding application data via <see cref="DataReceived"/>.
    /// </summary>
    private void ProcessIncoming(ReadOnlySpan<byte> data)
    {
        var appDataStart = 0;

        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] != IAC) continue;

            // Flush any application data before this IAC
            if (i > appDataStart)
            {
                EmitData(data.Slice(appDataStart, i - appDataStart));
            }

            if (i + 1 >= data.Length) break;

            var command = data[i + 1];
            switch (command)
            {
                case DO:
                    if (i + 2 < data.Length)
                    {
                        HandleDo(data[i + 2]);
                        i += 2;
                    }
                    break;

                case WILL:
                    if (i + 2 < data.Length)
                    {
                        HandleWill(data[i + 2]);
                        i += 2;
                    }
                    break;

                case WONT:
                case DONT:
                    // Acknowledge — no action needed
                    if (i + 2 < data.Length) i += 2;
                    break;

                case SB:
                    // Skip subnegotiation until IAC SE
                    i = SkipSubnegotiation(data, i + 2);
                    break;

                case IAC:
                    // Escaped 0xFF — emit a single 0xFF byte
                    EmitData(data.Slice(i, 1));
                    i += 1;
                    break;

                default:
                    // Unknown two-byte command — skip
                    i += 1;
                    break;
            }

            appDataStart = i + 1;
        }

        // Flush remaining application data
        if (appDataStart < data.Length)
        {
            EmitData(data[appDataStart..]);
        }
    }

    private void HandleDo(byte option)
    {
        if (option == NAWS)
        {
            // Accept NAWS — we can report window size
            SendCommand(WILL, NAWS);
            _nawsNegotiated = true;
            SendNawsSubnegotiation(_columns, _rows);
        }
        else
        {
            // Refuse everything else
            SendCommand(WONT, option);
        }
    }

    private void HandleWill(byte option)
    {
        // Refuse all server-side options
        SendCommand(DONT, option);
    }

    private void SendCommand(byte command, byte option)
    {
        if (_stream is null || _disposed) return;
        try
        {
            Span<byte> buf = stackalloc byte[3];
            buf[0] = IAC;
            buf[1] = command;
            buf[2] = option;
            _stream.Write(buf);
        }
        catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TelnetSession] SendCommand: {ex.Message}"); }
    }

    private void SendNawsSubnegotiation(int columns, int rows)
    {
        if (_stream is null || _disposed) return;
        try
        {
            // IAC SB NAWS <width-hi> <width-lo> <height-hi> <height-lo> IAC SE
            Span<byte> buf = stackalloc byte[9];
            buf[0] = IAC;
            buf[1] = SB;
            buf[2] = NAWS;
            buf[3] = (byte)(columns >> 8);
            buf[4] = (byte)(columns & 0xFF);
            buf[5] = (byte)(rows >> 8);
            buf[6] = (byte)(rows & 0xFF);
            buf[7] = IAC;
            buf[8] = SE;
            _stream.Write(buf);
        }
        catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TelnetSession] SendNawsSubnegotiation: {ex.Message}"); }
    }

    /// <summary>
    /// Skips past a subnegotiation block (SB ... IAC SE).
    /// Returns the index of the SE byte, or end-of-data if truncated.
    /// </summary>
    private static int SkipSubnegotiation(ReadOnlySpan<byte> data, int start)
    {
        for (var i = start; i < data.Length - 1; i++)
        {
            if (data[i] == IAC && data[i + 1] == SE)
            {
                return i + 1;
            }
        }

        return data.Length - 1;
    }

    private void EmitData(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;
        DataReceived?.Invoke(data.ToArray());
    }
}
