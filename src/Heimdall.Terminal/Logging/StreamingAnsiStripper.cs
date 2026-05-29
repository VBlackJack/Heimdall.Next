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

namespace Heimdall.Terminal.Logging;

/// <summary>
/// Incrementally strips ANSI/VT escape sequences while preserving escape boundaries across chunks.
/// </summary>
/// <remarks>
/// This type is not thread-safe. Callers must serialize access when a shared instance is used.
/// </remarks>
public sealed class StreamingAnsiStripper
{
    private const char Escape = '\u001B';

    private readonly StringBuilder _pending = new StringBuilder();
    private AnsiStripState _state;
    private bool _csiSeenIntermediate;

    public string Strip(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (chunk.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder output = new StringBuilder(chunk.Length);
        int index = 0;
        while (index < chunk.Length)
        {
            char current = chunk[index];
            bool consumed = ProcessChar(current, output);
            if (consumed)
            {
                index++;
            }
        }

        return output.ToString();
    }

    public void Flush()
    {
        Reset();
    }

    public void Reset()
    {
        _pending.Clear();
        _state = AnsiStripState.Normal;
        _csiSeenIntermediate = false;
    }

    private bool ProcessChar(char current, StringBuilder output)
    {
        switch (_state)
        {
            case AnsiStripState.Normal:
                if (current == Escape)
                {
                    _pending.Append(current);
                    _state = AnsiStripState.AfterEsc;
                }
                else
                {
                    output.Append(current);
                }

                return true;

            case AnsiStripState.AfterEsc:
                if (current == '[')
                {
                    _pending.Append(current);
                    _state = AnsiStripState.Csi;
                    _csiSeenIntermediate = false;
                    return true;
                }

                if (IsFeEscape(current))
                {
                    ClearPendingSequence();
                    return true;
                }

                EmitPending(output);
                return false;

            case AnsiStripState.Csi:
                if (IsCsiParameter(current) && !_csiSeenIntermediate)
                {
                    _pending.Append(current);
                    return true;
                }

                if (IsCsiIntermediate(current))
                {
                    _pending.Append(current);
                    _csiSeenIntermediate = true;
                    return true;
                }

                if (IsCsiFinal(current))
                {
                    ClearPendingSequence();
                    return true;
                }

                EmitPending(output);
                return false;

            default:
                throw new InvalidOperationException($"Unknown ANSI strip state: {_state}");
        }
    }

    private void EmitPending(StringBuilder output)
    {
        output.Append(_pending);
        ClearPendingSequence();
    }

    private void ClearPendingSequence()
    {
        _pending.Clear();
        _state = AnsiStripState.Normal;
        _csiSeenIntermediate = false;
    }

    private static bool IsFeEscape(char value)
    {
        return (value >= '\u0040' && value <= '\u005A')
            || (value >= '\u005C' && value <= '\u005F');
    }

    private static bool IsCsiParameter(char value)
    {
        return value >= '\u0030' && value <= '\u003F';
    }

    private static bool IsCsiIntermediate(char value)
    {
        return value >= '\u0020' && value <= '\u002F';
    }

    private static bool IsCsiFinal(char value)
    {
        return value >= '\u0040' && value <= '\u007E';
    }

    private enum AnsiStripState
    {
        Normal,
        AfterEsc,
        Csi
    }
}
