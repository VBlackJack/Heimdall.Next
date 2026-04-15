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
using System.Windows.Data;

namespace Heimdall.App.Converters;

/// <summary>
/// Selects one of two localized binding values based on a boolean state.
/// </summary>
public sealed class BoolToLocalizedKeyConverter : IMultiValueConverter
{
    public string TrueKey { get; set; } = string.Empty;

    public string FalseKey { get; set; } = string.Empty;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var isTrue = values.Length > 0 && values[0] is true;
        var localizedIndex = isTrue ? 2 : 1;

        if (values.Length > localizedIndex
            && values[localizedIndex] is string localized
            && !string.IsNullOrEmpty(localized))
        {
            return localized;
        }

        return isTrue ? TrueKey : FalseKey;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        var values = new object[targetTypes.Length];
        Array.Fill(values, System.Windows.Data.Binding.DoNothing);
        return values;
    }
}
