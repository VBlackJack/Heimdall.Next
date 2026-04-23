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

using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class LocalFileBrowserViewTests
{
    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("wsl.exe")]
    [InlineData(@"C:\scripts\open.ps1")]
    public void TryCreateEditorStartInfo_ShellTargetsAreRejected(string editorPath)
    {
        var created = LocalFileBrowserView.TryCreateEditorStartInfo(
            editorPath,
            @"C:\Temp\report.txt",
            out var processStartInfo,
            out var rejectionKey);

        Assert.False(created);
        Assert.Null(processStartInfo);
        Assert.Equal("EditorRejectedShellTarget", rejectionKey);
    }

    [Fact]
    public void TryCreateEditorStartInfo_RegularExecutablePreservesSingleFileArgument()
    {
        var created = LocalFileBrowserView.TryCreateEditorStartInfo(
            @"C:\Program Files\Notepad++\notepad++.exe",
            @"C:\Temp\Quarterly Report.txt",
            out var processStartInfo,
            out var rejectionKey);

        Assert.True(created);
        Assert.NotNull(processStartInfo);
        Assert.Null(rejectionKey);
        Assert.Equal(@"C:\Program Files\Notepad++\notepad++.exe", processStartInfo!.FileName);
        Assert.False(processStartInfo.UseShellExecute);
        Assert.Single(processStartInfo.ArgumentList);
        Assert.Equal(@"C:\Temp\Quarterly Report.txt", processStartInfo.ArgumentList[0]);
        Assert.Equal(string.Empty, processStartInfo.Arguments);
    }
}
