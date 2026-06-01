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
using Heimdall.Core.Models;

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
    public void CarriesPostConnectPayload_EnabledStepWithInput_ReturnsTrue()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "SSH",
            PostConnectSteps =
            [
                new PostConnectStep { Enabled = true, Input = "whoami" }
            ]
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesPostConnectPayload(profile);
        bool requiresConfirmation = ProfileExecutionTrust.RequiresPostConnectConfirmation(profile);

        Assert.True(carriesPayload);
        Assert.True(requiresConfirmation);
    }

    [Fact]
    public void CarriesPostConnectPayload_EnabledStepWithCommandLibraryId_ReturnsTrue()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "SSH",
            PostConnectSteps =
            [
                new PostConnectStep { Enabled = true, CommandLibraryId = "tail-log" }
            ]
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesPostConnectPayload(profile);

        Assert.True(carriesPayload);
    }

    [Fact]
    public void CarriesPostConnectPayload_DisabledRunnableStep_ReturnsFalse()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "SSH",
            PostConnectSteps =
            [
                new PostConnectStep { Enabled = false, Input = "whoami" }
            ]
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesPostConnectPayload(profile);
        bool requiresConfirmation = ProfileExecutionTrust.RequiresPostConnectConfirmation(profile);

        Assert.False(carriesPayload);
        Assert.False(requiresConfirmation);
    }

    [Fact]
    public void CarriesPostConnectPayload_EnabledEmptyStep_ReturnsFalse()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "SSH",
            PostConnectSteps =
            [
                new PostConnectStep { Enabled = true, Input = "   ", CommandLibraryId = "   " }
            ]
        };

        bool carriesPayload = ProfileExecutionTrust.CarriesPostConnectPayload(profile);
        bool requiresConfirmation = ProfileExecutionTrust.RequiresPostConnectConfirmation(profile);

        Assert.False(carriesPayload);
        Assert.False(requiresConfirmation);
    }

    [Fact]
    public void CarriesPostConnectPayload_EmptyOrNullSteps_ReturnsFalse()
    {
        ServerProfileDto emptyProfile = new()
        {
            ConnectionType = "SSH",
            PostConnectSteps = []
        };
        ServerProfileDto nullProfile = new()
        {
            ConnectionType = "SSH",
            PostConnectSteps = null!
        };

        Assert.False(ProfileExecutionTrust.CarriesPostConnectPayload(emptyProfile));
        Assert.False(ProfileExecutionTrust.RequiresPostConnectConfirmation(emptyProfile));
        Assert.False(ProfileExecutionTrust.CarriesPostConnectPayload(nullProfile));
        Assert.False(ProfileExecutionTrust.RequiresPostConnectConfirmation(nullProfile));
    }

    [Fact]
    public void RequiresPostConnectConfirmation_ConfirmedPayload_ReturnsFalse()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "SSH",
            ExecutionConfirmed = true,
            PostConnectSteps =
            [
                new PostConnectStep { Enabled = true, Input = "whoami" }
            ]
        };

        bool requiresConfirmation = ProfileExecutionTrust.RequiresPostConnectConfirmation(profile);

        Assert.False(requiresConfirmation);
    }

    [Fact]
    public void PostConnectOnlyPayload_IsIndependentFromLocalShellPayload()
    {
        ServerProfileDto profile = new()
        {
            ConnectionType = "SSH",
            PostConnectSteps =
            [
                new PostConnectStep { Enabled = true, Input = "whoami" }
            ]
        };

        bool carriesLocalPayload = ProfileExecutionTrust.CarriesLocalExecutionPayload(profile);
        bool carriesPostConnectPayload = ProfileExecutionTrust.CarriesPostConnectPayload(profile);

        Assert.False(carriesLocalPayload);
        Assert.True(carriesPostConnectPayload);
    }

    [Fact]
    public void NullProfile_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ProfileExecutionTrust.CarriesLocalExecutionPayload(null!));
        Assert.Throws<ArgumentNullException>(() => ProfileExecutionTrust.RequiresExecutionConfirmation(null!));
        Assert.Throws<ArgumentNullException>(() => ProfileExecutionTrust.CarriesPostConnectPayload(null!));
        Assert.Throws<ArgumentNullException>(() => ProfileExecutionTrust.RequiresPostConnectConfirmation(null!));
    }
}
