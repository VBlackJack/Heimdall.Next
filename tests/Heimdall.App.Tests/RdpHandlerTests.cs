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

public sealed class RdpHandlerTests
{
    [Fact]
    public async Task ConnectAsync_ForceEmbeddedUsesEmbeddedPathWithoutMutatingProfile()
    {
        var launcher = new TrackingRdpExternalClientLauncher();
        var handler = CreateHandler(launcher);
        var server = CreateServer("External");
        var settings = new AppSettings();

        var result = await handler.ConnectAsync(
            server,
            settings,
            CancellationToken.None,
            RdpModeOverride.ForceEmbedded);

        Assert.True(result.Success);
        Assert.IsType<RdpSessionResult>(result.Session);
        Assert.Equal(0, launcher.LaunchCalls);
        Assert.Equal("External", server.RdpMode);
    }

    [Fact]
    public async Task ConnectAsync_ForceExternalUsesExternalLauncherWithoutMutatingProfile()
    {
        var launcher = new TrackingRdpExternalClientLauncher
        {
            ProcessToReturn = new FakeLaunchedRdpClientProcess(4242)
        };
        var handler = CreateHandler(launcher);
        var server = CreateServer("Embedded");
        var settings = new AppSettings
        {
            RdpArtifactCleanupDelayMs = 1,
            RdpCredentialAutofillTimeoutMs = 1
        };

        var result = await handler.ConnectAsync(
            server,
            settings,
            CancellationToken.None,
            RdpModeOverride.ForceExternal);

        Assert.True(result.Success);
        Assert.Null(result.Session);
        Assert.Equal(1, launcher.LaunchCalls);
        Assert.False(string.IsNullOrWhiteSpace(launcher.LastRdpFilePath));
        Assert.Equal("Embedded", server.RdpMode);
    }

    [Fact]
    public async Task ConnectAsync_ForceExternalPostTunnelLaunchFailureReleasesTunnelReference()
    {
        FakeTunnelService tunnelService = new FakeTunnelService
        {
            UsesTunnel = true,
            TargetHost = "127.0.0.1",
            TargetPort = 53389
        };
        TrackingRdpExternalClientLauncher launcher = new TrackingRdpExternalClientLauncher();
        RdpHandler handler = CreateHandler(tunnelService, launcher);
        ServerProfileDto server = CreateServer("Embedded");
        AppSettings settings = new AppSettings
        {
            RdpArtifactCleanupDelayMs = 1,
            RdpCredentialAutofillTimeoutMs = 1
        };

        ConnectionResult result = await handler.ConnectAsync(
            server,
            settings,
            CancellationToken.None,
            RdpModeOverride.ForceExternal);

        Assert.False(result.Success);
        Assert.Equal(1, launcher.LaunchCalls);
        Assert.Equal(1, tunnelService.ReleaseCount);
        Assert.Equal(53389, tunnelService.ReleasedLocalPort);
    }

    [Fact]
    public async Task ConnectAsync_ForceExternalSuccessReleasesTunnelReferenceOnProcessExit()
    {
        FakeTunnelService tunnelService = new FakeTunnelService
        {
            UsesTunnel = true,
            TargetHost = "127.0.0.1",
            TargetPort = 53389
        };
        FakeLaunchedRdpClientProcess process = new FakeLaunchedRdpClientProcess(4242);
        TrackingRdpExternalClientLauncher launcher = new TrackingRdpExternalClientLauncher
        {
            ProcessToReturn = process
        };
        RdpHandler handler = CreateHandler(tunnelService, launcher);
        ServerProfileDto server = CreateServer("Embedded");
        AppSettings settings = new AppSettings
        {
            RdpArtifactCleanupDelayMs = 1,
            RdpCredentialAutofillTimeoutMs = 1
        };

        ConnectionResult result = await handler.ConnectAsync(
            server,
            settings,
            CancellationToken.None,
            RdpModeOverride.ForceExternal);

        Assert.True(result.Success);
        Assert.Equal(0, tunnelService.ReleaseCount);

        process.RaiseExited();

        Assert.Equal(1, tunnelService.ReleaseCount);
        Assert.Equal(53389, tunnelService.ReleasedLocalPort);
    }

