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

using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.ViewModels;

/// <summary>
/// Maps persisted profile origins to compact badge codes and localized display names.
/// </summary>
public static class ProfileOriginDisplay
{
    public static string GetBadgeCode(ProfileOrigin origin) => origin switch
    {
        ProfileOrigin.ImportRdp => "RDP",
        ProfileOrigin.ImportOpenSsh => "OSSH",
        ProfileOrigin.ImportPutty => "PTY",
        ProfileOrigin.ImportMRemoteNg => "MRNG",
        ProfileOrigin.ImportMobaXterm => "MXTM",
        ProfileOrigin.ImportRdcMan => "RDCM",
        _ => string.Empty
    };

    public static string GetDisplayName(ProfileOrigin origin, LocalizationManager? localizer)
    {
        if (origin == ProfileOrigin.Manual)
        {
            return string.Empty;
        }

        if (localizer is null)
        {
            // TODO: should never happen in production; keep tests and design-time usage resilient.
            return string.Empty;
        }

        var key = origin switch
        {
            ProfileOrigin.ImportRdp => "LabelImportedFromRdp",
            ProfileOrigin.ImportOpenSsh => "LabelImportedFromOpenSsh",
            ProfileOrigin.ImportPutty => "LabelImportedFromPutty",
            ProfileOrigin.ImportMRemoteNg => "LabelImportedFromMRemoteNg",
            ProfileOrigin.ImportMobaXterm => "LabelImportedFromMobaXterm",
            ProfileOrigin.ImportRdcMan => "LabelImportedFromRdcMan",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(key) ? string.Empty : localizer[key];
    }
}
