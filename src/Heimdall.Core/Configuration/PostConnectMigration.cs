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

using Heimdall.Core.Models;

namespace Heimdall.Core.Configuration;

public static class PostConnectMigration
{
    public static void Migrate(ServerProfileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.PostConnectSteps.Count > 0)
        {
            Normalize(dto.PostConnectSteps);
            return;
        }

        if (string.IsNullOrWhiteSpace(dto.PostConnectCommand))
        {
            return;
        }

        dto.PostConnectSteps = dto.PostConnectCommand
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => new PostConnectStep
            {
                Input = line,
                DelayMs = 150,
                Enabled = true,
                OnFailure = PostConnectFailurePolicy.Continue
            })
            .ToList();
    }

    public static void PrepareForSave(ServerProfileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        Normalize(dto.PostConnectSteps);
        if (dto.PostConnectSteps.Count == 0)
        {
            return;
        }

        dto.PostConnectCommand = string.Empty;
        dto.PostConnectDelayMs = 0;
    }

    private static void Normalize(List<PostConnectStep> steps)
    {
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var step = steps[i];
            if (step is null)
            {
                steps.RemoveAt(i);
                continue;
            }

            step.Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString() : step.Id;
            step.DelayMs = step.DelayMs < 0 ? 0 : step.DelayMs;
            step.Input ??= string.Empty;
        }
    }
}
