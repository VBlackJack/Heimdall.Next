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

using System.Security.Cryptography.X509Certificates;

namespace Heimdall.Core.Certificates;

public static class DistinguishedNameBuilder
{
    public static X500DistinguishedName Build(string cn, string org, string country)
    {
        var parts = new List<string> { $"CN={cn}" };
        if (!string.IsNullOrWhiteSpace(org))
        {
            parts.Add($"O={org}");
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            parts.Add($"C={country}");
        }

        return new X500DistinguishedName(string.Join(", ", parts));
    }
}
