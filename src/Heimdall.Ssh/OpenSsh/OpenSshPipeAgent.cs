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

namespace Heimdall.Ssh.OpenSsh;

/// <summary>
/// Windows OpenSSH Authentication Agent client using the named pipe transport.
/// </summary>
public sealed class OpenSshPipeAgent : ISshAgent
{
    public const string AgentName = "Windows OpenSSH Agent";
    public const string DefaultPipeName = "openssh-ssh-agent";
    internal const string DefaultPipePath = @"\\.\pipe\openssh-ssh-agent";

    private readonly string _pipeName;
    private readonly int _availabilityTimeoutMs;
    private readonly int _requestTimeoutMs;

    public OpenSshPipeAgent(
        string pipeName = DefaultPipeName,
        int availabilityTimeoutMs = 250,
        int requestTimeoutMs = 5000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(availabilityTimeoutMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestTimeoutMs);

        _pipeName = NormalizePipeName(pipeName);
        _availabilityTimeoutMs = availabilityTimeoutMs;
        _requestTimeoutMs = requestTimeoutMs;
    }

    public string Name => AgentName;

    public bool IsAvailable()
    {
        try
        {
            using var pipe = CreatePipe();
            pipe.Connect(_availabilityTimeoutMs);
            return pipe.IsConnected;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<ISshAgentKey> GetIdentities()
    {
        try
        {
            var response = SendRequest(OpenSshAgentProtocol.BuildRequestIdentitiesMessage());
            return OpenSshAgentProtocol.ParseIdentitiesResponse(response, this);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Core.Logging.FileLogger.Warn($"OpenSSH agent identity request failed: {ex.Message}");
            return [];
        }
    }

    internal byte[] Sign(byte[] publicKeyBlob, byte[] data, SshAgentSignFlags flags)
    {
        var request = OpenSshAgentProtocol.BuildSignRequestMessage(publicKeyBlob, data, flags);
        var response = SendRequest(request);
        return OpenSshAgentProtocol.ParseSignResponse(response);
    }

    internal byte[] SendRequest(byte[] request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var pipe = CreatePipe();
        pipe.Connect(_requestTimeoutMs);
        pipe.ReadMode = PipeTransmissionMode.Byte;
        if (pipe.CanTimeout)
        {
            pipe.ReadTimeout = _requestTimeoutMs;
            pipe.WriteTimeout = _requestTimeoutMs;
        }

        pipe.Write(request, 0, request.Length);
        pipe.Flush();

        var lengthBytes = ReadExact(pipe, sizeof(uint));
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        if (payloadLength == 0 || payloadLength > OpenSshAgentProtocol.MaxMessageLength)
        {
            throw new InvalidOperationException($"Invalid OpenSSH agent response length: {payloadLength}.");
        }

        var payload = ReadExact(pipe, (int)payloadLength);
        var response = new byte[sizeof(uint) + payload.Length];
        lengthBytes.CopyTo(response.AsSpan(0, sizeof(uint)));
        payload.CopyTo(response.AsSpan(sizeof(uint)));
        return response;
    }

    private NamedPipeClientStream CreatePipe()
    {
        return new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.None);
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("OpenSSH agent closed the pipe before sending a full response.");
            }

            offset += read;
        }

        return buffer;
    }

    private static string NormalizePipeName(string pipeName)
    {
        const string prefix = @"\\.\pipe\";
        return pipeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? pipeName[prefix.Length..]
            : pipeName;
    }
}
