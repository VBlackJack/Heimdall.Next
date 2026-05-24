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

using Heimdall.Rdp.Display;

namespace Heimdall.Rdp.Tests.Display;

public sealed class RdpSelectedMonitorValidatorTests
{
    [Fact]
    public void Validate_NullSelection_ReturnsEmptyWithoutWarning()
    {
        var warnings = new List<string>();

        var result = RdpSelectedMonitorValidator.Validate(null, 2, warnings.Add);

        Assert.Empty(result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Validate_EmptySelection_ReturnsEmptyWithoutWarning()
    {
        var warnings = new List<string>();

        var result = RdpSelectedMonitorValidator.Validate([], 2, warnings.Add);

        Assert.Empty(result);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NoAvailableMonitors_ReturnsEmptyWithWarning(int availableMonitorCount)
    {
        var warnings = new List<string>();

        var result = RdpSelectedMonitorValidator.Validate([0], availableMonitorCount, warnings.Add);

        Assert.Empty(result);
        Assert.Single(warnings);
        Assert.Contains("No local monitors were detected", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ValidIndices_PreservesConfiguredOrder()
    {
        var warnings = new List<string>();

        var result = RdpSelectedMonitorValidator.Validate([2, 0, 1], 3, warnings.Add);

        Assert.Equal([2, 0, 1], result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Validate_DuplicateIndices_RemovesDuplicatesAndPreservesFirstOccurrenceOrder()
    {
        var warnings = new List<string>();

        var result = RdpSelectedMonitorValidator.Validate([0, 1, 0, 1], 2, warnings.Add);

        Assert.Equal([0, 1], result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Validate_OutOfRangeIndex_DropsIndexWithWarning()
    {
        var warnings = new List<string>();

        var result = RdpSelectedMonitorValidator.Validate([0, 5, 1], 2, warnings.Add);

        Assert.Equal([0, 1], result);
        Assert.Single(warnings);
        Assert.Contains("selected monitor index 5", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NegativeIndex_DropsIndexWithWarning()
    {
        var warnings = new List<string>();

        var result = RdpSelectedMonitorValidator.Validate([-1, 0], 2, warnings.Add);

        Assert.Equal([0], result);
        Assert.Single(warnings);
        Assert.Contains("selected monitor index -1", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AllIndicesInvalid_ReturnsEmptyWithFallbackWarning()
    {
        var warnings = new List<string>();

        var result = RdpSelectedMonitorValidator.Validate([5], 2, warnings.Add);

        Assert.Empty(result);
        Assert.Contains(
            warnings,
            warning => warning.Contains("No selected monitor indices are valid", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_OutOfRangeIndexWithNullWarn_DoesNotThrow()
    {
        var result = RdpSelectedMonitorValidator.Validate([0, 5, 1], 2);

        Assert.Equal([0, 1], result);
    }
}
