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

using Heimdall.App.Services;

namespace Heimdall.App.Tests;

public sealed class EmbeddedSessionManagerRdpOptionsTests
{
    [Fact]
    public void ResolveRdpResizeEnableDelayMs_ProfileNullReturnsGlobal()
    {
        var result = EmbeddedSessionManager.ResolveRdpResizeEnableDelayMs(null, 15000);

        Assert.Equal(15000, result);
    }

    [Fact]
    public void ResolveRdpResizeEnableDelayMs_ProfileZeroReturnsZero()
    {
        var result = EmbeddedSessionManager.ResolveRdpResizeEnableDelayMs(0, 15000);

        Assert.Equal(0, result);
    }

    [Fact]
    public void ResolveRdpResizeEnableDelayMs_ProfilePositiveReturnsProfile()
    {
        var result = EmbeddedSessionManager.ResolveRdpResizeEnableDelayMs(3000, 15000);

        Assert.Equal(3000, result);
    }

    [Fact]
    public void ResolveRdpResizeEnableDelayMs_ProfileNegativeClampsToZero()
    {
        var result = EmbeddedSessionManager.ResolveRdpResizeEnableDelayMs(-1, 15000);

        Assert.Equal(0, result);
    }

    [Fact]
    public void ResolveRdpResizeEnableDelayMs_GlobalNegativeReturnsDefault()
    {
        var result = EmbeddedSessionManager.ResolveRdpResizeEnableDelayMs(null, -1);

        Assert.Equal(EmbeddedSessionManager.DefaultRdpResizeEnableDelayMs, result);
    }
}
