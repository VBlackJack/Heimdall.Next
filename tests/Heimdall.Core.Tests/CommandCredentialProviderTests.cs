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

using System.Runtime.Versioning;
using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

[SupportedOSPlatform("windows")]
public class CommandCredentialProviderTests
{
    // ---------------------------------------------------------------
    // Constructor & IsAvailable
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_NullCommand_DoesNotThrow()
    {
        var provider = new CommandCredentialProvider(null, null);
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Constructor_EmptyCommand_IsNotAvailable()
    {
        var provider = new CommandCredentialProvider("", null);
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Constructor_WhitespaceCommand_IsNotAvailable()
    {
        var provider = new CommandCredentialProvider("   ", null);
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Constructor_ValidCommand_IsAvailable()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo hello", null);
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public void Name_ReturnsCommand()
    {
        var provider = new CommandCredentialProvider("anything", null);
        Assert.Equal("Command", provider.Name);
    }

    // ---------------------------------------------------------------
    // GetCredentialAsync — unavailable provider
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_WhenNotAvailable_ReturnsNull()
    {
        var provider = new CommandCredentialProvider(null, null);
        var result = await provider.GetCredentialAsync("host", 22, "user", "title");
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Placeholder substitution
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_SubstitutesHostPlaceholder()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo {Host}", null);
        var result = await provider.GetCredentialAsync("myserver.local", 22, "admin", "MyServer");

        Assert.NotNull(result);
        Assert.Equal("myserver.local", result.Password);
    }

    [Fact]
    public async Task GetCredentialAsync_SubstitutesPortPlaceholder()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo {Port}", null);
        var result = await provider.GetCredentialAsync("host", 2222, "user", "title");

        Assert.NotNull(result);
        Assert.Equal("2222", result.Password);
    }

    [Fact]
    public async Task GetCredentialAsync_SubstitutesUserPlaceholder()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo {User}", null);
        var result = await provider.GetCredentialAsync("host", 22, "testuser", "title");

        Assert.NotNull(result);
        Assert.Equal("testuser", result.Password);
    }

    [Fact]
    public async Task GetCredentialAsync_SubstitutesTitlePlaceholder()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo {Title}", null);
        var result = await provider.GetCredentialAsync("host", 22, "user", "Production-DB");

        Assert.NotNull(result);
        Assert.Equal("Production-DB", result.Password);
    }

    [Fact]
    public async Task GetCredentialAsync_SubstitutesDatabasePlaceholder()
    {
        var provider = new CommandCredentialProvider(
            "cmd.exe /c echo {Database}", @"C:\vault\passwords.kdbx");
        var result = await provider.GetCredentialAsync("host", 22, "user", "title");

        Assert.NotNull(result);
        Assert.Contains("passwords.kdbx", result.Password);
    }

    [Fact]
    public async Task GetCredentialAsync_PlaceholderSubstitution_CaseInsensitive()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo {host}", null);
        var result = await provider.GetCredentialAsync("casetest", 22, "user", "title");

