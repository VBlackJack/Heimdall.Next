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

using Heimdall.App.Views.EmbeddedRdp;
using Heimdall.Core.SessionDiagnostics;

namespace Heimdall.App.Tests;

public sealed class RdpHostDiagnosticFactoryTests
{
    [Theory]
    [InlineData(0, "RdpDisconnectNoInfo")]
    [InlineData(516, "RdpDisconnectSocketConnectFailed")]
    [InlineData(2055, "RdpDisconnectBadCredentials")]
    [InlineData(99999, "RdpDisconnectUnknownCode")]
    public void FromDisconnect_MapsReasonToMessageKey(int code, string expectedKey)
    {
        var diagnostic = RdpHostDiagnosticFactory.FromDisconnect(code);

        Assert.Equal(SessionFailureStage.RdpActiveXDisconnect, diagnostic.Stage);
        Assert.Equal(expectedKey, diagnostic.MessageKey);
        Assert.Equal(code, diagnostic.Code);
        Assert.Null(diagnostic.Detail);
    }

    [Fact]
    public void FromFatalError_UsesFatalErrorMessageKey()
    {
        var diagnostic = RdpHostDiagnosticFactory.FromFatalError(260);

        Assert.Equal(SessionFailureStage.RdpActiveXDisconnect, diagnostic.Stage);
        Assert.Equal("RdpFatalError", diagnostic.MessageKey);
        Assert.Equal(260, diagnostic.Code);
        Assert.Null(diagnostic.Detail);
    }

    [Fact]
    public void FromDisconnect_WithDifferentReasons_UsesDistinctMessageKeys()
    {
        var socketFailure = RdpHostDiagnosticFactory.FromDisconnect(516);
        var resolutionFailure = RdpHostDiagnosticFactory.FromDisconnect(4360);

        Assert.NotEqual(socketFailure.MessageKey, resolutionFailure.MessageKey);
    }
}
