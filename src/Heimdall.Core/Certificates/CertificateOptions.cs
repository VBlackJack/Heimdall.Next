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

namespace Heimdall.Core.Certificates;

public enum CertificateValidationCode
{
    Ok,
    CnRequired,
    InvalidValidity,
}

public sealed record CertificateValidationResult(CertificateValidationCode Code);

public sealed record CertificateOptions(
    string Cn,
    string Org,
    string Country,
    int KeySize,
    int ValidityDays,
    IReadOnlyList<string> Sans)
{
    public CertificateValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Cn))
        {
            return new CertificateValidationResult(CertificateValidationCode.CnRequired);
        }

        if (ValidityDays < 1)
        {
            return new CertificateValidationResult(CertificateValidationCode.InvalidValidity);
        }

        return new CertificateValidationResult(CertificateValidationCode.Ok);
    }
}
