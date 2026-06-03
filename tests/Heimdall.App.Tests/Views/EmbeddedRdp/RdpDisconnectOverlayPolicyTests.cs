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

using Heimdall.App.Views;

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

public sealed class RdpDisconnectOverlayPolicyTests
{
    [Theory]
    [InlineData(true, 0, true, "user-initiated trumps any reason")]
    [InlineData(true, 516, true, "user-initiated trumps SocketConnectFailed")]
    [InlineData(true, 2055, true, "user-initiated trumps BadCredentials")]
    [InlineData(true, 3, true, "user-initiated trumps AdminDisconnect")]
    [InlineData(false, 0, true, "NoInfo is a clean exit")]
    [InlineData(false, 1, true, "LocalUser is a clean exit")]
    [InlineData(false, 2, true, "UserLogoff is a clean exit")]
    [InlineData(false, 3, false, "AdminDisconnect warrants the overlay")]
    [InlineData(false, 4, false, "code 4 is not a clean-exit code")]
    [InlineData(false, 264, false, "ConnectionTimeout warrants the overlay")]
    [InlineData(false, 516, false, "SocketConnectFailed warrants the overlay")]
    [InlineData(false, 2055, false, "BadCredentials warrants the overlay")]
    [InlineData(false, 4360, false, "ResolutionChangeTimeout warrants the overlay")]
    public void ShouldSuppressReconnectOverlay_ReturnsExpected(
        bool userInitiated,
        int reason,
        bool expected,
        string description)
    {
        _ = description;

        bool actual = EmbeddedRdpView.ShouldSuppressReconnectOverlay(userInitiated, reason);

        Assert.Equal(expected, actual);
    }
}
