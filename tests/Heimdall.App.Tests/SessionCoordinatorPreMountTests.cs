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

using System.ComponentModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.App.Services.Handlers;
using Heimdall.App.Services.Import;
using Heimdall.App.Services.PostConnect;
using Heimdall.App.Services.SessionSnapshot;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.ViewModels.Session;
using Heimdall.App.ViewModels.Settings;
using Heimdall.App.Views;
using Heimdall.Core.Configuration;
using Heimdall.Core.Import;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Core.SessionDiagnostics;
using Heimdall.Core.Ssh;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;
using Heimdall.Terminal;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.App.Tests;

public sealed class SessionCoordinatorPreMountTests
{
    [Fact]
    public async Task RunConnectionPipelineAsync_Ssh_MountsTabBeforeConnectCompletes()
    {
        using var harness = TestHarness.Create();
        var sshHandler = harness.GetHandler("SSH");
        var server = harness.CreateServer("SSH");

        var pipeline = harness.RunPipelineAsync(server, "session-ssh-connecting");
        await sshHandler.Started.Task.WaitAsync(TestTimeout);

        var tab = Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Equal("session-ssh-connecting", tab.ServerId);
        Assert.Equal("Connecting", tab.Status);
        Assert.Equal(1, harness.EmbeddedSessionManager.CreateConnectingSshHostControlCalls);
        Assert.False(pipeline.IsCompleted);

        sshHandler.Result.SetResult(SuccessWithTerminalSession());
        var outcome = await pipeline.WaitAsync(TestTimeout);

        Assert.Equal(BulkConnectOutcomeStatus.Success, outcome.Status);
    }

    [Fact]
    public async Task RunConnectionPipelineAsync_NonSsh_DoesNotPreMountTabBeforeConnectCompletes()
    {
        using var harness = TestHarness.Create();
        var rdpHandler = harness.GetHandler("RDP");
        var server = harness.CreateServer("RDP");

        var pipeline = harness.RunPipelineAsync(server, "session-rdp");
        await rdpHandler.Started.Task.WaitAsync(TestTimeout);

        Assert.Empty(harness.Main.Connection.ActiveSessions);
        Assert.Equal(0, harness.EmbeddedSessionManager.CreateConnectingSshHostControlCalls);
        Assert.False(pipeline.IsCompleted);

        rdpHandler.Result.SetResult(new ConnectionResult(
            true,
            null,
            new RdpSessionResult(server)));
        var outcome = await pipeline.WaitAsync(TestTimeout);

        Assert.Equal(BulkConnectOutcomeStatus.Success, outcome.Status);
    }

    [Fact]
    public async Task RunConnectionPipelineAsync_RdpForcedExternalCreatesLightweightTabWithSuffix()
    {
        using var harness = TestHarness.Create();
        var rdpHandler = harness.GetHandler("RDP");
        var server = harness.CreateServer("RDP");
        rdpHandler.Result.SetResult(new ConnectionResult(true, null, null));

        var outcome = await harness.RunPipelineAsync(
            server,
            "session-rdp-forced-external",
            RdpModeOverride.ForceExternal).WaitAsync(TestTimeout);

        Assert.Equal(BulkConnectOutcomeStatus.Success, outcome.Status);
        var tab = Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Equal(RdpModeOverride.ForceExternal, tab.RdpModeOverride);
        Assert.Equal("Demo RDP (forced external)", tab.DisplayTitle);
        Assert.Equal("External client launched", tab.Status);
    }

    [Fact]
    public async Task RunConnectionPipelineAsync_SshFailure_RemovesPlaceholderTab()
    {
        using var harness = TestHarness.Create();
        var sshHandler = harness.GetHandler("SSH");
        var server = harness.CreateServer("SSH");
        sshHandler.Result.SetResult(new ConnectionResult(false, "connection refused", null));

        var outcome = await harness.RunPipelineAsync(server, "session-ssh-failed").WaitAsync(TestTimeout);

        Assert.Equal(BulkConnectOutcomeStatus.ConnectionFailed, outcome.Status);
        Assert.Empty(harness.Main.Connection.ActiveSessions);
        Assert.Equal(1, harness.EmbeddedSessionManager.CreateConnectingSshHostControlCalls);
    }

