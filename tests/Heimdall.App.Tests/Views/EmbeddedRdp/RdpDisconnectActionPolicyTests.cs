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

public sealed class RdpDisconnectActionPolicyTests
{
    [Theory]
    [InlineData(2055)]
    [InlineData(2308)]
    [InlineData(2311)]
    [InlineData(2825)]
    [InlineData(3080)]
    [InlineData(3848)]
    [InlineData(4360)]
    public void ShouldOfferEditProfile_ReturnsTrueForProfileRemediationCodes(int disconnectCode)
    {
        var actual = RdpDisconnectActionPolicy.ShouldOfferEditProfile(disconnectCode);

        Assert.True(actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(260)]
    [InlineData(262)]
    [InlineData(264)]
    [InlineData(516)]
    [InlineData(772)]
    [InlineData(1030)]
    [InlineData(1796)]
    [InlineData(2056)]
    [InlineData(2567)]
    [InlineData(2822)]
    [InlineData(3335)]
    [InlineData(3591)]
    [InlineData(3847)]
    [InlineData(9999)]
    public void ShouldOfferEditProfile_ReturnsFalseForOtherCodes(int? disconnectCode)
    {
        var actual = RdpDisconnectActionPolicy.ShouldOfferEditProfile(disconnectCode);

        Assert.False(actual);
    }

    [Theory]
    [InlineData(2055)]
    [InlineData(2308)]
    [InlineData(2311)]
    [InlineData(2825)]
    [InlineData(3080)]
    [InlineData(3848)]
    [InlineData(4360)]
    public void ResolvePrimaryAction_ReturnsEditProfile_ForProfileRemediationCodes(int disconnectCode)
    {
        var actual = RdpDisconnectActionPolicy.ResolvePrimaryAction(disconnectCode);

        Assert.Equal(RdpOverlayPrimaryAction.EditProfile, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(2054)]
    [InlineData(2056)]
    [InlineData(4359)]
    [InlineData(4361)]
    [InlineData(99999)]
    public void ResolvePrimaryAction_ReturnsReconnect_ForOtherCodes(int disconnectCode)
    {
        var actual = RdpDisconnectActionPolicy.ResolvePrimaryAction(disconnectCode);

        Assert.Equal(RdpOverlayPrimaryAction.Reconnect, actual);
    }

    [Fact]
    public void ResolvePrimaryAction_ReturnsReconnect_ForNullCode()
    {
        var actual = RdpDisconnectActionPolicy.ResolvePrimaryAction(null);

        Assert.Equal(RdpOverlayPrimaryAction.Reconnect, actual);
    }

    [Theory]
    [InlineData(2055)]
    [InlineData(2308)]
    [InlineData(2311)]
    [InlineData(2825)]
    [InlineData(3080)]
    [InlineData(3848)]
    [InlineData(4360)]
    public void ResolvePrimaryAction_EditProfileCodes_AreAlsoOfferedAsEditProfileActions(int disconnectCode)
    {
        var actual = RdpDisconnectActionPolicy.ResolvePrimaryAction(disconnectCode);

        Assert.Equal(RdpOverlayPrimaryAction.EditProfile, actual);
        Assert.True(RdpDisconnectActionPolicy.ShouldOfferEditProfile(disconnectCode));
    }
}
