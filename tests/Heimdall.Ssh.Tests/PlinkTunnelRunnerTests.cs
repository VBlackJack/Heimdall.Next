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

using Heimdall.Ssh.Plink;

namespace Heimdall.Ssh.Tests;

/// <summary>
/// Tests for <see cref="PlinkTunnelRunner"/> argument building and lifecycle.
/// </summary>
public class PlinkTunnelRunnerTests : IDisposable
{
    private readonly PlinkTunnelRunner _runner = new();

    public void Dispose()
    {
        _runner.Dispose();
    }

    // ── Argument building ────────────────────────────────────────────

    [Fact]
    public void BuildArguments_BasicTunnel_ContainsRequiredFlags()
    {
        var args = _runner.BuildArguments(
            "gateway.example.com", 22, "admin", null, null,
            "target.internal", 3389, 13389);

        Assert.Contains("-ssh", args);
        Assert.Contains("-N", args);
        Assert.Contains("-L", args);
        Assert.Contains("13389:target.internal:3389", args);
        Assert.Contains("-P", args);
        Assert.Contains("22", args);
        Assert.Contains("admin@gateway.example.com", args);
    }

    [Fact]
    public void BuildArguments_WithKeyPath_IncludesKeyFlag()
    {
        var args = _runner.BuildArguments(
            "gw.test", 2222, "user", @"C:\keys\id_rsa.ppk", null,
            "remote", 22, 10022);

        Assert.Contains("-i", args);
        int keyIndex = args.IndexOf("-i");
        Assert.Contains(@"C:\keys\id_rsa.ppk", args[keyIndex + 1]);
    }

    [Fact]
    public void BuildArguments_WithPassword_UsesPwfile()
    {
        var args = _runner.BuildArguments(
            "gw.test", 22, "user", null, "s3cret",
            "remote", 22, 10022);

        Assert.Contains("-pwfile", args);
        Assert.DoesNotContain("-pw", args.Where(a => a == "-pw"));
    }

    [Fact]
    public void BuildArguments_NoKeyNoPassword_NoAuthFlags()
    {
        var args = _runner.BuildArguments(
            "gw.test", 22, "user", null, null,
            "remote", 22, 10022);

        Assert.DoesNotContain("-i", args);
        Assert.DoesNotContain("-pwfile", args);
        Assert.DoesNotContain("-pw", args);
    }

    [Fact]
    public void BuildArguments_NeverContainsBatchFlag()
    {
        // -batch breaks all tunnel creation (documented in MEMORY.md)
        var args = _runner.BuildArguments(
            "gw.test", 22, "user", @"C:\key.ppk", "pass",
            "remote", 22, 10022);

        Assert.DoesNotContain("-batch", args);
    }

    [Fact]
    public void BuildArguments_CustomPort_IncludedCorrectly()
    {
        var args = _runner.BuildArguments(
            "gw.test", 2222, "user", null, null,
            "remote", 3389, 13389);

        int portIndex = args.IndexOf("-P");
        Assert.NotEqual(-1, portIndex);
        Assert.Equal("2222", args[portIndex + 1]);
    }

    // ── Lifecycle ────────────────────────────────────────────────────

    [Fact]
    public void IsRunning_BeforeStart_ReturnsFalse()
    {
        Assert.False(_runner.IsRunning);
    }

    [Fact]
    public void ProcessId_BeforeStart_ReturnsNull()
    {
        Assert.Null(_runner.ProcessId);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _runner.Dispose();
        _runner.Dispose();
    }

    [Fact]
    public async Task StartAsync_PlinkNotFound_ReturnsFailure()
    {
        var result = await _runner.StartAsync(
            @"C:\nonexistent\plink.exe",
            "gw.test", 22, "user", null, null,
            "remote", 22, 10022);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public void Stop_BeforeStart_DoesNotThrow()
    {
        _runner.Stop();
    }
}
