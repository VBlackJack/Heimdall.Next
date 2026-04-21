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

using Heimdall.App.Services;
using Heimdall.Core.Codecs;

namespace Heimdall.App.Tests;

public sealed class JsonFormatterToolServiceTests
{
    private readonly JsonFormatterToolService _service = new();

    [Fact]
    public async Task FormatAsync_Whitespace_ReturnsEmpty()
    {
        var result = await _service.FormatAsync("   ", true, CancellationToken.None);

        Assert.Equal(JsonFormatStatus.Empty, result.Status);
    }

    [Fact]
    public async Task FormatAsync_SmallInput_ReturnsFormattedJson()
    {
        var result = await _service.FormatAsync("{\"a\":1}", true, CancellationToken.None);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Contains(Environment.NewLine, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatAsync_InputTooLarge_ReturnsPolicyError()
    {
        var input = new string('a', (int)JsonFormatterToolService.MaxInputSizeBytes + 1);

        var result = await _service.FormatAsync(input, true, CancellationToken.None);

        Assert.Equal(JsonFormatStatus.InputTooLarge, result.Status);
    }

    [Fact]
    public async Task FormatAsync_LargeInput_FormatsSuccessfully()
    {
        var repeated = string.Join(',', Enumerable.Repeat("{\"a\":1}", 15000));
        var input = $"[{repeated}]";

        var result = await _service.FormatAsync(input, false, CancellationToken.None);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.StartsWith("[", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatAsync_PreCancelledLargeInput_ThrowsOperationCanceledException()
    {
        var repeated = string.Join(',', Enumerable.Repeat("{\"a\":1}", 15000));
        var input = $"[{repeated}]";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.FormatAsync(input, true, cts.Token));
    }

    [Fact]
    public async Task FormatAsync_PreCancelledSmallInput_StillReturnsSynchronously()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _service.FormatAsync("{\"a\":1}", false, cts.Token);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Equal("{\"a\":1}", result.Output);
    }
}
