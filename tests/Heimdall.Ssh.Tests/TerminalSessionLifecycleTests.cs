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
using System.Text;
using Heimdall.Terminal;

namespace Heimdall.Ssh.Tests;

public sealed class TerminalSessionLifecycleTests
{
    [Fact]
    public async Task PipeModeSession_Dispose_TerminatesRunningProcess()
    {
        PipeModeSession session = new();
        await session.StartAsync(
            ResolvePowerShellExecutable(),
            "-NoLogo -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 60\"");
        int processId = Assert.IsType<int>(session.ProcessId);

        session.Dispose();

        AssertProcessHasExited(processId);
    }

    [Fact]
    public async Task PipeModeSession_StartAsync_AfterFailedStart_CanBeRetried()
    {
        PipeModeSession session = new();

        await Assert.ThrowsAnyAsync<Exception>(() => session.StartAsync(
            Path.Combine(Path.GetTempPath(), $"heimdall_missing_{Guid.NewGuid():N}.exe"),
            string.Empty));

        await session.StartAsync(
            ResolvePowerShellExecutable(),
            "-NoLogo -NoProfile -NonInteractive -Command \"exit 0\"");
        session.Dispose();
    }

    [Fact]
    public async Task TelnetSession_StartAsync_AfterFailedConnect_CanBeRetried()
    {
        int port = ReserveLoopbackPort();
        TelnetSession session = new(IPAddress.Loopback.ToString(), port, connectTimeoutMs: 250);

        await Assert.ThrowsAnyAsync<Exception>(() => session.StartAsync(string.Empty, string.Empty));

        TcpListener listener = new(IPAddress.Loopback, port);
        listener.Start();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

        try
        {
            await session.StartAsync(string.Empty, string.Empty);
            using TcpClient acceptedClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            session.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task PipeModeSession_DataReceivedSubscriberException_DoesNotStopReadLoop()
    {
        PipeModeSession session = new();
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbackCount = 0;

        session.DataReceived += _ =>
        {
            Interlocked.Increment(ref callbackCount);
            throw new InvalidOperationException("Test subscriber failure.");
        };
        session.ProcessExited += exitCode => exited.TrySetResult(exitCode);

        await session.StartAsync(
            ResolvePowerShellExecutable(),
            "-NoLogo -NoProfile -NonInteractive -Command \"Write-Output 'first'; Start-Sleep -Milliseconds 300; Write-Output 'second'; exit 0\"");

        int exitCode = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        session.Dispose();

        Assert.Equal(0, exitCode);
        Assert.True(callbackCount >= 2, $"Expected the read loop to survive the first subscriber exception, got {callbackCount} callback(s).");
    }

    [Fact]
    public async Task PipeModeSession_ProcessExitedSubscriberException_DoesNotSurfaceToCaller()
    {
        PipeModeSession session = new();
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        session.ProcessExited += exitCode => exited.TrySetResult(exitCode);
        session.ProcessExited += _ => throw new InvalidOperationException("Test subscriber failure.");

        Exception? observedException = await Record.ExceptionAsync(async () =>
        {
            await session.StartAsync(
                ResolvePowerShellExecutable(),
                "-NoLogo -NoProfile -NonInteractive -Command \"exit 12\"");
            int exitCode = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(12, exitCode);
            session.Dispose();
        });

        Assert.Null(observedException);
    }

    [Fact]
    public async Task TelnetSession_DataReceivedSubscriberException_DoesNotStopReadLoop()
    {
        int port = ReserveLoopbackPort();
        TcpListener listener = new(IPAddress.Loopback, port);
        TelnetSession session = new(IPAddress.Loopback.ToString(), port, connectTimeoutMs: 1000);
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbackCount = 0;

        listener.Start();
        Task serverTask = SendTwoTelnetChunksAsync(listener);

        session.DataReceived += _ =>
        {
            Interlocked.Increment(ref callbackCount);
            throw new InvalidOperationException("Test subscriber failure.");
        };
        session.ProcessExited += exitCode => exited.TrySetResult(exitCode);

        try
        {
            await session.StartAsync(string.Empty, string.Empty);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
            int exitCode = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, exitCode);
            Assert.True(callbackCount >= 2, $"Expected the read loop to survive the first subscriber exception, got {callbackCount} callback(s).");
        }
        finally
        {
            session.Dispose();
            listener.Stop();
        }
    }

    private static string ResolvePowerShellExecutable()
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string systemPowerShell = Path.Combine(
            windowsDirectory,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        return File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe";
    }

    private static int ReserveLoopbackPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task SendTwoTelnetChunksAsync(TcpListener listener)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        await using NetworkStream stream = client.GetStream();

        byte[] first = Encoding.ASCII.GetBytes("first");
        byte[] second = Encoding.ASCII.GetBytes("second");

        await stream.WriteAsync(first);
        await stream.FlushAsync();
        await Task.Delay(300);
        await stream.WriteAsync(second);
        await stream.FlushAsync();
    }

    private static void AssertProcessHasExited(int processId)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited || process.WaitForExit(100))
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }
        }

        Assert.Fail($"Process {processId} was still running after PipeModeSession.Dispose().");
    }
}
