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

using System.Buffers.Binary;
using System.Text;
using Heimdall.Core.Ssh;
using Heimdall.Ssh.OpenSsh;

namespace Heimdall.Ssh.Tests;

public sealed class OpenSshAgentProtocolTests
{
    [Fact]
    public void BuildRequestIdentitiesMessage_ProducesExpectedBytes()
    {
        var request = OpenSshAgentProtocol.BuildRequestIdentitiesMessage();

        Assert.Equal([0, 0, 0, 1, OpenSshAgentProtocol.SshAgentcRequestIdentities], request);
    }

    [Fact]
    public void ParseIdentitiesResponse_ParsesKeys()
    {
        var blob = BuildKeyBlob("ssh-ed25519");
        var response = OpenSshAgentProtocol.BuildMessage(
            OpenSshAgentProtocol.SshAgentIdentitiesAnswer,
            writer =>
            {
                writer.WriteUInt32(1);
                writer.WriteString(blob);
                writer.WriteUtf8String("ed25519@workstation");
            });
        var agent = new OpenSshPipeAgent($"heimdall-test-{Guid.NewGuid():N}");

        var identities = OpenSshAgentProtocol.ParseIdentitiesResponse(response, agent);

        var identity = Assert.Single(identities);
        Assert.Equal("ssh-ed25519", identity.KeyType);
        Assert.Equal("ed25519@workstation", identity.Comment);
        Assert.Equal(blob, identity.PublicKeyBlob);
    }

    [Fact]
    public void ParseIdentitiesResponse_FailureType_ReturnsEmpty()
    {
        var response = OpenSshAgentProtocol.BuildMessage(
            OpenSshAgentProtocol.SshAgentFailure,
            static _ => { });
        var agent = new OpenSshPipeAgent($"heimdall-test-{Guid.NewGuid():N}");

        var identities = OpenSshAgentProtocol.ParseIdentitiesResponse(response, agent);

        Assert.Empty(identities);
    }

    [Theory]
    [InlineData(SshAgentSignFlags.RsaSha2_256, 0x02u)]
    [InlineData(SshAgentSignFlags.RsaSha2_512, 0x04u)]
    public void BuildSignRequestMessage_WritesRsaSha2Flags(
        SshAgentSignFlags signFlags,
        uint expectedWireFlags)
    {
        var blob = BuildKeyBlob("ssh-rsa");
        var data = new byte[] { 0xCA, 0xFE };

        var request = OpenSshAgentProtocol.BuildSignRequestMessage(blob, data, signFlags);

        var reader = new OpenSshAgentProtocol.AgentMessageReader(request);
        Assert.Equal(OpenSshAgentProtocol.SshAgentcSignRequest, reader.ReadByte());
        Assert.Equal(blob, reader.ReadString());
        Assert.Equal(data, reader.ReadString());
        Assert.Equal(expectedWireFlags, reader.ReadUInt32());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void ParseSignResponse_ReturnsSignatureBlob()
    {
        var signatureBlob = BuildSignatureBlob("rsa-sha2-256", [1, 2, 3]);
        var response = OpenSshAgentProtocol.BuildMessage(
            OpenSshAgentProtocol.SshAgentSignResponse,
            writer => writer.WriteString(signatureBlob));

        var parsed = OpenSshAgentProtocol.ParseSignResponse(response);

        Assert.Equal(signatureBlob, parsed);
    }

    internal static byte[] BuildKeyBlob(string keyType)
    {
        var keyTypeBytes = Encoding.ASCII.GetBytes(keyType);
        var blob = new byte[sizeof(uint) + keyTypeBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(
            blob.AsSpan(0, sizeof(uint)),
            (uint)keyTypeBytes.Length);
        keyTypeBytes.CopyTo(blob.AsSpan(sizeof(uint)));
        return blob;
    }

    internal static byte[] BuildSignatureBlob(string algorithm, byte[] signature)
    {
        var writer = new OpenSshAgentProtocol.AgentMessageWriter();
        writer.WriteUtf8String(algorithm);
        writer.WriteString(signature);
        return writer.ToArray();
    }
}
