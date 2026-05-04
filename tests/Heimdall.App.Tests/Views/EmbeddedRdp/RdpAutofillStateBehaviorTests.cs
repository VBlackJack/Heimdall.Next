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

public sealed class RdpAutofillStateBehaviorTests
{
    [Theory]
    [InlineData((int)RdpAutofillStateForBehavior.Filled, true, true)]
    [InlineData((int)RdpAutofillStateForBehavior.Filled, false, true)]
    [InlineData((int)RdpAutofillStateForBehavior.TimedOut, false, false)]
    [InlineData((int)RdpAutofillStateForBehavior.TimedOut, true, true)]
    [InlineData((int)RdpAutofillStateForBehavior.Failed, false, false)]
    [InlineData((int)RdpAutofillStateForBehavior.Failed, true, true)]
    [InlineData((int)RdpAutofillStateForBehavior.None, false, false)]
    [InlineData((int)RdpAutofillStateForBehavior.Searching, false, false)]
    public void ShouldAutoDismiss_RespectsTerminalStateAndConnection(
        int stateValue,
        bool isConnected,
        bool expected)
    {
        var state = (RdpAutofillStateForBehavior)stateValue;

        var actual = RdpAutofillStateBehavior.ShouldAutoDismiss(state, isConnected);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData((int)RdpAutofillStateForBehavior.TimedOut, true, true)]
    [InlineData((int)RdpAutofillStateForBehavior.TimedOut, false, false)]
    [InlineData((int)RdpAutofillStateForBehavior.Failed, true, true)]
    [InlineData((int)RdpAutofillStateForBehavior.Failed, false, false)]
    [InlineData((int)RdpAutofillStateForBehavior.Filled, true, false)]
    [InlineData((int)RdpAutofillStateForBehavior.None, true, false)]
    [InlineData((int)RdpAutofillStateForBehavior.Searching, true, false)]
    [InlineData((int)RdpAutofillStateForBehavior.None, false, false)]
    public void CanRetry_RequiresTerminalFailureAndPromptPhase(
        int stateValue,
        bool canShowCredentialPrompt,
        bool expected)
    {
        var state = (RdpAutofillStateForBehavior)stateValue;

        var actual = RdpAutofillStateBehavior.CanRetry(state, canShowCredentialPrompt);

        Assert.Equal(expected, actual);
    }
}
