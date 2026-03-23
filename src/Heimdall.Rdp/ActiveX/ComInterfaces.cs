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

namespace Heimdall.Rdp.ActiveX;

/// <summary>
/// COM interface for secure password injection into the RDP ActiveX control.
/// ClearTextPassword is only available via IMsTscNonScriptable, not the default IDispatch.
/// The vtable order must match the COM interface exactly.
/// </summary>
[ComImport]
[Guid("C1E6743A-41C1-4A74-832A-0DD06C1C7A0E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMsTscNonScriptable
{
    void put_ClearTextPassword([MarshalAs(UnmanagedType.BStr)] string clearTextPassword);
    void put_PortablePassword([MarshalAs(UnmanagedType.BStr)] string portablePassword);
    void get_PortablePassword([MarshalAs(UnmanagedType.BStr)] out string portablePassword);
    void put_PortableSalt([MarshalAs(UnmanagedType.BStr)] string portableSalt);
    void get_PortableSalt([MarshalAs(UnmanagedType.BStr)] out string portableSalt);
    void put_BinaryPassword([MarshalAs(UnmanagedType.BStr)] string binaryPassword);
    void get_BinaryPassword([MarshalAs(UnmanagedType.BStr)] out string binaryPassword);
    void put_BinarySalt([MarshalAs(UnmanagedType.BStr)] string binarySalt);
    void get_BinarySalt([MarshalAs(UnmanagedType.BStr)] out string binarySalt);
}

/// <summary>
/// COM event source interface for MsTscAx ActiveX control.
/// DispId values must match the ActiveX type library exactly.
/// </summary>
[ComImport]
[Guid("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IMsTscAxEvents
{
    [DispId(2)]
    void OnConnected();

    [DispId(4)]
    void OnDisconnected(int discReason);

    [DispId(8)]
    void OnLoginComplete();

    [DispId(10)]
    void OnFatalError(int errorCode);

    [DispId(22)]
    void OnAutoReconnecting(int disconnectReason, int attemptCount, out bool continueReconnect);

    [DispId(23)]
    void OnAutoReconnected();
}

/// <summary>
/// COM event sink bridging ActiveX connection point events to .NET events.
/// Implements IMsTscAxEvents and forwards calls to the host control.
/// </summary>
public class MsTscAxEventSink : IMsTscAxEvents
{
    private readonly RdpActiveXHost _host;

    public MsTscAxEventSink(RdpActiveXHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public void OnConnected() => _host.RaiseConnected();
    public void OnDisconnected(int discReason) => _host.RaiseDisconnected(discReason);
    public void OnLoginComplete() => _host.RaiseLoginComplete();
    public void OnFatalError(int errorCode) => _host.RaiseFatalError(errorCode);

    public void OnAutoReconnecting(int disconnectReason, int attemptCount, out bool continueReconnect)
    {
        continueReconnect = !_host.CancelAutoReconnect;
        _host.RaiseAutoReconnecting(disconnectReason, attemptCount);
    }

    public void OnAutoReconnected() => _host.RaiseAutoReconnected();
}
