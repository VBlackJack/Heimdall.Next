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

using Heimdall.App.ViewModels.Settings;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Dialogs;

public sealed class TrustedHostKeyDetailsDialogViewModel
{
    public TrustedHostKeyDetailsDialogViewModel(TrustedHostKeyRowViewModel row, LocalizationManager localizer)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(localizer);

        DialogTitle = localizer["DialogTrustedHostKeyDetailsTitle"];
        CloseLabel = localizer["BtnClose"];
        HostPort = row.HostPort;
        Algorithm = row.Algorithm;
        Source = row.SourceDisplay;
        FirstSeen = row.FirstSeenDisplay;
        LastSeen = row.LastSeenDisplay;
        Fingerprint = row.Fingerprint;
        PublicKeyBase64 = row.PublicKeyDisplay;
    }

    public string DialogTitle { get; }

    public string CloseLabel { get; }

    public string HostPort { get; }

    public string Algorithm { get; }

    public string Source { get; }

    public string FirstSeen { get; }

    public string LastSeen { get; }

    public string Fingerprint { get; }

    public string PublicKeyBase64 { get; }
}
