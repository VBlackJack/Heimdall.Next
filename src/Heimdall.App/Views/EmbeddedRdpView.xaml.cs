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

using System.Diagnostics;
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
/// Owns the WindowsFormsHost, event sink lifecycle, password injection, and
/// dynamic resolution updates on resize.
/// </summary>
public partial class EmbeddedRdpView : UserControl, IDisposable
{
    private readonly DispatcherTimer _resizeTimer;
    private CancellationTokenSource? _autofillCts;

    private RdpActiveXHost? _rdpHost;
    private RdpServerDto? _server;
    private SessionTabViewModel? _sessionTab;

    private bool _initialized;
    private bool _connectStarted;
    private bool _eventSinkAttached;
    private bool _disposed;

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

        Loaded -= OnLoaded;
        SurfaceContainer.SizeChanged -= OnSurfaceContainerSizeChanged;
        _resizeTimer.Stop();
        CancelAutofill();

        if (_rdpHost is not null)
        {
            _rdpHost.Connected -= OnRdpConnected;
            _rdpHost.Disconnected -= OnRdpDisconnected;
            _rdpHost.FatalError -= OnRdpFatalError;

            try
            {
                _rdpHost.Disconnect();
            }
            catch
            {
                // Best-effort shutdown only.
            }

            try
            {
                if (_eventSinkAttached)
                {
                    _rdpHost.DetachEventSink();
                }
            }
            catch
            {
                // Best-effort shutdown only.
            }

            _rdpHost.Dispose();
            _rdpHost = null;
        }

        FormsHost.Child = null;
        _autofillCts?.Dispose();
        _autofillCts = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_disposed || !_initialized || _connectStarted)
        {
            return;
        }

        _connectStarted = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(BeginConnect));
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _rdpHost is null)
        {
            return;
        }

        try
        {
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

        try
        {
            if (_rdpHost.IsConnected)
            {
                _rdpHost.UpdateResolution(width, height);
            }
            // Only set display before connection starts — skip during connecting phase
            // SetDisplay after Connect() causes COM errors
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
            EnsureHostHandle();

            if (!_eventSinkAttached)
            {
                if (!_rdpHost.AttachEventSink())
                {
                    throw new InvalidOperationException(
                        _rdpHost.LastError ?? "Failed to attach the Remote Desktop event sink.");
                }

                _eventSinkAttached = true;
            }

            var connectHost = ResolveConnectHost(_server);
            var connectPort = ResolveConnectPort(_server);
            var (username, domain) = SplitUsername(_server.RdpUsername);
            var password = TryDecryptPassword(_server);
            var (width, height) = GetDisplayDimensions();

            Core.Logging.FileLogger.Info($"EmbeddedRDP BeginConnect: host={connectHost}:{connectPort} user={username} domain={domain} hasPassword={!string.IsNullOrEmpty(password)} size={width}x{height}");

            _rdpHost.SetServer(connectHost, connectPort);
            if (!string.IsNullOrWhiteSpace(username))
            {
                _rdpHost.SetCredentials(username, password, domain);
                Core.Logging.FileLogger.Info($"EmbeddedRDP SetCredentials called for user={username}");
            }

            _rdpHost.SetDisplay(width, height, NormalizeColorDepth(_server.RdpColorDepth));
            _rdpHost.SetRedirections(BuildRedirections(_server));

            Core.Logging.FileLogger.Info("EmbeddedRDP calling Connect()...");
            _rdpHost.Connect();

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
                Heimdall.Core.Logging.FileLogger.Warn(
                    $"Embedded RDP ClearPassword failed: {ex.Message}");
            }

            UpdateSessionState("Connected", "The embedded Remote Desktop session is active.");

            if (_server is not null && _server.RdpDynamicResolution)
            {
                _resizeTimer.Stop();
                _resizeTimer.Start();
            }
        });
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
            UpdateSessionState(
                "Disconnected",
                string.Format("Remote Desktop disconnected with code {0}.", reason));
        });
    }

    private void OnRdpFatalError(int errorCode)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            CancelAutofill();
            UpdateSessionState(
                "Error",
                string.Format("Remote Desktop reported a fatal error ({0}).", errorCode));
            StatusTextBlock.Foreground = GetBrush("ErrorBrush", Brushes.IndianRed);
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
        }
    }

    private void StartCredentialAutofill(string password, string hostHint)
    {
        CancelAutofill();
        _autofillCts = new CancellationTokenSource();
        var token = _autofillCts.Token;

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
            Heimdall.Core.Logging.FileLogger.Warn(
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
        DisconnectButton.IsEnabled = !_disposed && !string.Equals(status, "Disconnected", StringComparison.OrdinalIgnoreCase);
        StatusTextBlock.Foreground = GetBrush(
            string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase) ? "ErrorBrush" : "TextPrimaryBrush",
            string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase) ? Brushes.IndianRed : Brushes.White);
    }

    private void HandleFailure(string message, Exception ex, bool updateStatus = true)
    {
        Heimdall.Core.Logging.FileLogger.Error(message, ex);

        if (updateStatus)
        {
            UpdateSessionState("Error", string.Format("{0} {1}", message, ex.Message));
        }
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
            Heimdall.Core.Logging.FileLogger.Warn(
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
