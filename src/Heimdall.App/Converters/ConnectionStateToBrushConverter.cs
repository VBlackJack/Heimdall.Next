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
/// Converts a connection state value to a status indicator brush.
/// Connected = green, Error = red, transitional states = amber, idle = gray.
/// </summary>
public sealed class ConnectionStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string state = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        string resourceKey = state switch
        {
            "connected" => "SuccessBrush",
            "error" => "ErrorBrush",
            "disconnected" => "TextSecondaryBrush",
            "initializing" or "validatingconfig" or "establishingtunnel"
                or "launchingrdp" or "launchingssh" or "launchingsftp"
                or "disconnecting" => "WarningBrush",
            "tunnelestablished" => "InfoBrush",
            _ => "TextSecondaryBrush"
        };

        return Application.Current.TryFindResource(resourceKey) as Brush
               ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
