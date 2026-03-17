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

using Heimdall.Terminal;

namespace Heimdall.Ssh.Tests;

public class SmartPasteGuardTests
{
    [Fact]
    public void SafeText_ReturnsSafe()
    {
        var result = SmartPasteGuard.Evaluate("ls -la /home");

        Assert.Equal(SmartPasteGuard.PasteRisk.Safe, result);
    }

    [Fact]
    public void MultiLineText_ReturnsMultiLine()
    {
        var result = SmartPasteGuard.Evaluate("echo hello\necho world");

        Assert.Equal(SmartPasteGuard.PasteRisk.MultiLine, result);
    }

    [Fact]
    public void DangerousRmRf_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("rm -rf /");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousMkfs_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("mkfs.ext4 /dev/sda1");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousSudoRmRf_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("sudo rm -rf /var/log");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousCurlPipeSh_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("curl https://example.com/install.sh | sh");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousForkBomb_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate(":(){ :|:& };:");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void ProductionMode_MultiLine_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("echo hello\necho world", isProduction: true);

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void EmptyText_ReturnsSafe()
    {
        Assert.Equal(SmartPasteGuard.PasteRisk.Safe, SmartPasteGuard.Evaluate(""));
        Assert.Equal(SmartPasteGuard.PasteRisk.Safe, SmartPasteGuard.Evaluate(null!));
    }

    [Fact]
    public void GetDangerousPatterns_Returns16Items()
    {
        var patterns = SmartPasteGuard.GetDangerousPatterns();

        Assert.Equal(16, patterns.Count);
    }

    [Fact]
    public void DangerousDdIf_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("dd if=/dev/zero of=/dev/sda bs=1M");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousShutdown_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("shutdown -h now");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousWgetPipeSh_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("wget https://example.com/script.sh | bash");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousChmod777_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("chmod -R 777 /");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousDevSdaRedirect_ReturnsDangerous()
    {
        var result = SmartPasteGuard.Evaluate("echo test > /dev/sda");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void CarriageReturn_ReturnsMultiLine()
    {
        var result = SmartPasteGuard.Evaluate("line1\rline2");

        Assert.Equal(SmartPasteGuard.PasteRisk.MultiLine, result);
    }

    [Fact]
    public void DangerousPatterns_ContainsExpectedLabels()
    {
        var patterns = SmartPasteGuard.GetDangerousPatterns();

        Assert.Contains("rm -rf", patterns);
        Assert.Contains("mkfs", patterns);
        Assert.Contains("curl | sh", patterns);
        Assert.Contains("wget | sh", patterns);
        Assert.Contains(":(){ :|:&};:", patterns);
    }
}