        Assert.NotNull(result);
        Assert.Equal("casetest", result.Password);
    }

    // ---------------------------------------------------------------
    // Command execution — happy path
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_ReturnsCommandOutput()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo test-password", null);
        var result = await provider.GetCredentialAsync("host", 22, "user", "title");

        Assert.NotNull(result);
        Assert.Equal("test-password", result.Password);
    }

    [Fact]
    public async Task GetCredentialAsync_PreservesUsername()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo secret", null);
        var result = await provider.GetCredentialAsync("host", 22, "admin", "title");

        Assert.NotNull(result);
        Assert.Equal("admin", result.Username);
    }

    [Fact]
    public async Task GetCredentialAsync_NullUsername_ReturnsEmpty()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo secret", null);
        var result = await provider.GetCredentialAsync("host", 22, null, "title");

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Username);
    }

    // ---------------------------------------------------------------
    // Output trimming
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_TrimsWhitespaceFromOutput()
    {
        // cmd.exe /c echo adds a trailing newline; verify it's trimmed
        var provider = new CommandCredentialProvider("cmd.exe /c echo   padded-output  ", null);
        var result = await provider.GetCredentialAsync("host", 22, "user", "title");

        Assert.NotNull(result);
        Assert.Equal("padded-output", result.Password);
    }

    // ---------------------------------------------------------------
    // Empty output
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_EmptyOutput_ReturnsNull()
    {
        // "type nul" outputs nothing on Windows
        var provider = new CommandCredentialProvider("cmd.exe /c type nul", null);
        var result = await provider.GetCredentialAsync("host", 22, "user", "title");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Non-zero exit code
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_NonZeroExitCode_ReturnsNull()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c exit 1", null);
        var result = await provider.GetCredentialAsync("host", 22, "user", "title");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Timeout
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_Timeout_ThrowsOperationCanceled()
    {
        // Use a short cancellation token to simulate timeout faster than the 10s default
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // ping -n 30 ensures the process runs long enough to be canceled
        var provider = new CommandCredentialProvider("cmd.exe /c ping -n 30 127.0.0.1", null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetCredentialAsync("host", 22, "user", "title", cts.Token));
    }

    // ---------------------------------------------------------------
    // CancellationToken respected
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_AlreadyCancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new CommandCredentialProvider("cmd.exe /c echo should-not-run", null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetCredentialAsync("host", 22, "user", "title", cts.Token));
    }

    // ---------------------------------------------------------------
    // Invalid executable
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_InvalidExecutable_ReturnsNull()
    {
        var provider = new CommandCredentialProvider(
            "nonexistent-binary-xyz --get-password", null);
        var result = await provider.GetCredentialAsync("host", 22, "user", "title");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Sanitization — shell metacharacters stripped
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_SanitizesShellMetachars()
    {
        // The semicolon in the host should be stripped by SanitizeArgValue
        var provider = new CommandCredentialProvider("cmd.exe /c echo {Host}", null);
        var result = await provider.GetCredentialAsync("safe;injected", 22, "user", "title");

        Assert.NotNull(result);
        Assert.DoesNotContain(";", result.Password);
        Assert.Equal("safeinjected", result.Password);
    }

    // ---------------------------------------------------------------
    // Multiple placeholders in one command
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetCredentialAsync_MultiPlaceholders_AllSubstituted()
    {
        var provider = new CommandCredentialProvider(
            "cmd.exe /c echo {User}@{Host}:{Port}", null, timeoutMs: 60000);
        var result = await provider.GetCredentialAsync("server1", 3306, "dbadmin", "title");

        Assert.NotNull(result);
        Assert.Equal("dbadmin@server1:3306", result.Password);
    }

    // ---------------------------------------------------------------
    // Context-aware sanitization via ExpandTemplate (internal)
    // ---------------------------------------------------------------

    [Fact]
    public void ExpandTemplate_ShellTarget_StripsParensAndPercent()
    {
        var provider = new CommandCredentialProvider("cmd.exe /c echo {Title}", null);
        var expanded = provider.ExpandTemplate(
            "cmd.exe /c echo {Title}", "host", 22, "user", "Web (prod)");
        Assert.Equal("cmd.exe /c echo Web prod", expanded);
    }

    [Fact]
    public void ExpandTemplate_ShellTarget_StripsSingleQuotes()
    {
        var provider = new CommandCredentialProvider("pwsh.exe -c echo {Title}", null);
        var expanded = provider.ExpandTemplate(
            "pwsh.exe -c echo {Title}", "host", 22, "user", "John's Server");
        Assert.Equal("pwsh.exe -c echo Johns Server", expanded);
    }

    [Fact]
    public void ExpandTemplate_RegularExe_PreservesParens()
    {
        var provider = new CommandCredentialProvider("keepassxc-cli.exe show {Title}", null);
        var expanded = provider.ExpandTemplate(
            "keepassxc-cli.exe show {Title}", "host", 22, "user", "Web (prod)");
        Assert.Equal("keepassxc-cli.exe show Web (prod)", expanded);
    }

    [Fact]
    public void ExpandTemplate_RegularExe_PreservesSingleQuotes()
    {
        var provider = new CommandCredentialProvider("bw.exe get password {Title}", null);
        var expanded = provider.ExpandTemplate(
            "bw.exe get password {Title}", "host", 22, "user", "John's Server");
        Assert.Equal("bw.exe get password John's Server", expanded);
    }

    [Fact]
    public void ExpandTemplate_RegularExe_PreservesPercent()
    {
        var provider = new CommandCredentialProvider(
            "keepassxc-cli.exe show {Database} {Title}", "%AppData%\\db.kdbx");
        var expanded = provider.ExpandTemplate(
            "keepassxc-cli.exe show {Database} {Title}",
            "host", 22, "user", "entry1");
        Assert.Equal("keepassxc-cli.exe show %AppData%\\db.kdbx entry1", expanded);
    }

    [Fact]
    public void ExpandTemplate_RegularExe_StillStripsSemicolon()
    {
        var provider = new CommandCredentialProvider("op.exe get {Host}", null);
        var expanded = provider.ExpandTemplate(
            "op.exe get {Host}", "host;injected", 22, "user", "title");
        Assert.Equal("op.exe get hostinjected", expanded);
    }

    [Fact]
    public void ExpandTemplate_RegularExe_StillStripsDoubleQuotes()
    {
        var provider = new CommandCredentialProvider("bw.exe get {Title}", null);
        var expanded = provider.ExpandTemplate(
            "bw.exe get {Title}", "host", 22, "user", "entry\"injected");
        Assert.Equal("bw.exe get entryinjected", expanded);
    }

    [Fact]
    public void ExpandTemplate_UnknownTemplate_DefaultsToStrict()
    {
        var provider = new CommandCredentialProvider("", null);
        var expanded = provider.ExpandTemplate(
            "", "host(test)", 22, "user", "title");
        // Empty template → IsShellTarget returns true → strict
        Assert.DoesNotContain("(", expanded);
    }
}