    [Theory]
    [InlineData("External", RdpModeOverride.UseProfile, "External")]
    [InlineData("Embedded", RdpModeOverride.ForceExternal, "External")]
    [InlineData("External", RdpModeOverride.ForceEmbedded, "Embedded")]
    public void ResolveEffectiveMode_HonorsOneShotOverride(
        string profileMode,
        RdpModeOverride rdpModeOverride,
        string expectedMode)
    {
        var server = CreateServer(profileMode);

        var actualMode = RdpHandler.ResolveEffectiveMode(server, rdpModeOverride);

        Assert.Equal(expectedMode, actualMode);
        Assert.Equal(profileMode, server.RdpMode);
    }

    private static RdpHandler CreateHandler(IRdpExternalClientLauncher launcher)
    {
        return CreateHandler(new PassThroughTunnelService(), launcher);
    }

    private static RdpHandler CreateHandler(
        ITunnelService tunnelService,
        IRdpExternalClientLauncher launcher)
    {
        return new RdpHandler(
            tunnelService,
            new ConnectionStateMachine(),
            new LocalizationManager(),
            launcher);
    }

    private static ServerProfileDto CreateServer(string rdpMode) =>
        new()
        {
            Id = "rdp-test",
            DisplayName = "RDP Test",
            RemoteServer = "127.0.0.1",
            RemotePort = 3389,
            ConnectionType = "RDP",
            RdpMode = rdpMode,
            UseDirectConnection = true
        };

    private sealed class PassThroughTunnelService : ITunnelService
    {
        public Task<(bool Success, bool UsesTunnel, string Host, int Port, string? ErrorMessage)> SetupTunnelIfNeededAsync(
            ServerProfileDto server,
            int remotePort,
            AppSettings settings,
            CancellationToken ct)
        {
            return Task.FromResult((true, false, server.RemoteServer, remotePort, (string?)null));
        }

        public void UpdateSettings(AppSettings settings)
        {
        }

        public Heimdall.Ssh.TunnelForwardedPortFailure? GetRecentForwardedPortFailure(int localPort) => null;

        public void ReleaseTunnelReference(int localPort)
        {
        }
    }

    private sealed class FakeTunnelService : ITunnelService
    {
        public bool UsesTunnel { get; init; }
        public string TargetHost { get; init; } = "";
        public int TargetPort { get; init; }
        public int ReleaseCount { get; private set; }
        public int ReleasedLocalPort { get; private set; }

        public Task<(bool Success, bool UsesTunnel, string Host, int Port, string? ErrorMessage)> SetupTunnelIfNeededAsync(
            ServerProfileDto server,
            int remotePort,
            AppSettings settings,
            CancellationToken ct)
        {
            string host = UsesTunnel ? TargetHost : server.RemoteServer;
            int port = UsesTunnel ? TargetPort : remotePort;
            return Task.FromResult((true, UsesTunnel, host, port, (string?)null));
        }

        public void UpdateSettings(AppSettings settings)
        {
        }

        public Heimdall.Ssh.TunnelForwardedPortFailure? GetRecentForwardedPortFailure(int localPort) => null;

        public void ReleaseTunnelReference(int localPort)
        {
            ReleaseCount++;
            ReleasedLocalPort = localPort;
        }
    }

    private sealed class TrackingRdpExternalClientLauncher : IRdpExternalClientLauncher
    {
        public int LaunchCalls { get; private set; }

        public string? LastRdpFilePath { get; private set; }

        public ILaunchedRdpClientProcess? ProcessToReturn { get; init; }

        public ILaunchedRdpClientProcess? Launch(string rdpFilePath)
        {
            LaunchCalls++;
            LastRdpFilePath = rdpFilePath;
            return ProcessToReturn;
        }
    }

    private sealed class FakeLaunchedRdpClientProcess(int id) : ILaunchedRdpClientProcess
    {
        private EventHandler? exited;

        public int Id { get; } = id;

        public int ExitCode => 0;

        public bool EnableRaisingEvents { get; set; }

        public event EventHandler Exited
        {
            add => exited += value;
            remove => exited -= value;
        }

        public void RaiseExited()
        {
            EventHandler? handler = exited;
            handler?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }
}
