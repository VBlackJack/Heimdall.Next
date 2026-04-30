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

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

public sealed class RdpCancelConnectVisibilityTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, false)]
    [InlineData(4, false, true)]
    public void ResolveVisibility_ReturnsExpected(
        int phaseValue,
        bool expectedCancelConnectVisible,
        bool expectedDisconnectVisible)
    {
        var phase = (RdpConnectionPhase)phaseValue;
        var (cancelConnectVisible, disconnectVisible) =
            RdpConnectionPhasePolicy.ResolveVisibility(phase);

        Assert.Equal(expectedCancelConnectVisible, cancelConnectVisible);
        Assert.Equal(expectedDisconnectVisible, disconnectVisible);
    }
}
