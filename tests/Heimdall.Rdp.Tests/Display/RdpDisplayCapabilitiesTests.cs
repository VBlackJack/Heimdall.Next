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

public sealed class RdpDisplayCapabilitiesTests
{
    [Fact]
    public void IsMultimonAvailable_FalseForZeroScreens()
    {
        Assert.False(RdpDisplayCapabilities.IsMultimonAvailable(0));
    }

    [Fact]
    public void IsMultimonAvailable_FalseForOneScreen()
    {
        Assert.False(RdpDisplayCapabilities.IsMultimonAvailable(1));
    }

    [Fact]
    public void IsMultimonAvailable_TrueForTwoScreens()
    {
        Assert.True(RdpDisplayCapabilities.IsMultimonAvailable(2));
    }

    [Fact]
    public void IsMultimonAvailable_TrueForThreeScreens()
    {
        Assert.True(RdpDisplayCapabilities.IsMultimonAvailable(3));
    }
}
