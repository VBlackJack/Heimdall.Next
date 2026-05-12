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

using Heimdall.Core.Models;

namespace Heimdall.Rdp;

/// <summary>
/// Abstraction for an RDP session backed by the MsTscAx ActiveX control.
/// Implementations host the ActiveX control in a Windows Forms control
/// suitable for embedding in a WPF WindowsFormsHost.
/// </summary>
public interface IRdpSession : IDisposable
{
    /// <summary>Raised when the RDP connection is established.</summary>
    event Action? Connected;

    /// <summary>Raised when the RDP connection is terminated. Parameter is the disconnect reason code.</summary>
    event Action<int>? Disconnected;

    /// <summary>Raised on a fatal RDP error. Parameter is the error code.</summary>
    event Action<int>? FatalError;

    /// <summary>Raised when the server has accepted credentials and login is complete.</summary>
    event Action? LoginComplete;

    /// <summary>Raised when the client begins an auto-reconnect attempt (args: disconnectReason, attemptCount).</summary>
    event Action<int, int>? AutoReconnecting;

    /// <summary>Raised when an auto-reconnect attempt succeeds.</summary>
    event Action? AutoReconnected;

    /// <summary>Whether an active RDP connection is established.</summary>
    bool IsConnected { get; }

    /// <summary>Configure the target server host and port.</summary>
    void SetServer(string host, int port = DefaultPorts.Rdp);

    /// <summary>Configure connection credentials. Password is injected via IMsTscNonScriptable.</summary>
    void SetCredentials(string username, string? password = null, string? domain = null);

    /// <summary>Configure the remote desktop display dimensions and color depth.</summary>
    void SetDisplay(int width, int height, int colorDepth = 32);

    /// <summary>Configure device and feature redirections.</summary>
    void SetRedirections(RdpRedirectionOptions redirections);

    /// <summary>Configure ActiveX reconnect and keep-alive resilience knobs.</summary>
    void SetResilienceOptions(int maxAutoReconnectAttempts, int keepAliveIntervalMs);

    /// <summary>Initiate the RDP connection.</summary>
    void Connect();

    /// <summary>Disconnect the active RDP session.</summary>
    void Disconnect();

    /// <summary>Update the remote session display resolution and scale factors.</summary>
    RdpDisplayUpdateResult UpdateResolution(
        int width,
        int height,
        uint physicalWidthMm = 0,
        uint physicalHeightMm = 0,
        uint desktopScaleFactor = 100,
        uint deviceScaleFactor = 100,
        bool allowReconnectFallback = true);

    /// <summary>
    /// Returns the Windows Forms control that hosts the RDP ActiveX.
    /// Intended for embedding in a WPF WindowsFormsHost.
    /// </summary>
    System.Windows.Forms.Control GetHostControl();
}
