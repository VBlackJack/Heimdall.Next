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

public sealed class RemoteFileEditorSudoDownloadTests
{
    [Fact]
    public void BuildSudoDownloadCommand_EscapesRemotePath()
    {
        var command = RemoteFileEditor.BuildSudoDownloadCommand("/etc/ssh/it's config; rm -rf /");

        Assert.Equal(@"sudo base64 -- '/etc/ssh/it'\''s config; rm -rf /'", command);
    }

    [Fact]
    public async Task WriteBase64DecodedFileAsync_PreservesBinaryBytes()
    {
        byte[] expected = [0x00, 0xff, 0xfe, 0x41, 0x0a, 0xc3, 0x28];
        string encoded = Convert.ToBase64String(expected);
        string wrappedEncoded = encoded[..4] + "\n" + encoded[4..];
        var localPath = CreateTempPath();

        try
        {
            await RemoteFileEditor.WriteBase64DecodedFileAsync(localPath, wrappedEncoded);

            Assert.Equal(expected, await File.ReadAllBytesAsync(localPath));
        }
        finally
        {
            CleanupTempPath(localPath);
        }
    }

    [Fact]
    public async Task WriteBase64DecodedFileAsync_EmptyOutput_WritesEmptyFile()
    {
        var localPath = CreateTempPath();

        try
        {
            await RemoteFileEditor.WriteBase64DecodedFileAsync(localPath, "");

            Assert.Empty(await File.ReadAllBytesAsync(localPath));
        }
        finally
        {
            CleanupTempPath(localPath);
        }
    }

    [Fact]
    public async Task WriteBase64DecodedFileAsync_InvalidOutput_Throws()
    {
        var localPath = CreateTempPath();

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RemoteFileEditor.WriteBase64DecodedFileAsync(localPath, "not valid base64"));

            Assert.Contains("invalid base64", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempPath(localPath);
        }
    }

    private static string CreateTempPath()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "HeimdallTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, "downloaded.bin");
    }

    private static void CleanupTempPath(string localPath)
    {
        var directory = Path.GetDirectoryName(localPath);
        if (directory is null)
        {
            return;
        }

        Directory.Delete(directory, recursive: true);
    }
}
