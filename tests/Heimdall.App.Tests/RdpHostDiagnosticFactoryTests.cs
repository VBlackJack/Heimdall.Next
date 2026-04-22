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
    [Fact]
    public void FromDisconnect_WithKnownReason_UsesMappedMessageKey()
    {
        var diagnostic = RdpHostDiagnosticFactory.FromDisconnect(516);

        Assert.Equal(SessionFailureStage.RdpActiveXDisconnect, diagnostic.Stage);
        Assert.Equal("RdpDisconnectSocketConnectFailed", diagnostic.MessageKey);
        Assert.Equal(516, diagnostic.Code);
        Assert.Null(diagnostic.Detail);
    }

    [Fact]
    public void FromDisconnect_WithUnknownReason_UsesUnknownCodeMessageKey()
    {
        var diagnostic = RdpHostDiagnosticFactory.FromDisconnect(99999);

        Assert.Equal(SessionFailureStage.RdpActiveXDisconnect, diagnostic.Stage);
        Assert.Equal("RdpDisconnectUnknownCode", diagnostic.MessageKey);
        Assert.Equal(99999, diagnostic.Code);
        Assert.Null(diagnostic.Detail);
    }

    [Fact]
    public void FromDisconnect_WithZeroReason_UsesNoInfoMessageKey()
    {
        var diagnostic = RdpHostDiagnosticFactory.FromDisconnect(0);

        Assert.Equal(SessionFailureStage.RdpActiveXDisconnect, diagnostic.Stage);
        Assert.Equal("RdpDisconnectNoInfo", diagnostic.MessageKey);
        Assert.Equal(0, diagnostic.Code);
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
