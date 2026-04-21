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

using System.IO;
using Heimdall.App.ViewModels;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed class ProfileOriginDisplayTests
{
    [Fact]
    public void GetBadgeCode_Manual_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ProfileOriginDisplay.GetBadgeCode(ProfileOrigin.Manual));
    }

    [Theory]
    [InlineData(ProfileOrigin.ImportRdp, "RDP")]
    [InlineData(ProfileOrigin.ImportOpenSsh, "OSSH")]
    [InlineData(ProfileOrigin.ImportPutty, "PTY")]
    [InlineData(ProfileOrigin.ImportMRemoteNg, "MRNG")]
    [InlineData(ProfileOrigin.ImportMobaXterm, "MXTM")]
    [InlineData(ProfileOrigin.ImportRdcMan, "RDCM")]
    public void GetBadgeCode_EachImportOrigin_ReturnsExpectedCode(ProfileOrigin origin, string expected)
    {
        Assert.Equal(expected, ProfileOriginDisplay.GetBadgeCode(origin));
    }

    [Theory]
    [InlineData(ProfileOrigin.Manual, "")]
    [InlineData(ProfileOrigin.ImportRdp, "Imported from RDP file")]
    [InlineData(ProfileOrigin.ImportOpenSsh, "Imported from OpenSSH config")]
    [InlineData(ProfileOrigin.ImportPutty, "Imported from PuTTY registry")]
    [InlineData(ProfileOrigin.ImportMRemoteNg, "Imported from mRemoteNG")]
    [InlineData(ProfileOrigin.ImportMobaXterm, "Imported from MobaXterm")]
    [InlineData(ProfileOrigin.ImportRdcMan, "Imported from RDCMan")]
    public async Task GetDisplayName_EachOrigin_ResolvesViaLocalizer(ProfileOrigin origin, string expected)
    {
        var localizer = new LocalizationManager();
        await localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");

        Assert.Equal(expected, ProfileOriginDisplay.GetDisplayName(origin, localizer));
    }
}
