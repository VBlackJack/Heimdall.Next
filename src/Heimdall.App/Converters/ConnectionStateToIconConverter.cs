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
using System.Windows.Media.Imaging;

namespace Heimdall.App.Converters;

/// <summary>
/// Converts a connection state string (Connected, Disconnected, Error, etc.) to the
/// corresponding status icon <see cref="BitmapImage"/> from application resources.
/// </summary>
public sealed class ConnectionStateToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string stateStr = value?.ToString() ?? string.Empty;

        string resourceKey = stateStr switch
        {
            "Connected" => "Icon.Status.Connected",
            "Error" => "Icon.Status.Error",
            _ => "Icon.Status.Disconnected"
        };

        return Application.Current.TryFindResource(resourceKey) as BitmapImage;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
