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

namespace Heimdall.Core.Models;

/// <summary>
/// Well-known default port numbers for supported connection protocols.
/// </summary>
public static class DefaultPorts
{
    public const int Rdp = 3389;
    public const int Ssh = 22;
    public const int Sftp = 22;
    public const int Vnc = 5900;
    public const int Ftp = 21;
    public const int Telnet = 23;
    public const int Http = 8080;
    public const int Tftp = 69;
}
