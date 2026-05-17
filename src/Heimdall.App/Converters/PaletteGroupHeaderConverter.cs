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
using Heimdall.App.Localization;

namespace Heimdall.App.Converters;

/// <summary>
/// Resolves the palette result Group string to a non-empty section header.
/// Servers without an inventory group fall back to the localized
/// <c>PaletteServersHeader</c> so the grouped ListBox never renders an
/// untitled section.
/// </summary>
public sealed class PaletteGroupHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var raw = value as string;
        if (!string.IsNullOrWhiteSpace(raw)) return raw;
        return LocalizationSource.Instance["PaletteServersHeader"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
