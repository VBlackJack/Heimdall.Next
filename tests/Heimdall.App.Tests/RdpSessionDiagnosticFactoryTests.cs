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

using System.IO;
using Heimdall.App.Services.Handlers;
using Heimdall.Core.SessionDiagnostics;

namespace Heimdall.App.Tests;

public sealed class RdpSessionDiagnosticFactoryTests
{
    [Fact]
    public void CreateTunnelFailure_UsesRdpTunnelStage()
    {
        var diagnostic = RdpSessionDiagnosticFactory.CreateTunnelFailure("Gateway chain failed.");

        Assert.Equal(SessionFailureStage.RdpTunnel, diagnostic.Stage);
        Assert.Equal("SessionFailureStageRdpTunnel", diagnostic.MessageKey);
        Assert.Null(diagnostic.Code);
        Assert.Equal("Gateway chain failed.", diagnostic.Detail);
    }

    [Fact]
    public void FromCredentialWriteFailure_ParsesWin32Code()
    {
        var diagnostic = RdpSessionDiagnosticFactory.FromCredentialWriteFailure("WIN32_ERROR_1312");

        Assert.Equal(SessionFailureStage.RdpCredentialWrite, diagnostic.Stage);
        Assert.Equal("SessionFailureStageRdpCredentialWrite", diagnostic.MessageKey);
        Assert.Equal(1312, diagnostic.Code);
        Assert.Equal("WIN32_ERROR_1312", diagnostic.Detail);
    }

    [Fact]
    public void FromRdpFileWriteException_UsesRdpFileWriteStage()
    {
        var diagnostic = RdpSessionDiagnosticFactory.FromRdpFileWriteException(new IOException("Disk is full."));

        Assert.Equal(SessionFailureStage.RdpFileWrite, diagnostic.Stage);
        Assert.Equal("SessionFailureStageRdpFileWrite", diagnostic.MessageKey);
        Assert.Null(diagnostic.Code);
        Assert.Equal("Disk is full.", diagnostic.Detail);
    }

    [Fact]
    public void FromMstscLaunchException_UsesRdpLaunchStage()
    {
        var diagnostic = RdpSessionDiagnosticFactory.FromMstscLaunchException(
            new InvalidOperationException("mstsc.exe did not start."));

        Assert.Equal(SessionFailureStage.RdpLaunch, diagnostic.Stage);
        Assert.Equal("SessionFailureStageRdpLaunch", diagnostic.MessageKey);
        Assert.Null(diagnostic.Code);
        Assert.Equal("mstsc.exe did not start.", diagnostic.Detail);
    }

    [Fact]
    public void FromGenericException_UsesGenericStage()
    {
        var diagnostic = RdpSessionDiagnosticFactory.FromGenericException(
            new InvalidOperationException("Unexpected RDP failure."));

        Assert.Equal(SessionFailureStage.GenericFailure, diagnostic.Stage);
        Assert.Equal("SessionFailureStageGeneric", diagnostic.MessageKey);
        Assert.Null(diagnostic.Code);
        Assert.Equal("Unexpected RDP failure.", diagnostic.Detail);
    }
}
