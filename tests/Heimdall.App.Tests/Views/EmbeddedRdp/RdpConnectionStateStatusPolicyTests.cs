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

public sealed class RdpConnectionStateStatusPolicyTests
{
    [Theory]
    [InlineData("server-1", "server-1", false, false, true)]
    [InlineData("server-1", "server-2", false, false, false)]
    [InlineData("server-1", "", false, false, false)]
    [InlineData("server-1", null, false, false, false)]
    [InlineData("server-1", "server-1", true, false, false)]
    [InlineData("server-1", "server-1", false, true, false)]
    [InlineData("server-1", "SERVER-1", false, false, false)]
    [InlineData("server-1", "server-1 ", false, false, false)]
    public void ShouldHandleStateChange_ReturnsExpected(
        string serverId,
        string? targetServerId,
        bool comDrivenStatusActive,
        bool disposed,
        bool expected)
    {
        var actual = EmbeddedRdpView.ShouldHandleStateChange(
            serverId,
            targetServerId,
            comDrivenStatusActive,
            disposed);

        Assert.Equal(expected, actual);
    }
}
