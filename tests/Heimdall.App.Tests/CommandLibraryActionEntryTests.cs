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

using Heimdall.App.ViewModels.CommandLibrary;
using TwinShell.Core.Enums;

namespace Heimdall.App.Tests;

public sealed class CommandLibraryActionEntryTests
{
    [Theory]
    [InlineData(CriticalityLevel.Info, "TextSecondaryBrush")]
    [InlineData(CriticalityLevel.Run, "WarningBrush")]
    [InlineData(CriticalityLevel.Dangerous, "ErrorBrush")]
    [InlineData((CriticalityLevel)999, "TextSecondaryBrush")]
    public void RiskBrushKey_MapsCriticalityLevel_ToExpectedResourceKey(
        CriticalityLevel level,
        string expectedResourceKey)
    {
        var action = CommandLibraryTestHelpers.CreateLinuxAction("action-1", "Test action", "echo ok");
        action.Level = level;

        var entry = new CommandLibraryActionEntry(action, null!);

        Assert.Equal(expectedResourceKey, entry.RiskBrushKey);
    }
}
