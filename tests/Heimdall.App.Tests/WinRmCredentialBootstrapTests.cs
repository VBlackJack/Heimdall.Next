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
using Heimdall.App.Services.WinRm;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed class WinRmCredentialBootstrapTests
{
    [Fact]
    public void Write_CreatesProtectedBootstrapScriptWithoutPlaintextPassword()
    {
        string? writtenPath = null;
        string? writtenContent = null;
        WinRmCredentialBootstrap bootstrap = new WinRmCredentialBootstrap(
            createScriptPath: () => @"C:\Temp\heimdall_winrm_test.ps1",
            writeAndProtect: (path, content) =>
            {
                writtenPath = path;
                writtenContent = content;
            },
            unprotectStoredPassword: encrypted => encrypted == "stored-password" ? "p@ss'word!" : null,
            protectBootstrapPassword: plaintext => plaintext == "p@ss'word!" ? "dpapi-bootstrap-blob" : "unexpected");

        WinRmCredentialBootstrapResult result = bootstrap.Write(CreateCredentialServer());

        Assert.Equal(@"C:\Temp\heimdall_winrm_test.ps1", result.ScriptPath);
        Assert.Equal(result.ScriptPath, writtenPath);
        Assert.NotNull(writtenContent);
        Assert.Contains("$blob = 'dpapi-bootstrap-blob'", writtenContent, StringComparison.Ordinal);
        Assert.Contains("System.Security.Cryptography.ProtectedData", writtenContent, StringComparison.Ordinal);
        Assert.Contains("[System.Security.Cryptography.ProtectedData]::Unprotect", writtenContent, StringComparison.Ordinal);
        Assert.Contains("[System.Management.Automation.PSCredential]::new('CONTOSO\\operator'", writtenContent, StringComparison.Ordinal);
        Assert.Contains("Enter-PSSession -ComputerName 'server01.contoso.local' -Port 5986 -UseSSL -Credential $credential", writtenContent, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $scriptPath -Force", writtenContent, StringComparison.Ordinal);
        Assert.DoesNotContain("p@ss'word!", writtenContent, StringComparison.Ordinal);
        Assert.DoesNotContain("stored-password", writtenContent, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_QuotesDpapiBlobForPowerShell()
    {
        ServerProfileDto server = CreateCredentialServer();

        string script = WinRmCredentialBootstrap.BuildScript(server, "blob'value");

        Assert.Contains("$blob = 'blob''value'", script, StringComparison.Ordinal);
        Assert.Contains("[System.Management.Automation.PSCredential]::new('CONTOSO\\operator'", script, StringComparison.Ordinal);
        Assert.Contains("-ComputerName 'server01.contoso.local'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenStoredPasswordCannotBeDecrypted_Throws()
    {
        WinRmCredentialBootstrap bootstrap = new WinRmCredentialBootstrap(
            createScriptPath: () => @"C:\Temp\unused.ps1",
            writeAndProtect: (_, _) => throw new InvalidOperationException("should not write"),
            unprotectStoredPassword: _ => null,
            protectBootstrapPassword: plaintext => plaintext);

        Assert.Throws<InvalidOperationException>(() => bootstrap.Write(CreateCredentialServer()));
    }

    [Fact]
    public void Write_WithCurrentUserIdentity_Throws()
    {
        WinRmCredentialBootstrap bootstrap = new WinRmCredentialBootstrap(
            createScriptPath: () => @"C:\Temp\unused.ps1",
            writeAndProtect: (_, _) => throw new InvalidOperationException("should not write"),
            unprotectStoredPassword: _ => "secret",
            protectBootstrapPassword: plaintext => plaintext);
        ServerProfileDto server = CreateCredentialServer();
        server.WinRmIdentityMode = WinRmIdentityMode.CurrentUser;

        Assert.Throws<ArgumentException>(() => bootstrap.Write(server));
    }

    [Fact]
    public void CreateDefaultScriptPath_UsesHeimdallWinRmTempPattern()
    {
        string scriptPath = WinRmCredentialBootstrap.CreateDefaultScriptPath();

        Assert.StartsWith(Path.GetTempPath(), scriptPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("heimdall_winrm_", Path.GetFileName(scriptPath), StringComparison.Ordinal);
        Assert.EndsWith(".ps1", scriptPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Delete_RemovesExistingFile()
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"heimdall_winrm_delete_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, "bootstrap");
        WinRmCredentialBootstrap bootstrap = new WinRmCredentialBootstrap();

        bootstrap.Delete(scriptPath);

        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void Delete_WhenFileIsMissing_DoesNotThrow()
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"heimdall_winrm_missing_{Guid.NewGuid():N}.ps1");
        WinRmCredentialBootstrap bootstrap = new WinRmCredentialBootstrap();

        bootstrap.Delete(scriptPath);
    }

    private static ServerProfileDto CreateCredentialServer()
        => new ServerProfileDto
        {
            ConnectionType = "WINRM",
            RemoteServer = "server01.contoso.local",
            WinRmPort = DefaultPorts.WinRmHttps,
            WinRmUseSsl = true,
            WinRmIdentityMode = WinRmIdentityMode.Credential,
            WinRmUsername = @"CONTOSO\operator",
            WinRmPasswordEncrypted = "stored-password"
        };
}
