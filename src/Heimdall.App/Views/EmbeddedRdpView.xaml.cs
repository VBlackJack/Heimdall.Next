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

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.App.Views.EmbeddedRdp;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Core.SessionDiagnostics;
using Heimdall.Core.StateMachine;
using Heimdall.Rdp;
using Heimdall.Rdp.ActiveX;
using Microsoft.Extensions.DependencyInjection;
using WinForms = System.Windows.Forms;

namespace Heimdall.App.Views;

/// <summary>
/// WPF host for the MsTscAx ActiveX control used by embedded RDP sessions.
/// Applies the proven WPF/WinForms layout flush pattern before Connect()
/// and delays dynamic resolution reconnects until the session is stable.
/// </summary>
public partial class EmbeddedRdpView : UserControl, IDisposable
{
    private TimeSpan _initialResizeEnableDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BeginConnectRetryDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan ResizeDebounceInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan AutofillFilledDisplayDuration = TimeSpan.FromSeconds(3);
    private const string EnterFullscreenGlyph = "\uE1D9";
    private const string ExitFullscreenGlyph = "\uE799";
    private const string RedirectionClipboardGlyph = "\uE16D";
    private const string RedirectionDrivesGlyph = "\uEDA2";
    private const string RedirectionPrintersGlyph = "\uE749";
    private const string RedirectionComPortsGlyph = "\uE7BC";
    private const string RedirectionSmartCardsGlyph = "\uE192";
    private const string RedirectionUsbGlyph = "\uE88E";
    private const string RedirectionAudioGlyph = "\uE7F6";
    private const string RedirectionMultiMonitorGlyph = "\uE7F4";
    private static readonly string[] DefaultResolutionPresets =
    [
        "1920x1080", "1680x1050", "1600x900", "1440x900", "1366x768",
        "1280x1024", "1280x720", "1024x768", "2560x1440", "3840x2160"
    ];

    /// <summary>
    /// Transient state of the embedded credential autofill watcher.
    /// </summary>
    private enum RdpAutofillState
    {
        None,
        Searching,
        Filled,
        TimedOut,
        Failed
    }

    private readonly DispatcherTimer _resizeTimer;

    private CancellationTokenSource? _autofillCts;
    private DispatcherTimer? _antiIdleTimer;
    private DispatcherTimer? _autofillFilledTimer;
    private DispatcherTimer? _stabilizationTimer;
    private DispatcherTimer? _reconnectElapsedTimer;
    private RdpActiveXHost? _rdpHost;
    private RdpRedirectionOptions? _pendingRedirections;
    private ServerProfileDto? _server;
    private AppSettings? _settings;
    private SessionPaneModel? _ownerPane;
    private SessionTabViewModel? _sessionTab;
    private ConnectionStateMachine? _connectionStateMachine;

    private Core.Localization.LocalizationManager? _localizer;
    private int? _tunnelPort;

    private RdpConnectionPhase _connectionPhase = RdpConnectionPhase.None;
    private RdpSessionStatus _sessionStatus = RdpSessionStatus.Disconnecting;
    private bool _initialized;
    private bool _connectStarted;
    private bool _eventSinkAttached;
    private bool _disposed;
    private bool _allowResolutionUpdates;
    private bool _sleepPreventionActive;
    private bool _comDrivenStatusActive;
    private bool _escapeHookRegistered;
    private bool _isFullscreen;
    private bool _disconnectConfirmInFlight;

    /// <summary>
    /// One-shot flag set when the header bar explicitly initiates the disconnect.
    /// </summary>
    private bool _userInitiatedDisconnect;
    private int _antiIdleIntervalSeconds;
    private int _beginConnectAttempt;
    private int _lastAppliedWidth;
    private int _lastAppliedHeight;
    private int _manualResolutionWidth;
    private int _manualResolutionHeight;
    private DateTime _connectedAtUtc;
    private DateTime _stabilizationDeadlineUtc;
    private DateTime? _reconnectStartUtc;

    /// <summary>
    /// Raised when the user clicks the Split button in the header strip.
    /// The subscriber (EmbeddedSessionManager) shows the split picker context menu.
    /// </summary>
    public event Action? SplitRequested;

    /// <summary>
    /// Raised when the user clicks "Reconnect" in the disconnect overlay.
    /// The subscriber should close this session and open a new connection.
    /// </summary>
    public event Action? ReconnectRequested;

    /// <summary>
    /// Raised when the user clicks "Edit profile" in the disconnect overlay.
    /// The subscriber opens the server profile editor for the current session.
    /// </summary>
    public event Action<string>? EditServerRequested;

