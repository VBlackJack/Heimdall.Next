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

using Heimdall.App.Services;
using Heimdall.Ssh;

namespace Heimdall.App.Tests;

public sealed class TerminalReconnectPolicyTests
{
    [Fact]
    public void ClassifyProcessExit_ZeroExit_ReturnsCleanDisconnect()
    {
        SshSessionDisconnectInfo disconnect = TerminalReconnectPolicy.ClassifyProcessExit(
            exitCode: 0,
            autoReconnectOnProcessExit: false);

        Assert.True(disconnect.IsClean);
        Assert.False(disconnect.SuppressAutoReconnect);
        Assert.Equal("Process exited with code 0", disconnect.Message);
    }

    [Fact]
    public void ClassifyProcessExit_NonZeroExitWithAutoReconnect_ReturnsUnclassifiedDisconnect()
    {
        SshSessionDisconnectInfo disconnect = TerminalReconnectPolicy.ClassifyProcessExit(
            exitCode: 1,
            autoReconnectOnProcessExit: true);

        Assert.False(disconnect.IsClean);
        Assert.False(disconnect.SuppressAutoReconnect);
        Assert.Equal("Process exited with code 1", disconnect.Message);
    }

    [Fact]
    public void ClassifyProcessExit_NonZeroExitWithoutAutoReconnect_ReturnsSuppressedDisconnect()
    {
        SshSessionDisconnectInfo disconnect = TerminalReconnectPolicy.ClassifyProcessExit(
            exitCode: 1,
            autoReconnectOnProcessExit: false);

        Assert.False(disconnect.IsClean);
        Assert.True(disconnect.SuppressAutoReconnect);
        Assert.Equal("Process exited with code 1", disconnect.Message);
    }

    [Theory]
    [InlineData("WINRM")]
    [InlineData("winrm")]
    [InlineData("LOCAL")]
    [InlineData("local")]
    public void ReconnectsOnProcessExit_LocalOrHandedOffProcessProtocols_ReturnsFalse(
        string connectionType)
    {
        bool reconnects = TerminalReconnectPolicy.ReconnectsOnProcessExit(connectionType);

        Assert.False(reconnects);
    }

    [Theory]
    [InlineData("SSH")]
    [InlineData("TELNET")]
    [InlineData("UNKNOWN")]
    [InlineData(null)]
    public void ReconnectsOnProcessExit_NetworkOrUnknownProtocols_ReturnsTrue(
        string? connectionType)
    {
        bool reconnects = TerminalReconnectPolicy.ReconnectsOnProcessExit(connectionType);

        Assert.True(reconnects);
    }
}
