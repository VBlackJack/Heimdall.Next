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
using TwinShell.Core.Models;

namespace Heimdall.App.Services.PostConnect;

/// <summary>
/// Computes auto-prefill values for Command Library parameters using a strict,
/// explicit alias table and a minimal server snapshot context.
/// </summary>
public static class PostConnectParameterAutoPrefiller
{
    private static readonly string[] SecretSubstrings =
    [
        "password",
        "passwd",
        "pwd",
        "passphrase",
        "token",
        "secret",
        "credential",
        "cred",
        "apikey",
        "privatekey"
    ];

    public static IReadOnlyDictionary<string, string> Prefill(
        IReadOnlyList<TemplateParameter> parameters,
        AutoPrefillContext context,
        IReadOnlyDictionary<string, string>? existingValues = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
            {
                continue;
            }

            if (existingValues is not null && existingValues.TryGetValue(parameter.Name, out var existing))
            {
                result[parameter.Name] = existing ?? string.Empty;
                continue;
            }

            if (IsSecretParameter(parameter.Name))
            {
                continue;
            }

            if (TryMapFromContext(parameter.Name, context, out var prefilled))
            {
                result[parameter.Name] = prefilled;
            }
        }

        return result;
    }

    public static bool IsSecretParameter(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        var lower = parameterName.ToLowerInvariant();
        foreach (var candidate in SecretSubstrings)
        {
            if (lower.Contains(candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMapFromContext(string parameterName, AutoPrefillContext context, out string value)
    {
        switch (parameterName.ToLowerInvariant())
        {
            case "host":
            case "hostname":
            case "targethost":
            case "server":
            case "remotehost":
                if (!string.IsNullOrWhiteSpace(context.Host))
                {
                    value = context.Host!;
                    return true;
                }
                break;

            case "port":
            case "sshport":
            case "targetport":
                if (context.Port.HasValue)
                {
                    value = context.Port.Value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }
                break;

            case "user":
            case "username":
            case "sshuser":
            case "targetuser":
                if (!string.IsNullOrWhiteSpace(context.Username))
                {
                    value = context.Username!;
                    return true;
                }
                break;
        }

        value = string.Empty;
        return false;
    }
}
