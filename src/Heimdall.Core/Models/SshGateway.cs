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

using CommunityToolkit.Mvvm.ComponentModel;

namespace Heimdall.Core.Models;

/// <summary>
/// SSH gateway configuration for multi-hop tunneling.
/// </summary>
public partial class SshGateway : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string _host = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private int _port = 22;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string _user = string.Empty;

    [ObservableProperty]
    private string? _keyPath;

    [ObservableProperty]
    private string? _sshPasswordEncrypted;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private string? _parentGatewayId;

    [ObservableProperty]
    private string? _hostKeyFingerprint;

    /// <summary>
    /// Formatted display string showing gateway identity (name, user@host[:port]).
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (Port != 22)
            {
                return $"{Name} ({User}@{Host}:{Port})";
            }

            return $"{Name} ({User}@{Host})";
        }
    }
}
