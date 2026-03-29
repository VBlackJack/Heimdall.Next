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

using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Shared preset catalog for the default credential scanner.
/// </summary>
public static class DefaultCredentialPresets
{
    public static readonly Dictionary<string, List<(string User, string Pass)>> CredentialsByService = new()
    {
        ["SSH"] =
        [
            ("root", "root"), ("root", "toor"), ("admin", "admin"),
            ("admin", "password"), ("admin", "1234"), ("pi", "raspberry"),
            ("ubnt", "ubnt"), ("vagrant", "vagrant"),
        ],
        ["Telnet"] =
        [
            ("admin", "admin"), ("root", "root"),
            ("admin", "password"), ("admin", "1234"),
        ],
        ["HTTP"] =
        [
            ("admin", "admin"), ("admin", "password"), ("admin", "1234"),
            ("root", "root"), ("admin", ""), ("user", "user"),
        ],
        ["FTP"] =
        [
            ("anonymous", ""), ("admin", "admin"),
            ("ftp", "ftp"), ("root", "root"),
        ],
        ["SNMP"] =
        [
            ("public", ""), ("private", ""), ("community", ""),
            ("default", ""), ("monitor", ""),
        ],
        ["MySQL"] =
        [
            ("root", ""), ("root", "root"),
            ("root", "mysql"), ("admin", "admin"),
        ],
        ["PostgreSQL"] =
        [
            ("postgres", "postgres"), ("admin", "admin"),
        ],
        ["Redis"] =
        [
            ("", ""),
        ],
        ["MSSQL"] =
        [
            ("sa", ""), ("sa", "sa"),
            ("sa", "password"), ("sa", "Password1"),
        ],
        ["VNC"] =
        [
            ("", "password"), ("", "1234"), ("", "vnc"),
        ],
    };

    public static readonly Dictionary<int, string> ServicePorts = new()
    {
        [DefaultPorts.Ssh] = "SSH",
        [DefaultPorts.Telnet] = "Telnet",
        [DefaultPorts.Ftp] = "FTP",
        [80] = "HTTP",
        [443] = "HTTP",
        [DefaultPorts.Http] = "HTTP",
        [8443] = "HTTP",
        [3306] = "MySQL",
        [5432] = "PostgreSQL",
        [1433] = "MSSQL",
        [6379] = "Redis",
        [161] = "SNMP",
        [DefaultPorts.Vnc] = "VNC",
        [5901] = "VNC",
    };
}