    [Fact]
    public async Task RunConnectionPipelineAsync_SshReady_AttachesExistingTabWithoutAddingSecondTab()
    {
        using var harness = TestHarness.Create();
        var sshHandler = harness.GetHandler("SSH");
        var server = harness.CreateServer("SSH");
        sshHandler.Result.SetResult(SuccessWithTerminalSession());

        var outcome = await harness.RunPipelineAsync(server, "session-ssh-ready").WaitAsync(TestTimeout);

        Assert.Equal(BulkConnectOutcomeStatus.Success, outcome.Status);
        Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Equal(1, harness.EmbeddedSessionManager.CreateConnectingSshHostControlCalls);
        Assert.Equal(1, harness.EmbeddedSessionManager.AttachSshSessionCalls);
        Assert.Equal(0, harness.EmbeddedSessionManager.CreateHostControlCalls);
    }

    [Fact]
    public async Task ReconnectSession_Ssh_RemovesOldTabBeforeNewConnect()
    {
        using var harness = TestHarness.Create();
        var sshHandler = harness.GetHandler("SSH");
        var server = harness.CreateServer("SSH");
        await harness.PersistServerAsync(server);
        sshHandler.Result.SetResult(SuccessWithTerminalSession());

        // Establish first session and wait for tab to be present
        var firstOutcome = await harness.RunPipelineAsync(server, "session-ssh-first").WaitAsync(TestTimeout);
        Assert.Equal(BulkConnectOutcomeStatus.Success, firstOutcome.Status);
        var oldTab = Assert.Single(harness.Main.Connection.ActiveSessions);

        // Reset the handler so the reconnect attempt can be observed independently
        harness.ResetHandler("SSH");

        // Trigger reconnect via the same entry point as the tab context menu
        harness.Main.Session.ReconnectSession(oldTab);

        // The old tab must be removed synchronously (Close is sync via CloseSessionInternal)
        await WaitUntilAsync(() => !harness.Main.Connection.ActiveSessions.Contains(oldTab));

        Assert.DoesNotContain(oldTab, harness.Main.Connection.ActiveSessions);
    }

    [Fact]
    public void ReconnectSession_NullTab_DoesNothing()
    {
        using TestHarness harness = TestHarness.Create();
        string initialStatus = harness.Main.StatusText;

        harness.Main.Session.ReconnectSession(null);

        Assert.Empty(harness.Main.Connection.ActiveSessions);
        Assert.Equal(initialStatus, harness.Main.StatusText);
    }

    [Fact]
    public void ReconnectSession_TabWithEmptyServerId_DoesNothing()
    {
        using TestHarness harness = TestHarness.Create();
        LocalizationManager localizer = harness.Main.GetLocalizer();
        SessionTabViewModel bareTab = new SessionTabViewModel
        {
            Title = "Bare",
            ConnectionType = "SSH"
        };

        harness.Main.Session.ReconnectSession(bareTab);

        Assert.Empty(harness.Main.Connection.ActiveSessions);
        Assert.Equal(localizer["StatusReady"], harness.Main.StatusText);
    }

    [Fact]
    public async Task ReconnectSession_ServerMissingFromInventory_SetsServerNotFoundStatus()
    {
        using TestHarness harness = TestHarness.Create();
        LocalizationManager localizer = harness.Main.GetLocalizer();
        ControlledProtocolHandler sshHandler = harness.GetHandler("SSH");
        ServerProfileDto server = harness.CreateServer("SSH");
        sshHandler.Result.SetResult(SuccessWithTerminalSession());

        BulkConnectOutcome firstOutcome = await harness.RunPipelineAsync(
            server,
            "session-ssh-first").WaitAsync(TestTimeout);
        Assert.Equal(BulkConnectOutcomeStatus.Success, firstOutcome.Status);
        SessionTabViewModel oldTab = Assert.Single(harness.Main.Connection.ActiveSessions);

        harness.Main.Session.ReconnectSession(oldTab);

        await WaitUntilAsync(() => harness.Main.StatusText == localizer["ErrorServerNotFound"]);
        Assert.Equal(localizer["ErrorServerNotFound"], harness.Main.StatusText);
        Assert.DoesNotContain(oldTab, harness.Main.Connection.ActiveSessions);
    }

