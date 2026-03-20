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

using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Heimdall.Rdp.ActiveX;

/// <summary>
/// Hosts the Microsoft Terminal Services ActiveX control (MsTscAx).
/// Inherits from <see cref="AxHost"/> to create the COM object and implements
/// <see cref="IRdpSession"/> for a clean abstraction layer.
///
/// IMPORTANT: Do NOT call <see cref="AttachEventSink"/> from the
/// <see cref="AxHost.CreateSink"/> override — this causes hangs in non-STA
/// contexts (e.g., unit tests). Call it explicitly after the handle is created.
/// </summary>
public class RdpActiveXHost : AxHost, IRdpSession
{
    // MsTscAx ActiveX control CLSID — Terminal Services Client 8.0+
    public const string DefaultMsTscAxClsid = "7cacbd7b-0d99-468f-ac33-22e495c0afe5";
    public const string NotSafeForScriptingClsid = "A0F46F0A-3B66-4B79-A7A1-1C70A6BF37E1";

    private object? _activeX;
    private bool _disposed;
    private ConnectionPointCookie? _cookie;
    private MsTscAxEventSink? _sink;
    private readonly string _activeXClsid;

    // Pending configuration applied before the ActiveX handle is created
    private string _pendingHost = string.Empty;
    private int _pendingPort = 3389;
    private string _pendingUsername = string.Empty;
    private string? _pendingPassword;
    private string? _pendingDomain;
    private int _pendingWidth = 1024;
    private int _pendingHeight = 768;
    private int _pendingColorDepth = 32;
    private RdpRedirectionOptions _pendingRedirections = new();

    /// <inheritdoc />
    public event Action? Connected;

    /// <inheritdoc />
    public event Action<int>? Disconnected;

    /// <inheritdoc />
    public event Action<int>? FatalError;

    /// <summary>Stores the last error message for diagnostics.</summary>
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <summary>The ActiveX CLSID used to instantiate this control.</summary>
    public string ActiveXClsid => _activeXClsid;

    /// <summary>Current host window handle, or <see cref="IntPtr.Zero"/> when not created.</summary>
    public IntPtr HostHandle => IsHandleCreated ? Handle : IntPtr.Zero;

    public RdpActiveXHost(string? activeXClsid = null)
        : base(activeXClsid ?? DefaultMsTscAxClsid)
    {
        _activeXClsid = activeXClsid ?? DefaultMsTscAxClsid;
    }

    /// <summary>
    /// Returns the raw ActiveX COM object obtained via <see cref="AxHost.GetOcx"/>.
    /// </summary>
    public object? GetActiveXInstance()
    {
        if (_activeX is null && IsHandleCreated)
        {
            _activeX = GetOcx();
            Core.Logging.FileLogger.Info(
                $"RdpActiveXHost.GetActiveXInstance: handle=0x{HostHandle.ToInt64():X} ocxType={_activeX?.GetType().FullName ?? "null"} clsid={_activeXClsid}");
        }
        return _activeX;
    }

    /// <summary>
    /// Called by <see cref="AxHost"/> after the underlying COM object is created.
    /// Caches the ActiveX reference for later use.
    /// </summary>
    protected override void AttachInterfaces()
    {
        _activeX = GetOcx();
        Core.Logging.FileLogger.Info(
            $"RdpActiveXHost.AttachInterfaces: handle=0x{HostHandle.ToInt64():X} ocxType={_activeX?.GetType().FullName ?? "null"} clsid={_activeXClsid}");
    }

    /// <inheritdoc />
    public void SetServer(string host, int port = 3389)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _pendingHost = host;
        _pendingPort = port;
        Core.Logging.FileLogger.Info(
            $"RdpActiveXHost.SetServer: host={host} port={port} handleCreated={IsHandleCreated} clsid={_activeXClsid}");

