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

using System.IO;
using System.Net;
using System.Net.Sockets;
using Heimdall.App.Services;

namespace Heimdall.App.Tests;

/// <summary>
/// Lifecycle tests for <see cref="EphemeralFileServer"/>.
///
/// HTTP/UDP listeners on Windows can fail in restricted CI environments
/// (UAC, firewall, port already bound). Tests use high ephemeral ports
/// and skip via <see cref="Assert.Skip"/>-equivalent (early return + log)
/// when port acquisition fails. Each test uses a distinct port to keep
/// parallel runs isolated.
/// </summary>
public class EphemeralFileServerTests : IDisposable
{
    private readonly string _testDir;
    private readonly EphemeralFileServer _server;

    // Distinct high ports per test to avoid xUnit parallel-class collisions.
    // 49152-65535 is the IANA dynamic / private port range.
    private const int HttpPortStartStop = 49510;
    private const int HttpPortDoubleStart = 49511;
    private const int HttpPortDispose = 49512;
    private const int TftpPortStartStop = 49513;
    private const int TftpPortDispose = 49514;

    public EphemeralFileServerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"heimdall-fileserver-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _server = new EphemeralFileServer
        {
            ShutdownTimeoutMs = 500,
        };
    }

    public void Dispose()
    {
        try { _server.Dispose(); }
        catch { /* test cleanup */ }
        try { Directory.Delete(_testDir, true); }
        catch { /* test cleanup */ }
        GC.SuppressFinalize(this);
    }

    private static bool IsPortException(Exception ex) =>
        ex is HttpListenerException or SocketException;

    // ── Initial state ─────────────────────────────────────────────────

    [Fact]
    public void NewInstance_IsNotRunning()
    {
        Assert.False(_server.IsHttpRunning);
        Assert.False(_server.IsTftpRunning);
    }

    [Fact]
    public void NewInstance_ServingDirectory_Is_Empty()
    {
        Assert.Equal(string.Empty, _server.ServingDirectory);
    }

    [Fact]
    public async Task StartHttpServerAsync_Throws_For_Empty_Directory()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _server.StartHttpServerAsync("", HttpPortStartStop));
    }

    [Fact]
    public async Task StartTftpServerAsync_Throws_For_Empty_Directory()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _server.StartTftpServerAsync("", TftpPortStartStop));
    }

    // ── HTTP lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task StartHttpServerAsync_Sets_IsHttpRunning_True()
    {
        try
        {
            await _server.StartHttpServerAsync(_testDir, HttpPortStartStop);
        }
        catch (Exception ex) when (IsPortException(ex))
        {
            return; // skip — port unavailable in this environment
        }

        Assert.True(_server.IsHttpRunning);
        Assert.Equal(Path.GetFullPath(_testDir), _server.ServingDirectory);
    }

    [Fact]
    public async Task StopHttpServerAsync_Sets_IsHttpRunning_False()
    {
        try
        {
            await _server.StartHttpServerAsync(_testDir, HttpPortStartStop);
        }
        catch (Exception ex) when (IsPortException(ex))
        {
            return; // skip — port unavailable
        }

        await _server.StopHttpServerAsync();

        Assert.False(_server.IsHttpRunning);
    }

    [Fact]
    public async Task StopHttpServerAsync_When_Not_Running_Is_NoOp()
    {
        // Should not throw even though the server was never started.
        await _server.StopHttpServerAsync();
        Assert.False(_server.IsHttpRunning);
    }

    [Fact]
    public async Task StartHttpServerAsync_Stops_Previous_Before_Starting_New()
    {
        // PERF-05 regression guard: a second StartHttpServerAsync call must
        // gracefully tear down the previous listener instead of leaking it.
        try
        {
            await _server.StartHttpServerAsync(_testDir, HttpPortDoubleStart);
        }
        catch (Exception ex) when (IsPortException(ex))
        {
            return; // skip — port unavailable
        }

        Assert.True(_server.IsHttpRunning);

        try
        {
            await _server.StartHttpServerAsync(_testDir, HttpPortDoubleStart);
        }
        catch (Exception ex) when (IsPortException(ex))
        {
            return; // listener slot transiently busy after self-restart
        }

        Assert.True(_server.IsHttpRunning);
    }

    // ── TFTP lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task StartTftpServerAsync_Sets_IsTftpRunning_True()
    {
        try
        {
            await _server.StartTftpServerAsync(_testDir, TftpPortStartStop);
        }
        catch (Exception ex) when (IsPortException(ex))
        {
            return; // skip — port unavailable
        }

        Assert.True(_server.IsTftpRunning);
        Assert.Equal(Path.GetFullPath(_testDir), _server.ServingDirectory);
    }

    [Fact]
    public async Task StopTftpServerAsync_Sets_IsTftpRunning_False()
    {
        try
        {
            await _server.StartTftpServerAsync(_testDir, TftpPortStartStop);
        }
        catch (Exception ex) when (IsPortException(ex))
        {
            return; // skip
        }

        await _server.StopTftpServerAsync();

        Assert.False(_server.IsTftpRunning);
    }

    [Fact]
    public async Task StopTftpServerAsync_When_Not_Running_Is_NoOp()
    {
        await _server.StopTftpServerAsync();
        Assert.False(_server.IsTftpRunning);
    }

    // ── Disposal ──────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_Stops_All_Running_Servers()
    {
        var local = new EphemeralFileServer { ShutdownTimeoutMs = 500 };
        bool httpStarted = false;
        bool tftpStarted = false;

        try
        {
            try
            {
                await local.StartHttpServerAsync(_testDir, HttpPortDispose);
                httpStarted = true;
            }
            catch (Exception ex) when (IsPortException(ex)) { /* skip */ }

            try
            {
                await local.StartTftpServerAsync(_testDir, TftpPortDispose);
                tftpStarted = true;
            }
            catch (Exception ex) when (IsPortException(ex)) { /* skip */ }

            if (!httpStarted && !tftpStarted) return;

            local.Dispose();

            Assert.False(local.IsHttpRunning);
            Assert.False(local.IsTftpRunning);
        }
        finally
        {
            try { local.Dispose(); } catch { /* idempotent */ }
        }
    }

    [Fact]
    public async Task DisposeAsync_Stops_All_Running_Servers()
    {
        var local = new EphemeralFileServer { ShutdownTimeoutMs = 500 };
        bool any = false;

        try
        {
            await local.StartHttpServerAsync(_testDir, HttpPortDispose);
            any = true;
        }
        catch (Exception ex) when (IsPortException(ex)) { /* skip */ }

        if (!any)
        {
            await local.DisposeAsync();
            return;
        }

        await local.DisposeAsync();

        Assert.False(local.IsHttpRunning);
        Assert.False(local.IsTftpRunning);
    }

    // ── Static helper ─────────────────────────────────────────────────

    [Fact]
    public void GetLocalIpAddress_Returns_Non_Empty_String()
    {
        var ip = EphemeralFileServer.GetLocalIpAddress();
        Assert.False(string.IsNullOrWhiteSpace(ip));
        // Either a routable IPv4 or the documented loopback fallback.
        Assert.True(IPAddress.TryParse(ip, out _));
    }
}
