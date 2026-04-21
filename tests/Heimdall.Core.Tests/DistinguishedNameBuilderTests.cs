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

using Heimdall.Core.Certificates;

namespace Heimdall.Core.Tests;

public sealed class DistinguishedNameBuilderTests
{
    [Fact]
    public void Build_WithCnOnly_ReturnsCnOnly()
    {
        var dn = DistinguishedNameBuilder.Build("server.local", string.Empty, string.Empty);

        Assert.Equal("CN=server.local", dn.Name);
    }

    [Fact]
    public void Build_WithCnAndOrg_IncludesOrg()
    {
        var dn = DistinguishedNameBuilder.Build("server.local", "Heimdall", string.Empty);

        Assert.Equal("CN=server.local, O=Heimdall", dn.Name);
    }

    [Fact]
    public void Build_WithCnOrgCountry_IncludesAll()
    {
        var dn = DistinguishedNameBuilder.Build("server.local", "Heimdall", "FR");

        Assert.Equal("CN=server.local, O=Heimdall, C=FR", dn.Name);
    }

    [Fact]
    public void Build_WithWhitespaceOrg_OmitsOrg()
    {
        var dn = DistinguishedNameBuilder.Build("server.local", "   ", "FR");

        Assert.Equal("CN=server.local, C=FR", dn.Name);
    }

    [Fact]
    public void Build_WithWhitespaceCountry_OmitsCountry()
    {
        var dn = DistinguishedNameBuilder.Build("server.local", "Heimdall", "   ");

        Assert.Equal("CN=server.local, O=Heimdall", dn.Name);
    }

    [Fact]
    public void Build_JoinsWithCommaSpace()
    {
        var dn = DistinguishedNameBuilder.Build("server.local", "Heimdall", "FR");

        Assert.Contains(", O=Heimdall, C=FR", dn.Name, StringComparison.Ordinal);
    }
}
