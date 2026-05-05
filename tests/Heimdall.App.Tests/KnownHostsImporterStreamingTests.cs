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
using System.Text;
using Heimdall.Core.Ssh;
using Heimdall.Ssh;
using KnownHostsImporter = Heimdall.App.Services.Import.KnownHostsImporter;

namespace Heimdall.App.Tests;

public sealed class KnownHostsImporterStreamingTests
{
    private const string SampleKey = "AQIDBAU=";

    [Fact]
    public async Task ParseFileAsync_ParsesValidKnownHosts_FromTempFile()
    {
        var path = CreateTempFile(
            """
            alpha.example.com ssh-ed25519 AQIDBAU=
            [beta.example.com]:2222 ssh-ed25519 AQIDBAU=
            gamma.example.com,delta.example.com ssh-ed25519 AQIDBAU=
            """);

        try
        {
            var result = await CreateImporter().ParseFileAsync(path);

            Assert.Equal(4, result.Entries.Count);
            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    [Fact]
    public async Task ParseFileAsync_FileTooLarge_ReturnsEmptyResultWithDiagnostic()
    {
        var path = CreateTempPath();
        try
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                stream.SetLength(KnownHostsParser.MaxFileSizeBytes + 1);
            }

            var result = await CreateImporter().ParseFileAsync(path);

            Assert.Empty(result.Entries);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(KnownHostsDiagnosticLevel.Warning, diagnostic.Level);
            Assert.Equal(KnownHostsDiagnosticCode.FileTooLarge, diagnostic.Code);
            Assert.Equal(0, diagnostic.SourceLineNumber);
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    [Fact]
    public async Task ParseFileAsync_StreamingPath_ParsesAllEntries()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 2_000; i++)
        {
            builder.Append("host-")
                .Append(i)
                .Append(".example.com ssh-ed25519 ")
                .Append(SampleKey)
                .AppendLine();
        }

        var path = CreateTempFile(builder.ToString());
        try
        {
            var result = await CreateImporter().ParseFileAsync(path);

            Assert.Equal(2_000, result.Entries.Count);
            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    [Fact]
    public async Task ParseFileAsync_FileNotFound_ReturnsFileReadErrorDiagnostic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heimdall-missing-{Guid.NewGuid():N}", "known_hosts");

        var result = await CreateImporter().ParseFileAsync(path);

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticLevel.Warning, diagnostic.Level);
        Assert.Equal(KnownHostsDiagnosticCode.FileReadError, diagnostic.Code);
        Assert.Equal(0, diagnostic.SourceLineNumber);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Context));
    }

    [Fact]
    public async Task ParseFileAsync_HonorsCancellationToken()
    {
        var path = CreateTempFile($"cancel.example.com ssh-ed25519 {SampleKey}");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateImporter().ParseFileAsync(path, cts.Token));
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    [Fact]
    public async Task ParseFileAsync_ThrowsForNullOrWhitespacePath()
    {
        var importer = CreateImporter();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => importer.ParseFileAsync(null!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => importer.ParseFileAsync(string.Empty));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => importer.ParseFileAsync(" "));
    }

    [Fact]
    public async Task ParseFileAsync_DiagnosticForTooLongLine_StillProducedByCoreParser()
    {
        var path = CreateTempFile(new string('z', KnownHostsParser.MaxLineLength + 1));
        try
        {
            var result = await CreateImporter().ParseFileAsync(path);

            Assert.Empty(result.Entries);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(KnownHostsDiagnosticCode.MalformedLine, diagnostic.Code);
            Assert.Equal(KnownHostsParser.LineTooLongContext, diagnostic.Context);
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    private static KnownHostsImporter CreateImporter()
    {
        return new KnownHostsImporter(new InMemoryConfigManager(), new HostKeyStore());
    }

    private static string CreateTempFile(string content)
    {
        var path = CreateTempPath();
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static string CreateTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"heimdall-known-hosts-{Guid.NewGuid():N}");
    }

    private static void DeleteTempFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
