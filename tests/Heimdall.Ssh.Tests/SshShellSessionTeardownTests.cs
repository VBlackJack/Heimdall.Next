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

namespace Heimdall.Ssh.Tests;

public sealed class SshShellSessionTeardownTests
{
    [Fact]
    public void Dispose_OnUnconnectedSession_DoesNotThrow()
    {
        var session = new SshShellSession();

        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void Disconnect_OnUnconnectedSession_DoesNotThrow()
    {
        using var session = new SshShellSession();

        session.Disconnect();
    }

    [Fact]
    public void Dispose_AfterDisconnect_IsIdempotent()
    {
        var session = new SshShellSession();

        session.Disconnect();
        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenNotConnected()
    {
        using var session = new SshShellSession();

        Assert.False(session.IsConnected);
    }

    [Fact]
    public void Dispose_DoesNotBlockBeyondTotalWait()
    {
        var session = new SshShellSession();
        var stopwatch = Stopwatch.StartNew();

        session.Dispose();

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Dispose took {stopwatch.ElapsedMilliseconds} ms on an unconnected session.");
    }

    [Fact]
    public void Resize_OnDisposedSession_Throws()
    {
        var session = new SshShellSession();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Resize(80, 24));
    }

    [Fact]
    public void Write_OnDisposedSession_Throws()
    {
        var session = new SshShellSession();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Write("x"));
    }
}
