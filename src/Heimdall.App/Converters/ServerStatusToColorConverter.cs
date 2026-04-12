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
/// Brush values are resolved from theme resources so they adapt to the active theme.
/// </summary>
/// <remarks>
/// Accepts 2 or 3 binding values: <c>[0]</c> connection type, <c>[1]</c> connection state,
/// and optionally <c>[2]</c> a <c>ThemeRevision</c> trigger that forces WPF to re-run the
/// converter after a runtime theme swap (the trigger value itself is ignored).
/// </remarks>
public sealed class ServerStatusToColorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2
            || values[0] == DependencyProperty.UnsetValue
            || values[1] == DependencyProperty.UnsetValue)
        {
            return ResolveBrush("BorderBrush", Brushes.Gray);
        }

        string connectionType = values[0]?.ToString()?.ToUpperInvariant() ?? string.Empty;
        string connectionState = values[1]?.ToString()?.ToLowerInvariant() ?? string.Empty;
        // values[2], when present, is the ThemeRevision trigger — intentionally ignored.

        // State-based colors take priority over type-based colors
        return connectionState switch
        {
            "connected" => ResolveBrush("SuccessBrush", Brushes.Green),
            "error" => ResolveBrush("ErrorBrush", Brushes.Red),
            "initializing" or "validatingconfig" or "establishingtunnel"
                or "tunnelestablished" or "launchingrdp" or "launchingssh"
                or "launchingsftp" or "launchingftp" or "launchingvnc"
                or "launchingtelnet" or "launchinglocal" or "launchingcitrix"
                or "disconnecting" => ResolveBrush("WarningBrush", Brushes.Orange),
            // Disconnected or unknown: color by connection type
            _ => connectionType switch
            {
                "RDP" => ResolveBrush("InfoBrush", Brushes.Cyan),
                "SSH" => ResolveBrush("SuccessBrush", Brushes.Green),
                "SFTP" => ResolveBrush("WarningBrush", Brushes.Orange),
                "FTP" => ResolveBrush("WarningBrush", Brushes.Orange),
                "VNC" => ResolveBrush("InfoBrush", Brushes.Cyan),
                "TELNET" => ResolveBrush("SuccessBrush", Brushes.Green),
                "CITRIX" => ResolveBrush("InfoBrush", Brushes.Cyan),
                "LOCAL" => ResolveBrush("BorderBrush", Brushes.Gray),
                _ => ResolveBrush("BorderBrush", Brushes.Gray)
            }
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => [DependencyProperty.UnsetValue, DependencyProperty.UnsetValue, DependencyProperty.UnsetValue];

    private static Brush ResolveBrush(string resourceKey, Brush fallback)
        => Application.Current.TryFindResource(resourceKey) as Brush ?? fallback;
}
