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

namespace Heimdall.Core.Network;

/// <summary>
/// Neutral raw rows produced by the Win32 enumeration layer before mapping.
/// </summary>
public sealed record Tcp4RawRow(uint LocalAddr, uint LocalPort, uint RemoteAddr, uint RemotePort, uint State, uint OwningPid);

public sealed record Udp4RawRow(uint LocalAddr, uint LocalPort, uint OwningPid);

public sealed record Tcp6RawRow(byte[] LocalAddr, uint LocalPort, byte[] RemoteAddr, uint RemotePort, uint State, uint OwningPid);

public sealed record Udp6RawRow(byte[] LocalAddr, uint LocalPort, uint OwningPid);