    [Fact]
    public async Task ReconnectSession_ServerPresent_StartsNewConnection()
    {
        using TestHarness harness = TestHarness.Create();
        ControlledProtocolHandler sshHandler = harness.GetHandler("SSH");
        ServerProfileDto server = harness.CreateServer("SSH");
        await harness.PersistServerAsync(server);
        sshHandler.Result.SetResult(SuccessWithTerminalSession());

        BulkConnectOutcome firstOutcome = await harness.RunPipelineAsync(
            server,
            "session-ssh-first").WaitAsync(TestTimeout);
        Assert.Equal(BulkConnectOutcomeStatus.Success, firstOutcome.Status);
        SessionTabViewModel oldTab = Assert.Single(harness.Main.Connection.ActiveSessions);
        harness.ResetHandler("SSH");
        ControlledProtocolHandler reconnectHandler = harness.GetHandler("SSH");

        harness.Main.Session.ReconnectSession(oldTab);

        CancellationToken newConnectToken = await reconnectHandler.Started.Task.WaitAsync(TestTimeout);
        Assert.False(newConnectToken.IsCancellationRequested);
        await WaitUntilAsync(() => !harness.Main.Connection.ActiveSessions.Contains(oldTab));
        Assert.DoesNotContain(oldTab, harness.Main.Connection.ActiveSessions);

        reconnectHandler.Result.SetResult(SuccessWithTerminalSession());
        await WaitUntilAsync(() => harness.EmbeddedSessionManager.AttachSshSessionCalls == 2);
    }

    // SessionWindowService.UnsplitSession / DetachPaneToTab (window-free path)

    [Fact]
    public void UnsplitSession_NullSession_Throws()
    {
        using TestHarness harness = TestHarness.Create();
        SessionWindowService service = new SessionWindowService();

        Assert.Throws<ArgumentNullException>(() => service.UnsplitSession(null!, harness.Main));
    }

    [Fact]
    public void UnsplitSession_NullViewModel_Throws()
    {
        using TestHarness harness = TestHarness.Create();
        SessionWindowService service = new SessionWindowService();
        SessionTabViewModel session = new SessionTabViewModel
        {
            Title = "S",
            ConnectionType = "SSH"
        };

        Assert.Throws<ArgumentNullException>(() => service.UnsplitSession(session, null!));
    }

    [Fact]
    public void UnsplitSession_NotSplitSession_IsNoOp()
    {
        using TestHarness harness = TestHarness.Create();
        SessionWindowService service = new SessionWindowService();
        SessionTabViewModel session = harness.Main.Connection.AddSession("srv-1", "Primary", "SSH");
        int countBefore = harness.Main.Connection.ActiveSessions.Count;

        service.UnsplitSession(session, harness.Main);

        Assert.Equal(countBefore, harness.Main.Connection.ActiveSessions.Count);
        Assert.False(session.IsSplit);
    }

    [Fact]
    public void UnsplitSession_SplitWithConnectedSecondary_RestoresSecondaryAsIndependentTab()
    {
        using TestHarness harness = TestHarness.Create();
        SessionWindowService service = new SessionWindowService();
        SessionTabViewModel session = harness.Main.Connection.AddSession("srv-primary", "Primary", "SSH");
        object secondaryHost = new object();
        SessionPaneModel primaryPane = new SessionPaneModel
        {
            PaneId = "primary",
            ServerId = "srv-primary",
            ConnectionType = "SSH"
        };
        SessionPaneModel secondaryPane = new SessionPaneModel
        {
            PaneId = "secondary",
            ServerId = "srv-secondary",
            OriginalServerId = "orig-secondary",
            ConnectionType = "RDP",
            Title = "Secondary",
            Status = "Connected",
            TunnelRoute = "gw-1",
            EnvironmentColor = "#FF0000",
            HostControl = secondaryHost
        };
        session.RootContent = new SplitContainerModel
        {
            First = primaryPane,
            Second = secondaryPane
        };
        Assert.True(session.IsSplit);
        int countBefore = harness.Main.Connection.ActiveSessions.Count;

        service.UnsplitSession(session, harness.Main);

        Assert.False(session.IsSplit);
        Assert.Equal(countBefore + 1, harness.Main.Connection.ActiveSessions.Count);
        SessionTabViewModel restored = harness.Main.Connection.ActiveSessions[^1];
        Assert.Equal("srv-secondary", restored.ServerId);
        Assert.Equal("orig-secondary", restored.OriginalServerId);
        Assert.Same(secondaryHost, restored.HostControl);
        Assert.Equal("Connected", restored.Status);
        Assert.Equal("gw-1", restored.TunnelRoute);
        Assert.Equal("#FF0000", restored.EnvironmentColor);
    }

