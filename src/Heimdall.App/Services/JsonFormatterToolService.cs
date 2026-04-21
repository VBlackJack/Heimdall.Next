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

using System.Text;
using Heimdall.Core.Codecs;

namespace Heimdall.App.Services;

public sealed class JsonFormatterToolService : IJsonFormatterToolService
{
    public const long MaxInputSizeBytes = 5L * 1024 * 1024;
    public const int AsyncThresholdBytes = 100 * 1024;

    public Task<JsonFormatResult> FormatAsync(string input, bool indented, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input))
        {
            return Task.FromResult(new JsonFormatResult(JsonFormatStatus.Empty, string.Empty, string.Empty, null, null));
        }

        var inputSizeBytes = Encoding.UTF8.GetByteCount(input);
        if (inputSizeBytes > MaxInputSizeBytes)
        {
            return Task.FromResult(new JsonFormatResult(JsonFormatStatus.InputTooLarge, string.Empty, string.Empty, null, null));
        }

        if (inputSizeBytes <= AsyncThresholdBytes)
        {
            return Task.FromResult(JsonCodec.Format(input, indented));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return JsonCodec.Format(input, indented);
        }, cancellationToken);
    }
}
