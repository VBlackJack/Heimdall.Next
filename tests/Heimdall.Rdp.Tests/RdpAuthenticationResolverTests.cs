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

using Heimdall.Rdp;

namespace Heimdall.Rdp.Tests;

public sealed class RdpAuthenticationResolverTests
{
    [Fact]
    public void Resolve_WithNlaEnabled_ReturnsAuthLevel2AndCredSsp()
    {
        RdpAuthenticationSettings settings = RdpAuthenticationResolver.Resolve(true);

        Assert.Equal(2, settings.AuthenticationLevel);
        Assert.True(settings.EnableCredSspSupport);
    }

    [Fact]
    public void Resolve_WithNlaDisabled_ReturnsAuthLevel0AndDisablesCredSsp()
    {
        RdpAuthenticationSettings settings = RdpAuthenticationResolver.Resolve(false);

        Assert.Equal(0, settings.AuthenticationLevel);
        Assert.False(settings.EnableCredSspSupport);
    }
}
