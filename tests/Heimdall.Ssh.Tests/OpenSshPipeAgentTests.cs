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
using System.IO.Pipes;
using Heimdall.Core.Ssh;
using Heimdall.Ssh.OpenSsh;

namespace Heimdall.Ssh.Tests;

public sealed class OpenSshPipeAgentTests
{
    [Fact]
    public void IsAvailable_WhenPipeMissing_ReturnsFalseFast()
    {
        var agent = new OpenSshPipeAgent(
            $"heimdall-test-{Guid.NewGuid():N}",
            availabilityTimeoutMs: 25,
            requestTimeoutMs: 250);

        var available = agent.IsAvailable();

        Assert.False(available);
    }

    [Fact]
    public async Task GetIdentities_ReadsResponseFromNamedPipe()
    {
        var pipeName = $"heimdall-test-{Guid.NewGuid():N}";
        var keyBlob = OpenSshAgentProtocolTests.BuildKeyBlob("ssh-rsa");
        var response = OpenSshAgentProtocol.BuildMessage(
            OpenSshAgentProtocol.SshAgentIdentitiesAnswer,
            writer =>
            {
                writer.WriteUInt32(1);
                writer.WriteString(keyBlob);
                writer.WriteUtf8String("rsa@test");
            });
        var serverTask = RunSingleRequestPipeServerAsync(
            pipeName,
            expectedRequestType: OpenSshAgentProtocol.SshAgentcRequestIdentities,
            response);
        var agent = new OpenSshPipeAgent(pipeName, availabilityTimeoutMs: 1000, requestTimeoutMs: 1000);

        var identities = agent.GetIdentities();
        await serverTask;

        var identity = Assert.Single(identities);
        Assert.Equal("ssh-rsa", identity.KeyType);
        Assert.Equal("rsa@test", identity.Comment);
        Assert.Equal(keyBlob, identity.PublicKeyBlob);
    }

    [Fact]
    public async Task GetIdentities_WhenPipeClosesAfterConnect_ReturnsEmpty()
    {
        var pipeName = $"heimdall-test-{Guid.NewGuid():N}";
        var serverReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            await using var server = CreateServer(pipeName);
            serverReady.SetResult(true);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await server.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
        });
        var agent = new OpenSshPipeAgent(pipeName, availabilityTimeoutMs: 1000, requestTimeoutMs: 1000);

        await serverReady.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var identities = agent.GetIdentities();
        await serverTask;

        Assert.Empty(identities);
    }

    [Fact]
    public async Task AgentKeySign_SendsFlagsAndReturnsSignature()
    {
        var pipeName = $"heimdall-test-{Guid.NewGuid():N}";
        var keyBlob = OpenSshAgentProtocolTests.BuildKeyBlob("ssh-rsa");
        var data = new byte[] { 9, 8, 7 };
        var signatureBlob = OpenSshAgentProtocolTests.BuildSignatureBlob("rsa-sha2-512", [6, 5, 4]);
        var response = OpenSshAgentProtocol.BuildMessage(
            OpenSshAgentProtocol.SshAgentSignResponse,
            writer => writer.WriteString(signatureBlob));
        var serverTask = Task.Run(async () =>
        {
            await using var server = CreateServer(pipeName);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await server.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);

            var request = ReadFramedMessage(server);
            var reader = new OpenSshAgentProtocol.AgentMessageReader(request);
            Assert.Equal(OpenSshAgentProtocol.SshAgentcSignRequest, reader.ReadByte());
            Assert.Equal(keyBlob, reader.ReadString());
            Assert.Equal(data, reader.ReadString());
            Assert.Equal((uint)SshAgentSignFlags.RsaSha2_512, reader.ReadUInt32());
            reader.EnsureFullyRead();

            await server.WriteAsync(response).ConfigureAwait(false);
            await server.FlushAsync(cts.Token).ConfigureAwait(false);
        });
        var agent = new OpenSshPipeAgent(pipeName, availabilityTimeoutMs: 1000, requestTimeoutMs: 1000);
        var key = new OpenSshAgentKey(agent, keyBlob, "rsa@test", "ssh-rsa");

        var signature = key.Sign(data, SshAgentSignFlags.RsaSha2_512);
        await serverTask;

        Assert.Equal(signatureBlob, signature);
    }

    private static async Task RunSingleRequestPipeServerAsync(
        string pipeName,
        byte expectedRequestType,
        byte[] response)
    {
        await using var server = CreateServer(pipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await server.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);

        var request = ReadFramedMessage(server);
        var reader = new OpenSshAgentProtocol.AgentMessageReader(request);
        Assert.Equal(expectedRequestType, reader.ReadByte());

        await server.WriteAsync(response, cts.Token).ConfigureAwait(false);
        await server.FlushAsync(cts.Token).ConfigureAwait(false);
    }

    private static NamedPipeServerStream CreateServer(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static byte[] ReadFramedMessage(Stream stream)
    {
        var lengthBytes = ReadExact(stream, sizeof(uint));
        var length = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        var payload = ReadExact(stream, (int)length);
        var message = new byte[sizeof(uint) + payload.Length];
        lengthBytes.CopyTo(message.AsSpan(0, sizeof(uint)));
        payload.CopyTo(message.AsSpan(sizeof(uint)));
        return message;
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(buffer, offset, length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Pipe closed before the expected bytes were read.");
            }

            offset += read;
        }

        return buffer;
    }
}
