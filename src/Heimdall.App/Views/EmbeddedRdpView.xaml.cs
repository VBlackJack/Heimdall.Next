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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
    private static readonly TimeSpan InitialResizeEnableDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BeginConnectRetryDelay = TimeSpan.FromMilliseconds(120);

    private readonly DispatcherTimer _resizeTimer;

    private CancellationTokenSource? _autofillCts;
    private RdpActiveXHost? _rdpHost;
    private RdpServerDto? _server;
    private SessionTabViewModel? _sessionTab;

    private bool _initialized;
    private bool _connectStarted;
    private bool _eventSinkAttached;
    private bool _disposed;
    private bool _allowResolutionUpdates;
    private int _beginConnectAttempt;
    private int _lastAppliedWidth;
    private int _lastAppliedHeight;
    private DateTime _connectedAtUtc;

    public EmbeddedRdpView()
    {
        InitializeComponent();

        _resizeTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
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

    public void InitializeSession(RdpServerDto server, SessionTabViewModel sessionTab)
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
        _initialized = true;

        SessionTitleText.Text = server.DisplayName;
        EndpointTextBlock.Text = BuildEndpointText(server);

        CreateHostControl();
        UpdateSessionState("Connecting", "Preparing the embedded Remote Desktop session.");
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
        CancelAutofill();
        _allowResolutionUpdates = false;

        // CRITICAL: Hide the FormsHost FIRST to prevent WPF ArrangeOverride
        // from trying to resize the COM control after it's been released
        try
        {
            FormsHost.Visibility = System.Windows.Visibility.Collapsed;
            FormsHost.Child = null;
        }
        catch { }

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
            UpdateSessionState("Disconnecting", "Closing the embedded Remote Desktop session.");
            _rdpHost.Disconnect();
        }
        catch (Exception ex)
        {
            HandleFailure("Disconnect failed.", ex);
        }
    }

    private void OnSurfaceContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed || _server is null || !_server.RdpDynamicResolution)
        {
            return;
        }

        Core.Logging.FileLogger.Info(
            $"EmbeddedRDP SizeChanged: old={e.PreviousSize.Width:0.##}x{e.PreviousSize.Height:0.##} new={e.NewSize.Width:0.##}x{e.NewSize.Height:0.##} connected={_rdpHost?.IsConnected == true} allowResize={_allowResolutionUpdates}");

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

        if (_lastAppliedWidth == width && _lastAppliedHeight == height)
        {
            Core.Logging.FileLogger.Info(
                $"EmbeddedRDP Resize skipped because size is unchanged: {width}x{height}");
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
                    RetryBeginConnectAsync();
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
            UpdateSessionState("Connecting", "Waiting for the Remote Desktop control to connect.");

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

    private async void RetryBeginConnectAsync()
    {
        try
        {
            await Task.Delay(BeginConnectRetryDelay);
        }
        catch
        {
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

            if (_server is not null && _server.RdpDynamicResolution)
            {
                EnableResolutionUpdatesAsync();
            }
        });
    }

    private async void EnableResolutionUpdatesAsync()
    {
        try
        {
            await Task.Delay(InitialResizeEnableDelay);
        }
        catch
        {
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
            _allowResolutionUpdates = false;
            UpdateSessionState(
                "Disconnected",
                string.Format("Remote Desktop disconnected with code {0}.", reason));
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
            await CredentialAutofill.WaitAndFillAsync(
                Environment.ProcessId,
                hostHint,
                password,
                TimeSpan.FromSeconds(12),
                cancellationToken).ConfigureAwait(false);
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
        catch
        {
            // Best-effort cancellation only.
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

    private (int Width, int Height) GetDisplayDimensions()
    {
        if (_server is null)
        {
            return (1024, 768);
        }

        var width = Math.Max((int)Math.Floor(SurfaceContainer.ActualWidth), 2);
        var height = Math.Max((int)Math.Floor(SurfaceContainer.ActualHeight), 2);

        if (width <= 2 || height <= 2)
        {
            return (1024, 768);
        }

        return AspectRatioManager.Calculate(
            width,
            height,
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

    private static string ResolveConnectHost(RdpServerDto server)
    {
        return server.UseDirectConnection || string.IsNullOrWhiteSpace(server.SshGatewayId)
            ? server.RemoteServer
            : "127.0.0.1";
    }

    private static int ResolveConnectPort(RdpServerDto server)
    {
        return server.UseDirectConnection || string.IsNullOrWhiteSpace(server.SshGatewayId)
            ? server.RemotePort
            : server.LocalPort;
    }

    private static string BuildEndpointText(RdpServerDto server)
    {
        if (server.UseDirectConnection || string.IsNullOrWhiteSpace(server.SshGatewayId))
        {
            return string.Format("{0}:{1}", server.RemoteServer, server.RemotePort);
        }

        return string.Format(
            "{0}:{1} via localhost:{2}",
            server.RemoteServer,
            server.RemotePort,
            server.LocalPort);
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

    private static string? TryDecryptPassword(RdpServerDto server)
    {
        if (string.IsNullOrWhiteSpace(server.RdpPasswordEncrypted))
        {
            return null;
        }

        try
        {
            return DpapiProvider.Unprotect(server.RdpPasswordEncrypted);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"Embedded RDP password decrypt failed for {server.DisplayName}: {ex.Message}");
            return null;
        }
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

    private static RdpRedirectionOptions BuildRedirections(RdpServerDto server)
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
}
