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

namespace Heimdall.Ssh.Plink;

/// <summary>
/// Result of a plink tunnel establishment attempt.
/// </summary>
/// <param name="Success">Whether the tunnel was established and is forwarding traffic.</param>
/// <param name="ErrorMessage">Error description on failure; null on success.</param>
/// <param name="FailureCode">Structured failure code on failure; null on success.</param>
public sealed record PlinkTunnelResult(bool Success, string? ErrorMessage, SshFailureCode? FailureCode);

/// <summary>
/// Fallback tunnel implementation using an external plink.exe process.
/// Used when SSH.NET cannot handle the authentication method, such as
/// Pageant agent keys where the private key material is only accessible
/// to the agent process.
/// </summary>
/// <remarks>
/// This is a temporary bridge until SSH.NET adds native Pageant/agent support.
/// The legacy Heimdall (PowerShell) used plink exclusively for all tunnels.
/// </remarks>
public sealed class PlinkTunnelRunner : IDisposable
{
    private static readonly int PortCheckMaxAttempts = 15;
    private readonly TimeSpan _portCheckInterval;
    private readonly TimeSpan _processKillGracePeriod;
    private readonly TimeSpan _stderrReadTimeout;

    private Process? _process;
    private string? _pwFilePath;
    private bool _disposed;

    public PlinkTunnelRunner(
        int portCheckIntervalMs = 2000,
        int killGracePeriodMs = 2000,
        int stderrReadTimeoutMs = 10000)
    {
        _portCheckInterval = TimeSpan.FromMilliseconds(portCheckIntervalMs);
        _processKillGracePeriod = TimeSpan.FromMilliseconds(killGracePeriodMs);
        _stderrReadTimeout = TimeSpan.FromMilliseconds(stderrReadTimeoutMs);
    }

    /// <summary>Whether the underlying plink process is running.</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Process ID of the plink tunnel process, or null if not running.</summary>
    public int? ProcessId => IsRunning ? _process!.Id : null;

    /// <summary>
    /// Starts a plink.exe process that establishes an SSH tunnel
    /// with local port forwarding to the specified remote endpoint.
    /// </summary>
    /// <param name="plinkPath">Absolute path to plink.exe.</param>
    /// <param name="gatewayHost">SSH gateway hostname or IP.</param>
    /// <param name="gatewayPort">SSH gateway port.</param>
    /// <param name="username">SSH username.</param>
    /// <param name="keyPath">Path to private key file (PPK format). Optional.</param>
    /// <param name="password">SSH password. Optional. Written to a temporary file for -pwfile.</param>
    /// <param name="remoteHost">Target host on the remote network.</param>
    /// <param name="remotePort">Target port on the remote network.</param>
    /// <param name="localPort">Local port to bind for forwarding.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>Result indicating success or structured failure.</returns>
    public async Task<PlinkTunnelResult> StartAsync(
        string plinkPath,
        string gatewayHost,
        int gatewayPort,
        string username,
        string? keyPath,
        string? password,
        string remoteHost,
        int remotePort,
        int localPort,
        string? hostKeyFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_process is not null)
        {
            throw new InvalidOperationException("A plink tunnel is already running. Call Stop() first.");
        }

        if (!File.Exists(plinkPath))
        {
            return new PlinkTunnelResult(false, $"Plink executable not found: {plinkPath}", SshFailureCode.Unknown);
        }

        // Build argument list
        var args = BuildArguments(gatewayHost, gatewayPort, username, keyPath, password,
            remoteHost, remotePort, localPort, hostKeyFingerprint);

        var startInfo = new ProcessStartInfo
        {
            FileName = plinkPath,
            Arguments = string.Join(' ', args),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = false
        };

