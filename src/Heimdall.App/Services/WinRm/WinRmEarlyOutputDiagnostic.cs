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

namespace Heimdall.App.Services.WinRm;

internal sealed class WinRmEarlyOutputDiagnostic
{
    internal const int DefaultMaxBufferedBytes = 16 * 1024;

    private const string NtlmLoopbackCode = "0x8009030e";
    private const string WsManInvalidResponseCode = "12152";

    private readonly int _maxBufferedBytes;
    private readonly StringBuilder _buffer = new();
    private int _bufferedBytes;

    public WinRmEarlyOutputDiagnostic(int maxBufferedBytes = DefaultMaxBufferedBytes)
    {
        if (maxBufferedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBufferedBytes));
        }

        _maxBufferedBytes = maxBufferedBytes;
        IsActive = true;
    }

    public bool IsActive { get; private set; }

    public string? Observe(ReadOnlySpan<byte> data)
    {
        if (!IsActive || data.IsEmpty)
        {
            return null;
        }

        int remainingBytes = _maxBufferedBytes - _bufferedBytes;
        if (remainingBytes <= 0)
        {
            IsActive = false;
            return null;
        }

        bool exceedsCap = data.Length > remainingBytes;
        ReadOnlySpan<byte> observedBytes = exceedsCap ? data[..remainingBytes] : data;
        _buffer.Append(Encoding.UTF8.GetString(observedBytes));
        _bufferedBytes += observedBytes.Length;

        string? localizationKey = FindDiagnosticKey(_buffer.ToString());
        if (localizationKey is not null || exceedsCap)
        {
            IsActive = false;
        }

        return localizationKey;
    }

    private static string? FindDiagnosticKey(string output)
    {
        if (output.Contains(NtlmLoopbackCode, StringComparison.OrdinalIgnoreCase))
        {
            return "ErrorWinRmNtlmLoopback";
        }

        if (output.Contains(WsManInvalidResponseCode, StringComparison.OrdinalIgnoreCase)
            && ContainsWsManContext(output))
        {
            return "ErrorWinRmWsmanInvalidResponse";
        }

        return null;
    }

    private static bool ContainsWsManContext(string output)
    {
        return output.Contains("WSMan", StringComparison.OrdinalIgnoreCase)
            || output.Contains("WS-Man", StringComparison.OrdinalIgnoreCase)
            || output.Contains("WinRM", StringComparison.OrdinalIgnoreCase);
    }
}
