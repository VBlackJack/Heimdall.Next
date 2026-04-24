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
using Heimdall.Ssh.Agents;

namespace Heimdall.Ssh.Tests;

public sealed class SshAgentKeyWrapperTests
{
    [Fact]
    public void RsaKey_RegistersSha2AlgorithmsBeforeLegacySshRsa()
    {
        var key = new FakeAgentKey("ssh-rsa");
        var wrapper = new SshAgentKeyWrapper(key);

        var names = wrapper.HostKeyAlgorithms.Select(algorithm => algorithm.Name).ToList();

        Assert.Equal(["rsa-sha2-256", "rsa-sha2-512", "ssh-rsa"], names);
    }

    [Theory]
    [InlineData("rsa-sha2-256", SshAgentSignFlags.RsaSha2_256)]
    [InlineData("rsa-sha2-512", SshAgentSignFlags.RsaSha2_512)]
    [InlineData("ssh-rsa", SshAgentSignFlags.None)]
    public void RsaAlgorithms_PassExpectedSignFlags(string algorithmName, SshAgentSignFlags expectedFlags)
    {
        var key = new FakeAgentKey("ssh-rsa");
        var wrapper = new SshAgentKeyWrapper(key);
        var algorithm = wrapper.HostKeyAlgorithms.Single(a => a.Name == algorithmName);

        var signature = algorithm.Sign([9, 9]);

        Assert.Equal([4, 5, 6], signature);
        Assert.Equal(expectedFlags, key.LastSignFlags);
    }

    [Fact]
    public void NonRsaKey_RegistersSingleNativeAlgorithm()
    {
        var key = new FakeAgentKey("ssh-ed25519");
        var wrapper = new SshAgentKeyWrapper(key);

        var algorithm = Assert.Single(wrapper.HostKeyAlgorithms);
        Assert.Equal("ssh-ed25519", algorithm.Name);
    }

    private sealed class FakeAgentKey(string keyType) : ISshAgentKey
    {
        public SshAgentSignFlags LastSignFlags { get; private set; }
        public string Comment => "fake";
        public string KeyType => keyType;
        public byte[] PublicKeyBlob => OpenSshAgentProtocolTests.BuildKeyBlob(keyType);

        public byte[] Sign(byte[] data, SshAgentSignFlags flags)
        {
            LastSignFlags = flags;
            return [4, 5, 6];
        }
    }
}
