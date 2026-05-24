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
using Heimdall.App.Services;
using Heimdall.App.Services.Handlers;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed class ConnectionServiceDispatchTests
{
    [Fact]
    public async Task ConnectWinRmAsync_DispatchesToWinRmHandler()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            "heimdall-winrm-dispatch",
            Guid.NewGuid().ToString("N"));
        ConfigManager configManager = new ConfigManager(rootPath);

        try
        {
            await configManager.InitializeAsync();

            LocalizationManager localizer = new LocalizationManager();
            CapturingProtocolHandler handler = new CapturingProtocolHandler("WINRM");
            using ConnectionService service = new ConnectionService(
                configManager,
                localizer,
                new StubTunnelService(),
                new IProtocolHandler[] { handler });
            ServerProfileDto server = new ServerProfileDto
            {
                Id = "winrm-test",
                ConnectionType = "WINRM",
                RemoteServer = "server01.contoso.local"
            };
            AppSettings settings = new AppSettings();

            ConnectionResult result = await service.ConnectWinRmAsync(server, settings);

            Assert.True(result.Success);
            Assert.Equal(1, handler.CallCount);
            Assert.Same(server, handler.Server);
            Assert.Same(settings, handler.Settings);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class CapturingProtocolHandler(string protocol) : IProtocolHandler
    {
        public string Protocol { get; } = protocol;

        public int CallCount { get; private set; }

        public ServerProfileDto? Server { get; private set; }

        public AppSettings? Settings { get; private set; }

        public Task<ConnectionResult> ConnectAsync(
            ServerProfileDto server,
            AppSettings settings,
            CancellationToken ct,
            RdpModeOverride rdpModeOverride = RdpModeOverride.UseProfile)
        {
            CallCount++;
            Server = server;
            Settings = settings;
            return Task.FromResult(new ConnectionResult(true, null, null));
        }
    }

    private sealed class StubTunnelService : ITunnelService
    {
        public Task<(bool Success, bool UsesTunnel, string Host, int Port, string? ErrorMessage)> SetupTunnelIfNeededAsync(
            ServerProfileDto server,
            int remotePort,
            AppSettings settings,
            CancellationToken ct)
            => Task.FromResult((true, false, server.RemoteServer ?? string.Empty, remotePort, (string?)null));

        public void UpdateSettings(AppSettings settings)
        {
        }

        public Heimdall.Ssh.TunnelForwardedPortFailure? GetRecentForwardedPortFailure(int localPort)
            => null;

        public void ReleaseTunnelReference(int localPort)
        {
        }
    }
}
