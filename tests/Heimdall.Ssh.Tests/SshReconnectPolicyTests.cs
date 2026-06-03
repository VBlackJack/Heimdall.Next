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

namespace Heimdall.Ssh.Tests;

public sealed class SshReconnectPolicyTests
{
    [Theory]
    [InlineData(SshFailureCode.NetworkRefused)]
    [InlineData(SshFailureCode.NetworkTimedOut)]
    [InlineData(SshFailureCode.NetworkReset)]
    [InlineData(SshFailureCode.NetworkUnreachable)]
    [InlineData(SshFailureCode.SessionDisconnected)]
    [InlineData(SshFailureCode.TunnelBroken)]
    [InlineData(SshFailureCode.AuthTimeout)]
    public void AllowsAutoReconnect_TransientCodes_ReturnsTrue(SshFailureCode code)
    {
        Assert.True(SshReconnectPolicy.AllowsAutoReconnect(code));
    }

    [Theory]
    [InlineData(SshFailureCode.AuthRejected)]
    [InlineData(SshFailureCode.KeyRejected)]
    [InlineData(SshFailureCode.PassphraseRejected)]
    [InlineData(SshFailureCode.PasswordRejected)]
    [InlineData(SshFailureCode.NoSupportedAuth)]
    [InlineData(SshFailureCode.TooManyAuthFailures)]
    [InlineData(SshFailureCode.KeyboardInteractiveNoPassword)]
    [InlineData(SshFailureCode.HostKeyMismatch)]
    [InlineData(SshFailureCode.HostKeyUnavailable)]
    [InlineData(SshFailureCode.Cancelled)]
    [InlineData(SshFailureCode.Unknown)]
    public void AllowsAutoReconnect_FatalOrUserActionCodes_ReturnsFalse(SshFailureCode code)
    {
        Assert.False(SshReconnectPolicy.AllowsAutoReconnect(code));
    }

    [Fact]
    public void AllowsAutoReconnect_CleanDisconnect_ReturnsFalse()
    {
        var disconnect = SshSessionDisconnectInfo.Clean("Process exited with code 0");

        Assert.False(SshReconnectPolicy.AllowsAutoReconnect(disconnect));
    }

    [Fact]
    public void AllowsAutoReconnect_UnclassifiedDisconnect_PreservesLegacyRetry()
    {
        var disconnect = SshSessionDisconnectInfo.Unclassified("Process exited with code 1");

        Assert.True(SshReconnectPolicy.AllowsAutoReconnect(disconnect));
    }

    [Fact]
    public void AllowsAutoReconnect_ClassifiedAuthFailure_ReturnsFalse()
    {
        var failure = new SshFailureInfo(
            SshFailureCode.PasswordRejected,
            "SSH password was rejected.",
            IsFatal: true);
        var disconnect = SshSessionDisconnectInfo.FromFailure(failure);

        Assert.False(SshReconnectPolicy.AllowsAutoReconnect(disconnect));
    }
}