        var ocx = GetActiveXInstance();
        if (ocx is not null)
        {
            ApplyServerSettings(ocx);
        }
    }

    /// <inheritdoc />
    public void SetCredentials(string username, string? password = null, string? domain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        _pendingUsername = username;
        _pendingPassword = password;
        _pendingDomain = domain;
        Core.Logging.FileLogger.Info(
            $"RdpActiveXHost.SetCredentials: user={username} domain={domain ?? string.Empty} hasPassword={!string.IsNullOrEmpty(password)} handleCreated={IsHandleCreated}");

        var ocx = GetActiveXInstance();
        if (ocx is not null)
        {
            ApplyCredentialSettings(ocx);
        }
    }

    /// <inheritdoc />
    public void SetDisplay(int width, int height, int colorDepth = 32)
    {
        _pendingWidth = width;
        _pendingHeight = height;
        _pendingColorDepth = colorDepth;
        Core.Logging.FileLogger.Info(
            $"RdpActiveXHost.SetDisplay: width={width} height={height} colorDepth={colorDepth} handleCreated={IsHandleCreated}");

        var ocx = GetActiveXInstance();
        if (ocx is not null)
        {
            ApplyDisplaySettings(ocx);
        }
    }

    /// <inheritdoc />
    public void SetRedirections(RdpRedirectionOptions redirections)
    {
        ArgumentNullException.ThrowIfNull(redirections);
        _pendingRedirections = redirections;
        Core.Logging.FileLogger.Info(
            $"RdpActiveXHost.SetRedirections: clipboard={redirections.Clipboard} drives={redirections.Drives} printers={redirections.Printers} dynamicResolution={redirections.DynamicResolution}");

        var ocx = GetActiveXInstance();
        if (ocx is not null)
        {
            ApplyRedirectionSettings(ocx);
        }
    }

    /// <inheritdoc />
    public void Connect()
    {
        var ocx = GetActiveXInstance()
            ?? throw new InvalidOperationException("ActiveX control is not initialized. Ensure the host control handle is created first.");

        Core.Logging.FileLogger.Info(
            $"RdpActiveXHost.Connect: handle=0x{HostHandle.ToInt64():X} clsid={_activeXClsid} ocxType={ocx.GetType().FullName ?? "unknown"} size={_pendingWidth}x{_pendingHeight}");

        // Apply all pending settings before connecting
        ApplyServerSettings(ocx);
        ApplyCredentialSettings(ocx);
        ApplyDisplaySettings(ocx);
        ApplyRedirectionSettings(ocx);

        // Clear plaintext password from managed memory after COM handoff
        _pendingPassword = null;

        ((dynamic)ocx).Connect();
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        var ocx = GetActiveXInstance();
        if (ocx is not null)
        {
            try
            {
                ((dynamic)ocx).Disconnect();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }

    /// <inheritdoc />
    public void UpdateResolution(int width, int height)
    {
        var ocx = GetActiveXInstance();
        if (ocx is null)
        {
            Core.Logging.FileLogger.Warn(
                $"RdpActiveXHost.UpdateResolution skipped: no ActiveX instance for {width}x{height}");
            return;
        }

        try
        {
            // IMsRdpClient9+ (RDP 8.1+): change resolution without reconnection.
            // Parameters: desktopWidth, desktopHeight, physicalWidth, physicalHeight,
            //             orientation(0), desktopScaleFactor(100), deviceScaleFactor(100)
            ocx.GetType().InvokeMember(
                "UpdateSessionDisplaySettings",
                BindingFlags.InvokeMethod,
                null,
                ocx,
                new object[] { (uint)width, (uint)height, (uint)width, (uint)height,
                               (uint)0, (uint)100, (uint)100 });

            Core.Logging.FileLogger.Info(
                $"RdpActiveXHost.UpdateResolution: handle=0x{HostHandle.ToInt64():X} {width}x{height} (seamless)");
        }
        catch (Exception)
        {
            // Fallback: Reconnect(width, height) on IMsRdpClient7+ (older servers/clients)
            try
            {
                ocx.GetType().InvokeMember(
                    "Reconnect",
                    BindingFlags.InvokeMethod,
                    null,
                    ocx,
                    [(uint)width, (uint)height]);

                Core.Logging.FileLogger.Info(
                    $"RdpActiveXHost.UpdateResolution: handle=0x{HostHandle.ToInt64():X} {width}x{height} (reconnect fallback)");
            }
            catch (Exception exFallback)
            {
                LastError = exFallback.Message;
                Core.Logging.FileLogger.Warn(
                    $"RdpActiveXHost.UpdateResolution failed: {exFallback.Message}");
            }
        }
    }

    /// <summary>
    /// Attach the COM event sink via connection point.
    /// Must be called explicitly after the control handle is created, on the STA thread.
    /// Returns true if the event sink was successfully connected.
    /// </summary>
    public bool AttachEventSink()
    {
        try
        {
            Core.Logging.FileLogger.Info(
                $"RdpActiveXHost.AttachEventSink: handle=0x{HostHandle.ToInt64():X} handleCreated={IsHandleCreated} clsid={_activeXClsid}");
            var ocx = GetOcx();
            if (ocx is null)
            {
                LastError = "GetOcx() returned null — connection point not available for event sink";
                Core.Logging.FileLogger.Warn("RdpActiveXHost.AttachEventSink failed: GetOcx returned null");
                return false;
            }

            _sink = new MsTscAxEventSink(this);
            _cookie = new ConnectionPointCookie(ocx, _sink, typeof(IMsTscAxEvents));
            Core.Logging.FileLogger.Info("RdpActiveXHost.AttachEventSink succeeded");

            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Core.Logging.FileLogger.Warn($"RdpActiveXHost.AttachEventSink failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Detach the COM event sink. Call during cleanup before Dispose.
    /// </summary>
    public void DetachEventSink()
    {
        if (_cookie is not null)
        {
            try
            {
                _cookie.Disconnect();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            _cookie = null;
        }
        _sink = null;
    }

    /// <inheritdoc />
    public Control GetHostControl() => this;

    /// <summary>
    /// Inject password via the IMsTscNonScriptable COM interface (QueryInterface).
    /// Returns true on success.
    /// </summary>
    public bool SetClearTextPassword(string password)
    {
        try
        {
            var ocx = GetOcx();
            if (ocx is null)
            {
                LastError = "GetOcx() returned null";
                return false;
            }

            var nonScriptable = (IMsTscNonScriptable)ocx;
            nonScriptable.put_ClearTextPassword(password);
            LastError = null;
            Core.Logging.FileLogger.Info("RdpActiveXHost.SetClearTextPassword: success=True");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Core.Logging.FileLogger.Warn($"RdpActiveXHost.SetClearTextPassword: success=False error={ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Best-effort clearing of the COM-side password after the connection handoff.
    /// Should be called in the OnConnected event handler.
    /// </summary>
    public void ClearPassword()
    {
        try
        {
            var ocx = GetOcx();
            if (ocx is null) return;

            var nonScriptable = (IMsTscNonScriptable)ocx;
            nonScriptable.put_ClearTextPassword(string.Empty);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    #region Internal event raisers (called by MsTscAxEventSink)

    internal void RaiseConnected()
    {
        IsConnected = true;
        Connected?.Invoke();
    }

    internal void RaiseDisconnected(int discReason)
    {
        IsConnected = false;
        Disconnected?.Invoke(discReason);
    }

    internal void RaiseFatalError(int errorCode)
    {
        IsConnected = false;
        FatalError?.Invoke(errorCode);
    }

    #endregion

    #region Private apply methods (late-bound COM property access)

    private void ApplyServerSettings(object ocx)
    {
        if (string.IsNullOrWhiteSpace(_pendingHost)) return;

        dynamic ax = ocx;
        ax.Server = _pendingHost;
        ax.AdvancedSettings2.RDPPort = _pendingPort;
    }

    private void ApplyCredentialSettings(object ocx)
    {
        dynamic ax = ocx;

        if (!string.IsNullOrWhiteSpace(_pendingUsername))
        {
            ax.UserName = _pendingUsername;
        }

        if (!string.IsNullOrWhiteSpace(_pendingDomain))
        {
            ax.Domain = _pendingDomain;
        }

        // Password must be set via IMsTscNonScriptable, not via IDispatch
        if (_pendingPassword is not null)
        {
            var passwordInjected = SetClearTextPassword(_pendingPassword);
            Core.Logging.FileLogger.Info(
                $"RdpActiveXHost.ApplyCredentialSettings: passwordInjected={passwordInjected}");
        }
    }

    private void ApplyDisplaySettings(object ocx)
    {
        dynamic ax = ocx;
        ax.DesktopWidth = _pendingWidth;
        ax.DesktopHeight = _pendingHeight;
        ax.ColorDepth = _pendingColorDepth;

        // SmartSizing stretches the RDP session to fill the control surface,
        // absorbing pixel rounding differences and providing smooth resize
        // during the debounce delay before UpdateResolution kicks in.
        try { ax.AdvancedSettings2.SmartSizing = true; }
        catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[RdpActiveXHost] SmartSizing: {ex.Message}"); }
    }

    private void ApplyRedirectionSettings(object ocx)
    {
        dynamic ax = ocx;
        var adv = ax.AdvancedSettings9;

        // Clipboard
        adv.RedirectClipboard = _pendingRedirections.Clipboard;

        // Drives
        adv.RedirectDrives = _pendingRedirections.Drives;

        // Printers
        adv.RedirectPrinters = _pendingRedirections.Printers;

        // COM ports
        adv.RedirectPorts = _pendingRedirections.ComPorts;

        // Smart cards
        adv.RedirectSmartCards = _pendingRedirections.SmartCards;

        // Audio mode: 0 = redirect to client, 1 = play at remote, 2 = disable
        // Map our enum: 0 = disabled, 1 = local, 2 = remote
        adv.AudioRedirectionMode = _pendingRedirections.AudioMode switch
        {
            1 => 0, // Local playback = redirect to client
            2 => 1, // Remote playback = play at remote
            _ => 2  // Disabled
        };

        // Audio capture
        adv.AudioCaptureRedirectionMode = _pendingRedirections.AudioCapture;

        // NLA
        adv.EnableCredSspSupport = _pendingRedirections.Nla;

        // Bitmap caching
        adv.BitmapPersistence = _pendingRedirections.BitmapCaching ? 1 : 0;

        // Compression
        adv.Compress = _pendingRedirections.Compression ? 1 : 0;

        // Auto-reconnect
        adv.EnableAutoReconnect = _pendingRedirections.AutoReconnect;

        // Allow background input — CRITICAL for anti-idle on background tabs.
        // Without this, the RDP ActiveX control discards PostMessage input
        // when it does not have focus, silently breaking anti-idle.
        try { adv.allowBackgroundInput = 1; } catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[RdpActiveXHost] allowBackgroundInput: {ex.Message}"); }

        // Multi-monitor (requires IMsRdpClientNonScriptable5)
        if (_pendingRedirections.MultiMonitor)
        {
            try
            {
                adv.UseMultimon = true;
            }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn($"[RdpActiveXHost] UseMultimon: {ex.Message}");
            }
        }
    }

    #endregion

    #region Cleanup

    private void ReleaseActiveX()
    {
        if (_activeX is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_activeX);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            _activeX = null;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;

            if (disposing)
            {
                try { DetachEventSink(); }
                catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[RdpActiveXHost] Dispose DetachEventSink: {ex.Message}"); }
            }

            try { ReleaseActiveX(); }
            catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[RdpActiveXHost] Dispose ReleaseActiveX: {ex.Message}"); }
        }

        base.Dispose(disposing);
    }

    #endregion
}
