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
using Heimdall.Sftp;

namespace Heimdall.App.Tests;

public sealed class ResolveEditorPathTests
{
    [Fact]
    public void NullInput_ReturnsNotepad()
    {
        var result = RemoteFileEditor.ResolveEditorPath(null);

        AssertResolvedToNotepad(result);
    }

    [Fact]
    public void EmptyInput_ReturnsNotepad()
    {
        var result = RemoteFileEditor.ResolveEditorPath(string.Empty);

        AssertResolvedToNotepad(result);
    }

    [Fact]
    public void WhitespaceInput_ReturnsNotepad()
    {
        var result = RemoteFileEditor.ResolveEditorPath("   ");

        AssertResolvedToNotepad(result);
    }

    [Fact]
    public void ExplicitNotepad_ReturnsAbsoluteSystemPath()
    {
        var result = RemoteFileEditor.ResolveEditorPath("notepad.exe");

        AssertResolvedToNotepad(result);
    }

    [Fact]
    public void CustomEditorPath_IsReturnedUnchanged()
    {
        const string custom = @"C:\Program Files\Notepad++\notepad++.exe";

        Assert.Equal(custom, RemoteFileEditor.ResolveEditorPath(custom));
    }

    [Fact]
    public void CustomEditorPath_TrimsWhitespace()
    {
        const string custom = @"C:\Program Files\VS Code\Code.exe";

        Assert.Equal(custom, RemoteFileEditor.ResolveEditorPath("  " + custom + "  "));
    }

    private static void AssertResolvedToNotepad(string actual)
    {
        if (OperatingSystem.IsWindows())
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "notepad.exe");
            Assert.Equal(expected, actual);
        }
        else
        {
            Assert.Equal("notepad.exe", actual);
        }
    }
}