    [Fact]
    public void UnsplitSession_SplitWithConnectingSecondary_CleansOrphanWithoutRestoringTab()
    {
        using TestHarness harness = TestHarness.Create();
        SessionWindowService service = new SessionWindowService();
        SessionTabViewModel session = harness.Main.Connection.AddSession("srv-primary", "Primary", "SSH");
        SessionPaneModel primaryPane = new SessionPaneModel
        {
            PaneId = "primary",
            ServerId = "srv-primary",
            ConnectionType = "SSH"
        };
        SessionPaneModel connectingSecondary = new SessionPaneModel
        {
            PaneId = "secondary",
            ServerId = "srv-connecting",
            ConnectionType = "RDP",
            Title = "Connecting",
            HostControl = null
        };
        session.RootContent = new SplitContainerModel
        {
            First = primaryPane,
            Second = connectingSecondary
        };
        int countBefore = harness.Main.Connection.ActiveSessions.Count;

        service.UnsplitSession(session, harness.Main);

        Assert.Equal(countBefore, harness.Main.Connection.ActiveSessions.Count);
        Assert.False(session.IsSplit);
    }

    [Fact]
    public async Task CloseSessionCommand_WhileSshConnecting_CancelsPipelineToken()
    {
        using var harness = TestHarness.Create();
        var sshHandler = harness.GetHandler("SSH");
        var server = harness.CreateServer("SSH");

        var pipeline = harness.RunPipelineAsync(server, "session-ssh-cancel");
        var connectToken = await sshHandler.Started.Task.WaitAsync(TestTimeout);
        var tab = Assert.Single(harness.Main.Connection.ActiveSessions);

        await harness.Main.Connection.CloseSessionCommand.ExecuteAsync(tab);

        await WaitUntilAsync(() => connectToken.IsCancellationRequested);
        var outcome = await pipeline.WaitAsync(TestTimeout);

        Assert.Equal(BulkConnectOutcomeStatus.Cancelled, outcome.Status);
        Assert.Empty(harness.Main.Connection.ActiveSessions);
    }

    [Fact]
    public void OnSessionFailed_WhenRaisedOffUiThread_MarshalsThroughDispatcher()
    {
        using TestHarness harness = TestHarness.Create(checkAccess: false);
        SessionDiagnostic diagnostic = new(
            SessionFailureStage.SshAuth,
            "ErrorSshAuthRejected",
            7,
            "Access denied");
        MethodInfo method = typeof(SessionCoordinator).GetMethod(
            "OnSessionFailed",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(
            harness.Main.Session,
            [
                "session-ssh-failed",
                "server-ssh",
                "Demo SSH",
                "SSH",
                "Access denied",
                diagnostic
            ]);

        Assert.Equal(1, harness.Dispatcher.InvokeCalls);
        SessionTabViewModel tab = Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Equal("session-ssh-failed", tab.ServerId);
        Assert.Equal("server-ssh", tab.OriginalServerId);
        Assert.Equal("Access denied", tab.Status);
        Assert.Same(diagnostic, tab.FailureDetails);
        Assert.Equal("Access denied", harness.Main.StatusText);
    }

    [Fact]
    public async Task OpenToolTabAsync_NonNetworkToolWithoutContext_ReusesExistingTab()
    {
        using TestHarness harness = TestHarness.Create();

        await harness.Main.OpenToolTabAsync("HASH", "Hash", null);
        SessionTabViewModel firstTab = Assert.Single(harness.Main.Connection.ActiveSessions);

        await harness.Main.OpenToolTabAsync("HASH", "Hash", null);

        Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Same(firstTab, harness.Main.Connection.ActiveSession);
        Assert.Equal(1, harness.EmbeddedSessionManager.CreateToolControlCalls);
    }

    [Fact]
    public async Task OpenToolTabAsync_NetworkTool_CreatesSeparateTabs()
    {
        using TestHarness harness = TestHarness.Create();

        await harness.Main.OpenToolTabAsync("PING", "Ping", null);
        await harness.Main.OpenToolTabAsync("PING", "Ping", null);

        Assert.Equal(2, harness.Main.Connection.ActiveSessions.Count);
        Assert.Equal(2, harness.EmbeddedSessionManager.CreateToolControlCalls);
    }

    [Fact]
    public async Task OpenToolTabAsync_NonNetworkToolWithContext_CreatesSeparateTabs()
    {
        using TestHarness harness = TestHarness.Create();
        ToolContext context = new ToolContext(TargetHost: "demo.example.com");

        await harness.Main.OpenToolTabAsync("HASH", "Hash", context);
        await harness.Main.OpenToolTabAsync("HASH", "Hash", context);

        Assert.Equal(2, harness.Main.Connection.ActiveSessions.Count);
    }

    [Fact]
    public async Task OpenToolTabAsync_Success_SetsHostControlAndReadyStatus()
    {
        using TestHarness harness = TestHarness.Create();
        object sentinel = new object();
        harness.EmbeddedSessionManager.CreateToolControlBehavior = (
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings) => sentinel;
        LocalizationManager localizer = new LocalizationManager();
        await localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");

        await harness.Main.OpenToolTabAsync("HASH", "Hash", null);

        SessionTabViewModel tab = Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Same(sentinel, tab.HostControl);
        Assert.Equal(localizer["StatusReady"], tab.Status);
        Assert.True(harness.Main.Connection.HasActiveSessions);
    }

    [Fact]
    public async Task OpenToolTabAsync_FactoryThrows_NoPriorTabs_RemovesOrphanAndRethrows()
    {
        using TestHarness harness = TestHarness.Create();
        harness.EmbeddedSessionManager.CreateToolControlBehavior = (
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings) => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Main.OpenToolTabAsync("HASH", "Hash", null));

        Assert.Empty(harness.Main.Connection.ActiveSessions);
        Assert.False(harness.Main.Connection.HasActiveSessions);
    }