        try
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.Exited += (_, _) =>
                Core.Logging.FileLogger.Warn($"Plink tunnel process exited (pid={_process?.Id}, port={localPort})");
            _process.Start();
        }
        catch (Exception ex)
        {
            // Ensure password file is cleaned up if process fails to start
            CleanupPasswordFile();
            return new PlinkTunnelResult(false, $"Failed to start plink process: {ex.Message}", SshFailureCode.Unknown);
        }

        // Continuously drain stderr in the background to prevent buffer saturation.
        // Uses the cancellation token so the drain terminates cleanly on Stop().
        _ = Task.Run(async () =>
        {
            try
            {
                while (_process is { HasExited: false } && _process.StandardError is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = await _process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null) break;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Core.Logging.FileLogger.Info($"Plink stderr (port {localPort}): {line}");
                    }
                }
            }
            catch (OperationCanceledException) { /* Clean shutdown */ }
            catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[PlinkTunnelRunner] stderr drain: {ex.Message}"); }
        }, cancellationToken);

        try
        {
            // Wait for the local port to become reachable (tunnel established)
            bool portReady = await WaitForPortBindAsync(localPort, cancellationToken).ConfigureAwait(false);

            if (!portReady)
            {
                // Process may have already exited with an error
                var exitInfo = _process is { HasExited: true }
                    ? $"(exit code {_process.ExitCode})"
                    : "(still running but port not bound)";
                Stop();

                var message = $"Plink tunnel failed to bind port {localPort} within timeout {exitInfo}";
                Core.Logging.FileLogger.Error(message);
                return new PlinkTunnelResult(false, message, SshFailureCode.Unknown);
            }

            return new PlinkTunnelResult(true, null, null);
        }
        catch (OperationCanceledException)
        {
            Stop();
            return new PlinkTunnelResult(false, "Tunnel establishment was cancelled.", SshFailureCode.Cancelled);
        }
        catch (Exception ex)
        {
            Stop();
            return new PlinkTunnelResult(false, $"Failed to start plink process: {ex.Message}", SshFailureCode.Unknown);
        }
    }

    /// <summary>
    /// Stops the plink tunnel process and cleans up temporary files.
    /// </summary>
    public void Stop()
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit((int)_processKillGracePeriod.TotalMilliseconds);
                }
            }
            catch (InvalidOperationException ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn($"[PlinkTunnelRunner] Stop: {ex.Message}");
            }

            _process.Dispose();
            _process = null;
        }

        CleanupPasswordFile();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    /// <summary>
    /// Builds the plink command-line argument list for tunnel mode.
    /// Uses -N (no shell), -ssh (force SSH), -L (local forwarding).
    /// Password is written to a temp file and passed via -pwfile to avoid
    /// exposing it on the command line.
    /// </summary>
    internal List<string> BuildArguments(
        string gatewayHost,
        int gatewayPort,
        string username,
        string? keyPath,
        string? password,
        string remoteHost,
        int remotePort,
        int localPort,
        string? hostKeyFingerprint = null)
    {
        var args = new List<string>
        {
            "-ssh",
            "-batch", // non-interactive: fail instead of prompting
            "-N", // no shell, tunnel only
            "-L", $"{localPort}:{remoteHost}:{remotePort}",
            "-P", gatewayPort.ToString()
        };

        // Use TOFU host key fingerprint if available to prevent interactive prompts
        if (!string.IsNullOrEmpty(hostKeyFingerprint))
        {
            args.Add("-hostkey");
            args.Add($"\"{hostKeyFingerprint}\"");
        }

        if (!string.IsNullOrEmpty(keyPath))
        {
            args.Add("-i");
            args.Add($"\"{keyPath}\"");
        }

        if (!string.IsNullOrEmpty(password))
        {
            // Write password to a temporary file and use -pwfile.
            // This avoids exposing the password on the command line.
            // Create the file with restricted ACL atomically to eliminate
            // the TOCTOU window between creation and permission enforcement.
            CleanupPasswordFile();
            _pwFilePath = Path.Combine(Path.GetTempPath(), $"heimdall_pw_{Guid.NewGuid():N}");

            if (OperatingSystem.IsWindows())
            {
                Heimdall.Core.Security.SecureFileWriter.WriteAndProtect(_pwFilePath, password);
            }
            else
            {
                File.WriteAllText(_pwFilePath, password);
                // Best-effort POSIX permission restriction (mode 0600)
                try
                {
                    File.SetUnixFileMode(_pwFilePath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex)
                {
                    Heimdall.Core.Logging.FileLogger.Warn(
                        $"[PlinkTunnelRunner] Unix file mode restriction failed: {ex.Message}");
                }
            }

            args.Add("-pwfile");
            args.Add($"\"{_pwFilePath}\"");
        }

        args.Add($"{username}@{gatewayHost}");

        return args;
    }

    /// <summary>
    /// Waits for the local forwarded port to become reachable, indicating
    /// the tunnel is established and forwarding traffic.
    /// Uses a retry loop with configurable attempts and interval.
    /// </summary>
    private async Task<bool> WaitForPortBindAsync(int localPort, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < PortCheckMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(_portCheckInterval, cancellationToken).ConfigureAwait(false);

            if (await IsPortListeningAsync(localPort).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a TCP port is listening on localhost by attempting a brief connection.
    /// </summary>
    private static async Task<bool> IsPortListeningAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(1)))
                .ConfigureAwait(false);
            return completed == connectTask && client.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads any available stderr output from the plink process.
    /// Uses a short timeout to avoid blocking if the process is still running.
    /// </summary>
    private async Task<string> ReadStderrSafeAsync()
    {
        if (_process?.StandardError is null)
        {
            return string.Empty;
        }

        try
        {
            using var cts = new CancellationTokenSource(_stderrReadTimeout);
            return await _process.StandardError.ReadToEndAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn($"[PlinkTunnelRunner] ReadStderrSafe: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Deletes the temporary password file if one was created.
    /// </summary>
    private void CleanupPasswordFile()
    {
        if (_pwFilePath is not null)
        {
            try
            {
                File.Delete(_pwFilePath);
            }
            catch (IOException ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn($"[PlinkTunnelRunner] CleanupPasswordFile: {ex.Message}");
            }

            _pwFilePath = null;
        }
    }
}
