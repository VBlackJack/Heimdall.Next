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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class TcpConnectionStateTableTests
{
    [Theory]
    [InlineData(1u, "CLOSED")]
    [InlineData(2u, "LISTEN")]
    [InlineData(3u, "SYN_SENT")]
    [InlineData(4u, "SYN_RCVD")]
    [InlineData(5u, "ESTABLISHED")]
    [InlineData(6u, "FIN_WAIT1")]
    [InlineData(7u, "FIN_WAIT2")]
    [InlineData(8u, "CLOSE_WAIT")]
    [InlineData(9u, "CLOSING")]
    [InlineData(10u, "LAST_ACK")]
    [InlineData(11u, "TIME_WAIT")]
    [InlineData(12u, "DELETE_TCB")]
    [InlineData(99u, "99")]
    public void NameOf_ReturnsExpected(uint state, string expected)
    {
        Assert.Equal(expected, TcpConnectionStateTable.NameOf(state));
    }
}
