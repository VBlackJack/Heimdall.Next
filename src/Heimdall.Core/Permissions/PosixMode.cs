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

namespace Heimdall.Core.Permissions;

public enum PosixRole
{
    Owner,
    Group,
    Others,
}

public enum PosixPermission
{
    Read,
    Write,
    Execute,
}

public readonly record struct PosixMode
{
    public static readonly PosixMode Empty = new(0, 0, 0);
    public static readonly PosixMode Preset644 = new(6, 4, 4);
    public static readonly PosixMode Preset755 = new(7, 5, 5);
    public static readonly PosixMode Preset600 = new(6, 0, 0);
    public static readonly PosixMode Preset700 = new(7, 0, 0);
    public static readonly PosixMode Preset777 = new(7, 7, 7);

    public PosixMode(int owner, int group, int others)
    {
        ValidateDigit(owner, nameof(owner));
        ValidateDigit(group, nameof(group));
        ValidateDigit(others, nameof(others));
        Owner = owner;
        Group = group;
        Others = others;
    }

    public int Owner { get; }
    public int Group { get; }
    public int Others { get; }

    public bool OwnerRead => (Owner & 4) != 0;
    public bool OwnerWrite => (Owner & 2) != 0;
    public bool OwnerExecute => (Owner & 1) != 0;
    public bool GroupRead => (Group & 4) != 0;
    public bool GroupWrite => (Group & 2) != 0;
    public bool GroupExecute => (Group & 1) != 0;
    public bool OthersRead => (Others & 4) != 0;
    public bool OthersWrite => (Others & 2) != 0;
    public bool OthersExecute => (Others & 1) != 0;

    public static PosixMode FromOctal(string octal)
        => TryParseOctal(octal, out var mode) ? mode : throw new FormatException("Invalid octal mode.");

    public static bool TryParseOctal(string? octal, out PosixMode mode)
    {
        mode = default;
        if (string.IsNullOrWhiteSpace(octal) || octal.Length != 3)
        {
            return false;
        }

        for (var i = 0; i < octal.Length; i++)
        {
            if (octal[i] is < '0' or > '7')
            {
                return false;
            }
        }

        mode = new PosixMode(octal[0] - '0', octal[1] - '0', octal[2] - '0');
        return true;
    }

    public string ToOctal() => $"{Owner}{Group}{Others}";

    public string ToSymbolic()
    {
        Span<char> chars = stackalloc char[9];
        WriteRole(chars[..3], Owner);
        WriteRole(chars.Slice(3, 3), Group);
        WriteRole(chars.Slice(6, 3), Others);
        return chars.ToString();
    }

    public PosixMode WithBit(PosixRole role, PosixPermission permission, bool enabled)
    {
        var mask = permission switch
        {
            PosixPermission.Read => 4,
            PosixPermission.Write => 2,
            PosixPermission.Execute => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(permission)),
        };

        return role switch
        {
            PosixRole.Owner => new PosixMode(Apply(Owner, mask, enabled), Group, Others),
            PosixRole.Group => new PosixMode(Owner, Apply(Group, mask, enabled), Others),
            PosixRole.Others => new PosixMode(Owner, Group, Apply(Others, mask, enabled)),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }

    private static int Apply(int current, int mask, bool enabled) => enabled ? current | mask : current & ~mask;

    private static void WriteRole(Span<char> chars, int digit)
    {
        chars[0] = (digit & 4) != 0 ? 'r' : '-';
        chars[1] = (digit & 2) != 0 ? 'w' : '-';
        chars[2] = (digit & 1) != 0 ? 'x' : '-';
    }

    private static void ValidateDigit(int digit, string paramName)
    {
        if (digit is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(paramName, "POSIX permission digits must be between 0 and 7.");
        }
    }
}
