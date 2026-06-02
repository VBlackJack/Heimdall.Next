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

namespace Heimdall.Rdp.ActiveX;

/// <summary>
/// Extended disconnect reasons reported by the MsTscAx ExtendedDisconnectReasonCode enum.
/// </summary>
internal enum ExtendedDisconnectReasonCode
{
    NoInfo = 0,
    ServerLogonTimeout = 4,
    ServerDeniedConnection = 7,
    ServerDeniedConnectionFips = 8,
    ServerInsufficientPrivileges = 9,
    ServerFreshCredsRequired = 10,
    LicenseInternal = 256,
    LicenseNoLicenseServer = 257,
    LicenseNoLicense = 258,
    LicenseErrClientMsg = 259,
    LicenseHwidDoesntMatchLicense = 260,
    LicenseErrClientLicense = 261,
    LicenseCantFinishProtocol = 262,
    LicenseClientEndedProtocol = 263,
    LicenseErrClientEncryption = 264,
    LicenseCantUpgradeLicense = 265
}
