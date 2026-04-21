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
using Heimdall.App.Services;

namespace Heimdall.App.Tests;

public sealed class Base64ToolServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public Base64ToolServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task EncodeAsync_SmallPayload_CompletesSuccessfully()
    {
        var service = new Base64ToolService();

        var task = service.EncodeAsync(Encoding.UTF8.GetBytes("abc"), false, CancellationToken.None);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal("YWJj", await task);
    }

    [Fact]
    public async Task EncodeAsync_PreCancelledLargePayload_ThrowsOperationCanceledException()
    {
        var service = new Base64ToolService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var data = Enumerable.Repeat((byte)'a', 101 * 1024).ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EncodeAsync(data, false, cts.Token));
    }

    [Fact]
    public async Task EncodeAsync_PreCancelledSmallPayload_CompletesInline()
    {
        var service = new Base64ToolService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var actual = await service.EncodeAsync(Encoding.UTF8.GetBytes("abc"), false, cts.Token);

        Assert.Equal("YWJj", actual);
    }

    [Fact]
    public async Task DecodeAsync_SmallPayload_CompletesSuccessfully()
    {
        var service = new Base64ToolService();

        var task = service.DecodeAsync("YWJj", false, CancellationToken.None);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal("abc", Encoding.UTF8.GetString(await task));
    }

    [Fact]
    public async Task DecodeAsync_PreCancelledLargePayload_ThrowsOperationCanceledException()
    {
        var service = new Base64ToolService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var payload = Convert.ToBase64String(Enumerable.Repeat((byte)'a', 101 * 1024).ToArray());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DecodeAsync(payload, false, cts.Token));
    }

    [Fact]
    public async Task DecodeAsync_PreCancelledSmallPayload_CompletesInline()
    {
        var service = new Base64ToolService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var actual = await service.DecodeAsync("YWJj", false, cts.Token);

        Assert.Equal("abc", Encoding.UTF8.GetString(actual));
    }

    [Fact]
    public async Task LoadFileAsync_Success_ReturnsBytesAndFileName()
    {
        var service = new Base64ToolService();
        var path = Path.Combine(_tempDir, "sample.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);

        var result = await service.LoadFileAsync(path, 1024, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(FileLoadError.None, result.Error);
        Assert.Equal("sample.bin", result.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Bytes);
    }

    [Fact]
    public async Task LoadFileAsync_TooLarge_ReturnsTypedOutcome()
    {
        var service = new Base64ToolService();
        var path = Path.Combine(_tempDir, "large.bin");
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)42, 32).ToArray());

        var result = await service.LoadFileAsync(path, 16, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FileLoadError.FileTooLarge, result.Error);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public async Task LoadFileAsync_MissingFile_ReturnsIoFailure()
    {
        var service = new Base64ToolService();

        var result = await service.LoadFileAsync(Path.Combine(_tempDir, "missing.bin"), 1024, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FileLoadError.IoFailure, result.Error);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadFileAsync_PreCancelled_ThrowsOperationCanceledException()
    {
        var service = new Base64ToolService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadFileAsync("ignored.bin", 1024, cts.Token));
    }

    [Fact]
    public async Task SaveFileAsync_WritesBytes()
    {
        var service = new Base64ToolService();
        var path = Path.Combine(_tempDir, "saved.bin");

        await service.SaveFileAsync(path, [9, 8, 7], CancellationToken.None);

        Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task SaveFileAsync_PreCancelled_ThrowsOperationCanceledException()
    {
        var service = new Base64ToolService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveFileAsync(Path.Combine(_tempDir, "saved.bin"), [9, 8, 7], cts.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}