    [Fact]
    public async Task OpenToolTabAsync_FactoryThrows_PreservesExistingSessions()
    {
        using TestHarness harness = TestHarness.Create();

        await harness.Main.OpenToolTabAsync("HASH", "Hash", null);
        SessionTabViewModel healthyTab = Assert.Single(harness.Main.Connection.ActiveSessions);
        harness.EmbeddedSessionManager.CreateToolControlBehavior = (
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings) => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Main.OpenToolTabAsync("JWT", "Jwt", null));

        SessionTabViewModel remainingTab = Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Same(healthyTab, remainingTab);
        Assert.True(harness.Main.Connection.HasActiveSessions);
    }

    [Fact]
    public async Task OpenToolTabAsync_FactoryThrows_PropagatesOriginalExceptionUnwrapped()
    {
        using TestHarness harness = TestHarness.Create();
        InvalidOperationException expected = new InvalidOperationException("specific-boom");
        harness.EmbeddedSessionManager.CreateToolControlBehavior = (
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings) => throw expected;

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Main.OpenToolTabAsync("HASH", "Hash", null));

        Assert.Same(expected, actual);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static ConnectionResult SuccessWithTerminalSession()
    {
        return new ConnectionResult(true, null, new TerminalSessionResult(new FakeTerminalSession()));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow + TestTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate(), "Condition was not met before timeout.");
    }

    private sealed class TestHarness : IDisposable
    {
        private readonly string _rootPath;

        private TestHarness(
            string rootPath,
            MainViewModel main,
            FakeUiDispatcher dispatcher,
            FakeEmbeddedSessionManager embeddedSessionManager,
            IReadOnlyDictionary<string, ControlledProtocolHandler> handlers)
        {
            _rootPath = rootPath;
            Main = main;
            Dispatcher = dispatcher;
            EmbeddedSessionManager = embeddedSessionManager;
            Handlers = handlers;
        }

        public MainViewModel Main { get; }

        public FakeUiDispatcher Dispatcher { get; }

        public FakeEmbeddedSessionManager EmbeddedSessionManager { get; }

        private IReadOnlyDictionary<string, ControlledProtocolHandler> Handlers { get; }

        public static TestHarness Create(bool checkAccess = true)
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "heimdall-session-premount-tests",
                Guid.NewGuid().ToString("N"));
            var configManager = new ConfigManager(rootPath);
            configManager.InitializeAsync().GetAwaiter().GetResult();

            var localizer = new LocalizationManager();
            localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en")
                .GetAwaiter()
                .GetResult();

            var dialogService = new FakeDialogService();
            var connectionStateMachine = new ConnectionStateMachine();
            var appStatus = new ApplicationStatusMachine();
            var tunnelManager = new TunnelManager();
            var hostKeyStore = new HostKeyStore();
            var embeddedSessionManager = new FakeEmbeddedSessionManager();
            var toolRegistry = new ToolRegistry();
            var handlers = new Dictionary<string, ControlledProtocolHandler>(StringComparer.OrdinalIgnoreCase)
            {
                ["SSH"] = new("SSH"),
                ["RDP"] = new("RDP")
            };
            var connectionService = new ConnectionService(
                configManager,
                localizer,
                new FakeTunnelService(),
                handlers.Values);
            var splitService = new SplitService(
                configManager,
                localizer,
                connectionStateMachine,
                tunnelManager,
                embeddedSessionManager,
                connectionService,
                toolRegistry,
                dialogService);
            FakeUiDispatcher dispatcher = new(checkAccess);
            var connection = new ConnectionViewModel(localizer, dialogService, splitService);
            var serverList = new ServerListViewModel(
                configManager,
                localizer,
                dispatcher,
                connectionStateMachine,
                connectionService,
                dialogService,
                new FakeRdpImportService(),
                new PuttySessionImporter(new FakePuttySessionRegistrySource(), configManager),
                new Heimdall.App.Services.Import.KnownHostsImporter(configManager, hostKeyStore));
            SettingsViewModel settings = new SettingsViewModel(
                configManager,
                localizer,
                dialogService,
                null!,
                new PinManager());
            var main = new MainViewModel(
                configManager,
                localizer,
                connectionStateMachine,
                appStatus,
                tunnelManager,
                hostKeyStore,
                Heimdall.Core.Ssh.RejectingHostKeyVerifier.Instance,
                dialogService,
                embeddedSessionManager,
                new HeimdallThemeService(configManager),
                new FakeSessionSnapshotService(rootPath),
                new FakePostConnectSequenceRunner(),
                new FakePostConnectStepResolver(),
                toolRegistry,
                splitService,
                new ExternalToolLaunchService(dialogService),
                new ToolsTabPopulationService(toolRegistry),
                new FakeToolContextProvider(),
                dispatcher,
                serverList,
                connection,
                settings,
                new ServiceCollection().BuildServiceProvider());

            return new TestHarness(rootPath, main, dispatcher, embeddedSessionManager, handlers);
        }

        public ControlledProtocolHandler GetHandler(string protocol) => Handlers[protocol];

        public ServerProfileDto CreateServer(string protocol)
        {
            return new ServerProfileDto
            {
                Id = $"server-{protocol.ToLowerInvariant()}",
                DisplayName = $"Demo {protocol}",
                RemoteServer = "demo.example.com",
                RemotePort = 3389,
                ConnectionType = protocol,
                UseDirectConnection = true,
                SshUsername = "admin",
                SshPort = 22,
                SshMode = "Embedded",
                RdpMode = "Embedded"
            };
        }

        /// <summary>
        /// Saves the server to the inventory and refreshes the ServerList VM so
        /// reconnect lookups via <c>_main.ServerList.Servers.FirstOrDefault</c>
        /// can find the server by id (the lookup is the same path used by the
        /// production reconnect flow).
        /// </summary>
        public async Task PersistServerAsync(ServerProfileDto server)
        {
            var inventory = await Main.ConfigManager.LoadServersAsync();
            var list = inventory.ToList();
            list.RemoveAll(s => string.Equals(s.Id, server.Id, StringComparison.Ordinal));
            list.Add(server);
            await Main.ConfigManager.SaveServersAsync(list);

            var settings = await Main.ConfigManager.LoadSettingsAsync();
            Main.ServerList.LoadServers(list, settings);
        }

        public void ResetHandler(string protocol)
        {
            Handlers[protocol].Reset();
        }

        public Task<BulkConnectOutcome> RunPipelineAsync(
            ServerProfileDto server,
            string sessionId,
            RdpModeOverride rdpModeOverride = RdpModeOverride.UseProfile)
        {
            var item = ServerItemViewModel.FromDto(server, localizer: Main.GetLocalizer());
            return Main.ServerList.RunConnectionPipelineAsync(
                server,
                new AppSettings { SftpAutoOpenOnSsh = false },
                sessionId,
                server.Id,
                item,
                CancellationToken.None,
                rdpModeOverride);
        }

        public void Dispose()
        {
            Main.Session.Dispose();

            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, recursive: true);
                }
            }
            catch
            {
                // Test cleanup should not mask assertion failures.
            }
        }
    }

    private sealed class ControlledProtocolHandler(string protocol) : IProtocolHandler
    {
        public string Protocol { get; } = protocol;

        public TaskCompletionSource<CancellationToken> Started { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ConnectionResult> Result { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Replaces the started/result task completion sources so the next
        /// <see cref="ConnectAsync"/> call starts fresh. Used by reconnect
        /// tests that need to observe a second connection attempt after the
        /// first has already completed.
        /// </summary>
        public void Reset()
        {
            Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<ConnectionResult> ConnectAsync(
            ServerProfileDto server,
            AppSettings settings,
            CancellationToken ct,
            RdpModeOverride rdpModeOverride = RdpModeOverride.UseProfile)
        {
            Started.TrySetResult(ct);
            return Result.Task.WaitAsync(ct);
        }
    }

    private sealed class FakeEmbeddedSessionManager : IEmbeddedSessionManager
    {
        public Action<byte[], object?>? BroadcastCallback { get; set; }
        public Action<SessionTabViewModel>? SplitRequestedCallback { get; set; }
        public Action? CommandPaletteRequestedCallback { get; set; }
        public Func<bool>? IsBroadcastActive { get; set; }
        public Action<SessionTabViewModel, string, string>? ReconnectRequestedCallback { get; set; }
        public Action<SessionTabViewModel, SessionPaneModel, DisconnectReason>? DisconnectRequestedCallback { get; set; }
        public Action<string>? EditServerRequestedCallback { get; set; }
        public Action<SessionTabViewModel>? CloseRequestedCallback { get; set; }
        public Func<string, string, ToolContext?, Task>? OpenToolCallback { get; set; }

        public int CreateHostControlCalls { get; private set; }
        public int CreateConnectingSshHostControlCalls { get; private set; }
        public int AttachSshSessionCalls { get; private set; }
        public Func<SessionTabViewModel, string, ToolContext?, AppSettings?, object>? CreateToolControlBehavior { get; set; }
        public int CreateToolControlCalls { get; private set; }

        public object CreateHostControl(
            SessionTabViewModel sessionTab,
            string displayName,
            string connectionType,
            ISessionResult session,
            AppSettings? settings = null)
        {
            CreateHostControlCalls++;
            return new object();
        }

        public void DisconnectSession(SessionPaneModel pane, DisconnectReason reason)
        {
            if (pane.HostControl is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public EmbeddedSshView CreateConnectingSshHostControl(
            SessionTabViewModel sessionTab,
            string displayName,
            ServerProfileDto server,
            AppSettings? settings = null)
        {
            CreateConnectingSshHostControlCalls++;
            return null!;
        }

        public void AttachSshSession(
            SessionTabViewModel sessionTab,
            ISessionResult sessionResult,
            AppSettings? settings = null)
        {
            AttachSshSessionCalls++;
        }

        public object CreateToolControl(
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings = null)
        {
            CreateToolControlCalls++;
            return CreateToolControlBehavior?.Invoke(sessionTab, toolId, context, settings) ?? new object();
        }
    }

    private sealed class FakeTerminalSession : ITerminalSession
    {
        public event Action<ReadOnlyMemory<byte>>? DataReceived;
        public event Action<int>? ProcessExited;

        public bool IsRunning => true;
        public int? ProcessId => 1234;
        public Dictionary<string, string>? EnvironmentVariables { get; set; }

        public Task StartAsync(
            string executable,
            string arguments,
            int columns = 80,
            int rows = 24,
            string? workingDirectory = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            DataReceived?.Invoke(data.ToArray());
        }

        public void Write(string text)
        {
        }

        public void Resize(int columns, int rows)
        {
        }

        public void Kill()
        {
            ProcessExited?.Invoke(0);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeTunnelService : ITunnelService
    {
        public Task<(bool Success, bool UsesTunnel, string Host, int Port, string? ErrorMessage)>
            SetupTunnelIfNeededAsync(
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

    private sealed class FakeRdpImportService : IRdpImportService
    {
        public Task<RdpImportPreview> PreviewAsync(string[] filePaths, CancellationToken ct)
        {
            return Task.FromResult(new RdpImportPreview
            {
                Entries = [],
                FilesNotFound = [],
                FilesUnreadable = []
            });
        }

        public Task<RdpImportResult> ApplyAsync(
            RdpImportPreview preview,
            RdpImportSelection selection,
            CancellationToken ct)
        {
            return Task.FromResult(new RdpImportResult());
        }
    }

    private sealed class FakePuttySessionRegistrySource : IPuttySessionRegistrySource
    {
        public Task<IReadOnlyList<RawPuttySession>> ReadSessionsAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<RawPuttySession>>([]);
        }
    }

    private sealed class FakePostConnectSequenceRunner : IPostConnectSequenceRunner
    {
        public Task<PostConnectRunResult> RunAsync(
            IReadOnlyList<PostConnectStep> steps,
            Action<string> writeCallback,
            IProgress<PostConnectRunProgress>? progress,
            CancellationToken ct,
            IPostConnectStepResolver? resolver = null)
        {
            return Task.FromResult(new PostConnectRunResult());
        }
    }

    private sealed class FakePostConnectStepResolver : IPostConnectStepResolver
    {
        public Task<PostConnectResolveResult> ResolveAsync(PostConnectStep step, CancellationToken ct)
        {
            return Task.FromResult(new PostConnectResolveResult
            {
                Status = PostConnectResolveStatus.Literal,
                ResolvedInput = step.Input
            });
        }
    }

    private sealed class FakeSessionSnapshotService(string rootPath) : ISessionSnapshotService
    {
        public string SnapshotPath { get; } = Path.Combine(rootPath, "snapshot.json");

        public Task SaveAsync(
            IReadOnlyList<SessionSnapshotEntry> sessions,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<SessionSnapshotFile?> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SessionSnapshotFile?>(null);
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeToolContextProvider : IToolContextProvider
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string? TargetHost => null;
        public bool HasTarget => false;
        public string ContextLabel => string.Empty;
        public string ContextTooltip => string.Empty;
        public string ContextBrushKey => "TextSecondaryBrush";

        public void SetSelectedServer(ServerItemViewModel? server)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetHost)));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<bool> ShowConfirmAsync(string title, string message, string severity = "info")
        {
            return Task.FromResult(true);
        }

        public Task<bool?> ShowSaveDiscardCancelAsync(string title, string message)
        {
            return Task.FromResult<bool?>(false);
        }

        public Task<string?> ShowInputAsync(string title, string prompt, string? defaultValue = null)
        {
            return Task.FromResult(defaultValue);
        }

        public Task<string?> ShowPasswordInputAsync(
            string title,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<ServerDialogResult?> ShowServerDialogAsync(ServerDialogViewModel? editVm = null)
        {
            return Task.FromResult<ServerDialogResult?>(null);
        }

        public Task<GatewayDialogResult?> ShowGatewayDialogAsync(GatewayDialogViewModel? editVm = null)
        {
            return Task.FromResult<GatewayDialogResult?>(null);
        }

        public Task<ProjectDialogResult?> ShowProjectDialogAsync(ProjectDialogViewModel? editVm = null)
        {
            return Task.FromResult<ProjectDialogResult?>(null);
        }

        public Task<ScheduledTaskDialogResult?> ShowScheduledTaskDialogAsync(
            ScheduledTaskDialogViewModel? editVm = null)
        {
            return Task.FromResult<ScheduledTaskDialogResult?>(null);
        }

        public Task ShowPinDialogAsync(PinDialogViewModel viewModel)
        {
            return Task.CompletedTask;
        }

        public Task<PinSetupResult?> ShowPinSetupDialogAsync(PinSetupDialogViewModel viewModel)
        {
            return Task.FromResult<PinSetupResult?>(null);
        }

        public Task<SnapshotRestoreDialogResult?> ShowSnapshotRestoreDialogAsync(
            SnapshotRestoreDialogViewModel viewModel)
        {
            return Task.FromResult<SnapshotRestoreDialogResult?>(null);
        }

        public Task<RdpImportSelection?> ShowRdpImportDialogAsync(RdpImportDialogViewModel viewModel)
        {
            return Task.FromResult<RdpImportSelection?>(null);
        }

        public Task<ImportOutcome?> ShowImportOpenSshConfigAsync(OpenSshParseResult parseResult)
        {
            return Task.FromResult<ImportOutcome?>(null);
        }

        public Task<ImportOutcome?> ShowImportPuttySessionsAsync(PuttySessionParseResult parseResult)
        {
            return Task.FromResult<ImportOutcome?>(null);
        }

        public Task<KnownHostsImportOutcome?> ShowImportKnownHostsAsync(KnownHostsImportPreview preview)
        {
            return Task.FromResult<KnownHostsImportOutcome?>(null);
        }

        public Task ShowTrustedHostKeyDetailsAsync(TrustedHostKeyDetailsDialogViewModel viewModel)
        {
            return Task.CompletedTask;
        }

        public Task<ImportKnownHostsConflictResolution?> ShowImportKnownHostsConflictAsync(
            ImportKnownHostsConflictDialogViewModel viewModel)
        {
            return Task.FromResult<ImportKnownHostsConflictResolution?>(null);
        }

        public Task<CommandLibraryPickerResult?> ShowCommandLibraryPickerAsync(
            CommandLibraryPickerDialogViewModel viewModel,
            AutoPrefillContext? prefillContext = null,
            string? existingActionId = null,
            IReadOnlyDictionary<string, string>? existingValues = null)
        {
            return Task.FromResult<CommandLibraryPickerResult?>(null);
        }

        public Task<int?> ShowBulkEditPortAsync(
            int count,
            int? initialPort,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(initialPort);
        }

        public Task<string?> ShowBulkEditUsernameAsync(
            int count,
            string? initialUsername,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(initialUsername);
        }

        public Task<string?> ShowBulkEditPasswordAsync(
            int count,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public void ShowError(string title, string message)
        {
        }

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }
    }
}
