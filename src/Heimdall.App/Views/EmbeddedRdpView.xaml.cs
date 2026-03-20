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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Rdp;
using Heimdall.Rdp.ActiveX;
using WinForms = System.Windows.Forms;

namespace Heimdall.App.Views;

/// <summary>
/// WPF host for the MsTscAx ActiveX control used by embedded RDP sessions.
/// Applies the proven WPF/WinForms layout flush pattern before Connect()
/// and delays dynamic resolution reconnects until the session is stable.
/// </summary>
public partial class EmbeddedRdpView : UserControl, IDisposable
{
    private static readonly TimeSpan InitialResizeEnableDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BeginConnectRetryDelay = TimeSpan.FromMilliseconds(120);

    private readonly DispatcherTimer _resizeTimer;

    private CancellationTokenSource? _autofillCts;
    private DispatcherTimer? _antiIdleTimer;
    private RdpActiveXHost? _rdpHost;
    private ServerProfileDto? _server;
    private SessionTabViewModel? _sessionTab;

    private Core.Localization.LocalizationManager? _localizer;
    private int? _tunnelPort;

    private bool _initialized;
    private bool _connectStarted;
    private bool _eventSinkAttached;
    private bool _disposed;
    private bool _allowResolutionUpdates;
    private bool _sleepPreventionActive;
    private int _antiIdleIntervalSeconds;
    private int _beginConnectAttempt;
    private int _lastAppliedWidth;
    private int _lastAppliedHeight;
    private int _manualResolutionWidth;
    private int _manualResolutionHeight;
    private DateTime _connectedAtUtc;

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

