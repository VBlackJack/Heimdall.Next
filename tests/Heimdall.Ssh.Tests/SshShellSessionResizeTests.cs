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

using System.Reflection;
using Renci.SshNet;

namespace Heimdall.Ssh.Tests;

public sealed class SshShellSessionResizeTests
{
    [Fact]
    public void ShellStream_ExposesPublicChangeWindowSizeApi()
    {
        var method = typeof(ShellStream).GetMethod(
            "ChangeWindowSize",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(uint), typeof(uint), typeof(uint), typeof(uint)],
            null);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Equal(
            ["columns", "rows", "width", "height"],
            method.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void Resize_WhenSessionIsNotConnected_ThrowsPredictableException()
    {
        using var session = new SshShellSession();

        var ex = Assert.Throws<InvalidOperationException>(() => session.Resize(120, 40));

        Assert.Equal("Session is not connected.", ex.Message);
    }
}
