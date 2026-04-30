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

using Heimdall.Rdp.ActiveX;

namespace Heimdall.App.Tests;

public sealed class RdpDisconnectCodeFormattingTests
{
    [Theory]
    [InlineData(2055, "RDP_BAD_CREDENTIALS \u00B7 2055")]
    [InlineData(264, "RDP_CONNECTION_TIMEOUT \u00B7 264")]
    [InlineData(260, "RDP_DNS_LOOKUP_FAILED \u00B7 260")]
    [InlineData(3848, "RDP_CRED_SSP_POLICY_ERROR \u00B7 3848")]
    [InlineData(9999, "RDP_UNKNOWN \u00B7 9999")]
    [InlineData(0, "RDP_NO_INFO \u00B7 0")]
    public void FormatDisconnectCode_ReturnsSymbolicNameAndRawCode(int reason, string expected)
    {
        var actual = RdpActiveXHost.FormatDisconnectCode(reason);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FormatDisconnectCode_UsesMiddleDotSeparator()
    {
        var actual = RdpActiveXHost.FormatDisconnectCode(2055);

        Assert.Contains('\u00B7', actual);
        Assert.DoesNotContain('\u2022', actual);
    }
}
