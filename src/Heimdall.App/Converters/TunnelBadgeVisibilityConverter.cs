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
using Heimdall.App.ViewModels.Tunnels;

namespace Heimdall.App.Converters;

public sealed class TunnelBadgeVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 ||
            values[0] is not TunnelBadgeState state ||
            values[1] is not bool isPanelOpen)
        {
            return Visibility.Collapsed;
        }

        return state != TunnelBadgeState.Hidden && !isPanelOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        var results = new object[targetTypes.Length];
        Array.Fill(results, DependencyProperty.UnsetValue);
        return results;
    }
}
