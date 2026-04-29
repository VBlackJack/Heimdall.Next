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
using System.Threading;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

/// <summary>
/// Unit tests for <see cref="SplitService"/>. Covers the synchronous and
/// self-contained methods that can be exercised without a live WPF dispatcher
/// or a full integration harness: the per-session cancellation token
/// lifecycle, <c>CloseAllPanes</c>'s tool-pane blocking guard,
/// <c>ToggleSplitOrientation</c>, and <c>SplitSessionWithTool</c>'s
/// short-circuit guards (unknown tool, max panes). The async flows
/// (<c>SplitSessionWithServerAsync</c>, <c>ReconnectPaneAsync</c>,
/// <c>SwapSplitPanesAsync</c>) depend on the WPF dispatcher and real
/// connection plumbing and are out of scope here.
/// </summary>
public sealed class SplitServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly ToolRegistry _toolRegistry;
    private readonly SplitService _sut;

    public SplitServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"heimdall-split-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "config"));

        _configManager = new ConfigManager(_tempDir);
        _localizer = new LocalizationManager();
        _toolRegistry = new ToolRegistry();

        // All 8 SplitService dependencies are sealed (Moq cannot mock sealed
        // types at runtime). We pass real instances for the three that the
        // targeted methods actually read, and null! for the five that the
        // targeted code paths never touch: ConnectionStateMachine and
        // TunnelManager are only invoked by the "server pane" branch of
        // CloseAllPanes (we only build tool-pane / empty trees) and by
        // CleanupOrphanedPane / ReleaseOldConnectionState (not tested here);
        // EmbeddedSessionManager is only invoked by the positive path of
        // SplitSessionWithTool (we only test its guard short-circuits);
        // ConnectionService and DialogService are only invoked by the async
        // connection flows.
        _sut = new SplitService(
            _configManager,
            _localizer,
            connectionSm: null!,
            tunnelManager: null!,
            sessionManager: null!,
            connectionService: null!,
            _toolRegistry,
            dialogService: null!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* test cleanup */ }
        GC.SuppressFinalize(this);
    }

    // ── Category A: CancellationTokenSource lifecycle ───────────────────

    [Fact]
    public void RegisterSession_CreatesToken_ThatIsNotCancelled()
    {
        var session = new SessionTabViewModel();
        _sut.RegisterSession(session);

        var token = _sut.GetType()
            .GetMethod("GetSessionToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(_sut, new object[] { session });

        Assert.IsType<CancellationToken>(token);
        Assert.False(((CancellationToken)token!).IsCancellationRequested);
    }

    [Fact]
    public void CancelSession_CancelsTheToken()
    {
        var session = new SessionTabViewModel();
        _sut.RegisterSession(session);
        var tokenBefore = InvokeGetSessionToken(session);
        Assert.False(tokenBefore.IsCancellationRequested);

        _sut.CancelSession(session);

        // After CancelSession, the session is unregistered; the previously
        // captured token should now observe the cancellation signal.
        Assert.True(tokenBefore.IsCancellationRequested);
    }

    [Fact]
    public void GetSessionToken_ReturnsNone_ForUnknownSession()
    {
        var session = new SessionTabViewModel();

        var token = InvokeGetSessionToken(session);

        Assert.Equal(CancellationToken.None, token);
    }

    [Fact]
    public void CancelSession_UnknownSession_DoesNotThrow()
    {
        var session = new SessionTabViewModel();

        var ex = Record.Exception(() => _sut.CancelSession(session));

        Assert.Null(ex);
    }

    [Fact]
    public void RegisterSession_TwiceForSameSession_IsIdempotent()
    {
        var session = new SessionTabViewModel();

        _sut.RegisterSession(session);
        var token1 = InvokeGetSessionToken(session);
        _sut.RegisterSession(session);
        var token2 = InvokeGetSessionToken(session);

        // ConcurrentDictionary.TryAdd keeps the first entry on collision,
        // so the second Register must not replace the original token.
        Assert.Equal(token1, token2);
        Assert.False(token1.IsCancellationRequested);
    }

    // ── Category B: CloseAllPanes tool-pane blocking ────────────────────

    [Fact]
    public void CloseAllPanes_EmptyTree_ReturnsTrue()
    {
        var session = new SessionTabViewModel();
        // Default RootContent is a single empty SessionPaneModel with
        // ServerId="" and ConnectionType="" — no server cleanup path hit.

        var result = _sut.CloseAllPanes(session);

        Assert.True(result);
    }

    [Fact]
    public void CloseAllPanes_ToolPaneCanClose_ReturnsTrue_AndClearsHostControl()
    {
        var session = new SessionTabViewModel();
        var toolPane = MakePane(connectionType: "TOOL:PING");
        var closableView = new StubToolView(canClose: true);
        toolPane.HostControl = closableView;
        session.RootContent = toolPane;

        var result = _sut.CloseAllPanes(session);

        Assert.True(result);
        Assert.Null(toolPane.HostControl);
        Assert.True(closableView.Disposed);
    }

    [Fact]
    public void CloseAllPanes_ToolPaneBlocking_ReturnsFalse_AndPreservesHostControl()
    {
        var session = new SessionTabViewModel();
        var toolPane = MakePane(connectionType: "TOOL:PING");
        var blockingView = new StubToolView(canClose: false);
        toolPane.HostControl = blockingView;
        session.RootContent = toolPane;

        var result = _sut.CloseAllPanes(session);

        Assert.False(result);
        Assert.Same(blockingView, toolPane.HostControl);
        Assert.False(blockingView.Disposed);
    }

    [Fact]
    public void CloseAllPanes_MixedTree_OneBlockingTool_ReturnsFalse_NothingDisposed()
    {
        var session = new SessionTabViewModel();
        var freePane = MakePane(connectionType: "TOOL:HASH");
        var freeView = new StubToolView(canClose: true);
        freePane.HostControl = freeView;

        var blockedPane = MakePane(connectionType: "TOOL:PING");
        var blockedView = new StubToolView(canClose: false);
        blockedPane.HostControl = blockedView;

        session.RootContent = new SplitContainerModel
        {
            First = freePane,
            Second = blockedPane,
            Orientation = SplitOrientation.Vertical
        };

        var result = _sut.CloseAllPanes(session);

        Assert.False(result);
        // The blocking check runs before the disposal loop, so neither
        // host control is torn down when any tool pane is busy.
        Assert.Same(freeView, freePane.HostControl);
        Assert.False(freeView.Disposed);
        Assert.Same(blockedView, blockedPane.HostControl);
        Assert.False(blockedView.Disposed);
    }

    // ── Category D: ToggleSplitOrientation ──────────────────────────────

    [Fact]
    public void ToggleSplitOrientation_HorizontalBecomesVertical()
    {
        var session = new SessionTabViewModel();
        var container = new SplitContainerModel
        {
            First = MakePane(),
            Second = MakePane(),
            Orientation = SplitOrientation.Horizontal
        };
        session.RootContent = container;

        _sut.ToggleSplitOrientation(session);

        Assert.Equal(SplitOrientation.Vertical, container.Orientation);
    }

    [Fact]
    public void ToggleSplitOrientation_VerticalBecomesHorizontal()
    {
        var session = new SessionTabViewModel();
        var container = new SplitContainerModel
        {
            First = MakePane(),
            Second = MakePane(),
            Orientation = SplitOrientation.Vertical
        };
        session.RootContent = container;

        _sut.ToggleSplitOrientation(session);

        Assert.Equal(SplitOrientation.Horizontal, container.Orientation);
    }

    [Fact]
    public void ToggleSplitOrientation_UnsplitSession_NoOp()
    {
        var session = new SessionTabViewModel();
        var leaf = MakePane();
        session.RootContent = leaf;

        var ex = Record.Exception(() => _sut.ToggleSplitOrientation(session));

        Assert.Null(ex);
        Assert.Same(leaf, session.RootContent);
    }

    // ── Category E: SplitSessionWithTool guards ─────────────────────────

    [Fact]
    public void SplitSessionWithTool_UnknownToolId_LeavesTreeUnchanged()
    {
        var session = new SessionTabViewModel();
        var originalRoot = session.RootContent;

        _sut.SplitSessionWithTool(session, "NOT_A_REAL_TOOL", SplitOrientation.Vertical);

        // Unknown toolId short-circuits before touching _sessionManager.
        Assert.Same(originalRoot, session.RootContent);
        Assert.False(session.IsSplit);
    }

    [Fact]
    public void SplitSessionWithTool_AtMaxPanes_SetsStatusAndLeavesTreeUnchanged()
    {
        var session = new SessionTabViewModel();
        session.RootContent = BuildEightLeafTree();
        Assert.Equal(SplitService.MaxPanesPerTab, SplitTreeHelper.CountLeaves(session.RootContent));
        var rootBefore = session.RootContent;

        string? capturedStatus = null;
        _sut.SetStatusText = s => capturedStatus = s;

        _sut.SplitSessionWithTool(session, "PING", SplitOrientation.Horizontal);

        Assert.Same(rootBefore, session.RootContent);
        Assert.Equal(SplitService.MaxPanesPerTab, SplitTreeHelper.CountLeaves(session.RootContent));
        // LocalizationManager has no strings loaded so the key is returned verbatim.
        Assert.Equal("SplitMaxPanesReached", capturedStatus);
    }

    // ── Category F: Forced embedded mode policy ────────────────────────

    [Fact]
    public void ForceEmbeddedMode_RdpExternal_ReturnsTrue_AndSetsEmbedded()
    {
        var server = new ServerProfileDto
        {
            ConnectionType = "RDP",
            RdpMode = "External"
        };

        var converted = SplitService.ForceEmbeddedMode(server);

        Assert.True(converted);
        Assert.Equal("Embedded", server.RdpMode);
    }

    [Fact]
    public void ForceEmbeddedMode_RdpEmbedded_ReturnsFalse_AndKeepsEmbedded()
    {
        var server = new ServerProfileDto
        {
            ConnectionType = "RDP",
            RdpMode = "Embedded"
        };

        var converted = SplitService.ForceEmbeddedMode(server);

        Assert.False(converted);
        Assert.Equal("Embedded", server.RdpMode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private CancellationToken InvokeGetSessionToken(SessionTabViewModel session)
    {
        var method = typeof(SplitService).GetMethod(
            "GetSessionToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (CancellationToken)method!.Invoke(_sut, new object[] { session })!;
    }

    private static SessionPaneModel MakePane(
        string? paneId = null,
        string? serverId = null,
        string connectionType = "")
    {
        var pane = new SessionPaneModel { ConnectionType = connectionType };
        if (paneId is not null) pane.PaneId = paneId;
        if (serverId is not null) pane.ServerId = serverId;
        return pane;
    }

    /// <summary>
    /// Builds a binary tree with exactly <see cref="SplitService.MaxPanesPerTab"/>
    /// leaves (8), all marked as tool panes with canClose() == true so the
    /// tree passes <c>CloseAllPanes</c> but saturates the pane budget for
    /// <c>SplitSessionWithTool</c>.
    /// </summary>
    private static ISplitContent BuildEightLeafTree()
    {
        static SplitContainerModel Split(ISplitContent a, ISplitContent b)
            => new() { First = a, Second = b, Orientation = SplitOrientation.Vertical };

        return Split(
            Split(Split(MakePane(), MakePane()), Split(MakePane(), MakePane())),
            Split(Split(MakePane(), MakePane()), Split(MakePane(), MakePane())));
    }

    /// <summary>
    /// Minimal <see cref="IToolView"/> stub for exercising the
    /// <c>CanClose()</c> guard in <see cref="SplitService.CloseAllPanes"/>.
    /// </summary>
    private sealed class StubToolView : IToolView
    {
        private readonly bool _canClose;
        public bool Disposed { get; private set; }

        public StubToolView(bool canClose)
        {
            _canClose = canClose;
        }

        public void Initialize(ToolContext? context, LocalizationManager? localizer) { }
        public bool CanClose() => _canClose;
        public void Dispose() => Disposed = true;
    }
}
