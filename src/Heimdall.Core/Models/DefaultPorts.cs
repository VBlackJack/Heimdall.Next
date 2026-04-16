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
    public const int Ftp = 21;
    public const int Ssh = 22;
    public const int Sftp = 22;
    public const int Telnet = 23;
    public const int Smtp = 25;
    public const int Dns = 53;
    public const int Tftp = 69;
    public const int HttpStd = 80;
    public const int Pop3 = 110;
    public const int Imap = 143;
    public const int Snmp = 161;
    public const int HttpsStd = 443;
    public const int Smtps = 465;
    public const int SmtpSubmission = 587;
    public const int Ldaps = 636;
    public const int Imaps = 993;
    public const int Pop3s = 995;
    public const int Mssql = 1433;
    public const int OracleDb = 1521;
    public const int MySql = 3306;
    public const int Rdp = 3389;
    public const int RdpTunnel = 33890;
    public const int PostgreSql = 5432;
    public const int Vnc = 5900;
    public const int VncAlt = 5901;
    public const int Redis = 6379;
    public const int SshTunnel = 2222;
    public const int Http = 8080;
    public const int HttpsAlt = 8443;
    public const int Prometheus = 9090;
    public const int MongoDb = 27017;
}
