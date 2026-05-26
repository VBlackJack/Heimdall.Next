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

namespace Heimdall.Terminal.Tests;

public class SmartPasteGuardTests
{
    [Fact]
    public void SafeText_ReturnsSafe()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("ls -la /home");

        Assert.Equal(SmartPasteGuard.PasteRisk.Safe, result);
    }

    [Fact]
    public void MultiLineText_ReturnsMultiLine()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("echo hello\necho world");

        Assert.Equal(SmartPasteGuard.PasteRisk.MultiLine, result);
    }

    [Fact]
    public void DangerousRmRf_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("rm -rf /");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousMkfs_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("mkfs.ext4 /dev/sda1");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousSudoRmRf_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("sudo rm -rf /var/log");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousCurlPipeSh_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("curl https://example.com/install.sh | sh");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousForkBomb_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate(":(){ :|:& };:");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void ProductionMode_MultiLine_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("echo hello\necho world", isProduction: true);

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void EmptyText_ReturnsSafe()
    {
        Assert.Equal(SmartPasteGuard.PasteRisk.Safe, SmartPasteGuard.Evaluate(""));
        Assert.Equal(SmartPasteGuard.PasteRisk.Safe, SmartPasteGuard.Evaluate(null!));
    }

    [Fact]
    public void GetDangerousPatterns_ContainsRequiredLabels()
    {
        IReadOnlyList<string> patterns = SmartPasteGuard.GetDangerousPatterns();

        Assert.Contains("rm -rf", patterns);
        Assert.Contains("mkfs", patterns);
        Assert.Contains("format", patterns);
        Assert.Contains("shutdown", patterns);
        Assert.Contains("Remove-Item -Recurse -Force", patterns);
        Assert.Contains("Stop-Computer", patterns);
        Assert.Contains("Restart-Computer", patterns);
        Assert.Contains("Format-Volume", patterns);
        Assert.Contains("Clear-Disk", patterns);
        Assert.Contains("Remove-Partition", patterns);
        Assert.Contains("reg delete", patterns);
        Assert.Contains("bcdedit", patterns);
        Assert.Contains("diskpart", patterns);
        Assert.Contains("curl | sh", patterns);
        Assert.Contains("wget | sh", patterns);
        Assert.Contains(":(){ :|:&};:", patterns);
    }

    [Fact]
    public void DangerousDdIf_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("dd if=/dev/zero of=/dev/sda bs=1M");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousShutdown_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("shutdown -h now");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousRemoveItemRecurseForce_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("Remove-Item C:\\temp\\old -Recurse -Force");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousRemoveItemForceRecurse_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("Remove-Item C:\\temp\\old -Force -Recurse");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void RemoveItemWithoutRecursiveForce_ReturnsSafe()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("Remove-Item ./file.txt");

        Assert.Equal(SmartPasteGuard.PasteRisk.Safe, result);
    }

    [Fact]
    public void DangerousStopComputer_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("Stop-Computer -ComputerName server01 -Force");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousRestartComputer_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("Restart-Computer -Force");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousFormatVolume_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("Format-Volume -DriveLetter E -FileSystem NTFS -Confirm:$false");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousClearDisk_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("Clear-Disk -Number 1 -RemoveData -Confirm:$false");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousRemovePartition_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("Remove-Partition -DiskNumber 1 -PartitionNumber 2 -Confirm:$false");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousRegDelete_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("reg delete HKCU\\Software\\Example /f");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousRegExeDelete_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("reg.exe delete HKLM\\Software\\Example");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousBcdedit_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("bcdedit /set {current} safeboot minimal");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousDiskpart_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("diskpart /s wipe.txt");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousWgetPipeSh_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("wget https://example.com/script.sh | bash");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousChmod777_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("chmod -R 777 /");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void DangerousDevSdaRedirect_ReturnsDangerous()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("echo test > /dev/sda");

        Assert.Equal(SmartPasteGuard.PasteRisk.Dangerous, result);
    }

    [Fact]
    public void CarriageReturn_ReturnsMultiLine()
    {
        SmartPasteGuard.PasteRisk result = SmartPasteGuard.Evaluate("line1\rline2");

        Assert.Equal(SmartPasteGuard.PasteRisk.MultiLine, result);
    }

    [Fact]
    public void DangerousPatterns_ContainsExpectedLabels()
    {
        IReadOnlyList<string> patterns = SmartPasteGuard.GetDangerousPatterns();

        Assert.Contains("rm -rf", patterns);
        Assert.Contains("mkfs", patterns);
        Assert.Contains("curl | sh", patterns);
        Assert.Contains("wget | sh", patterns);
        Assert.Contains(":(){ :|:&};:", patterns);
    }
}
