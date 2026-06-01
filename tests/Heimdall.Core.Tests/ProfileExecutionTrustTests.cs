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

using Heimdall.Core.Configuration;

namespace Heimdall.Core.Tests;

public sealed class ProfileExecutionTrustTests
{
    [Fact]
    public void CarriesLocalExecutionPayload_LocalExecutable_ReturnsTrue()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "LOCAL",
            LocalShellExecutable = "pwsh.exe"
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesLocalExecutionPayload(profile);
        bool requiresConfirmation = ProfileExecutionTrust.RequiresExecutionConfirmation(profile);

        Assert.True(carriesPayload);
        Assert.True(requiresConfirmation);
    }

    [Fact]
    public void CarriesLocalExecutionPayload_LocalArgumentsOnly_ReturnsTrue()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "LOCAL",
            LocalShellArguments = "-NoProfile"
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesLocalExecutionPayload(profile);

        Assert.True(carriesPayload);
    }

    [Fact]
    public void CarriesLocalExecutionPayload_LocalWithoutExecutableOrArguments_ReturnsFalse()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "LOCAL"
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesLocalExecutionPayload(profile);
        bool requiresConfirmation = ProfileExecutionTrust.RequiresExecutionConfirmation(profile);

        Assert.False(carriesPayload);
        Assert.False(requiresConfirmation);
    }

    [Fact]
    public void CarriesLocalExecutionPayload_LowercaseLocal_ReturnsTrue()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "local",
            LocalShellExecutable = "pwsh.exe"
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesLocalExecutionPayload(profile);

        Assert.True(carriesPayload);
    }

    [Fact]
    public void CarriesLocalExecutionPayload_NonLocalWithExecutable_ReturnsFalse()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "SSH",
            LocalShellExecutable = "pwsh.exe"
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesLocalExecutionPayload(profile);
        bool requiresConfirmation = ProfileExecutionTrust.RequiresExecutionConfirmation(profile);

        Assert.False(carriesPayload);
        Assert.False(requiresConfirmation);
    }

    [Fact]
    public void RequiresExecutionConfirmation_ConfirmedPayload_ReturnsFalse()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "LOCAL",
            LocalShellExecutable = "pwsh.exe",
            ExecutionConfirmed = true
        };

        bool requiresConfirmation = ProfileExecutionTrust.RequiresExecutionConfirmation(profile);

        Assert.False(requiresConfirmation);
    }

    [Fact]
    public void NullProfile_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ProfileExecutionTrust.CarriesLocalExecutionPayload(null!));
        Assert.Throws<ArgumentNullException>(() => ProfileExecutionTrust.RequiresExecutionConfirmation(null!));
    }
}
