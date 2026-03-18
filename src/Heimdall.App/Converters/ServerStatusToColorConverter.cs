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
/// Multi-value converter that merges connection type and connection state into a single
/// status indicator color. When connected/connecting/error, the state color wins;
/// when disconnected, the color reflects the connection type (RDP/SSH/SFTP).
/// All brush instances are frozen for rendering performance.
/// </summary>
public sealed class ServerStatusToColorConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush ConnectedBrush = CreateFrozen("#50FA7B");
    private static readonly SolidColorBrush ConnectingBrush = CreateFrozen("#FFB86C");
    private static readonly SolidColorBrush ErrorBrush = CreateFrozen("#FF5555");
    private static readonly SolidColorBrush RdpBrush = CreateFrozen("#8BE9FD");
    private static readonly SolidColorBrush SshBrush = CreateFrozen("#50FA7B");
    private static readonly SolidColorBrush SftpBrush = CreateFrozen("#FFB86C");
    private static readonly SolidColorBrush DefaultBrush = CreateFrozen("#6272A4");

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return DefaultBrush;
        }

        string connectionType = values[0]?.ToString()?.ToUpperInvariant() ?? string.Empty;
        string connectionState = values[1]?.ToString()?.ToLowerInvariant() ?? string.Empty;

        // State-based colors take priority over type-based colors
        return connectionState switch
        {
            "connected" => ConnectedBrush,
            "error" => ErrorBrush,
            "initializing" or "validatingconfig" or "establishingtunnel"
                or "tunnelestablished" or "launchingrdp" or "launchingssh"
                or "launchingsftp" or "launchingftp" or "launchingvnc"
                or "launchingtelnet" or "launchinglocal" or "launchingcitrix"
                or "disconnecting" => ConnectingBrush,
            // Disconnected or unknown: color by connection type
            _ => connectionType switch
            {
                "RDP" => RdpBrush,
                "SSH" => SshBrush,
                "SFTP" => SftpBrush,
                "FTP" => SftpBrush,
                "VNC" => RdpBrush,
                "TELNET" => SshBrush,
                "CITRIX" => RdpBrush,
                "LOCAL" => DefaultBrush,
                _ => DefaultBrush
            }
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => [DependencyProperty.UnsetValue, DependencyProperty.UnsetValue];

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
