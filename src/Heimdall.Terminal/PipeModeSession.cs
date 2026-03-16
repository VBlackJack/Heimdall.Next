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
using System.Text;

namespace Heimdall.Terminal;

/// <summary>
/// Pipe mode terminal session: launches an external process with redirected
/// stdin/stdout (no pseudo-console). VT sequences pass through raw, which is
/// required for plink SSH where ConPTY breaks arrow keys.
/// The -t flag on plink forces remote PTY allocation.
/// </summary>
public class PipeModeSession : ITerminalSession
{
    private Process? _process;
    private Task? _readLoop;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<int>? ProcessExited;

    public bool IsRunning => _process is not null && !_process.HasExited;
    public int? ProcessId => _process?.Id;

    public Task StartAsync(string executable, string arguments, int columns = 80, int rows = 24)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PipeModeSession));
        if (_process is not null) throw new InvalidOperationException("Session already started.");

        _cts = new CancellationTokenSource();

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = null,  // Binary mode
            StandardErrorEncoding = Encoding.UTF8
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;

        if (!_process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {executable}");
        }

        // Background read loop for stdout (raw bytes → DataReceived events)
        _readLoop = Task.Run(() => ReadOutputLoop(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed || _process?.HasExited != false) return;
        try
        {
            _process.StandardInput.BaseStream.Write(data);
            _process.StandardInput.BaseStream.Flush();
        }
        catch (Exception)
        {
            // Process may have exited
        }
    }

    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Write(Encoding.UTF8.GetBytes(text));
    }

    public void Resize(int columns, int rows)
    {
        // Pipe mode cannot resize — the remote PTY size is fixed at connection time.
        // This is a known limitation vs ConPTY.
    }

    public void Kill()
    {
        if (_disposed || _process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(3000);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        Kill();

        try { _process?.Dispose(); } catch { }
        _process = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ReadOutputLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            var stream = _process!.StandardOutput.BaseStream;
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (bytesRead <= 0) break;

                DataReceived?.Invoke(new ReadOnlyMemory<byte>(buffer, 0, bytesRead));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = 0;
        try { exitCode = _process?.ExitCode ?? -1; } catch { }
        ProcessExited?.Invoke(exitCode);
    }
}
