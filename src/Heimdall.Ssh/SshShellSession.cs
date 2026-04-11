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

using System.Reflection;
using System.Text;
using Renci.SshNet;

namespace Heimdall.Ssh;

/// <summary>
/// Manages an interactive SSH shell session with PTY allocation.
/// Provides event-driven data reception and supports terminal resize.
/// Replaces the legacy plink + ConPTY / WebView2+xterm.js approach.
/// </summary>
public sealed class SshShellSession : IDisposable
{
    private const int ReadBufferSize = 8192;

    private SshClient? _client;
    private ShellStream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readLoopTask;
    private bool _disposed;

    /// <summary>Raised when data is received from the remote shell.</summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>
    /// Raised when the session is disconnected. The argument contains an error
    /// message if the disconnect was unexpected, or null for a clean disconnect.
    /// </summary>
    public event Action<string?>? Disconnected;

    /// <summary>Whether the underlying SSH connection is active.</summary>
    public bool IsConnected => _client?.IsConnected == true && _stream is not null;

    /// <summary>Exposes the underlying SSH client for multiplexed operations (e.g. health monitoring).</summary>
    public SshClient? Client => _client;

    /// <summary>
    /// Connects to the SSH server, allocates a PTY, and starts the interactive shell.
    /// A background read loop begins immediately after connection, raising
    /// <see cref="DataReceived"/> for each chunk of output.
    /// </summary>
    /// <param name="connectionParams">SSH connection parameters.</param>
    /// <param name="terminalColumns">Initial terminal width in columns.</param>
    /// <param name="terminalRows">Initial terminal height in rows.</param>
    /// <param name="hostKeyStore">Optional TOFU host key store for server verification.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public async Task ConnectAsync(
        SshConnectionParams connectionParams,
        int terminalColumns = 80,
        int terminalRows = 24,
        HostKeyStore? hostKeyStore = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(connectionParams);

        if (_client is not null)
        {
            throw new InvalidOperationException("Session is already connected. Call Disconnect() first.");
        }

        var connectionInfo = SshConnectionFactory.Create(connectionParams);
        _client = new SshClient(connectionInfo);

        if (hostKeyStore is not null)
        {
            SshConnectionFactory.AttachHostKeyVerification(
                _client, connectionParams.Host, connectionParams.Port, hostKeyStore);
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _client.Connect();
        }, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        _stream = _client.CreateShellStream(
            terminalName: "xterm-256color",
            columns: (uint)terminalColumns,
            rows: (uint)terminalRows,
            width: 0,
            height: 0,
            bufferSize: ReadBufferSize);

        _readCts = new CancellationTokenSource();
        _readLoopTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);
    }

    /// <summary>Writes raw bytes to the shell's standard input.</summary>
    /// <param name="data">Byte data to send.</param>
    public void Write(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_stream is null)
        {
            throw new InvalidOperationException("Session is not connected.");
        }

        _stream.Write(data, 0, data.Length);
        _stream.Flush();
    }

    /// <summary>Writes a UTF-8 encoded string to the shell's standard input.</summary>
    /// <param name="text">Text to send.</param>
    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Sends a terminal window-change request to the server,
    /// notifying it of the new terminal dimensions.
    /// </summary>
    /// <param name="columns">New terminal width in columns.</param>
    /// <param name="rows">New terminal height in rows.</param>
    public void Resize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_stream is null)
        {
            throw new InvalidOperationException("Session is not connected.");
        }

        // SSH.NET ShellStream does not expose window-change directly, but the
        // underlying IChannelSession has SendWindowChangeRequest. Access it via
        // reflection on the private _channel field.
        //
        // TODO: Remove reflection once SSH.NET exposes a public window-change API on ShellStream.
        // Track: https://github.com/sshnet/SSH.NET/issues — search "window change" or "resize".
        // If SSH.NET's internal field layout changes, this fails gracefully (logged as Warn,
        // session continues — only terminal reflow is lost).
        try
        {
            var channelField = _stream.GetType().GetField(
                "_channel",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (channelField?.GetValue(_stream) is { } channel)
            {
                var method = channel.GetType().GetMethod(
                    "SendWindowChangeRequest",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    [typeof(uint), typeof(uint), typeof(uint), typeof(uint)],
                    null);

                if (method is null)
                {
                    Core.Logging.FileLogger.Warn(
                        "SSH window-change: SendWindowChangeRequest not found — SSH.NET internal layout may have changed.");
                    return;
                }

                method.Invoke(channel, [(uint)columns, (uint)rows, 0u, 0u]);
            }
            else
            {
                Core.Logging.FileLogger.Warn(
                    "SSH window-change: _channel field is null on ShellStream — resize skipped.");
            }
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"SSH window-change request failed for {columns}x{rows}: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    /// <summary>
    /// Gracefully disconnects the SSH session, stopping the read loop and
    /// closing the shell stream and client connection.
    /// </summary>
    public void Disconnect()
    {
        if (_disposed || _client is null)
        {
            return;
        }

        StopReadLoop();
        CleanupStream();
        DisconnectClient();

        Disconnected?.Invoke(null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopReadLoop();
        CleanupStream();
        DisconnectClient();
    }

    /// <summary>
    /// Background loop that reads data from the shell stream and dispatches it
    /// via the <see cref="DataReceived"/> event. Runs until cancellation or disconnect.
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadBufferSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                int bytesRead;

                try
                {
                    bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (bytesRead <= 0)
                {
                    break;
                }

                var chunk = new byte[bytesRead];
                Array.Copy(buffer, chunk, bytesRead);
                DataReceived?.Invoke(chunk);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation during Disconnect/Dispose
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                Disconnected?.Invoke(ex.Message);
            }
        }
    }

    /// <summary>Signals the read loop to stop and waits briefly for it to complete.</summary>
    private void StopReadLoop()
    {
        if (_readCts is not null)
        {
            try
            {
                _readCts.Cancel();
                _readLoopTask?.Wait(TimeSpan.FromMilliseconds(500));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException or ObjectDisposedException))
            {
                // Expected from task cancellation during teardown
            }
            catch (AggregateException ex)
            {
                Core.Logging.FileLogger.Warn($"SshShellSession read loop stop: {ex.InnerException?.Message ?? ex.Message}");
            }
            finally
            {
                _readCts.Dispose();
                _readCts = null;
                _readLoopTask = null;
            }
        }
    }

    /// <summary>Closes and disposes the shell stream.</summary>
    private void CleanupStream()
    {
        if (_stream is not null)
        {
            try
            {
                _stream.Close();
                _stream.Dispose();
            }
            catch (ObjectDisposedException) { /* Expected when disposing already-closed resources */ }

            _stream = null;
        }
    }

    /// <summary>Disconnects and disposes the SSH client.</summary>
    private void DisconnectClient()
    {
        if (_client is not null)
        {
            try
            {
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }

                _client.Dispose();
            }
            catch (ObjectDisposedException) { /* Expected when disposing already-closed resources */ }

            _client = null;
        }
    }
}
