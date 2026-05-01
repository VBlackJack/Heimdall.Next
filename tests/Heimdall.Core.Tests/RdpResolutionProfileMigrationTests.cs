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

public class RdpResolutionProfileMigrationTests
{
    [Fact]
    public void Migrate_MissingModeWithFixedDimensions_InfersFixed()
    {
        var server = new ServerProfileDto
        {
            RdpFixedWidth = 1920,
            RdpFixedHeight = 1080
        };

        RdpResolutionProfileMigration.Migrate(server);

        Assert.Equal(RdpResolutionMode.Fixed, server.RdpResolutionMode);
        Assert.True(server.HasRdpResolutionModeField);
    }

    [Fact]
    public void Migrate_MissingModeWithLegacyMultimon_InfersMultimonBeforeFixed()
    {
        var server = new ServerProfileDto
        {
            RdpMultiMonitor = true,
            RdpFixedWidth = 1920,
            RdpFixedHeight = 1080
        };

        RdpResolutionProfileMigration.Migrate(server);

        Assert.Equal(RdpResolutionMode.Multimon, server.RdpResolutionMode);
        Assert.True(server.RdpMultiMonitor);
    }

    [Fact]
    public void Migrate_ExplicitModeStillWinsOverLegacyMultimon()
    {
        var server = new ServerProfileDto
        {
            RdpResolutionMode = RdpResolutionMode.Fixed,
            RdpMultiMonitor = true,
            RdpFixedWidth = 1920,
            RdpFixedHeight = 1080
        };

        RdpResolutionProfileMigration.Migrate(server);

        Assert.Equal(RdpResolutionMode.Fixed, server.RdpResolutionMode);
    }

    [Fact]
    public void Migrate_MissingModeWithoutFixedDimensions_InfersFitWindow()
    {
        var server = new ServerProfileDto();

        RdpResolutionProfileMigration.Migrate(server);

        Assert.Equal(RdpResolutionMode.FitWindow, server.RdpResolutionMode);
        Assert.True(server.HasRdpResolutionModeField);
    }

    [Fact]
    public void Migrate_ExplicitModeWinsOverLegacyFixedDimensions()
    {
        var server = new ServerProfileDto
        {
            RdpResolutionMode = RdpResolutionMode.FitWindow,
            RdpFixedWidth = 1920,
            RdpFixedHeight = 1080
        };

        RdpResolutionProfileMigration.Migrate(server);

        Assert.Equal(RdpResolutionMode.FitWindow, server.RdpResolutionMode);
    }

    [Fact]
    public void Migrate_MultimonModeBackfillsLegacyBool()
    {
        var server = new ServerProfileDto
        {
            RdpResolutionMode = RdpResolutionMode.Multimon,
            RdpMultiMonitor = false
        };

        RdpResolutionProfileMigration.Migrate(server);

        Assert.True(server.RdpMultiMonitor);
    }

    [Fact]
    public void PrepareForSave_ModeWinsAndBackfillsMultimonBool()
    {
        var server = new ServerProfileDto
        {
            RdpResolutionMode = RdpResolutionMode.FitWindow,
            RdpMultiMonitor = true
        };

        RdpResolutionProfileMigration.PrepareForSave(server);

        Assert.False(server.RdpMultiMonitor);
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var server = new ServerProfileDto
        {
            RdpFixedWidth = 1920,
            RdpFixedHeight = 1080
        };

        RdpResolutionProfileMigration.Migrate(server);
        RdpResolutionProfileMigration.Migrate(server);

        Assert.Equal(RdpResolutionMode.Fixed, server.RdpResolutionMode);
        Assert.Equal(1920, server.RdpFixedWidth);
        Assert.Equal(1080, server.RdpFixedHeight);
    }
}
