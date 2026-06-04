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

using Heimdall.Sftp;

namespace Heimdall.Sftp.Tests;

public sealed class SftpAtomicUploadTests
{
    [Fact]
    public void CommitRename_UsesAtomicRenameOnly_WhenAtomicRenameSucceeds()
    {
        var atomicCalls = new List<(string Temp, string Final)>();
        var plainCalls = new List<(string Temp, string Final)>();
        var deleteCalls = new List<string>();

        SftpAtomicUpload.CommitRename(
            "/srv/app/config.txt.part",
            "/srv/app/config.txt",
            atomicRename: (temp, final) => atomicCalls.Add((temp, final)),
            plainRename: (temp, final) => plainCalls.Add((temp, final)),
            deleteFinalIfExists: final => deleteCalls.Add(final));

        var atomicCall = Assert.Single(atomicCalls);
        Assert.Equal("/srv/app/config.txt.part", atomicCall.Temp);
        Assert.Equal("/srv/app/config.txt", atomicCall.Final);
        Assert.Empty(plainCalls);
        Assert.Empty(deleteCalls);
    }

    [Fact]
    public void CommitRename_FallsBackToDeleteAndPlainRename_WhenAtomicRenameThrows()
    {
        var plainCalls = new List<(string Temp, string Final)>();
        var deleteCalls = new List<string>();

        SftpAtomicUpload.CommitRename(
            "/srv/app/config.txt.part",
            "/srv/app/config.txt",
            atomicRename: (_, _) => throw new InvalidOperationException("extension unavailable"),
            plainRename: (temp, final) => plainCalls.Add((temp, final)),
            deleteFinalIfExists: final => deleteCalls.Add(final));

        Assert.Equal("/srv/app/config.txt", Assert.Single(deleteCalls));
        var plainCall = Assert.Single(plainCalls);
        Assert.Equal("/srv/app/config.txt.part", plainCall.Temp);
        Assert.Equal("/srv/app/config.txt", plainCall.Final);
    }

    [Fact]
    public void Rollback_DeletesOnlyTempPath()
    {
        var deletedPaths = new List<string>();

        SftpAtomicUpload.Rollback(
            "/srv/app/config.txt.part",
            temp => deletedPaths.Add(temp));

        Assert.Equal("/srv/app/config.txt.part", Assert.Single(deletedPaths));
    }

    [Fact]
    public void CreateRemoteTempPath_KeepsSameRemoteDirectoryAndUsesSlashSeparators()
    {
        string tempPath = SftpAtomicUpload.CreateRemoteTempPath("/srv/app/config.txt");

        Assert.StartsWith("/srv/app/config.txt.", tempPath, StringComparison.Ordinal);
        Assert.EndsWith(".part", tempPath, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', tempPath);
        Assert.Equal("/srv/app", GetRemoteDirectory(tempPath));
    }

    private static string GetRemoteDirectory(string remotePath)
    {
        int separator = remotePath.LastIndexOf('/');
        return separator <= 0 ? string.Empty : remotePath[..separator];
    }
}
