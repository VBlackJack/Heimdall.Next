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

using Heimdall.App.Services;
using Heimdall.App.Services.Handlers;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;

namespace Heimdall.App.Tests;

public sealed class TelnetHandlerValidationTests
{
    [Fact]
    public async Task ConnectAsync_RejectsOutOfRangePort()
    {
        TelnetHandler handler = CreateHandler();
        ServerProfileDto server = CreateServer("host.example.com", 70_000);
        AppSettings settings = new AppSettings();

        ConnectionResult result = await handler.ConnectAsync(server, settings, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ErrorInvalidPort", result.ErrorMessage);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task ConnectAsync_RejectsWhitespaceHost()
    {
        TelnetHandler handler = CreateHandler();
        ServerProfileDto server = CreateServer("   ", 23);
        AppSettings settings = new AppSettings();

        ConnectionResult result = await handler.ConnectAsync(server, settings, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ErrorInvalidTargetHost", result.ErrorMessage);
        Assert.Null(result.Session);
    }

    private static TelnetHandler CreateHandler()
    {
        return new TelnetHandler(new ConnectionStateMachine(), new LocalizationManager());
    }

    private static ServerProfileDto CreateServer(string host, int port)
    {
        return new ServerProfileDto
        {
            Id = "telnet-test",
            DisplayName = "TELNET Test",
            RemoteServer = host,
            ConnectionType = "TELNET",
            TelnetPort = port,
            UseDirectConnection = true
        };
    }
}
