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

public sealed class PostConnectMigrationTests
{
    [Fact]
    public void Migrate_LegacyCommand_CreatesStructuredSteps()
    {
        var dto = new ServerProfileDto
        {
            PostConnectCommand = "pwd\nwhoami\nhostname",
            PostConnectDelayMs = 800
        };

        PostConnectMigration.Migrate(dto);

        Assert.Collection(
            dto.PostConnectSteps,
            step =>
            {
                Assert.Equal("pwd", step.Input);
                Assert.Equal(150, step.DelayMs);
                Assert.True(step.Enabled);
                Assert.Equal(PostConnectFailurePolicy.Continue, step.OnFailure);
                Assert.False(string.IsNullOrWhiteSpace(step.Id));
            },
            step => Assert.Equal("whoami", step.Input),
            step => Assert.Equal("hostname", step.Input));
        Assert.Equal("pwd\nwhoami\nhostname", dto.PostConnectCommand);
    }

    [Fact]
    public void Migrate_WhenStructuredStepsAlreadyExist_DoesNotReplaceThem()
    {
        var dto = new ServerProfileDto
        {
            PostConnectCommand = "legacy",
            PostConnectSteps =
            [
                new PostConnectStep
                {
                    Id = "step-1",
                    Input = "structured",
                    DelayMs = 250,
                    Enabled = false,
                    OnFailure = PostConnectFailurePolicy.Stop
                }
            ]
        };

        PostConnectMigration.Migrate(dto);

        Assert.Single(dto.PostConnectSteps);
        Assert.Equal("structured", dto.PostConnectSteps[0].Input);
        Assert.Equal(250, dto.PostConnectSteps[0].DelayMs);
        Assert.False(dto.PostConnectSteps[0].Enabled);
        Assert.Equal(PostConnectFailurePolicy.Stop, dto.PostConnectSteps[0].OnFailure);
    }

    [Fact]
    public void Migrate_WhitespaceLegacyCommand_IsNoOp()
    {
        var dto = new ServerProfileDto { PostConnectCommand = " \r\n " };

        PostConnectMigration.Migrate(dto);

        Assert.Empty(dto.PostConnectSteps);
    }

    [Fact]
    public void PrepareForSave_WithStructuredSteps_ClearsLegacyFields()
    {
        var dto = new ServerProfileDto
        {
            PostConnectCommand = "legacy",
            PostConnectDelayMs = 800,
            PostConnectSteps =
            [
                new PostConnectStep
                {
                    Id = "step-1",
                    Input = "pwd"
                }
            ]
        };

        PostConnectMigration.PrepareForSave(dto);

        Assert.Equal(string.Empty, dto.PostConnectCommand);
        Assert.Equal(0, dto.PostConnectDelayMs);
    }

    [Fact]
    public void PrepareForSave_WithoutStructuredSteps_PreservesLegacyFields()
    {
        var dto = new ServerProfileDto
        {
            PostConnectCommand = "legacy",
            PostConnectDelayMs = 800
        };

        PostConnectMigration.PrepareForSave(dto);

        Assert.Equal("legacy", dto.PostConnectCommand);
        Assert.Equal(800, dto.PostConnectDelayMs);
    }

    [Fact]
    public void PrepareForSave_NormalizesMissingIdsNegativeDelayAndNullInput()
    {
        var dto = new ServerProfileDto
        {
            PostConnectSteps =
            [
                new PostConnectStep
                {
                    Id = "",
                    Input = null!,
                    DelayMs = -25
                }
            ]
        };

        PostConnectMigration.PrepareForSave(dto);

        var step = Assert.Single(dto.PostConnectSteps);
        Assert.False(string.IsNullOrWhiteSpace(step.Id));
        Assert.Equal(string.Empty, step.Input);
        Assert.Equal(0, step.DelayMs);
    }
}
