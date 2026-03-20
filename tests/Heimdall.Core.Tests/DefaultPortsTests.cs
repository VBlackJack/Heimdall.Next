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

using Heimdall.Core.Models;

namespace Heimdall.Core.Tests;

public class DefaultPortsTests
{
    [Fact]
    public void Rdp_Is3389()
    {
        Assert.Equal(3389, DefaultPorts.Rdp);
    }

    [Fact]
    public void Ssh_Is22()
    {
        Assert.Equal(22, DefaultPorts.Ssh);
    }

    [Fact]
    public void Vnc_Is5900()
    {
        Assert.Equal(5900, DefaultPorts.Vnc);
    }

    [Fact]
    public void Ftp_Is21()
    {
        Assert.Equal(21, DefaultPorts.Ftp);
    }

    [Fact]
    public void Telnet_Is23()
    {
        Assert.Equal(23, DefaultPorts.Telnet);
    }

    [Fact]
    public void Http_Is8080()
    {
        Assert.Equal(8080, DefaultPorts.Http);
    }

    [Fact]
    public void Tftp_Is69()
    {
        Assert.Equal(69, DefaultPorts.Tftp);
    }

    [Fact]
    public void Sftp_Is22()
    {
        Assert.Equal(22, DefaultPorts.Sftp);
    }
}
