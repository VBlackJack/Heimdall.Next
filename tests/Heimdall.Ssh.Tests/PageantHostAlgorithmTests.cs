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

using Heimdall.Ssh.Pageant;

namespace Heimdall.Ssh.Tests;

public sealed class PageantHostAlgorithmTests
{
    [Fact]
    public void Sign_ReturnsBlobFromAgentUnchanged()
    {
        byte[] publicKeyBlob =
        [
            0x00, 0x00, 0x00, 0x07,
            (byte)'s', (byte)'s', (byte)'h', (byte)'-', (byte)'r', (byte)'s', (byte)'a'
        ];
        byte[] dataToSign = [0x01, 0x02, 0x03, 0x04];
        byte[] expectedAgentResponse = [0x10, 0x20, 0x30, 0x40, 0x50];

        var fakeClient = new FakePageantClient(expectedAgentResponse);
        var algorithm = new PageantHostAlgorithm(
            "ssh-rsa",
            publicKeyBlob,
            fakeClient,
            signFlags: 0);

        var actual = algorithm.Sign(dataToSign);

        Assert.Equal(expectedAgentResponse, actual);
        Assert.Equal(publicKeyBlob, fakeClient.LastKeyBlob);
        Assert.Equal(dataToSign, fakeClient.LastData);
        Assert.Equal(0u, fakeClient.LastFlags);
    }

    private sealed class FakePageantClient(byte[] response) : IPageantClient
    {
        public byte[]? LastKeyBlob { get; private set; }

        public byte[]? LastData { get; private set; }

        public uint LastFlags { get; private set; }

        public byte[] SignData(byte[] keyBlob, byte[] data, uint flags = 0)
        {
            LastKeyBlob = keyBlob;
            LastData = data;
            LastFlags = flags;
            return response;
        }
    }
}
