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

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

public sealed class RdpDisconnectSeverityTests
{
    [Theory]
    [InlineData(260)]
    [InlineData(264)]
    [InlineData(516)]
    [InlineData(772)]
    [InlineData(2308)]
    [InlineData(2825)]
    [InlineData(3080)]
    [InlineData(4360)]
    public void GetDisconnectSeverity_MapsTransientCodes(int reason)
    {
        var actual = RdpActiveXHost.GetDisconnectSeverity(reason);

        Assert.Equal(RdpActiveXHost.RdpDisconnectSeverity.Transient, actual);
    }

    [Theory]
    [InlineData(2055)]
    [InlineData(2567)]
    [InlineData(3335)]
    [InlineData(3591)]
    [InlineData(3847)]
    public void GetDisconnectSeverity_MapsAuthIssueCodes(int reason)
    {
        var actual = RdpActiveXHost.GetDisconnectSeverity(reason);

        Assert.Equal(RdpActiveXHost.RdpDisconnectSeverity.AuthIssue, actual);
    }

    [Theory]
    [InlineData(262)]
    [InlineData(1030)]
    [InlineData(1796)]
    [InlineData(2056)]
    [InlineData(2311)]
    [InlineData(2822)]
    [InlineData(3848)]
    public void GetDisconnectSeverity_MapsTerminalErrorCodes(int reason)
    {
        var actual = RdpActiveXHost.GetDisconnectSeverity(reason);

        Assert.Equal(RdpActiveXHost.RdpDisconnectSeverity.TerminalError, actual);
    }

    [Fact]
    public void GetDisconnectSeverity_MapsUnknownCodeToTerminalError()
    {
        var actual = RdpActiveXHost.GetDisconnectSeverity(9999);

        Assert.Equal(RdpActiveXHost.RdpDisconnectSeverity.TerminalError, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GetDisconnectSeverity_MapsSuppressedCleanExitCodesToTerminalError(int reason)
    {
        var actual = RdpActiveXHost.GetDisconnectSeverity(reason);

        Assert.Equal(RdpActiveXHost.RdpDisconnectSeverity.TerminalError, actual);
    }
}
