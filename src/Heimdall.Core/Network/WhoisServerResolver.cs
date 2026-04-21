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

namespace Heimdall.Core.Network;

/// <summary>
/// Resolves the WHOIS server endpoint for a given domain or IP-like input.
/// Unknown TLDs and non-domain inputs fall back to the IANA referral server.
/// </summary>
public static class WhoisServerResolver
{
    /// <summary>
    /// IANA's WHOIS referral server, used for unknown TLDs and non-domain inputs.
    /// </summary>
    public const string IanaServer = "whois.iana.org";

    /// <summary>
    /// Returns the WHOIS server that should be queried for <paramref name="domain"/>.
    /// Null, whitespace, inputs without a dot, or a trailing-dot empty TLD all
    /// fall back to <see cref="IanaServer"/>.
    /// </summary>
    public static string GetWhoisServer(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return IanaServer;
        }

        var trimmed = domain.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        if (lastDot < 0 || lastDot == trimmed.Length - 1)
        {
            return IanaServer;
        }

        var tld = trimmed[(lastDot + 1)..].ToLowerInvariant();
        return tld switch
        {
            "com" or "net" => "whois.verisign-grs.com",
            "org" => "whois.pir.org",
            "io" => "whois.nic.io",
            "dev" => "whois.nic.google",
            "fr" => "whois.nic.fr",
            "de" => "whois.denic.de",
            "uk" => "whois.nic.uk",
            "eu" => "whois.eu",
            "nl" => "whois.domain-registry.nl",
            "be" => "whois.dns.be",
            "ch" => "whois.nic.ch",
            "au" => "whois.auda.org.au",
            "ca" => "whois.cira.ca",
            "jp" => "whois.jprs.jp",
            _ => IanaServer,
        };
    }
}
