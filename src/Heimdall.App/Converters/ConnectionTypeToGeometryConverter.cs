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

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Heimdall.App.Converters;

/// <summary>
/// Converts a connection type string (RDP, SSH, SFTP, etc.) to the corresponding
/// vector <see cref="Geometry"/> from application resources.
/// </summary>
public sealed class ConnectionTypeToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string typeStr = value?.ToString()?.ToUpperInvariant() ?? string.Empty;

        string resourceKey;
        if (typeStr.StartsWith("TOOL:", StringComparison.Ordinal))
        {
            resourceKey = "Geo.Tree.Server";
        }
        else
        {
            resourceKey = typeStr switch
            {
                "RDP" => "Geo.Protocol.Rdp",
                "SSH" => "Geo.Protocol.Ssh",
                "SFTP" => "Geo.Protocol.Sftp",
                "VNC" => "Geo.Protocol.Vnc",
                "TELNET" => "Geo.Protocol.Telnet",
                "FTP" => "Geo.Protocol.Ftp",
                "CITRIX" => "Geo.Protocol.Citrix",
                "LOCAL" => "Geo.Protocol.LocalShell",
                _ => "Geo.Tree.Server"
            };
        }

        return Application.Current.TryFindResource(resourceKey) as Geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
