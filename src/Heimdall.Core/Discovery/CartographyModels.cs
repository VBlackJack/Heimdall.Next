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

namespace Heimdall.Core.Discovery;

/// <summary>
/// Defines the parameters for a network cartography scan.
/// </summary>
public record ScanProfile(
    string Subnet,
    ScanDepth Depth,
    int[]? CustomPorts,
    int MaxConcurrency,
    int TimeoutMs,
    bool SkipPing,
    bool ReverseDns);

/// <summary>
/// Controls the breadth of the port scan.
/// </summary>
public enum ScanDepth { Quick, Standard, Deep }

/// <summary>
/// Complete snapshot of a network scan run.
/// </summary>
public record NetworkScanSnapshot(
    string Id,
    DateTime Timestamp,
    ScanProfile Profile,
    string? GatewayName,
    TimeSpan Duration,
    List<HostScanResult> Hosts,
    List<VlanInfo>? DetectedVlans = null);

/// <summary>
/// Aggregated scan result for a single host.
/// </summary>
public record HostScanResult(
    string IpAddress,
    string? Hostname,
    bool IsAlive,
    long PingLatencyMs,
    List<ServiceResult> Services,
    RoleMatch? PrimaryRole,
    List<RoleMatch> AllRoles,
    string? MacAddress = null,
    string? Manufacturer = null);

/// <summary>
/// Result of probing a single port on a host.
/// </summary>
public record ServiceResult(
    int Port,
    bool IsOpen,
    string? ServiceName,
    string? Banner,
    string? Version,
    long ResponseTimeMs,
    CertificateInfo? Certificate = null);

/// <summary>
/// TLS certificate information gathered from a host's service.
/// </summary>
public record CertificateInfo(
    string Subject,
    string Issuer,
    DateTime NotBefore,
    DateTime NotAfter,
    bool IsExpired,
    bool ExpiresSoon,
    string KeyAlgorithm,
    string SignatureAlgorithm,
    string[] SubjectAltNames,
    string TlsVersion,
    string Thumbprint);

/// <summary>
/// A heuristic role match with confidence score and supporting evidence.
/// </summary>
public record RoleMatch(
    string Role,
    int Confidence,
    string[] Evidence);

/// <summary>
/// VLAN or network segment information inferred from scan results.
/// </summary>
public record VlanInfo(
    int? VlanId,
    string Name,
    string Subnet,
    string? Gateway,
    List<string> MemberIps);

/// <summary>
/// Difference between two scan snapshots.
/// </summary>
public record ScanDiff(
    List<HostScanResult> NewHosts,
    List<HostScanResult> RemovedHosts,
    List<(HostScanResult Old, HostScanResult New, List<string> Changes)> ModifiedHosts);
