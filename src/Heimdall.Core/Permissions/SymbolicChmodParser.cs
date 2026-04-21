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

using System.Text.RegularExpressions;

namespace Heimdall.Core.Permissions;

public static partial class SymbolicChmodParser
{
    public static bool TryParse(string? input, out PosixMode mode)
    {
        mode = PosixMode.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var clauses = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (clauses.Length == 0)
        {
            return false;
        }

        var current = PosixMode.Empty;
        foreach (var clause in clauses)
        {
            var match = ClauseRegex().Match(clause);
            if (!match.Success)
            {
                mode = PosixMode.Empty;
                return false;
            }

            var who = match.Groups[1].Value;
            var operation = match.Groups[2].Value[0];
            var permissions = match.Groups[3].Value;

            if (AppliesTo(who, 'u'))
            {
                current = ApplyToRole(current, PosixRole.Owner, operation, permissions);
            }

            if (AppliesTo(who, 'g'))
            {
                current = ApplyToRole(current, PosixRole.Group, operation, permissions);
            }

            if (AppliesTo(who, 'o'))
            {
                current = ApplyToRole(current, PosixRole.Others, operation, permissions);
            }
        }

        mode = current;
        return true;
    }

    [GeneratedRegex(@"^([ugoa]+)([\+\-=])([rwx]*)$", RegexOptions.Compiled)]
    private static partial Regex ClauseRegex();

    private static bool AppliesTo(string who, char role) => who.Contains(role) || who.Contains('a');

    private static PosixMode ApplyToRole(PosixMode current, PosixRole role, char operation, string permissions)
    {
        var mode = operation == '=' ? ClearRole(current, role) : current;
        mode = ApplyPermission(mode, role, PosixPermission.Read, operation, permissions.Contains('r'));
        mode = ApplyPermission(mode, role, PosixPermission.Write, operation, permissions.Contains('w'));
        mode = ApplyPermission(mode, role, PosixPermission.Execute, operation, permissions.Contains('x'));
        return mode;
    }

    private static PosixMode ClearRole(PosixMode current, PosixRole role)
        => role switch
        {
            PosixRole.Owner => new PosixMode(0, current.Group, current.Others),
            PosixRole.Group => new PosixMode(current.Owner, 0, current.Others),
            PosixRole.Others => new PosixMode(current.Owner, current.Group, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private static PosixMode ApplyPermission(PosixMode current, PosixRole role, PosixPermission permission, char operation, bool present)
    {
        if (operation == '+' && present)
        {
            return current.WithBit(role, permission, true);
        }

        if (operation == '-' && present)
        {
            return current.WithBit(role, permission, false);
        }

        if (operation == '=')
        {
            return current.WithBit(role, permission, present);
        }

        return current;
    }
}
