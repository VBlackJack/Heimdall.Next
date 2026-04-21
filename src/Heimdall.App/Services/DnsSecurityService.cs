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
using Heimdall.Core.Configuration;
using Heimdall.Core.Security;

namespace Heimdall.App.Services;

public interface IDnsSecurityService
{
    /// <summary>
    /// Runs all six DNS security checks for the supplied domain.
    /// </summary>
    Task<IReadOnlyList<DnsCheckResult>> RunAllChecksAsync(string domain, CancellationToken ct);

    /// <summary>
    /// Sets or clears the SSH gateway used for remote DNS queries.
    /// </summary>
    void SetGateway(SshGatewayDto? gateway);
}

/// <summary>
/// Stateless service that performs local or tunnel-based DNS security checks.
/// </summary>
public sealed class DnsSecurityService : IDnsSecurityService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);
    private readonly Func<string, string, CancellationToken, Task<string>> _localQuery;
    private readonly Func<SshGatewayDto, string, string, CancellationToken, Task<string>> _tunnelQuery;
    private SshGatewayDto? _gateway;

    public DnsSecurityService(SshGatewayDto? gateway = null)
        : this(gateway, QueryDnsLocalAsync, QueryDnsViaTunnelAsync)
    {
    }

    internal DnsSecurityService(
        SshGatewayDto? gateway,
        Func<string, string, CancellationToken, Task<string>> localQuery,
        Func<SshGatewayDto, string, string, CancellationToken, Task<string>> tunnelQuery)
    {
        _gateway = gateway;
        _localQuery = localQuery ?? throw new ArgumentNullException(nameof(localQuery));
        _tunnelQuery = tunnelQuery ?? throw new ArgumentNullException(nameof(tunnelQuery));
    }

    public void SetGateway(SshGatewayDto? gateway)
    {
        _gateway = gateway;
    }

    public async Task<IReadOnlyList<DnsCheckResult>> RunAllChecksAsync(string domain, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ct.ThrowIfCancellationRequested();

        var spf = CheckSpfAsync(domain, ct);
        var dkim = CheckDkimAsync(domain, ct);
        var dmarc = CheckDmarcAsync(domain, ct);
        var caa = CheckCaaAsync(domain, ct);
        var dnssec = CheckDnssecAsync(domain, ct);
        var mx = CheckMxAsync(domain, ct);

        await Task.WhenAll(spf, dkim, dmarc, caa, dnssec, mx).ConfigureAwait(false);

        return
        [
            await spf.ConfigureAwait(false),
            await dkim.ConfigureAwait(false),
            await dmarc.ConfigureAwait(false),
            await caa.ConfigureAwait(false),
            await dnssec.ConfigureAwait(false),
            await mx.ConfigureAwait(false),
        ];
    }

    private Task<string> QueryDnsAsync(string type, string domain, CancellationToken ct)
        => _gateway is null
            ? _localQuery(type, domain, ct)
            : _tunnelQuery(_gateway, type, domain, ct);

    private async Task<DnsCheckResult> CheckSpfAsync(string domain, CancellationToken ct)
    {
        try
        {
            var raw = await QueryDnsAsync("TXT", domain, ct).ConfigureAwait(false);
            return DnsSecurityEvaluationEngine.EvaluateSpf(raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Spf, ex.Message);
        }
    }

    private async Task<DnsCheckResult> CheckDkimAsync(string domain, CancellationToken ct)
    {
        try
        {
            foreach (var selector in DnsSecurityEvaluationEngine.DefaultDkimSelectors)
            {
                var dkimDomain = $"{selector}._domainkey.{domain}";
                var raw = await QueryDnsAsync("TXT", dkimDomain, ct).ConfigureAwait(false);
                var result = DnsSecurityEvaluationEngine.EvaluateDkim(selector, raw);
                if (result.Status == DnsCheckStatus.Pass)
                {
                    return result;
                }
            }

            return DnsSecurityEvaluationEngine.EvaluateDkim(null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Dkim, ex.Message);
        }
    }

    private async Task<DnsCheckResult> CheckDmarcAsync(string domain, CancellationToken ct)
    {
        try
        {
            var raw = await QueryDnsAsync("TXT", $"_dmarc.{domain}", ct).ConfigureAwait(false);
            return DnsSecurityEvaluationEngine.EvaluateDmarc(raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Dmarc, ex.Message);
        }
    }

    private async Task<DnsCheckResult> CheckCaaAsync(string domain, CancellationToken ct)
    {
        try
        {
            var raw = await QueryDnsAsync("CAA", domain, ct).ConfigureAwait(false);
            return DnsSecurityEvaluationEngine.EvaluateCaa(raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Caa, ex.Message);
        }
    }

    private async Task<DnsCheckResult> CheckDnssecAsync(string domain, CancellationToken ct)
    {
        try
        {
            var dnskeyRaw = await QueryDnsAsync("DNSKEY", domain, ct).ConfigureAwait(false);
            string? rrsigRaw = null;
            if (!DnsSecurityEvaluationEngine.ContainsDnsRecords(dnskeyRaw, "DNSKEY"))
            {
                rrsigRaw = await QueryDnsAsync("RRSIG", domain, ct).ConfigureAwait(false);
            }

            return DnsSecurityEvaluationEngine.EvaluateDnssec(dnskeyRaw, rrsigRaw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Dnssec, ex.Message);
        }
    }

    private async Task<DnsCheckResult> CheckMxAsync(string domain, CancellationToken ct)
    {
        try
        {
            var raw = await QueryDnsAsync("MX", domain, ct).ConfigureAwait(false);
            return DnsSecurityEvaluationEngine.EvaluateMx(raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Mx, ex.Message);
        }
    }

    private static async Task<string> QueryDnsLocalAsync(string type, string domain, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "nslookup",
            Arguments = $"-type={type} {domain}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(output) ? error : output;
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
    }

    private static async Task<string> QueryDnsViaTunnelAsync(
        SshGatewayDto gateway,
        string type,
        string domain,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var client = ToolGatewayConnector.Connect(gateway);

            try
            {
                var escapedDomain = InputValidator.EscapeShellArg(domain);

                using var digCommand = client.CreateCommand($"dig {type} {escapedDomain} +short 2>/dev/null");
                digCommand.CommandTimeout = CommandTimeout;
                var digResult = digCommand.Execute()?.Trim();
                if (!string.IsNullOrWhiteSpace(digResult))
                {
                    return digResult;
                }

                using var nslookupCommand = client.CreateCommand($"nslookup -type={type} {escapedDomain} 2>&1");
                nslookupCommand.CommandTimeout = CommandTimeout;
                return nslookupCommand.Execute()?.Trim() ?? string.Empty;
            }
            finally
            {
                try
                {
                    client.Disconnect();
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }, ct).ConfigureAwait(false);
    }
}
