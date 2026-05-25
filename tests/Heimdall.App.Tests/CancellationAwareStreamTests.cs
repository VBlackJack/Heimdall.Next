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

using System.IO;
using Heimdall.Sftp;

namespace Heimdall.App.Tests;

public sealed class CancellationAwareStreamTests
{
    [Fact]
    public void Read_WhenCanceled_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        using MemoryStream inner = new([1, 2, 3]);
        using CancellationAwareStream stream = new(inner, cts.Token);
        byte[] buffer = new byte[3];

        Assert.Throws<OperationCanceledException>(() => stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void Write_WhenCanceled_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        using MemoryStream inner = new();
        using CancellationAwareStream stream = new(inner, cts.Token);
        byte[] buffer = [1, 2, 3];

        Assert.Throws<OperationCanceledException>(() => stream.Write(buffer, 0, buffer.Length));
    }

    [Fact]
    public void Read_WhenNotCanceled_DelegatesToInnerStream()
    {
        using CancellationTokenSource cts = new();
        using MemoryStream inner = new([1, 2, 3]);
        using CancellationAwareStream stream = new(inner, cts.Token);
        byte[] buffer = new byte[3];

        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal(3, bytesRead);
        Assert.Equal([1, 2, 3], buffer);
    }
}