    public EmbeddedRdpView()
    {
        InitializeComponent();

        _resizeTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1000),
            DispatcherPriority.Background,
            OnResizeTimerTick,
            Dispatcher)
        {
            IsEnabled = false
        };

        Loaded += OnLoaded;
        SurfaceContainer.SizeChanged += OnSurfaceContainerSizeChanged;
    }

    public void SetFullscreen(bool isFullscreen)
    {
        SessionHeaderBar.Visibility = isFullscreen
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
    }

    public void InitializeSession(
        ServerProfileDto server,
        SessionTabViewModel sessionTab,
        int antiIdleIntervalSeconds = 60,
        Core.Localization.LocalizationManager? localizer = null,
        int? tunnelPort = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(sessionTab);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EmbeddedRdpView));
        }

        if (_initialized)
        {
            return;
        }

        _server = server;
        _sessionTab = sessionTab;
        _antiIdleIntervalSeconds = antiIdleIntervalSeconds;
        _localizer = localizer;
        _tunnelPort = tunnelPort;
        _initialized = true;

        SessionTitleText.Text = server.DisplayName;
        EndpointTextBlock.Text = BuildEndpointText(server);

        if (_localizer is not null)
        {
            DisconnectButton.Content = L("BtnDisconnectSession");
            SplitButton.ToolTip = L("ToolTipSplitPane");
            ResolutionButton.ToolTip = L("RdpTooltipResolution");
            ResMenuFit.Header = L("RdpResolutionFitToWindow");

            // Accessibility: automation names for toolbar buttons
            System.Windows.Automation.AutomationProperties.SetName(DisconnectButton, L("BtnDisconnectSession"));
            System.Windows.Automation.AutomationProperties.SetName(SplitButton, L("ToolTipSplitPane"));
            System.Windows.Automation.AutomationProperties.SetName(ResolutionButton, L("RdpTooltipResolution"));
        }

        CreateHostControl();
        UpdateSessionState("Connecting", L("RdpStatusPreparing"));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Core.Logging.FileLogger.Info("EmbeddedRDP Dispose started");

        Loaded -= OnLoaded;
        SurfaceContainer.SizeChanged -= OnSurfaceContainerSizeChanged;
        _resizeTimer.Stop();
        StopAntiIdleTimer();
        ReleaseSleepPrevention();
        CancelAutofill();
        _allowResolutionUpdates = false;

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_disposed || !_initialized || _connectStarted)
        {
            return;
        }

        _connectStarted = true;
        Core.Logging.FileLogger.Info(
            $"EmbeddedRDP Loaded: isVisible={IsVisible} formsVisible={FormsHost.IsVisible} formsSize={FormsHost.ActualWidth:0.##}x{FormsHost.ActualHeight:0.##} surfaceSize={SurfaceContainer.ActualWidth:0.##}x{SurfaceContainer.ActualHeight:0.##}");

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(BeginConnect));
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _rdpHost is null)
        {
            return;
        }

        try
        {
            Core.Logging.FileLogger.Info("EmbeddedRDP Disconnect requested by user");
            _allowResolutionUpdates = false;
            UpdateSessionState("Disconnecting", L("RdpStatusDisconnecting"));
            _rdpHost.Disconnect();
        }
        catch (Exception ex)
        {
            HandleFailure("Disconnect failed.", ex);
        }
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
        if (_disposed || _rdpHost is null || _server is null)
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

            _rdpHost.SetDisplay(width, height, NormalizeColorDepth(_server.RdpColorDepth));
            _lastAppliedWidth = width;
            _lastAppliedHeight = height;
            _rdpHost.SetRedirections(BuildRedirections(_server));

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

            FlushLayoutPipeline("post-connect");
            UpdateSessionState("Connecting", L("RdpStatusWaiting"));

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
            UpdateSessionState("Connected", "The embedded Remote Desktop session is active.");
            FlushLayoutPipeline("on-connected");

            if (_server is not null && _server.RdpAntiIdle && _antiIdleIntervalSeconds > 0)
            {
                StartAntiIdleTimer(_antiIdleIntervalSeconds);
            }

            AcquireSleepPrevention();

            if (_server is not null && _server.RdpDynamicResolution)
            {
                _ = EnableResolutionUpdatesAsync();
            }
        });
    }

    private async Task EnableResolutionUpdatesAsync()
    {
        try
        {
            await Task.Delay(InitialResizeEnableDelay);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"[EmbeddedRdpView] resolution delay: {ex.Message}");
            return;
        }

        if (_disposed || _rdpHost is null || !_rdpHost.IsConnected)
        {
            return;
        }

        _allowResolutionUpdates = true;
        Core.Logging.FileLogger.Info("EmbeddedRDP dynamic resolution is now enabled.");
    }

    private void OnRdpDisconnected(int reason)
    {
        Core.Logging.FileLogger.Info($"EmbeddedRDP OnDisconnected fired: reason={reason}");
        if (_disposed)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            CancelAutofill();
            StopAntiIdleTimer();
            ReleaseSleepPrevention();
            _allowResolutionUpdates = false;
            UpdateSessionState(
                "Disconnected",
                string.Format("Remote Desktop disconnected with code {0}.", reason));
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

        Dispatcher.Invoke(() =>
        {
            CancelAutofill();
            _allowResolutionUpdates = false;
            UpdateSessionState(
                "Error",
                string.Format("Remote Desktop reported a fatal error ({0}).", errorCode));
            ShowReconnectOverlay();
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
        try
        {
            var filled = await CredentialAutofill.WaitAndFillAsync(
                Environment.ProcessId,
                hostHint,
                password,
                TimeSpan.FromSeconds(90),
                cancellationToken).ConfigureAwait(false);

            if (!filled)
            {
                Core.Logging.FileLogger.Warn(
                    $"EmbeddedRDP CredUI autofill timed out for hostHint={hostHint}");
            }
        }
        catch (OperationCanceledException)
        {
            // Session connected or was disposed before a credential dialog appeared.
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"Embedded RDP credential autofill failed: {ex.Message}");
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

    private void UpdateSessionState(string status, string detail)
    {
        if (_sessionTab is not null)
        {
            _sessionTab.Status = status;
        }

        StatusTextBlock.Text = status;
        DetailTextBlock.Text = detail;

        var isConnecting = string.Equals(status, "Connecting", StringComparison.OrdinalIgnoreCase);
        RdpLoadingBar.Visibility = isConnecting ? Visibility.Visible : Visibility.Collapsed;

        DisconnectButton.IsEnabled = !_disposed
            && !string.Equals(status, "Disconnected", StringComparison.OrdinalIgnoreCase);
        StatusTextBlock.Foreground = GetBrush(
            string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase)
                ? "ErrorBrush"
                : "TextPrimaryBrush",
            string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase)
                ? Brushes.IndianRed
                : Brushes.White);
    }

    private void HandleFailure(string message, Exception ex)
    {
        Core.Logging.FileLogger.Error(message, ex);
        _allowResolutionUpdates = false;
        UpdateSessionState("Error", string.Format("{0} {1}", message, ex.Message));
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

    private void ShowReconnectOverlay()
    {
        ReconnectMessageText.Text = L("RdpDisconnectedMessage");
        OverlayReconnectButton.Content = L("BtnReconnectSession");
        OverlayCloseButton.Content = L("BtnCloseOverlay");
        ReconnectOverlay.Visibility = System.Windows.Visibility.Visible;
    }

    private void OnOverlayReconnectClick(object sender, RoutedEventArgs e)
    {
        ReconnectOverlay.Visibility = System.Windows.Visibility.Collapsed;
        ReconnectRequested?.Invoke();
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

                NativeMethods.PostMessage(target, NativeMethods.WM_KEYDOWN, NativeMethods.VK_SHIFT, IntPtr.Zero);
                NativeMethods.PostMessage(target, NativeMethods.WM_KEYUP, NativeMethods.VK_SHIFT, IntPtr.Zero);
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
        return string.Format(
            "{0}:{1} via localhost:{2}",
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

        var separatorIndex = username.IndexOf('\\');
        if (separatorIndex > 0 && separatorIndex < username.Length - 1)
        {
            return (
                username[(separatorIndex + 1)..],
                username[..separatorIndex]);
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

    private static int NormalizeColorDepth(int colorDepth)
    {
        return colorDepth switch
        {
            <= 16 => 16,
            <= 24 => 24,
            _ => 32
        };
    }

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

    private static RdpRedirectionOptions BuildRedirections(ServerProfileDto server)
    {
        return new RdpRedirectionOptions
        {
            Clipboard = server.RdpRedirectClipboard,
            Drives = server.RdpRedirectDrives,
            Printers = server.RdpRedirectPrinters,
            ComPorts = server.RdpRedirectComPorts,
            SmartCards = server.RdpRedirectSmartCards,
            Usb = server.RdpRedirectUsb,
            Webcam = server.RdpRedirectWebcam,
            AudioCapture = server.RdpAudioCapture,
            AudioMode = server.RdpAudioMode,
            MultiMonitor = server.RdpMultiMonitor,
            DynamicResolution = server.RdpDynamicResolution,
            Nla = server.RdpNla,
            BitmapCaching = server.RdpBitmapCaching,
            Compression = server.RdpCompression,
            AutoReconnect = server.RdpAutoReconnect
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
        internal static readonly IntPtr VK_SHIFT = new(0x10);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
    }
}
