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
using System.Net;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Services.WinRm;

/// <summary>
/// Local PowerShell process launch shape expected by Heimdall terminal sessions.
/// </summary>
internal sealed record WinRmPowerShellLaunchSpec(string Executable, string Arguments);

/// <summary>
/// Builds local PowerShell command lines that enter a remote WinRM session.
/// </summary>
internal sealed class WinRmPowerShellLaunchBuilder
{
    private readonly Func<string, string?> _findExecutable;

    public WinRmPowerShellLaunchBuilder(Func<string, string?>? findExecutable = null)
    {
        _findExecutable = findExecutable ?? ConnectionHelpers.FindInPath;
    }

    public WinRmPowerShellLaunchSpec Build(
        ServerProfileDto server,
        string? bootstrapScriptPath = null)
    {
        ValidateWinRmProfile(server);

        string executable = ResolvePowerShellExecutable();
        if (server.WinRmIdentityMode == WinRmIdentityMode.Credential)
        {
            if (string.IsNullOrWhiteSpace(bootstrapScriptPath))
            {
                throw new ArgumentException(
                    "A bootstrap script path is required for WinRM stored-credential sessions.",
                    nameof(bootstrapScriptPath));
            }

            return new WinRmPowerShellLaunchSpec(
                executable,
                "-NoLogo -NoExit -NoProfile -ExecutionPolicy Bypass -File "
                + QuoteCommandLineArgument(bootstrapScriptPath));
        }

        string command = BuildEnterPSSessionCommand(server, credentialExpression: null);
        return new WinRmPowerShellLaunchSpec(
            executable,
            "-NoLogo -NoExit -NoProfile -Command "
            + QuoteCommandLineArgument(command));
    }

    internal static string BuildEnterPSSessionCommand(
        ServerProfileDto server,
        string? credentialExpression)
    {
        ValidateWinRmProfile(server);

        List<string> parts =
        [
            "Enter-PSSession",
            "-ComputerName",
            QuotePowerShellLiteral(server.RemoteServer),
            "-Port",
            ResolvePort(server).ToString(CultureInfo.InvariantCulture)
        ];

        if (server.WinRmUseSsl)
        {
            parts.Add("-UseSSL");
        }

        if (!string.IsNullOrWhiteSpace(credentialExpression))
        {
            parts.Add("-Credential");
            parts.Add(credentialExpression);
        }

        return string.Join(" ", parts);
    }

    internal static string QuotePowerShellLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    internal static string QuoteCommandLineArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            + "\"";
    }

    internal static int ResolvePort(ServerProfileDto server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return server.WinRmPort > 0
            ? server.WinRmPort
            : server.WinRmUseSsl ? DefaultPorts.WinRmHttps : DefaultPorts.WinRmHttp;
    }

    private string ResolvePowerShellExecutable()
    {
        string? pwshPath = _findExecutable("pwsh.exe");
        return string.IsNullOrWhiteSpace(pwshPath) ? "powershell.exe" : pwshPath;
    }

    private static void ValidateWinRmProfile(ServerProfileDto server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!string.Equals(server.ConnectionType, "WINRM", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Server profile is not a WinRM profile.", nameof(server));
        }

        if (!IsValidHost(server.RemoteServer))
        {
            throw new ArgumentException("Invalid WinRM host.", nameof(server));
        }

        if (!InputValidator.ValidatePortRange(ResolvePort(server)))
        {
            throw new ArgumentOutOfRangeException(nameof(server), "Invalid WinRM port.");
        }
    }

    private static bool IsValidHost(string? host)
    {
        return !string.IsNullOrWhiteSpace(host)
            && (InputValidator.ValidateDomain(host) || IPAddress.TryParse(host, out _));
    }
}
