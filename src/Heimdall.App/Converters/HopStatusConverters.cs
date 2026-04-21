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
using Heimdall.App.Localization;
using Heimdall.Core.Network;

namespace Heimdall.App.Converters;

/// <summary>
/// Maps a <see cref="HopStatus"/> to the corresponding status brush.
/// </summary>
public sealed class HopStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HopStatus status)
        {
            return DependencyProperty.UnsetValue;
        }

        var resourceKey = status switch
        {
            HopStatus.Destination => "SuccessTextBrush",
            HopStatus.Timeout => "WarningTextBrush",
            _ => "TextSecondaryBrush",
        };

        return Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="HopStatus"/> to the localized status text.
/// </summary>
public sealed class HopStatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HopStatus status)
        {
            return string.Empty;
        }

        var key = status switch
        {
            HopStatus.Destination => "ToolTraceStatusDestination",
            HopStatus.Timeout => "ToolTraceStatusTimeout",
            _ => "ToolTraceStatusReply",
        };

        return LocalizationSource.Instance[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
