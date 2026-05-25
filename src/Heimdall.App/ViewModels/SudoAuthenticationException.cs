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

namespace Heimdall.App.ViewModels;

internal sealed class SudoAuthenticationException : Exception
{
    public SudoAuthenticationException(SudoFailureKind kind, string? rawStderr)
        : this(kind, rawStderr, null)
    {
    }

    public SudoAuthenticationException(
        SudoFailureKind kind,
        string? rawStderr,
        Exception? innerException)
        : base(BuildMessage(kind, rawStderr), innerException)
    {
        Kind = kind;
        RawStderr = rawStderr ?? string.Empty;
    }

    public SudoFailureKind Kind { get; }

    public string RawStderr { get; }

    private static string BuildMessage(SudoFailureKind kind, string? rawStderr)
    {
        if (!string.IsNullOrWhiteSpace(rawStderr))
        {
            return rawStderr;
        }

        return $"sudo authentication failed: {kind}";
    }
}
