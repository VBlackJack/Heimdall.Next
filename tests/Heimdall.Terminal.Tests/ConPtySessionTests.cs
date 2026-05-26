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

using System.Text;
using Heimdall.Terminal.ConPty;

namespace Heimdall.Terminal.Tests;

/// <summary>
/// Seam-free ConPTY coverage. Native setup failure handle-leak paths and
/// deterministic interactive input timing are intentionally not covered here.
/// Non-interactive PowerShell output is also not asserted because, under the
/// xUnit console runner, it is emitted through the inherited stdout stream
/// instead of the ConPTY output pipe.
/// </summary>
public sealed class ConPtySessionTests
{
    [Fact]
    [Trait("Category", "CIUnstable")]
    public async Task StartAsync_LaunchesShell_DeliversInitialTerminalOutput()
    {
        if (!ConPtySession.IsAvailable)
        {
            return;
        }

        ConPtySession session = new();
        StringBuilder output = new();
        object outputLock = new object();
        TaskCompletionSource<string> outputObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        session.DataReceived += data =>
        {
            lock (outputLock)
            {
                output.Append(Encoding.UTF8.GetString(data.Span));
                string text = output.ToString();
                if (text.Length > 0)
                {
                    outputObserved.TrySetResult(text);
                }
            }
        };

        try
        {
            await session.StartAsync(
                TerminalTestHelpers.ResolvePowerShellExecutable(),
                "-NoLogo -NoProfile");

            string text = await outputObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotEmpty(text);
            Assert.True(session.IsRunning);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task Dispose_TerminatesPseudoConsoleAndProcess()
    {
        if (!ConPtySession.IsAvailable)
        {
            return;
        }

        ConPtySession session = new();

        await session.StartAsync(
            TerminalTestHelpers.ResolvePowerShellExecutable(),
            BuildEncodedPowerShellArguments("Start-Sleep -Seconds 60"));
        int processId = Assert.IsType<int>(session.ProcessId);

        session.Dispose();

        TerminalTestHelpers.AssertProcessHasExited(processId);
    }

    [Fact]
    public async Task Resize_AfterStart_DoesNotThrow()
    {
        if (!ConPtySession.IsAvailable)
        {
            return;
        }

        ConPtySession session = new();

        try
        {
            await session.StartAsync(
                TerminalTestHelpers.ResolvePowerShellExecutable(),
                BuildEncodedPowerShellArguments("Start-Sleep -Seconds 60"));

            Exception? exception = Record.Exception(() =>
            {
                session.Resize(80, 24);
                session.Resize(120, 40);
            });

            Assert.Null(exception);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task Resize_OversizeDimensions_DoesNotThrow()
    {
        if (!ConPtySession.IsAvailable)
        {
            return;
        }

        ConPtySession session = new();

        try
        {
            await session.StartAsync(
                TerminalTestHelpers.ResolvePowerShellExecutable(),
                BuildEncodedPowerShellArguments("Start-Sleep -Seconds 60"));

            Exception? exception = Record.Exception(() =>
            {
                session.Resize(int.MaxValue, int.MaxValue);
            });

            Assert.Null(exception);
        }
        finally
        {
            session.Dispose();
        }
    }

    private static string BuildEncodedPowerShellArguments(string command)
    {
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        return $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}";
    }
}
