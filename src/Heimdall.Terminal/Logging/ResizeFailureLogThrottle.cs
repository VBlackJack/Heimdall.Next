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

using System.Globalization;

namespace Heimdall.Terminal.Logging;

public enum ResizeFailureLogAction
{
    Skip,
    LogCurrent,
    LogRepeatSummaryThenCurrent
}

public sealed record ResizeFailureLogDecision(
    ResizeFailureLogAction Action,
    int PreviousRepeatCount);

public sealed class ResizeFailureLogThrottle
{
    private const string FailureSignatureFormat = "{0}|0x{1:X8}|{2}";

    private readonly object _syncRoot = new object();

    private string? _lastSignature;
    private int _repeatCount;

    public ResizeFailureLogDecision RecordFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string signature = BuildSignature(exception);
        lock (_syncRoot)
        {
            if (_lastSignature is null)
            {
                _lastSignature = signature;
                _repeatCount = 0;
                return new ResizeFailureLogDecision(ResizeFailureLogAction.LogCurrent, 0);
            }

            if (string.Equals(_lastSignature, signature, StringComparison.Ordinal))
            {
                _repeatCount++;
                return new ResizeFailureLogDecision(ResizeFailureLogAction.Skip, 0);
            }

            int previousRepeatCount = _repeatCount;
            _lastSignature = signature;
            _repeatCount = 0;

            if (previousRepeatCount >= 1)
            {
                return new ResizeFailureLogDecision(
                    ResizeFailureLogAction.LogRepeatSummaryThenCurrent,
                    previousRepeatCount);
            }

            return new ResizeFailureLogDecision(ResizeFailureLogAction.LogCurrent, 0);
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _lastSignature = null;
            _repeatCount = 0;
        }
    }

    private static string BuildSignature(Exception exception)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            FailureSignatureFormat,
            exception.GetType().FullName,
            exception.HResult,
            exception.Message);
    }
}
