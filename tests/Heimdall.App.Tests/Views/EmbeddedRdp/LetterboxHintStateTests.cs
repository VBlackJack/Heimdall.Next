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

using Heimdall.App.Views.EmbeddedRdp;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

public sealed class LetterboxHintStateTests
{
    [Fact]
    public void ShouldShow_WhenLetterboxFirstBecomesActive_ReturnsTrueOnce()
    {
        var state = new LetterboxHintState();

        Assert.True(state.ShouldShow(RdpResolutionMode.Fixed, usesFixedLocalResolution: true, isLetterboxActive: true));
        Assert.False(state.ShouldShow(RdpResolutionMode.Fixed, usesFixedLocalResolution: true, isLetterboxActive: true));
    }

    [Fact]
    public void ShouldShow_WhenLetterboxIsInactive_ReturnsFalse()
    {
        var state = new LetterboxHintState();

        Assert.False(state.ShouldShow(RdpResolutionMode.Fixed, usesFixedLocalResolution: true, isLetterboxActive: false));
    }

    [Fact]
    public void ShouldShow_WhenResolutionChoiceLeavesAndReturnsToFixed_AllowsHintAgain()
    {
        var state = new LetterboxHintState();

        Assert.True(state.ShouldShow(RdpResolutionMode.Fixed, usesFixedLocalResolution: true, isLetterboxActive: true));
        state.Observe(RdpResolutionMode.Fixed, usesFixedLocalResolution: false);

        Assert.True(state.ShouldShow(RdpResolutionMode.Fixed, usesFixedLocalResolution: true, isLetterboxActive: true));
    }

    [Fact]
    public void ShouldShow_WhenResolutionModeChanges_AllowsHintAgain()
    {
        var state = new LetterboxHintState();

        Assert.True(state.ShouldShow(RdpResolutionMode.Fixed, usesFixedLocalResolution: true, isLetterboxActive: true));
        state.Observe(RdpResolutionMode.FitWindow, usesFixedLocalResolution: false);

        Assert.True(state.ShouldShow(RdpResolutionMode.Fixed, usesFixedLocalResolution: true, isLetterboxActive: true));
    }
}
