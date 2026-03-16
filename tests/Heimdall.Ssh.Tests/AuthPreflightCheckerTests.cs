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

namespace Heimdall.Ssh.Tests;

public class AuthPreflightCheckerTests
{
    private static SshConnectionParams MakeParams(
        string? keyPath = null,
        string? password = null) =>
        new()
        {
            Host = "server.example.com",
            Username = "testuser",
            KeyPath = keyPath,
            Password = password
        };

    // ── Key file existence ─────────────────────────────────────────────

    [Fact]
    public void Check_KeyFileNotFound_ReturnsFailure()
    {
        var connParams = MakeParams(keyPath: @"C:\nonexistent\key.pem");

        var result = AuthPreflightChecker.Check(connParams);

        Assert.False(result.Success);
        Assert.Equal(SshFailureCode.KeyFileNotFound, result.FailureCode);
        Assert.Contains("key.pem", result.Message);
    }

    [Fact]
    public void Check_KeyFileExists_ReturnsOk()
    {
        // Use a file that exists on any Windows system
        var connParams = MakeParams(keyPath: @"C:\Windows\System32\drivers\etc\hosts");

        var result = AuthPreflightChecker.Check(connParams);

        Assert.True(result.Success);
    }

    // ── No key, no password, interactive mode ──────────────────────────

    [Fact]
    public void Check_NoKeyNoPassword_InteractiveMode_ReturnsOk()
    {
        // Interactive mode does not require Pageant (user can type password)
        var connParams = MakeParams();

        var result = AuthPreflightChecker.Check(connParams, isTunnelMode: false);

        Assert.True(result.Success);
    }

    // ── Password-only auth ─────────────────────────────────────────────

    [Fact]
    public void Check_PasswordOnly_TunnelMode_ReturnsOk()
    {
        var connParams = MakeParams(password: "mysecret");

        var result = AuthPreflightChecker.Check(connParams, isTunnelMode: true);

        Assert.True(result.Success);
    }

    // ── Key + password (passphrase) ────────────────────────────────────

    [Fact]
    public void Check_KeyWithPassword_TunnelMode_ReturnsOk()
    {
        var connParams = MakeParams(
            keyPath: @"C:\Windows\System32\drivers\etc\hosts",
            password: "passphrase");

        var result = AuthPreflightChecker.Check(connParams, isTunnelMode: true);

        Assert.True(result.Success);
    }

    // ── Null params ────────────────────────────────────────────────────

    [Fact]
    public void Check_NullParams_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => AuthPreflightChecker.Check(null!));
    }

    // ── PreflightResult factory methods ────────────────────────────────

    [Fact]
    public void PreflightResult_Ok_HasCorrectValues()
    {
        var result = PreflightResult.Ok();

        Assert.True(result.Success);
        Assert.Null(result.FailureCode);
        Assert.Null(result.Message);
    }

    [Fact]
    public void PreflightResult_Fail_HasCorrectValues()
    {
        var result = PreflightResult.Fail(SshFailureCode.KeyFileNotFound, "Not found");

        Assert.False(result.Success);
        Assert.Equal(SshFailureCode.KeyFileNotFound, result.FailureCode);
        Assert.Equal("Not found", result.Message);
    }
}
