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

using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

public class InputValidatorTests
{
    // ── Hostname validation ─────────────────────────────────────────────

    [Theory]
    [InlineData("server.example.com", true)]
    [InlineData("my-host", true)]
    [InlineData("host123", true)]
    [InlineData("a.b.c.d.e", true)]
    [InlineData("-invalid", false)]
    [InlineData("invalid-", false)]
    [InlineData("host..double", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("host;rm -rf /", false)]
    [InlineData("host$(whoami)", false)]
    public void Validate_Hostname_ReturnsExpected(string? value, bool expected)
    {
        Assert.Equal(expected, InputValidator.Validate(value, "Hostname"));
    }

    // ── SshUser validation (CWE-78) ────────────────────────────────────

    [Theory]
    [InlineData("admin", true)]
    [InlineData("deploy_user", true)]
    [InlineData("user.name", true)]
    [InlineData("user@domain", true)]
    [InlineData(@"DOMAIN\user", true)]
    [InlineData("user-name", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("user;echo pwned", false)]
    [InlineData("user$(id)", false)]
    [InlineData("user`whoami`", false)]
    [InlineData("user | cat /etc/passwd", false)]
    public void Validate_SshUser_RejectsInjection(string? value, bool expected)
    {
        Assert.Equal(expected, InputValidator.Validate(value, "SshUser"));
    }

    // ── Username validation (RDP) ───────────────────────────────────────

    [Theory]
    [InlineData("admin", true)]
    [InlineData(@"DOMAIN\user", true)]
    [InlineData("user@domain.com", true)]
    [InlineData("user-name_01", true)]
    [InlineData("user name", false)]
    [InlineData("", false)]
    public void Validate_Username_ReturnsExpected(string? value, bool expected)
    {
        Assert.Equal(expected, InputValidator.Validate(value, "Username"));
    }

    // ── IPv4 validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("256.0.0.1", false)]
    [InlineData("192.168.1", false)]
    [InlineData("not-an-ip", false)]
    public void Validate_IPv4_ReturnsExpected(string? value, bool expected)
    {
        Assert.Equal(expected, InputValidator.Validate(value, "IPv4"));
    }

    // ── Port validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("22", true)]
    [InlineData("3389", true)]
    [InlineData("65535", true)]
    [InlineData("1", true)]
    [InlineData("0", true)]
    [InlineData("99999", true)]
    [InlineData("123456", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    public void Validate_Port_RegexCheck(string? value, bool expected)
    {
        Assert.Equal(expected, InputValidator.Validate(value, "Port"));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(22, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(65536, false)]
    public void ValidatePortRange_ReturnsExpected(int port, bool expected)
    {
        Assert.Equal(expected, InputValidator.ValidatePortRange(port));
    }

    // ── TunnelTarget validation ─────────────────────────────────────────

    [Theory]
    [InlineData("localhost:3389", true)]
    [InlineData("10.0.0.1:22", true)]
    [InlineData("server.local:8080", true)]
    [InlineData("host-only", false)]
    [InlineData(":3389", false)]
    [InlineData("host:", false)]
    public void Validate_TunnelTarget_ReturnsExpected(string? value, bool expected)
    {
        Assert.Equal(expected, InputValidator.Validate(value, "TunnelTarget"));
    }

    // ── SshGateway DNS validation ───────────────────────────────────────

    [Theory]
    [InlineData("gateway.example.com", true)]
    [InlineData("gw-01.corp.local", true)]
    [InlineData("bastion", true)]
    [InlineData("host..invalid", false)]
    [InlineData("host.-invalid", false)]
    [InlineData("-starts-hyphen", false)]
    public void Validate_SshGateway_EnforcesDnsRules(string? value, bool expected)
    {
        Assert.Equal(expected, InputValidator.Validate(value, "SshGateway"));
    }

    [Fact]
    public void Validate_Hostname_RejectsExcessiveFqdnLength()
    {
        // Build a hostname with total length > 255
        var longLabel = new string('a', 63);
        var longHostname = string.Join(".", longLabel, longLabel, longLabel, longLabel, "com");
        // Should be >255 chars total
        Assert.True(longHostname.Length > 255);

        Assert.False(InputValidator.Validate(longHostname, "Hostname"));
    }

    [Fact]
    public void Validate_Hostname_RejectsLabelOver63Chars()
    {
        var longLabel = new string('a', 64);
        var hostname = $"{longLabel}.example.com";

        Assert.False(InputValidator.Validate(hostname, "Hostname"));
    }

    // ── Unknown pattern ─────────────────────────────────────────────────

    [Fact]
    public void Validate_UnknownPattern_ReturnsFalse()
    {
        Assert.False(InputValidator.Validate("some-value", "NonExistentPattern"));
    }

    // ── GetPattern / GetPatternNames ────────────────────────────────────

    [Fact]
    public void GetPattern_KnownPattern_ReturnsNonNull()
    {
        Assert.NotNull(InputValidator.GetPattern("Hostname"));
    }

    [Fact]
    public void GetPattern_UnknownPattern_ReturnsNull()
    {
        Assert.Null(InputValidator.GetPattern("FakePattern"));
    }

    [Fact]
    public void GetPatternNames_ReturnsAllExpectedPatterns()
    {
        var names = InputValidator.GetPatternNames().ToList();

        Assert.Contains("SshGateway", names);
        Assert.Contains("SshUser", names);
        Assert.Contains("Username", names);
        Assert.Contains("Hostname", names);
        Assert.Contains("IPv4", names);
        Assert.Contains("Address", names);
        Assert.Contains("Port", names);
        Assert.Contains("TunnelTarget", names);
    }
}