    public EmbeddedRdpView()
    {
        InitializeComponent();

        _resizeTimer = new DispatcherTimer(
            ResizeDebounceInterval,
            DispatcherPriority.Background,
            OnResizeTimerTick,
            Dispatcher)
        {
            IsEnabled = false
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SurfaceContainer.SizeChanged += OnSurfaceContainerSizeChanged;
    }

    public void SetFullscreen(bool isFullscreen)
    {
        _isFullscreen = isFullscreen;
        SessionHeaderBar.Visibility = isFullscreen
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        FullscreenButton.Content = isFullscreen ? ExitFullscreenGlyph : EnterFullscreenGlyph;
    }

    public void ToggleFullscreen()
    {
        SetFullscreen(!_isFullscreen);
    }

    public void InitializeSession(
        ServerProfileDto server,
        SessionTabViewModel sessionTab,
        AppSettings settings,
        int antiIdleIntervalSeconds = 60,
        Core.Localization.LocalizationManager? localizer = null,
        int? tunnelPort = null,
        int resizeEnableDelayMs = 10000,
        ConnectionStateMachine? connectionStateMachine = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(sessionTab);
        ArgumentNullException.ThrowIfNull(settings);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EmbeddedRdpView));
        }

        if (_initialized)
        {
            return;
        }

        _server = server;
        _settings = settings;
        _sessionTab = sessionTab;
        _antiIdleIntervalSeconds = antiIdleIntervalSeconds;
        _localizer = localizer;
        _tunnelPort = tunnelPort;
        _initialResizeEnableDelay = TimeSpan.FromMilliseconds(resizeEnableDelayMs);
        _connectionStateMachine = connectionStateMachine;
        if (_connectionStateMachine is not null)
        {
            _connectionStateMachine.StateChanged += OnConnectionStateChanged;
        }

        _initialized = true;

        SessionTitleText.Text = server.DisplayName;
        EndpointTextBlock.Text = BuildEndpointText(server);

        if (_localizer is not null)
        {
            ResolutionButton.ToolTip = L("TooltipChangeResolution");
        }

        PopulateResolutionMenu();
        CreateHostControl();
        UpdateConnectingStatusFromStateMachineOrDefault();
    }

    public void SetOwningPane(SessionPaneModel pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        _ownerPane = pane;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Core.Logging.FileLogger.Info("EmbeddedRDP Dispose started");
        UnregisterEscapeHook();

        if (_connectionStateMachine is not null)
        {
            _connectionStateMachine.StateChanged -= OnConnectionStateChanged;
            _connectionStateMachine = null;
        }

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        SurfaceContainer.SizeChanged -= OnSurfaceContainerSizeChanged;
        _resizeTimer.Stop();
        _autofillFilledTimer?.Stop();
        _autofillFilledTimer = null;
        StopStabilizationCountdown();
        StopReconnectElapsedTracking();
        StopAntiIdleTimer();
        ReleaseSleepPrevention();
        CancelAutofill();
        TransitionPhase(RdpConnectionPhase.None);
        HideRedirectionIndicators();
        _allowResolutionUpdates = false;
        _sessionStatus = RdpSessionStatus.Disconnecting;
        UpdateHealthDot();

        // CRITICAL: Hide the FormsHost FIRST to prevent WPF ArrangeOverride
        // from trying to resize the COM control after it's been released
        try
        {
            FormsHost.Visibility = System.Windows.Visibility.Collapsed;
            FormsHost.Child = null;
        }
        catch (Exception ex) { Core.Logging.FileLogger.Warn($"[EmbeddedRdpView] FormsHost cleanup: {ex.Message}"); }

        if (_rdpHost is not null)
        {
            _rdpHost.Connected -= OnRdpConnected;
            _rdpHost.Disconnected -= OnRdpDisconnected;
            _rdpHost.FatalError -= OnRdpFatalError;
            _rdpHost.LoginComplete -= OnRdpLoginComplete;
            _rdpHost.AutoReconnecting -= OnRdpAutoReconnecting;
            _rdpHost.AutoReconnected -= OnRdpAutoReconnected;

            _rdpHost.CancelAutoReconnect = true;

            try { _rdpHost.Disconnect(); }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"EmbeddedRDP Disconnect during dispose failed: {ex.Message}");
            }

            try
            {
                if (_eventSinkAttached) _rdpHost.DetachEventSink();
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"EmbeddedRDP DetachEventSink during dispose failed: {ex.Message}");
            }

            try { _rdpHost.Dispose(); }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"EmbeddedRDP host dispose failed: {ex.Message}");
            }
            _rdpHost = null;
        }
        _autofillCts?.Dispose();
        _autofillCts = null;
        Core.Logging.FileLogger.Info("EmbeddedRDP Dispose completed");
    }

    internal IntPtr GetRdpKeyboardInputHandle()
    {
        return FormsHost.Child is WinForms.Control control && control.IsHandleCreated
            ? control.Handle
            : IntPtr.Zero;
    }

    internal void FocusRdpToolbarFromEscapeHook()
    {
        if (_disposed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(FocusRdpToolbarFromEscapeHook));
            return;
        }

        _ = DisconnectButton.Focus();
        _ = Keyboard.Focus(DisconnectButton);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_disposed || !_initialized)
        {
            return;
        }

        RegisterEscapeHook();

        if (_connectStarted)
        {
            return;
        }

        _connectStarted = true;
        Core.Logging.FileLogger.Info(
            $"EmbeddedRDP Loaded: isVisible={IsVisible} formsVisible={FormsHost.IsVisible} formsSize={FormsHost.ActualWidth:0.##}x{FormsHost.ActualHeight:0.##} surfaceSize={SurfaceContainer.ActualWidth:0.##}x{SurfaceContainer.ActualHeight:0.##}");

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(BeginConnect));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnregisterEscapeHook();
    }

    private void RegisterEscapeHook()
    {
        if (_escapeHookRegistered)
        {
            return;
        }

        _escapeHookRegistered = RdpKeyboardEscapeHook.Register(
            this,
            new RdpHookShortcuts(
                _settings?.RdpReleaseFocusShortcut,
                _settings?.RdpFullscreenToggleShortcut));
    }

    private void UnregisterEscapeHook()
    {
        if (!_escapeHookRegistered)
        {
            return;
        }

        RdpKeyboardEscapeHook.Unregister(this);
        _escapeHookRegistered = false;
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _rdpHost is null)
        {
            return;
        }

        if (_settings?.RdpConfirmDisconnect == true)
        {
            if (_disconnectConfirmInFlight)
            {
                return;
            }

            var dialogService = (Application.Current as App)?.Services?.GetService<IDialogService>();
            if (dialogService is not null)
            {
                try
                {
                    _disconnectConfirmInFlight = true;
                    var confirmed = await dialogService.ShowConfirmAsync(
                        _localizer?["RdpConfirmDisconnectTitle"] ?? "Disconnect",
                        _localizer?.Format(
                            "RdpConfirmDisconnectMessage",
                            _server?.DisplayName ?? string.Empty)
                        ?? "Disconnect from this session?",
                        "warning");

                    if (!confirmed)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Core.Logging.FileLogger.Warn(
                        $"[EmbeddedRdpView] Disconnect confirmation failed: {ex.Message}");
                }
                finally
                {
                    _disconnectConfirmInFlight = false;
                }

                if (_disposed || _rdpHost is null)
                {
                    return;
                }
            }
        }

        try
        {
            Core.Logging.FileLogger.Info("EmbeddedRDP Disconnect requested by user");
            _userInitiatedDisconnect = true;
            UpdateHealthDot();
            _allowResolutionUpdates = false;
            StopStabilizationCountdown();
            StopReconnectElapsedTracking();
            TransitionPhase(RdpConnectionPhase.None);
            TryTransitionConnectionState(ConnectionState.Disconnecting);
            UpdateSessionStatus(RdpSessionStatus.Disconnecting);
            _rdpHost.Disconnect();
        }
        catch (Exception ex)
        {
            HandleFailure("Disconnect failed.", ex);
        }
    }

    private void OnCancelReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _rdpHost is null)
        {
            return;
        }

        TransitionPhase(RdpConnectionPhase.Preparing);

        try
        {
            Core.Logging.FileLogger.Info("EmbeddedRDP user cancelled auto-reconnect");
            _userInitiatedDisconnect = true;
            UpdateHealthDot();
            _rdpHost.CancelAutoReconnect = true;
            StopReconnectElapsedTracking();
            TryTransitionConnectionState(ConnectionState.Disconnecting);
            UpdateSessionStatus(RdpSessionStatus.Disconnecting);
        }
        catch (Exception ex)
        {
            HandleFailure("Cancel reconnect failed.", ex);
        }
    }

    private void OnCancelConnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _rdpHost is null)
        {
            return;
        }

        try
        {
            Core.Logging.FileLogger.Info("EmbeddedRDP user cancelled in-progress connection");
            _userInitiatedDisconnect = true;
            UpdateHealthDot();
            _allowResolutionUpdates = false;
            StopStabilizationCountdown();
            StopReconnectElapsedTracking();
            TransitionPhase(RdpConnectionPhase.None);
            _rdpHost.CancelAutoReconnect = true;
            TryTransitionConnectionState(ConnectionState.Disconnecting);
            UpdateSessionStatus(RdpSessionStatus.Disconnecting);
            _rdpHost.Disconnect();
        }
        catch (Exception ex)
        {
            HandleFailure("Cancel connection failed.", ex);
        }
    }

    private void OnFullscreenButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void OnSendKeysButtonClick(object sender, RoutedEventArgs e)
    {
        SendKeysMenu.PlacementTarget = SendKeysButton;
        SendKeysMenu.IsOpen = true;
    }

    [SupportedOSPlatform("windows")]
    private void OnSendKeysCtrlAltDelClick(object sender, RoutedEventArgs e)
        => SendKeysToRemote(NativeMethods.VK_CONTROL, NativeMethods.VK_MENU, NativeMethods.VK_DELETE);

    [SupportedOSPlatform("windows")]
    private void OnSendKeysWindowsClick(object sender, RoutedEventArgs e)
        => SendKeysToRemote(NativeMethods.VK_LWIN);

    [SupportedOSPlatform("windows")]
    private void OnSendKeysAltTabClick(object sender, RoutedEventArgs e)
        => SendKeysToRemote(NativeMethods.VK_MENU, NativeMethods.VK_TAB);

    [SupportedOSPlatform("windows")]
    private void OnSendKeysCtrlEscClick(object sender, RoutedEventArgs e)
        => SendKeysToRemote(NativeMethods.VK_CONTROL, NativeMethods.VK_ESCAPE);

    [SupportedOSPlatform("windows")]
    private void OnSendKeysPrintScreenClick(object sender, RoutedEventArgs e)
        => SendKeysToRemote(NativeMethods.VK_SNAPSHOT);

    [SupportedOSPlatform("windows")]
    private void OnSendKeysEscapeClick(object sender, RoutedEventArgs e)
        => SendKeysToRemote(NativeMethods.VK_ESCAPE);

    [SupportedOSPlatform("windows")]
    private void SendKeysToRemote(params byte[] virtualKeys)
    {
        if (_disposed || _rdpHost is null || !_rdpHost.IsConnected || virtualKeys.Length == 0)
        {
            return;
        }

        try
        {
            var hwnd = _rdpHost.HostHandle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var target = FindDeepestRdpChildWindow(hwnd);
            foreach (var virtualKey in virtualKeys)
            {
                NativeMethods.PostMessage(
                    target,
                    NativeMethods.WM_KEYDOWN,
                    new IntPtr(virtualKey),
                    IntPtr.Zero);
            }

            for (var index = virtualKeys.Length - 1; index >= 0; index--)
            {
                NativeMethods.PostMessage(
                    target,
                    NativeMethods.WM_KEYUP,
                    new IntPtr(virtualKeys[index]),
                    IntPtr.Zero);
            }

            Core.Logging.FileLogger.Info("EmbeddedRDP user sent keys to the remote session");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error("Send keys to the remote session failed.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr FindDeepestRdpChildWindow(IntPtr hwnd)
    {
        var target = hwnd;
        var child = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            target = child;
            child = NativeMethods.FindWindowEx(target, IntPtr.Zero, null, null);
        }

        return target;
    }

    private void OnSplitClick(object sender, RoutedEventArgs e)
    {
        SplitRequested?.Invoke();
    }

    private void OnSurfaceContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed || _server is null || !_server.RdpDynamicResolution)
        {
            return;
        }

        // Only log significant size changes to avoid polluting logs
        double dw = Math.Abs(e.NewSize.Width - e.PreviousSize.Width);
        double dh = Math.Abs(e.NewSize.Height - e.PreviousSize.Height);
        if (dw > 50 || dh > 50)
        {
            Core.Logging.FileLogger.Info(
                $"EmbeddedRDP SizeChanged: {e.PreviousSize.Width:0}x{e.PreviousSize.Height:0} -> {e.NewSize.Width:0}x{e.NewSize.Height:0}");
        }

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void OnResizeTimerTick(object? sender, EventArgs e)
    {
        _resizeTimer.Stop();

        if (_disposed || _rdpHost is null || _server is null)
        {
            return;
        }

        var (width, height) = GetDisplayDimensions();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (!_rdpHost.IsConnected)
        {
            Core.Logging.FileLogger.Info(
                $"EmbeddedRDP Resize skipped while not connected: target={width}x{height}");
            return;
        }

        if (!_allowResolutionUpdates)
        {
            Core.Logging.FileLogger.Info(
                $"EmbeddedRDP Resize deferred until post-connect stabilization: target={width}x{height}");
            return;
        }

        // Skip resizes under 50px delta — these are caused by tab hover, panel toggle,
        // scrollbar toggling, etc. Only reconnect on intentional resizes (window resize,
        // fullscreen toggle, split pane drag).
        int deltaW = Math.Abs(width - _lastAppliedWidth);
        int deltaH = Math.Abs(height - _lastAppliedHeight);
        if (deltaW < 50 && deltaH < 50)
        {
            return;
        }

        try
        {
            Core.Logging.FileLogger.Info(
                $"EmbeddedRDP UpdateResolution requested: {width}x{height} connectedFor={(DateTime.UtcNow - _connectedAtUtc).TotalSeconds:0.0}s");
            _rdpHost.UpdateResolution(width, height);
            _lastAppliedWidth = width;
            _lastAppliedHeight = height;
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Resize during RDP session: {ex.Message}");
        }
    }

    private void BeginConnect()
    {
        var settings = _settings;
        if (_disposed || _rdpHost is null || _server is null || settings is null)
        {
            return;
        }

        try
        {
            _beginConnectAttempt++;
            Core.Logging.FileLogger.Info(
                $"EmbeddedRDP BeginConnect attempt={_beginConnectAttempt} viewVisible={IsVisible} formsVisible={FormsHost.IsVisible} formsSize={FormsHost.ActualWidth:0.##}x{FormsHost.ActualHeight:0.##} surfaceSize={SurfaceContainer.ActualWidth:0.##}x{SurfaceContainer.ActualHeight:0.##}");

            if (!IsVisualSurfaceReady())
            {
                if (_beginConnectAttempt <= 10)
                {
                    Core.Logging.FileLogger.Warn("EmbeddedRDP visual surface is not ready; retrying after render pass.");
                    _ = RetryBeginConnectAsync();
                    return;
                }

                Core.Logging.FileLogger.Warn("EmbeddedRDP continuing even though the visual surface did not report as ready.");
                SetPaneDiagnostic(new SessionDiagnostic(
                    SessionFailureStage.RdpActiveXDisconnect,
                    "RdpSurfaceNotReady"));
            }

            FlushLayoutPipeline("pre-connect");
            EnsureHostHandle();
            FlushLayoutPipeline("post-handle");

            var connectHost = ResolveConnectHost(_server);
            var connectPort = ResolveConnectPort(_server);
            var (username, domain) = SplitUsername(_server.RdpUsername);
            var password = TryDecryptPassword(_server);
            var (width, height) = GetDisplayDimensions();

            Core.Logging.FileLogger.Info(
                $"EmbeddedRDP BeginConnect: host={connectHost}:{connectPort} user={username} domain={domain} hasPassword={!string.IsNullOrEmpty(password)} size={width}x{height} handle=0x{_rdpHost.HostHandle.ToInt64():X} clsid={_rdpHost.ActiveXClsid}");

            _rdpHost.SetServer(connectHost, connectPort);
            if (!string.IsNullOrWhiteSpace(username))
            {
                _rdpHost.SetCredentials(username, password, domain);
                Core.Logging.FileLogger.Info($"EmbeddedRDP SetCredentials called for user={username}");
            }

            _rdpHost.SetDisplay(width, height, RdpProfileResolver.ResolveColorDepth(_server, settings));
            _lastAppliedWidth = width;
            _lastAppliedHeight = height;
            _pendingRedirections = RdpProfileResolver.BuildRedirections(_server, settings);
            _rdpHost.SetRedirections(_pendingRedirections);

            if (!_eventSinkAttached)
            {
                if (!_rdpHost.AttachEventSink())
                {
                    throw new InvalidOperationException(
                        _rdpHost.LastError ?? "Failed to attach the Remote Desktop event sink.");
                }

                _eventSinkAttached = true;
            }

            Core.Logging.FileLogger.Info("EmbeddedRDP calling Connect()...");
            _rdpHost.Connect();
            TransitionPhase(RdpConnectionPhase.Connecting);

            // Post-connect flush removed: layout is already stable after pre-connect + post-handle flushes.
            // The third flush added ~50-150ms latency with no airspace benefit since Connect() is async.
            UpdateConnectingStatusFromStateMachineOrDefault();

            if (!string.IsNullOrWhiteSpace(password))
            {
                StartCredentialAutofill(password, _server.RemoteServer);
            }
        }
        catch (Exception ex)
        {
            HandleFailure("Unable to start the embedded Remote Desktop session.", ex);
        }
    }

    private async Task RetryBeginConnectAsync()
    {
        try
        {
            await Task.Delay(BeginConnectRetryDelay);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"[EmbeddedRdpView] retry delay: {ex.Message}");
            return;
        }

        if (_disposed)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(BeginConnect));
    }

    private void OnRdpConnected()
    {
        Core.Logging.FileLogger.Info("EmbeddedRDP OnConnected fired");
        if (_disposed)
        {
            return;
        }

        _comDrivenStatusActive = true;
        TryTransitionConnectionState(ConnectionState.Connected);

        Dispatcher.Invoke(() =>
        {
            CancelAutofill();

            try
            {
                _rdpHost?.ClearPassword();
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"Embedded RDP ClearPassword failed: {ex.Message}");
            }

            _connectedAtUtc = DateTime.UtcNow;
            _allowResolutionUpdates = false;
            ClearPaneDiagnostic();
            TransitionPhase(RdpConnectionPhase.Connected);
            UpdateSessionStatus(RdpSessionStatus.Connected);
            UpdateRedirectionIndicators();
            FlushLayoutPipeline("on-connected");

            if (_server is not null && _server.RdpAntiIdle && _antiIdleIntervalSeconds > 0)
            {
                StartAntiIdleTimer(_antiIdleIntervalSeconds);
            }

            AcquireSleepPrevention();

            if (_server is not null && _server.RdpDynamicResolution)
            {
                StartStabilizationCountdown(_initialResizeEnableDelay);
                _ = EnableResolutionUpdatesAsync();
            }
        });
    }

    private async Task EnableResolutionUpdatesAsync()
    {
        try
        {
            await Task.Delay(_initialResizeEnableDelay);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"[EmbeddedRdpView] resolution delay: {ex.Message}");
            return;
        }

        if (_disposed || _rdpHost is null || !_rdpHost.IsConnected)
        {
            StopStabilizationCountdown();
            return;
        }

        _allowResolutionUpdates = true;
        StopStabilizationCountdown();
        Core.Logging.FileLogger.Info("EmbeddedRDP dynamic resolution is now enabled.");

        var (queuedWidth, queuedHeight) = GetDisplayDimensions();
        if (queuedWidth > 0 && queuedHeight > 0
            && (queuedWidth != _lastAppliedWidth || queuedHeight != _lastAppliedHeight))
        {
            try
            {
                Core.Logging.FileLogger.Info(
                    $"EmbeddedRDP applying queued resolution after stabilization: {queuedWidth}x{queuedHeight}");
                _rdpHost.UpdateResolution(queuedWidth, queuedHeight);
                _lastAppliedWidth = queuedWidth;
                _lastAppliedHeight = queuedHeight;
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"[EmbeddedRdpView] queued resolution flush: {ex.Message}");
            }
        }
    }

    private void OnRdpDisconnected(int reason)
    {
        Core.Logging.FileLogger.Info($"EmbeddedRDP OnDisconnected fired: reason={reason}");
        if (_disposed)
        {
            return;
        }

        var wasUserInitiated = _userInitiatedDisconnect;
        _userInitiatedDisconnect = false;
        var suppressOverlay = ShouldSuppressReconnectOverlay(wasUserInitiated, reason);
        TryTransitionConnectionState(ConnectionState.Disconnected);

        Dispatcher.Invoke(() =>
        {
            CancelAutofill();
            StopAntiIdleTimer();
            StopStabilizationCountdown();
            StopReconnectElapsedTracking();
            ReleaseSleepPrevention();
            TransitionPhase(RdpConnectionPhase.None);
            HideRedirectionIndicators();
            _allowResolutionUpdates = false;

            if (suppressOverlay)
            {
                ClearPaneDiagnostic();
                UpdateSessionStatus(RdpSessionStatus.Disconnected);
                UpdateHealthDot(wasUserInitiated);
                Core.Logging.FileLogger.Info(
                    $"EmbeddedRDP suppressed reconnect overlay: userInitiated={wasUserInitiated} reason={reason}");
                return;
            }

            SetPaneDiagnostic(RdpHostDiagnosticFactory.FromDisconnect(reason));
            UpdateSessionStatus(RdpSessionStatus.Disconnected);
            UpdateHealthDot(wasUserInitiated);
            ShowReconnectOverlay();
        });
    }

    private void OnRdpFatalError(int errorCode)
    {
        Core.Logging.FileLogger.Warn($"EmbeddedRDP OnFatalError fired: errorCode={errorCode}");
        if (_disposed)
        {
            return;
        }

        var fatalMessage = _localizer?.Format("RdpStatusFatalErrorDetail", errorCode)
            ?? $"Remote Desktop reported a fatal error ({errorCode}).";
        SetConnectionStateError(fatalMessage);

        Dispatcher.Invoke(() =>
        {
            CancelAutofill();
            StopStabilizationCountdown();
            StopReconnectElapsedTracking();
            TransitionPhase(RdpConnectionPhase.None);
            HideRedirectionIndicators();
            _allowResolutionUpdates = false;
            SetPaneDiagnostic(RdpHostDiagnosticFactory.FromFatalError(errorCode));
            UpdateSessionStatus(RdpSessionStatus.Error);
            ShowReconnectOverlay();
        });
    }

    private void OnRdpLoginComplete()
    {
        Core.Logging.FileLogger.Info("EmbeddedRDP OnLoginComplete fired");
        if (_disposed) return;

        Dispatcher.Invoke(() =>
        {
            if (_connectionPhase is RdpConnectionPhase.Preparing or RdpConnectionPhase.Connecting)
            {
                TransitionPhase(RdpConnectionPhase.Loading);
            }

            try { _rdpHost?.ClearPassword(); }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"EmbeddedRDP ClearPassword (login): {ex.Message}"); }
        });
    }

    private void OnRdpAutoReconnecting(int disconnectReason, int attemptCount)
    {
        Core.Logging.FileLogger.Info($"EmbeddedRDP OnAutoReconnecting: reason={disconnectReason} attempt={attemptCount}");
        if (_disposed) return;

        Dispatcher.Invoke(() =>
        {
            StopStabilizationCountdown();
            TransitionPhase(RdpConnectionPhase.None);
            StartReconnectElapsedTracking();
            HideRedirectionIndicators();
            _allowResolutionUpdates = false;
            UpdateSessionStatus(RdpSessionStatus.Reconnecting, attemptCount);
        });
    }

    private void OnRdpAutoReconnected()
    {
        Core.Logging.FileLogger.Info("EmbeddedRDP OnAutoReconnected fired");
        if (_disposed) return;

        Dispatcher.Invoke(() =>
        {
            ClearPaneDiagnostic();
            StopReconnectElapsedTracking();
            TransitionPhase(RdpConnectionPhase.Connected);
            UpdateSessionStatus(RdpSessionStatus.Connected);
            UpdateRedirectionIndicators();

            if (_server is not null && _server.RdpDynamicResolution)
            {
                StartStabilizationCountdown(_initialResizeEnableDelay);
                _ = EnableResolutionUpdatesAsync();
            }
        });
    }

    private void CreateHostControl()
    {
        _rdpHost = new RdpActiveXHost
        {
            Dock = WinForms.DockStyle.Fill
        };

        _rdpHost.Connected += OnRdpConnected;
        _rdpHost.Disconnected += OnRdpDisconnected;
        _rdpHost.FatalError += OnRdpFatalError;
        _rdpHost.LoginComplete += OnRdpLoginComplete;
        _rdpHost.AutoReconnecting += OnRdpAutoReconnecting;
        _rdpHost.AutoReconnected += OnRdpAutoReconnected;

        FormsHost.Child = _rdpHost.GetHostControl();

        Core.Logging.FileLogger.Info(
            $"EmbeddedRDP host created: clsid={_rdpHost.ActiveXClsid} childType={FormsHost.Child?.GetType().FullName ?? "null"}");
    }

    private void EnsureHostHandle()
    {
        if (_rdpHost is null)
        {
            throw new InvalidOperationException("The RDP host control is not available.");
        }

        if (!_rdpHost.IsHandleCreated)
        {
            _ = _rdpHost.Handle;
            WinForms.Application.DoEvents();
        }

        Core.Logging.FileLogger.Info(
            $"EmbeddedRDP EnsureHostHandle: handle=0x{_rdpHost.HostHandle.ToInt64():X} handleCreated={_rdpHost.IsHandleCreated}");
    }

    private void StartCredentialAutofill(string password, string hostHint)
    {
        CancelAutofill();
        _autofillCts = new CancellationTokenSource();
        var token = _autofillCts.Token;

        Core.Logging.FileLogger.Info(
            $"EmbeddedRDP starting CredUI autofill watcher for hostHint={hostHint}");

        _ = TryAutofillCredentialsAsync(password, hostHint, token);
    }

    private async Task TryAutofillCredentialsAsync(string password, string hostHint, CancellationToken cancellationToken)
    {
        Dispatcher.Invoke(() => UpdateAutofillState(RdpAutofillState.Searching));

        try
        {
            var timeoutMs = _settings?.RdpCredentialAutofillTimeoutMs ?? 90000;
            var filled = await CredentialAutofill.WaitAndFillAsync(
                Environment.ProcessId,
                hostHint,
                password,
                TimeSpan.FromMilliseconds(timeoutMs),
                cancellationToken).ConfigureAwait(false);

            if (filled)
            {
                Dispatcher.Invoke(() => UpdateAutofillState(RdpAutofillState.Filled));
            }
            else
            {
                Core.Logging.FileLogger.Warn(
                    $"EmbeddedRDP CredUI autofill timed out for hostHint={hostHint}");
                Dispatcher.Invoke(() => UpdateAutofillState(RdpAutofillState.TimedOut));
            }
        }
        catch (OperationCanceledException)
        {
            // Session connected or was disposed before a credential dialog appeared.
            if (!_disposed)
            {
                Dispatcher.Invoke(() => UpdateAutofillState(RdpAutofillState.None));
            }
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"Embedded RDP credential autofill failed: {ex.Message}");
            if (!_disposed)
            {
                Dispatcher.Invoke(() => UpdateAutofillState(RdpAutofillState.Failed));
            }
        }
    }

    private void CancelAutofill()
    {
        if (_autofillCts is null)
        {
            return;
        }

        var cts = _autofillCts;
        _autofillCts = null;

        try
        {
            cts.Cancel();
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"[EmbeddedRdpView] autofill cancel: {ex.Message}");
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Updates the credential autofill sub-status in the header.
    /// </summary>
    private void UpdateAutofillState(RdpAutofillState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateAutofillState(state));
            return;
        }

        if (_disposed)
        {
            return;
        }

        _autofillFilledTimer?.Stop();

        var key = state switch
        {
            RdpAutofillState.Searching => "RdpAutofillSearching",
            RdpAutofillState.Filled => "RdpAutofillFilled",
            RdpAutofillState.TimedOut => "RdpAutofillTimedOut",
            RdpAutofillState.Failed => "RdpAutofillFailed",
            _ => null
        };

        if (key is null)
        {
            AutofillStatusText.Text = string.Empty;
            AutofillStatusText.Visibility = Visibility.Collapsed;
            AutofillSeparator.Visibility = Visibility.Collapsed;
            return;
        }

        AutofillStatusText.Text = L(key);
        AutofillStatusText.Visibility = Visibility.Visible;
        AutofillSeparator.Visibility = Visibility.Visible;

        if (state == RdpAutofillState.Filled)
        {
            _autofillFilledTimer ??= new DispatcherTimer(
                AutofillFilledDisplayDuration,
                DispatcherPriority.Background,
                OnAutofillFilledTimerTick,
                Dispatcher);
            _autofillFilledTimer.Interval = AutofillFilledDisplayDuration;
            _autofillFilledTimer.Start();
        }
    }

    private void OnAutofillFilledTimerTick(object? sender, EventArgs e)
    {
        _autofillFilledTimer?.Stop();
        UpdateAutofillState(RdpAutofillState.None);
    }

    private void TransitionPhase(RdpConnectionPhase newPhase)
    {
        if (newPhase == _connectionPhase)
        {
            return;
        }

        if (newPhase is RdpConnectionPhase.Preparing
            or RdpConnectionPhase.Connecting
            or RdpConnectionPhase.Loading
            or RdpConnectionPhase.Connected)
        {
            StopReconnectElapsedTracking();
        }

        _connectionPhase = newPhase;
        UpdatePhaseStepper();
        UpdateVisibilityForPhase();

        var statusKey = RdpConnectionPhasePolicy.GetStatusKey(newPhase);
        if (statusKey is not null)
        {
            StatusTextBlock.Text = L(statusKey);
        }

        UpdateHealthDot();
    }

    private void UpdatePhaseStepper()
    {
        var litSegments = RdpConnectionPhasePolicy.GetLitSegmentCount(_connectionPhase);
        if (litSegments == 0)
        {
            ConnectionPhaseStepper.Visibility = Visibility.Collapsed;
            return;
        }

        ConnectionPhaseStepper.Visibility = Visibility.Visible;
        SetPhaseSegmentState(PhaseSegmentPreparing, litSegments >= 1);
        SetPhaseSegmentState(PhaseSegmentConnecting, litSegments >= 2);
        SetPhaseSegmentState(PhaseSegmentLoading, litSegments >= 3);
        SetPhaseSegmentState(PhaseSegmentConnected, litSegments >= 4);
    }

    private static void SetPhaseSegmentState(Border segment, bool isLit)
    {
        segment.SetResourceReference(
            Border.BackgroundProperty,
            isLit ? "AccentBrush" : "TextDisabledBrush");
    }

    private void UpdateVisibilityForPhase()
    {
        var (cancelConnectVisible, disconnectVisible) =
            RdpConnectionPhasePolicy.ResolveVisibility(_connectionPhase);

        CancelConnectButton.Visibility = cancelConnectVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        CancelConnectButton.IsEnabled = !_disposed && cancelConnectVisible;

        DisconnectButton.Visibility = disconnectVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        DisconnectButton.IsEnabled = !_disposed && disconnectVisible;
    }

    private void UpdateHealthDot(bool? wasUserInitiatedDisconnectOverride = null)
    {
        var state = RdpHealthDotPolicy.Resolve(
            _connectionPhase,
            _sessionStatus,
            wasUserInitiatedDisconnectOverride ?? _userInitiatedDisconnect);

        HealthDot.SetResourceReference(
            Border.BackgroundProperty,
            ResolveHealthDotBrushKey(state));

        var label = L(ResolveHealthDotLabelKey(state));
        var endpoint = _server is null ? string.Empty : BuildEndpointText(_server);
        HealthDot.ToolTip = string.IsNullOrWhiteSpace(endpoint)
            ? label
            : string.Format(CultureInfo.CurrentCulture, "{0} - {1}", label, endpoint);
    }

    private static string ResolveHealthDotBrushKey(RdpHealthDotState state) => state switch
    {
        RdpHealthDotState.Healthy => "SuccessBrush",
        RdpHealthDotState.Transitional => "WarningBrush",
        RdpHealthDotState.Faulted => "ErrorBrush",
        _ => "TextDisabledBrush"
    };

    private static string ResolveHealthDotLabelKey(RdpHealthDotState state) => state switch
    {
        RdpHealthDotState.Healthy => "RdpHealthDotHealthy",
        RdpHealthDotState.Transitional => "RdpHealthDotTransitional",
        RdpHealthDotState.Faulted => "RdpHealthDotFaulted",
        _ => "RdpHealthDotIdle"
    };

    private void UpdateRedirectionIndicators()
    {
        if (_localizer is null
            || _pendingRedirections is null
            || _rdpHost is null
            || !_rdpHost.IsConnected)
        {
            HideRedirectionIndicators();
            return;
        }

        SetRedirectionIndicator(
            RedirIconClipboard,
            RedirectionClipboardGlyph,
            "RdpRedirectionLabelClipboard",
            _pendingRedirections.Clipboard);
        SetRedirectionIndicator(
            RedirIconDrives,
            RedirectionDrivesGlyph,
            "RdpRedirectionLabelDrives",
            _pendingRedirections.Drives);
        SetRedirectionIndicator(
            RedirIconPrinters,
            RedirectionPrintersGlyph,
            "RdpRedirectionLabelPrinters",
            _pendingRedirections.Printers);
        SetRedirectionIndicator(
            RedirIconComPorts,
            RedirectionComPortsGlyph,
            "RdpRedirectionLabelComPorts",
            _pendingRedirections.ComPorts);
        SetRedirectionIndicator(
            RedirIconSmartCards,
            RedirectionSmartCardsGlyph,
            "RdpRedirectionLabelSmartCards",
            _pendingRedirections.SmartCards);
        SetRedirectionIndicator(
            RedirIconUsb,
            RedirectionUsbGlyph,
            "RdpRedirectionLabelUsb",
            _pendingRedirections.Usb);
        SetRedirectionIndicator(
            RedirIconAudio,
            RedirectionAudioGlyph,
            "RdpRedirectionLabelAudio",
            _pendingRedirections.AudioMode != 0);
        SetRedirectionIndicator(
            RedirIconMultiMonitor,
            RedirectionMultiMonitorGlyph,
            "RdpRedirectionLabelMultiMonitor",
            _pendingRedirections.MultiMonitor);

        RedirectionIndicatorsPanel.Visibility = Visibility.Visible;
    }

    private void HideRedirectionIndicators()
    {
        RedirectionIndicatorsPanel.Visibility = Visibility.Collapsed;
    }

    private void SetRedirectionIndicator(
        TextBlock icon,
        string glyph,
        string labelKey,
        bool isActive)
    {
        var label = L(labelKey);
        var state = L(isActive ? "RdpRedirectionStateOn" : "RdpRedirectionStateOff");

        icon.Text = glyph;
        icon.ToolTip = label;
        icon.SetResourceReference(
            TextBlock.ForegroundProperty,
            isActive ? "AccentBrush" : "TextDisabledBrush");
        AutomationProperties.SetName(icon, $"{label}: {state}");
    }

    private void OnConnectionStateChanged(
        string serverId,
        ConnectionState previousState,
        ConnectionState newState,
        string? errorMessage)
    {
        _ = previousState;
        _ = errorMessage;

        if (_server is null
            || !ShouldHandleStateChange(serverId, _server.Id, _comDrivenStatusActive, _disposed))
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyConnectionStateStatus(newState);
        }
        else
        {
            Dispatcher.Invoke(() => ApplyConnectionStateStatus(newState));
        }
    }

    private void UpdateConnectingStatusFromStateMachineOrDefault()
    {
        UpdateSessionStatus(RdpSessionStatus.Connecting);
        ApplyCurrentConnectionStateStatus();
    }

    private void ApplyCurrentConnectionStateStatus()
    {
        if (_connectionStateMachine is null || _server is null || _comDrivenStatusActive)
        {
            return;
        }

        var state = _connectionStateMachine.GetState(_server.Id);
        if (state is ConnectionState.Disconnected)
        {
            return;
        }

        ApplyConnectionStateStatus(state);
    }

    private void ApplyConnectionStateStatus(ConnectionState state)
    {
        var metadata = ConnectionStateMachine.GetMetadata(state);
        if (string.IsNullOrWhiteSpace(metadata.DisplayKey))
        {
            return;
        }

        StatusTextBlock.Text = FormatConnectionStateStatus(metadata.DisplayKey);
        RdpLoadingBar.Visibility = metadata.IsProgress ? Visibility.Visible : Visibility.Collapsed;
        StatusTextBlock.Foreground = GetBrush("TextPrimaryBrush", Brushes.White);
    }

    private string FormatConnectionStateStatus(string statusKey)
    {
        if (_localizer is null)
        {
            return statusKey;
        }

        return statusKey switch
        {
            "StatusConnecting" => _localizer.Format(statusKey, BuildConnectionStateTarget()),
            "StatusEstablishingTunnel" => _localizer.Format(statusKey, BuildConnectionStateTarget()),
            "StatusTunnelEstablished" => _localizer.Format(statusKey, _tunnelPort ?? 0),
            _ => L(statusKey),
        };
    }

    private string BuildConnectionStateTarget()
        => _server is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(_server.RemoteServer)
                ? _server.DisplayName
                : _server.RemoteServer;

    private void TryTransitionConnectionState(ConnectionState state)
    {
        if (_connectionStateMachine is null || _server is null)
        {
            return;
        }

        _connectionStateMachine.TryTransition(_server.Id, state);
    }

    private void SetConnectionStateError(string message)
    {
        if (_connectionStateMachine is null || _server is null)
        {
            return;
        }

        _connectionStateMachine.SetError(_server.Id, message);
    }

    private void UpdateSessionStatus(
        RdpSessionStatus status,
        int? reconnectAttempt = null)
    {
        _sessionStatus = status;
        var invariantCode = RdpSessionStatusKeys.GetInvariantCode(status);
        string localizedLabel;

        if (status == RdpSessionStatus.Reconnecting && _localizer is not null)
        {
            var attempt = reconnectAttempt ?? 1;
            localizedLabel = _localizer.Format(
                RdpSessionStatusKeys.GetKey(status),
                attempt,
                RdpActiveXHost.MaxAutoReconnectAttempts);
        }
        else
        {
            localizedLabel = L(RdpSessionStatusKeys.GetKey(status));
        }

        if (_sessionTab is not null)
        {
            _sessionTab.Status = invariantCode;
        }

        StatusTextBlock.Text = localizedLabel;

        var isProgress = status is RdpSessionStatus.Connecting
            or RdpSessionStatus.Preparing
            or RdpSessionStatus.Reconnecting;
        RdpLoadingBar.Visibility = isProgress ? Visibility.Visible : Visibility.Collapsed;
        if (status is RdpSessionStatus.Reconnecting)
        {
            RdpLoadingBar.IsIndeterminate = false;
            RdpLoadingBar.Minimum = 0;
            RdpLoadingBar.Maximum = RdpActiveXHost.MaxAutoReconnectAttempts;
            RdpLoadingBar.Value = ResolveReconnectProgressValue(
                reconnectAttempt ?? 0,
                RdpActiveXHost.MaxAutoReconnectAttempts);
        }
        else
        {
            RdpLoadingBar.IsIndeterminate = true;
            RdpLoadingBar.Value = 0;
        }

        if (status is not RdpSessionStatus.Connected)
        {
            HideRedirectionIndicators();
        }

        UpdateVisibilityForPhase();
        FullscreenButton.IsEnabled = !_disposed;
        SendKeysButton.IsEnabled = !_disposed && status is RdpSessionStatus.Connected;
        CancelReconnectButton.Visibility = status == RdpSessionStatus.Reconnecting
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusTextBlock.Foreground = GetBrush(
            status is RdpSessionStatus.Error ? "ErrorBrush" : "TextPrimaryBrush",
            status is RdpSessionStatus.Error ? Brushes.IndianRed : Brushes.White);
        UpdateHealthDot();
    }

    internal static int ResolveReconnectProgressValue(int currentAttempt, int maxAttempts)
    {
        if (maxAttempts <= 0)
        {
            Core.Logging.FileLogger.Warn(
                $"EmbeddedRDP invalid auto-reconnect maxAttempts={maxAttempts}; progress reset to 0.");
            return 0;
        }

        return Math.Clamp(currentAttempt, 0, maxAttempts);
    }

    private void HandleFailure(string message, Exception ex)
    {
        Core.Logging.FileLogger.Error(message, ex);
        _allowResolutionUpdates = false;
        StopReconnectElapsedTracking();
        TransitionPhase(RdpConnectionPhase.None);
        HideRedirectionIndicators();
        UpdateSessionStatus(RdpSessionStatus.Error);
        StatusTextBlock.Text = _localizer?.Format("RdpStatusErrorDetail", message, ex.Message)
            ?? $"{message} {ex.Message}";
    }

    private void SetPaneDiagnostic(SessionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var pane = _ownerPane ?? _sessionTab?.PrimaryPane;
        if (pane is not null)
        {
            pane.FailureDetails = diagnostic;
        }
    }

    private void ClearPaneDiagnostic()
    {
        var pane = _ownerPane ?? _sessionTab?.PrimaryPane;
        if (pane is not null)
        {
            pane.FailureDetails = null;
        }
    }

    /// <summary>
    /// Updates the aspect ratio setting and triggers a resolution recalculation.
    /// </summary>
    public void UpdateAspectRatio(string ratioName)
    {
        if (_server is null) return;
        _server.RdpAspectRatio = ratioName;

        // Trigger a resolution recalculation via the resize timer
        // (don't call OnSurfaceContainerSizeChanged directly — it needs a real SizeChangedEventArgs)
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void OnResolutionButtonClick(object sender, RoutedEventArgs e)
    {
        // Update checkmarks to reflect current resolution
        var currentTag = _manualResolutionWidth > 0
            ? $"{_manualResolutionWidth}x{_manualResolutionHeight}"
            : "Fit";

        foreach (var menuItem in ResolutionMenu.Items.OfType<MenuItem>())
        {
            menuItem.IsChecked = menuItem.Tag is string tag && tag == currentTag;
        }

        ResolutionMenu.PlacementTarget = ResolutionButton;
        ResolutionMenu.IsOpen = true;
    }

    /// <summary>
    /// Populates the resolution context menu from AppSettings.RdpResolutionPresets,
    /// with a built-in fallback when the setting is missing or empty.
    /// </summary>
    private void PopulateResolutionMenu()
    {
        while (ResolutionMenu.Items.Count > 2)
        {
            ResolutionMenu.Items.RemoveAt(2);
        }

        var presets = _settings?.RdpResolutionPresets is { Length: > 0 } configured
            ? configured
            : DefaultResolutionPresets;

        foreach (var preset in presets)
        {
            if (string.IsNullOrWhiteSpace(preset))
            {
                continue;
            }

            var parts = preset.Trim().Split(new[] { 'x', 'X' }, StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !int.TryParse(parts[0], out var width)
                || !int.TryParse(parts[1], out var height)
                || width <= 0
                || height <= 0)
            {
                Core.Logging.FileLogger.Warn(
                    $"EmbeddedRDP skipping malformed resolution preset: '{preset}'");
                continue;
            }

            var item = new MenuItem
            {
                Header = $"{width} x {height}",
                Tag = $"{width}x{height}"
            };
            item.Click += OnResolutionMenuClick;
            ResolutionMenu.Items.Add(item);
        }
    }

    private void OnAntiIdleBadgeClick(object sender, RoutedEventArgs e)
    {
        if (_antiIdleTimer is null)
        {
            return;
        }

        Core.Logging.FileLogger.Info(
            "EmbeddedRDP user disabled anti-idle for the current session");
        StopAntiIdleTimer();
    }

    private void OnResolutionMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag)
            return;

        if (tag == "Fit")
        {
            _manualResolutionWidth = 0;
            _manualResolutionHeight = 0;
            ResolutionButton.ToolTip = L("RdpTooltipResolution");
            if (TryFindResource("TextPrimaryBrush") is System.Windows.Media.Brush defaultBrush)
                ResolutionButton.Foreground = defaultBrush;
            Core.Logging.FileLogger.Info("RDP resolution set to: Fit to Window");
        }
        else
        {
            var parts = tag.Split('x');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w)
                && int.TryParse(parts[1], out var h))
            {
                _manualResolutionWidth = w;
                _manualResolutionHeight = h;
                ResolutionButton.ToolTip = $"{L("RdpTooltipResolution")} ({w}x{h})";
                if (TryFindResource("AccentBrush") is System.Windows.Media.Brush accentBrush)
                    ResolutionButton.Foreground = accentBrush;
                Core.Logging.FileLogger.Info($"RDP resolution set to: {w}x{h}");
            }
        }

        // Apply immediately if connected
        if (_rdpHost?.IsConnected == true && _allowResolutionUpdates)
        {
            var (width, height) = GetDisplayDimensions();
            if (width > 0 && height > 0)
            {
                _rdpHost.UpdateResolution(width, height);
                _lastAppliedWidth = width;
                _lastAppliedHeight = height;
            }
        }
    }

    /// <summary>Resolves a locale key, falling back to the key name if no localizer is set.</summary>
    private string L(string key) => _localizer?[key] ?? key;

    private void StartStabilizationCountdown(TimeSpan delay)
    {
        StopStabilizationCountdown();

        if (_localizer is null || delay <= TimeSpan.Zero)
        {
            return;
        }

        _stabilizationDeadlineUtc = DateTime.UtcNow + delay;
        _stabilizationTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnStabilizationTimerTick,
            Dispatcher);
        _stabilizationTimer.Start();
        UpdateStabilizationDisplay();
    }

    private void OnStabilizationTimerTick(object? sender, EventArgs e)
    {
        UpdateStabilizationDisplay();
    }

    private void UpdateStabilizationDisplay()
    {
        var localizer = _localizer;
        if (localizer is null)
        {
            StopStabilizationCountdown();
            return;
        }

        var remaining = _stabilizationDeadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            StopStabilizationCountdown();
            return;
        }

        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        StabilizingStatusText.Text = string.Format(
            CultureInfo.CurrentCulture,
            localizer["RdpStabilizingStatus"],
            seconds);
        StabilizingSeparator.Visibility = System.Windows.Visibility.Visible;
        StabilizingStatusText.Visibility = System.Windows.Visibility.Visible;
    }

    private void StopStabilizationCountdown()
    {
        if (_stabilizationTimer is not null)
        {
            _stabilizationTimer.Stop();
            _stabilizationTimer.Tick -= OnStabilizationTimerTick;
            _stabilizationTimer = null;
        }

        StabilizingSeparator.Visibility = System.Windows.Visibility.Collapsed;
        StabilizingStatusText.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void StartReconnectElapsedTracking()
    {
        if (_reconnectStartUtc.HasValue || _localizer is null)
        {
            return;
        }

        _reconnectStartUtc = DateTime.UtcNow;
        _reconnectElapsedTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnReconnectElapsedTick,
            Dispatcher);
        _reconnectElapsedTimer.Start();
        UpdateReconnectElapsedDisplay();
    }

    private void OnReconnectElapsedTick(object? sender, EventArgs e)
    {
        UpdateReconnectElapsedDisplay();
    }

    private void UpdateReconnectElapsedDisplay()
    {
        var localizer = _localizer;
        if (localizer is null || _reconnectStartUtc is null)
        {
            StopReconnectElapsedTracking();
            return;
        }

        var elapsed = DateTime.UtcNow - _reconnectStartUtc.Value;
        var seconds = Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds));
        ReconnectElapsedText.Text = string.Format(
            CultureInfo.CurrentCulture,
            localizer["RdpReconnectElapsedFormat"],
            seconds);
        ReconnectElapsedSeparator.Visibility = Visibility.Visible;
        ReconnectElapsedText.Visibility = Visibility.Visible;
    }

    private void StopReconnectElapsedTracking()
    {
        if (_reconnectElapsedTimer is not null)
        {
            _reconnectElapsedTimer.Stop();
            _reconnectElapsedTimer.Tick -= OnReconnectElapsedTick;
            _reconnectElapsedTimer = null;
        }

        _reconnectStartUtc = null;
        ReconnectElapsedSeparator.Visibility = Visibility.Collapsed;
        ReconnectElapsedText.Visibility = Visibility.Collapsed;
    }

    private void ShowReconnectOverlay()
    {
        var diagnostic = _ownerPane?.FailureDetails
                         ?? _sessionTab?.PrimaryPane?.FailureDetails;

        var hasDiagnosticMessage = diagnostic is not null
            && !string.IsNullOrWhiteSpace(diagnostic.MessageKey);

        string primary;
        if (hasDiagnosticMessage)
        {
            var template = L(diagnostic!.MessageKey);
            if (template.Contains("{0}", StringComparison.Ordinal)
                && diagnostic.Code is int fatalCode)
            {
                try
                {
                    primary = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        template,
                        fatalCode);
                }
                catch (FormatException ex)
                {
                    Core.Logging.FileLogger.Warn(
                        $"[EmbeddedRdpView] Format failed for key '{diagnostic.MessageKey}': {ex.Message}");
                    primary = template;
                }
            }
            else
            {
                primary = template;
            }
        }
        else
        {
            primary = L("RdpDisconnectedMessage");
        }

        ReconnectMessageText.Text = primary;

        var hasSpecificPrimary = hasDiagnosticMessage
            && !string.Equals(diagnostic!.MessageKey, "RdpDisconnectedMessage", StringComparison.Ordinal);

        if (hasSpecificPrimary)
        {
            ReconnectSecondaryText.Text = L("RdpDisconnectedMessage");
            ReconnectSecondaryText.Visibility = System.Windows.Visibility.Visible;
        }
        else
        {
            ReconnectSecondaryText.Text = string.Empty;
            ReconnectSecondaryText.Visibility = System.Windows.Visibility.Collapsed;
        }

        int? disconnectCode = null;
        if (diagnostic?.Code is int code)
        {
            disconnectCode = IsFatalErrorDiagnostic(diagnostic) ? null : code;
            ReconnectCodeText.Text = FormatOverlayCode(diagnostic);
            ReconnectCodeText.Visibility = System.Windows.Visibility.Visible;
        }
        else
        {
            ReconnectCodeText.Text = string.Empty;
            ReconnectCodeText.Visibility = System.Windows.Visibility.Collapsed;
        }

        ApplyOverlaySeverity(ResolveOverlaySeverity(diagnostic));
        OverlayCopyErrorButton.Visibility = System.Windows.Visibility.Visible;
        OverlayEditProfileButton.Visibility = RdpDisconnectActionPolicy.ShouldOfferEditProfile(disconnectCode)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        ReconnectOverlay.Visibility = System.Windows.Visibility.Visible;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_disposed || ReconnectOverlay.Visibility != System.Windows.Visibility.Visible)
            {
                return;
            }

            var target = OverlayReconnectButton.IsVisible
                ? (UIElement)OverlayReconnectButton
                : OverlayCloseButton;
            _ = target.Focus();
            _ = Keyboard.Focus(target);
        }));
    }

    private void OnReconnectOverlayPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            OnOverlayCloseClick(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter
            && Keyboard.FocusedElement is Button focusedButton
            && IsWithinReconnectOverlay(focusedButton))
        {
            focusedButton.RaiseEvent(new RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent,
                focusedButton));
            e.Handled = true;
        }
    }

    private bool IsWithinReconnectOverlay(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ReconnectOverlay))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatOverlayCode(Core.SessionDiagnostics.SessionDiagnostic diagnostic)
    {
        if (diagnostic.Code is not int code)
        {
            return string.Empty;
        }

        return IsFatalErrorDiagnostic(diagnostic)
            ? $"RDP_FATAL_ERROR \u00B7 {code}"
            : RdpActiveXHost.FormatDisconnectCode(code);
    }

    private static bool IsFatalErrorDiagnostic(Core.SessionDiagnostics.SessionDiagnostic diagnostic)
    {
        return string.Equals(diagnostic.MessageKey, "RdpStatusFatalErrorDetail", StringComparison.Ordinal);
    }

    private static RdpActiveXHost.RdpDisconnectSeverity ResolveOverlaySeverity(
        Core.SessionDiagnostics.SessionDiagnostic? diagnostic)
    {
        if (diagnostic?.MessageKey == "RdpStatusFatalErrorDetail")
        {
            return RdpActiveXHost.RdpDisconnectSeverity.TerminalError;
        }

        return diagnostic?.Code is int code
            ? RdpActiveXHost.GetDisconnectSeverity(code)
            : RdpActiveXHost.RdpDisconnectSeverity.TerminalError;
    }

    private void ApplyOverlaySeverity(RdpActiveXHost.RdpDisconnectSeverity severity)
    {
        var (brushKey, glyph) = ResolveSeverityVisual(severity);
        OverlaySeverityStrip.SetResourceReference(Border.BackgroundProperty, brushKey);
        OverlaySeverityIcon.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        OverlaySeverityIcon.Text = glyph;
    }

    private static (string BrushKey, string Glyph) ResolveSeverityVisual(
        RdpActiveXHost.RdpDisconnectSeverity severity)
    {
        return severity switch
        {
            RdpActiveXHost.RdpDisconnectSeverity.Transient => ("InfoBrush", "\uE7BA"),
            RdpActiveXHost.RdpDisconnectSeverity.AuthIssue => ("WarningBrush", "\uE192"),
            _ => ("ErrorBrush", "\uE783")
        };
    }

    private void OnOverlayReconnectClick(object sender, RoutedEventArgs e)
    {
        ReconnectOverlay.Visibility = System.Windows.Visibility.Collapsed;
        ReconnectRequested?.Invoke();
    }

    private void OnOverlayCopyErrorClick(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var lines = new List<string>();
        AddClipboardLine(lines, ReconnectMessageText);
        if (ReconnectSecondaryText.Visibility == System.Windows.Visibility.Visible)
        {
            AddClipboardLine(lines, ReconnectSecondaryText);
        }

        if (ReconnectCodeText.Visibility == System.Windows.Visibility.Visible)
        {
            AddClipboardLine(lines, ReconnectCodeText);
        }

        if (lines.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"[EmbeddedRdpView] Copy reconnect overlay error failed: {ex.Message}");
        }
    }

    private static void AddClipboardLine(ICollection<string> lines, TextBlock textBlock)
    {
        if (!string.IsNullOrWhiteSpace(textBlock.Text))
        {
            lines.Add(textBlock.Text.Trim());
        }
    }

    private void OnOverlayEditProfileClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || string.IsNullOrWhiteSpace(_server?.Id))
        {
            return;
        }

        ReconnectOverlay.Visibility = System.Windows.Visibility.Collapsed;
        EditServerRequested?.Invoke(_server.Id);
    }

    private void OnOverlayCloseClick(object sender, RoutedEventArgs e)
    {
        ReconnectOverlay.Visibility = System.Windows.Visibility.Collapsed;
    }

    private (int Width, int Height) GetDisplayDimensions()
    {
        if (_server is null)
        {
            return (1024, 768);
        }

        // Manual resolution override — use exact dimensions if set
        if (_manualResolutionWidth > 0 && _manualResolutionHeight > 0)
        {
            return (_manualResolutionWidth, _manualResolutionHeight);
        }

        double logicalWidth = Math.Max(SurfaceContainer.ActualWidth, 2);
        double logicalHeight = Math.Max(SurfaceContainer.ActualHeight, 2);

        if (logicalWidth <= 2 || logicalHeight <= 2)
        {
            return (1024, 768);
        }

        // Convert WPF logical pixels (DIPs) to physical pixels for the ActiveX control.
        // On a 150% DPI display, WPF reports 2238 DIPs but the control needs 3357 physical pixels.
        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        int physicalWidth = (int)Math.Round(logicalWidth * dpiScaleX);
        int physicalHeight = (int)Math.Round(logicalHeight * dpiScaleY);

        return AspectRatioManager.Calculate(
            physicalWidth,
            physicalHeight,
            ParseAspectRatio(_server.RdpAspectRatio));
    }

    private bool IsVisualSurfaceReady()
    {
        return IsLoaded
            && IsVisible
            && FormsHost.IsVisible
            && SurfaceContainer.ActualWidth >= 64
            && SurfaceContainer.ActualHeight >= 64;
    }

    private void FlushLayoutPipeline(string stage)
    {
        Core.Logging.FileLogger.Info(
            $"EmbeddedRDP layout flush ({stage}): viewVisible={IsVisible} formsVisible={FormsHost.IsVisible} formsSize={FormsHost.ActualWidth:0.##}x{FormsHost.ActualHeight:0.##} surfaceSize={SurfaceContainer.ActualWidth:0.##}x{SurfaceContainer.ActualHeight:0.##}");

        UpdateLayout();
        SurfaceContainer.UpdateLayout();
        FormsHost.UpdateLayout();

        if (FormsHost.Child is WinForms.Control control)
        {
            if (!control.IsHandleCreated)
            {
                control.CreateControl();
            }

            control.PerformLayout();
            control.Refresh();
        }

        WinForms.Application.DoEvents();
        Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
    }

    private void StartAntiIdleTimer(int intervalSeconds)
    {
        StopAntiIdleTimer();

        _antiIdleTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(intervalSeconds),
            DispatcherPriority.Background,
            OnAntiIdleTick,
            Dispatcher);
        _antiIdleTimer.Start();
        Core.Logging.FileLogger.Info(
            $"RDP anti-idle timer started ({intervalSeconds}s interval)");
        AntiIdleBadge.Visibility = Visibility.Visible;
    }

    private void StopAntiIdleTimer()
    {
        if (_antiIdleTimer is null)
        {
            return;
        }

        _antiIdleTimer.Stop();
        _antiIdleTimer = null;
        Core.Logging.FileLogger.Info("RDP anti-idle timer stopped");

        if (!_disposed)
        {
            AntiIdleBadge.Visibility = Visibility.Collapsed;
        }
    }

    [SupportedOSPlatform("windows")]
    private void OnAntiIdleTick(object? sender, EventArgs e)
    {
        if (_disposed || _rdpHost is null || !_rdpHost.IsConnected)
        {
            StopAntiIdleTimer();
            return;
        }

        try
        {
            // Send a Shift key press/release to the RDP ActiveX inner rendering window.
            // PostMessage places the input directly into the target window's message queue.
            // With allowBackgroundInput=1, the RDP control processes it and relays to the
            // remote server, resetting the server-side idle timer (GetLastInputInfo).
            // Shift key has no visible effect on the remote desktop.
            IntPtr hwnd = _rdpHost.HostHandle;
            if (hwnd != IntPtr.Zero)
            {
                // Drill to the deepest child window (the ActiveX rendering surface)
                IntPtr target = hwnd;
                IntPtr child = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, null, null);
                while (child != IntPtr.Zero)
                {
                    target = child;
                    child = NativeMethods.FindWindowEx(target, IntPtr.Zero, null, null);
                }

                NativeMethods.PostMessage(
                    target,
                    NativeMethods.WM_KEYDOWN,
                    new IntPtr(NativeMethods.VK_SHIFT),
                    IntPtr.Zero);
                NativeMethods.PostMessage(
                    target,
                    NativeMethods.WM_KEYUP,
                    new IntPtr(NativeMethods.VK_SHIFT),
                    IntPtr.Zero);
            }

            // Also keep the local display alive
            NativeMethods.SetThreadExecutionState(
                NativeMethods.ES_CONTINUOUS |
                NativeMethods.ES_DISPLAY_REQUIRED);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"RDP anti-idle tick failed: {ex.Message}");
        }
    }

    private void AcquireSleepPrevention()
    {
        if (!_sleepPreventionActive)
        {
            _sleepPreventionActive = true;
            SleepPrevention.SessionStarted();
        }
    }

    private void ReleaseSleepPrevention()
    {
        if (_sleepPreventionActive)
        {
            _sleepPreventionActive = false;
            SleepPrevention.SessionEnded();
        }
    }

    private static string ResolveConnectHost(ServerProfileDto server)
    {
        return server.UseDirectConnection || string.IsNullOrWhiteSpace(server.SshGatewayId)
            ? server.RemoteServer
            : "127.0.0.1";
    }

    private int ResolveConnectPort(ServerProfileDto server)
    {
        if (server.UseDirectConnection || string.IsNullOrWhiteSpace(server.SshGatewayId))
        {
            return server.RemotePort;
        }

        // Use the dynamically allocated tunnel port, falling back to server.LocalPort
        return _tunnelPort ?? server.LocalPort;
    }

    private string BuildEndpointText(ServerProfileDto server)
    {
        if (server.UseDirectConnection || string.IsNullOrWhiteSpace(server.SshGatewayId))
        {
            return string.Format("{0}:{1}", server.RemoteServer, server.RemotePort);
        }

        var localPort = _tunnelPort ?? server.LocalPort;
        var format = _localizer?["RdpEndpointTunneledFormat"]
            ?? "{0}:{1} via localhost:{2}";
        return string.Format(
            format,
            server.RemoteServer,
            server.RemotePort,
            localPort);
    }

    private static (string Username, string? Domain) SplitUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return (string.Empty, null);
        }

        // DOMAIN\user format (NetBIOS)
        var separatorIndex = username.IndexOf('\\');
        if (separatorIndex > 0 && separatorIndex < username.Length - 1)
        {
            return (
                username[(separatorIndex + 1)..],
                username[..separatorIndex]);
        }

        // user@domain.com format (UPN) — pass the full UPN as the username
        // and extract the domain for logging/diagnostics. The RDP ActiveX control
        // accepts UPN directly in the UserName field.
        var atIndex = username.IndexOf('@');
        if (atIndex > 0 && atIndex < username.Length - 1)
        {
            return (username, username[(atIndex + 1)..]);
        }

        return (username, null);
    }

    private static string? TryDecryptPassword(ServerProfileDto server)
    {
        if (string.IsNullOrWhiteSpace(server.RdpPasswordEncrypted))
        {
            return null;
        }

        return CredentialProtector.Unprotect(server.RdpPasswordEncrypted);
    }

    /// <summary>
    /// Suppresses the reconnect overlay for explicit user disconnects and clean-exit COM codes.
    /// </summary>
    internal static bool ShouldSuppressReconnectOverlay(bool userInitiated, int reason)
        => userInitiated || reason is 0 or 1 or 2 or 3;

    internal static bool ShouldHandleStateChange(
        string serverId,
        string? targetServerId,
        bool comDrivenStatusActive,
        bool disposed)
        => !disposed
            && !comDrivenStatusActive
            && !string.IsNullOrWhiteSpace(targetServerId)
            && string.Equals(serverId, targetServerId, StringComparison.Ordinal);

    private static AspectRatio ParseAspectRatio(string? aspectRatio)
    {
        if (string.IsNullOrWhiteSpace(aspectRatio))
        {
            return AspectRatio.Stretch;
        }

        return aspectRatio.Trim() switch
        {
            "Preserve" => AspectRatio.Auto,
            "16:9" => AspectRatio.Ratio16x9,
            "4:3" => AspectRatio.Ratio4x3,
            "21:9" => AspectRatio.Ratio21x9,
            _ when Enum.TryParse<AspectRatio>(aspectRatio, true, out var parsed) => parsed,
            _ => AspectRatio.Stretch
        };
    }

    private Brush GetBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        internal const uint ES_CONTINUOUS = 0x80000000;
        internal const uint ES_DISPLAY_REQUIRED = 0x00000002;

        internal const uint WM_KEYDOWN = 0x0100;
        internal const uint WM_KEYUP = 0x0101;
        internal const byte VK_SHIFT = 0x10;
        internal const byte VK_CONTROL = 0x11;
        internal const byte VK_MENU = 0x12;
        internal const byte VK_ESCAPE = 0x1B;
        internal const byte VK_DELETE = 0x2E;
        internal const byte VK_TAB = 0x09;
        internal const byte VK_SNAPSHOT = 0x2C;
        internal const byte VK_LWIN = 0x5B;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
    }
}
