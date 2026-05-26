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

using System.Net;
using System.Text;
using Heimdall.Terminal;

namespace Heimdall.Terminal.Tests;

public sealed class TelnetSessionTests
{
    [Fact]
    public async Task Write_BytesAppearOnTheWire()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        TelnetSession session = new(IPAddress.Loopback.ToString(), port, connectTimeoutMs: 1000);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

        try
        {
            await session.StartAsync(string.Empty, string.Empty);
            using TcpClient client = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
            await using NetworkStream stream = client.GetStream();

            session.Write(Encoding.ASCII.GetBytes("abc"));

            byte[] received = await ReadExactlyAsync(stream, 3);

            Assert.Equal(new byte[] { 0x61, 0x62, 0x63 }, received);
        }
        finally
        {
            session.Dispose();
            listener.Stop();
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, length - offset))
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (bytesRead == 0)
            {
                break;
            }

            offset += bytesRead;
        }

        Assert.Equal(length, offset);
        return buffer;
    }
}
