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

using Heimdall.Core.Ssh;

namespace Heimdall.Core.Tests.Ssh;

public sealed class HostKeyFormatsTests
{
    [Fact]
    public void ComputeSha256Fingerprint_KnownInput_ProducesOpenSshFormat()
    {
        var fingerprint = HostKeyFormats.ComputeSha256Fingerprint([0x01, 0x02, 0x03, 0x04, 0x05]);

        Assert.Equal("SHA256:dPgf4WfZm0y0HW0MzagieMrunz4vJdXlo5Nv89zsYNA", fingerprint);
    }

    [Fact]
    public void MakeKey_Ipv4Host_ProducesHostColonPort()
    {
        Assert.Equal("10.0.0.1:22", HostKeyFormats.MakeKey("10.0.0.1", 22));
    }

    [Fact]
    public void MakeKey_Ipv6Host_WrapsInBrackets()
    {
        Assert.Equal("[::1]:2222", HostKeyFormats.MakeKey("::1", 2222));
    }
}
