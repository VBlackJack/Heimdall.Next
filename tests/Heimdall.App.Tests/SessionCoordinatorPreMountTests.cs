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
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.App.Services.Handlers;
using Heimdall.App.Services.Import;
using Heimdall.App.Services.PostConnect;
using Heimdall.App.Services.SessionSnapshot;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.ViewModels.Settings;
using Heimdall.App.Views;
using Heimdall.Core.Configuration;
using Heimdall.Core.Import;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;
using Heimdall.Core.Ssh;
using Heimdall.Ssh;
using Heimdall.Terminal;

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
            FakeEmbeddedSessionManager embeddedSessionManager,
            IReadOnlyDictionary<string, ControlledProtocolHandler> handlers)
        {
            _rootPath = rootPath;
            Main = main;
            EmbeddedSessionManager = embeddedSessionManager;
            Handlers = handlers;
        }

        public MainViewModel Main { get; }

        public FakeEmbeddedSessionManager EmbeddedSessionManager { get; }

        private IReadOnlyDictionary<string, ControlledProtocolHandler> Handlers { get; }

        public static TestHarness Create()
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
                toolRegistry);
            var dispatcher = new FakeUiDispatcher();
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
            var settings = new SettingsViewModel(configManager, localizer, dialogService, trustedHostKeys: null!);
            var main = new MainViewModel(
                configManager,
                localizer,
                connectionStateMachine,
                appStatus,
                tunnelManager,
                hostKeyStore,
                dialogService,
                embeddedSessionManager,
                new ThemeService(configManager),
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
                settings);

            return new TestHarness(rootPath, main, embeddedSessionManager, handlers);
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

        public Task<BulkConnectOutcome> RunPipelineAsync(ServerProfileDto server, string sessionId)
        {
            var item = ServerItemViewModel.FromDto(server, localizer: Main.GetLocalizer());
            return Main.ServerList.RunConnectionPipelineAsync(
                server,
                new AppSettings { SftpAutoOpenOnSsh = false },
                sessionId,
                server.Id,
                item,
                CancellationToken.None);
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

        public TaskCompletionSource<CancellationToken> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ConnectionResult> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ConnectionResult> ConnectAsync(
            ServerProfileDto server,
            AppSettings settings,
            CancellationToken ct)
        {
            Started.TrySetResult(ct);
            return Result.Task.WaitAsync(ct);
        }
    }

    private sealed class FakeEmbeddedSessionManager : IEmbeddedSessionManager
    {
        public Action<byte[], object?>? BroadcastCallback { get; set; }
        public Action<SessionTabViewModel>? SplitRequestedCallback { get; set; }
        public Func<bool>? IsBroadcastActive { get; set; }
        public Action<SessionTabViewModel, string, string>? ReconnectRequestedCallback { get; set; }
        public Func<string, string, ToolContext?, Task>? OpenToolCallback { get; set; }

        public int CreateHostControlCalls { get; private set; }
        public int CreateConnectingSshHostControlCalls { get; private set; }
        public int AttachSshSessionCalls { get; private set; }

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
            return new object();
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
            string? workingDirectory = null)
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
