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

using System.Net.Security;
using Heimdall.App.Services.WinRm;

namespace Heimdall.App.Tests;

public sealed class WinRmTransportProbesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldAcceptServerCertificate_WithNoPolicyErrors_ReturnsTrue(
        bool skipCertValidation)
    {
        bool accepted = WinRmTransportProbes.ShouldAcceptServerCertificate(
            SslPolicyErrors.None,
            skipCertValidation);

        Assert.True(accepted);
    }

    [Fact]
    public void ShouldAcceptServerCertificate_WithPolicyErrorsAndSkipEnabled_ReturnsTrue()
    {
        SslPolicyErrors sslPolicyErrors =
            SslPolicyErrors.RemoteCertificateChainErrors
            | SslPolicyErrors.RemoteCertificateNameMismatch;

        bool accepted = WinRmTransportProbes.ShouldAcceptServerCertificate(
            sslPolicyErrors,
            skipCertValidation: true);

        Assert.True(accepted);
    }

    [Fact]
    public void ShouldAcceptServerCertificate_WithPolicyErrorsAndSkipDisabled_ReturnsFalse()
    {
        SslPolicyErrors sslPolicyErrors =
            SslPolicyErrors.RemoteCertificateChainErrors
            | SslPolicyErrors.RemoteCertificateNameMismatch;

        bool accepted = WinRmTransportProbes.ShouldAcceptServerCertificate(
            sslPolicyErrors,
            skipCertValidation: false);

        Assert.False(accepted);
    }
}
