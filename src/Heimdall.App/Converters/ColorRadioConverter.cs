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

namespace Heimdall.App.Converters;

/// <summary>
/// Two-way converter that maps a color string property to a radio button's checked state.
/// The ConverterParameter is the hex color this radio button represents.
/// Returns true when the bound value matches the parameter, and sets the bound
/// value to the parameter when the radio button is checked.
/// </summary>
public sealed class ColorRadioConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance for use with x:Static markup extension.
    /// </summary>
    public static ColorRadioConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Equals(
            value?.ToString(),
            parameter?.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
        {
            return parameter?.ToString() ?? DependencyProperty.UnsetValue;
        }

        return DependencyProperty.UnsetValue;
    }
}
