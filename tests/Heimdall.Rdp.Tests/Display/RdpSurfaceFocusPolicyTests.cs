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

public sealed class RdpSurfaceFocusPolicyTests
{
    [Theory]
    [InlineData(true, false, true, false, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, false, true, true, false)]
    public void ShouldFocusSurface_OnlyWhenAllFocusTheftGuardsAllow(
        bool viewVisible,
        bool reconnectOverlayVisible,
        bool windowIsForeground,
        bool autofillInFlight,
        bool expected)
    {
        bool result = RdpSurfaceFocusPolicy.ShouldFocusSurface(
            viewVisible,
            reconnectOverlayVisible,
            windowIsForeground,
            autofillInFlight);

        Assert.Equal(expected, result);
    }
}
