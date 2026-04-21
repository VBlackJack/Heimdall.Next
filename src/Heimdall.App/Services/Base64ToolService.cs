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
using Heimdall.Core.Codecs;

namespace Heimdall.App.Services;

public sealed class Base64ToolService : IBase64ToolService
{
    private const int AsyncThresholdBytes = 100 * 1024;

    public Task<string> EncodeAsync(byte[] data, bool urlSafe, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length <= AsyncThresholdBytes)
        {
            return Task.FromResult(Base64Codec.Encode(data, urlSafe));
        }

        return Task.Run(() => Base64Codec.Encode(data, urlSafe), ct);
    }

    public Task<byte[]> DecodeAsync(string base64, bool urlSafe, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(base64);

        if (System.Text.Encoding.UTF8.GetByteCount(base64) <= AsyncThresholdBytes)
        {
            return Task.FromResult(Base64Codec.Decode(base64, urlSafe));
        }

        return Task.Run(() => Base64Codec.Decode(base64, urlSafe), ct);
    }

    public async Task<FileLoadOutcome> LoadFileAsync(string path, long maxBytes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ct.ThrowIfCancellationRequested();

        try
        {
            var fileInfo = new FileInfo(path);
            var fileName = fileInfo.Name;
            if (fileInfo.Length > maxBytes)
            {
                return new FileLoadOutcome(false, null, fileName, FileLoadError.FileTooLarge);
            }

            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return new FileLoadOutcome(true, bytes, fileName, FileLoadError.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileLoadOutcome(false, null, Path.GetFileName(path), FileLoadError.IoFailure, ex.Message);
        }
    }

    public Task SaveFileAsync(string path, byte[] data, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(data);
        return File.WriteAllBytesAsync(path, data, ct);
    }
}
