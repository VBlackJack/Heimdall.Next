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

using System.Diagnostics;
using System.Text;
using Heimdall.Core.Network;

namespace Heimdall.App.Services;

/// <summary>
/// Synchronous route table loader backed by <c>route print</c>.
/// </summary>
public interface IRouteTableService
{
    IReadOnlyList<RouteEntry> Load();
}

public sealed class RouteTableService : IRouteTableService
{
    private readonly Func<string> _loadOutput;

    public RouteTableService()
        : this(DefaultLoadOutput)
    {
    }

    internal RouteTableService(Func<string> loadOutput)
    {
        ArgumentNullException.ThrowIfNull(loadOutput);
        _loadOutput = loadOutput;
    }

    public IReadOnlyList<RouteEntry> Load()
    {
        try
        {
            var output = _loadOutput();
            return RoutePrintParser.Parse(output);
        }
        catch
        {
            return [];
        }
    }

    private static string DefaultLoadOutput()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "route",
            Arguments = "print",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            throw new InvalidOperationException("Unable to start route.");
        }

        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"route exited with code {proc.ExitCode}.");
        }

        return output;
    }
}
