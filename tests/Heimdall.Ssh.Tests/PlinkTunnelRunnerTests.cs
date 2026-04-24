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
        var keyPath = Path.GetTempFileName();

        try
        {
            var args = _runner.BuildArguments(
                "gw.test", 2222, "user", keyPath, null,
                "remote", 22, 10022);

            Assert.Contains("-i", args);
            int keyIndex = args.IndexOf("-i");
            Assert.Equal(keyPath, args[keyIndex + 1]);
        }
        finally
        {
            File.Delete(keyPath);
        }
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
    public void BuildArguments_ContainsBatchFlag()
    {
        // -batch prevents interactive prompts; safe because -hostkey is
        // passed from TOFU store for known hosts, and unknown hosts fail
        // deterministically instead of hanging.
        var keyPath = Path.GetTempFileName();

        try
        {
            var args = _runner.BuildArguments(
                "gw.test", 22, "user", keyPath, "pass",
                "remote", 22, 10022);

            Assert.Contains("-batch", args);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public void BuildArguments_WithHostKey_IncludesHostKeyFlag()
    {
        var args = _runner.BuildArguments(
            "gw.test", 22, "user", null, null,
            "remote", 22, 10022, "SHA256:abc123");

        Assert.Contains("-hostkey", args);
        var idx = args.IndexOf("-hostkey");
        Assert.Equal("SHA256:abc123", args[idx + 1]);
    }

    [Fact]
    public void BuildArguments_WithoutHostKey_OmitsHostKeyFlag()
    {
        var args = _runner.BuildArguments(
            "gw.test", 22, "user", null, null,
            "remote", 22, 10022);

        Assert.DoesNotContain("-hostkey", args);
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
    public async Task StartAsync_KeyPathWithKeyPassphrase_ReturnsFailureWithoutStartingProcess()
    {
        var result = await _runner.StartAsync(
            @"C:\nonexistent\plink.exe",
            "gw.test", 22, "user", @"C:\keys\id_rsa.ppk", null,
            "remote", 22, 10022,
            keyPassphrase: "key-passphrase",
            passphraseUnsupportedMessage: "localized plink passphrase unsupported");

        Assert.False(result.Success);
        Assert.Equal(SshFailureCode.PassphraseRequired, result.FailureCode);
        Assert.Equal("localized plink passphrase unsupported", result.ErrorMessage);
        Assert.False(_runner.IsRunning);
    }

    [Fact]
    public void BuildArguments_RejectsKeyPathWithQuoteInjection()
    {
        var ex = Assert.Throws<ArgumentException>(() => _runner.BuildArguments(
            "gw.test", 22, "user", "C:\\keys\\id\" --corrupt.ppk", null,
            "remote", 22, 10022));

        Assert.Contains("Invalid SSH key path", ex.Message);
    }

    [Fact]
    public void BuildArguments_RejectsRelativeKeyPath()
    {
        var ex = Assert.Throws<ArgumentException>(() => _runner.BuildArguments(
            "gw.test", 22, "user", "id.ppk", null,
            "remote", 22, 10022));

        Assert.Contains("must be absolute", ex.Message);
    }

    [Fact]
    public void BuildArguments_RejectsMissingKeyPath()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => _runner.BuildArguments(
            "gw.test", 22, "user", @"C:\nope\does-not-exist.ppk", null,
            "remote", 22, 10022));

        Assert.Contains("SSH key file not found", ex.Message);
    }

    [Fact]
    public void CreateStartInfo_UsesArgumentListForValidInputs()
    {
        var keyPath = Path.GetTempFileName();

        try
        {
            var args = _runner.BuildArguments(
                "gateway.example.com", 22, "user", keyPath, null,
                "target.internal", 3389, 13389, "SHA256:abc123");
            var psi = _runner.CreateStartInfo(@"C:\tools\plink.exe", args);

            Assert.Equal(@"C:\tools\plink.exe", psi.FileName);
            Assert.Contains("-i", psi.ArgumentList);
            Assert.Contains(keyPath, psi.ArgumentList);
            Assert.Contains("-hostkey", psi.ArgumentList);
            Assert.Contains("SHA256:abc123", psi.ArgumentList);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public void Stop_BeforeStart_DoesNotThrow()
    {
        _runner.Stop();
    }

    [Fact]
    public void SanitizeForLog_StripsControlChars_PreservesPrintable()
    {
        var sanitized = PlinkTunnelRunner.SanitizeForLog($"banner\r\nok{(char)127}");

        Assert.Equal("banner??ok?", sanitized);
    }

    [Fact]
    public void SanitizeForLog_TruncatesAtCap_AppendsEllipsis()
    {
        var line = new string('x', 260);

        var sanitized = PlinkTunnelRunner.SanitizeForLog(line);

        Assert.Equal(new string('x', 256) + " [...]", sanitized);
    }

    [Fact]
    public void SanitizeForLog_PreservesTab()
    {
        var sanitized = PlinkTunnelRunner.SanitizeForLog("left\tright");

        Assert.Equal("left\tright", sanitized);
    }

    [Fact]
    public void SanitizeForLog_OnNullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PlinkTunnelRunner.SanitizeForLog(null));
        Assert.Equal(string.Empty, PlinkTunnelRunner.SanitizeForLog(string.Empty));
    }
}
