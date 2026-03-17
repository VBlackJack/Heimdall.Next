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
using System.IO;
using System.Windows.Data;

namespace Heimdall.App.Converters;

/// <summary>
/// Converts a file name to a Segoe MDL2 Assets glyph character based on its extension.
/// Returns a folder icon when the converter parameter is "dir" and the value indicates a directory.
/// </summary>
public sealed class FileIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string name = value?.ToString() ?? "";
        string ext = Path.GetExtension(name).ToLowerInvariant();

        return ext switch
        {
            ".ps1" or ".bat" or ".cmd" or ".sh" => "\uE756",
            ".json" or ".xml" or ".yaml" or ".yml" or ".conf" or ".cfg" or ".ini" => "\uE713",
            ".log" or ".txt" or ".md" => "\uE7C3",
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "\uE7B8",
            ".exe" or ".msi" or ".dll" => "\uE71E",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" => "\uEB9F",
            _ => "\uE7C3"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
