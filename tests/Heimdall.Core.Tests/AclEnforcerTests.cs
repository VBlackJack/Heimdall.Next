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
public class AclEnforcerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly List<string> _tempDirs = [];

    private string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heimdall_acl_test_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "test");
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heimdall_acl_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { /* cleanup best-effort */ }
        }
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* cleanup best-effort */ }
        }
    }

    // ── SetFileAcl ─────────────────────────────────────────────────────

    [Fact]
    public void SetFileAcl_OnTempFile_DoesNotThrow()
    {
        var path = CreateTempFile();

        var ex = Record.Exception(() => AclEnforcer.SetFileAcl(path));

        Assert.Null(ex);
    }

    [Fact]
    public void SetFileAcl_OnNonexistentFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.tmp");

        Assert.Throws<FileNotFoundException>(() => AclEnforcer.SetFileAcl(path));
    }

    [Fact]
    public void SetFileAcl_NullPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => AclEnforcer.SetFileAcl(null!));
    }

    [Fact]
    public void SetFileAcl_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AclEnforcer.SetFileAcl(string.Empty));
    }

    // ── SetDirectoryAcl ────────────────────────────────────────────────

    [Fact]
    public void SetDirectoryAcl_OnTempDirectory_DoesNotThrow()
    {
        var path = CreateTempDir();

        var ex = Record.Exception(() => AclEnforcer.SetDirectoryAcl(path));

        Assert.Null(ex);
    }

    [Fact]
    public void SetDirectoryAcl_OnNonexistentDirectory_ThrowsDirectoryNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");

        Assert.Throws<DirectoryNotFoundException>(() => AclEnforcer.SetDirectoryAcl(path));
    }

    [Fact]
    public void SetDirectoryAcl_NullPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => AclEnforcer.SetDirectoryAcl(null!));
    }

    // ── VerifyFileAcl ──────────────────────────────────────────────────

    [Fact]
    public void VerifyFileAcl_ReturnsTrueAfterSetFileAcl()
    {
        var path = CreateTempFile();
        AclEnforcer.SetFileAcl(path);

        var result = AclEnforcer.VerifyFileAcl(path);

        Assert.True(result);
    }

    [Fact]
    public void VerifyFileAcl_ReturnsFalseOnUnprotectedFile()
    {
        var path = CreateTempFile();

        // File has default inherited ACL, not the restricted one
        var result = AclEnforcer.VerifyFileAcl(path);

        Assert.False(result);
    }

    [Fact]
    public void VerifyFileAcl_ReturnsFalseForNonexistentFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.tmp");

        var result = AclEnforcer.VerifyFileAcl(path);

        Assert.False(result);
    }

    [Fact]
    public void VerifyFileAcl_ReturnsFalseForNullPath()
    {
        var result = AclEnforcer.VerifyFileAcl(null!);

        Assert.False(result);
    }
}
