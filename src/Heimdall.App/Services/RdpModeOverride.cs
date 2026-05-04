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

namespace Heimdall.App.Services;

/// <summary>
/// One-shot RDP mode override used by explicit launch actions.
/// </summary>
public enum RdpModeOverride
{
    /// <summary>Use the mode stored on the server profile.</summary>
    UseProfile = 0,

    /// <summary>Launch through Heimdall's embedded RDP host for this connection only.</summary>
    ForceEmbedded,

    /// <summary>Launch through the external mstsc.exe client for this connection only.</summary>
    ForceExternal
}
