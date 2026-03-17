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
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Heimdall.App.Converters;

/// <summary>
/// Converts a file name to a <see cref="Brush"/> based on its extension category.
/// Used alongside <see cref="FileIconConverter"/> for color-coded file type icons.
/// </summary>
public sealed class FileIconColorConverter : IValueConverter
{
    private static readonly Brush ScriptBrush = new SolidColorBrush(Color.FromRgb(0x50, 0xFA, 0x7B));
    private static readonly Brush ConfigBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0xE9, 0xFD));
    private static readonly Brush DocumentBrush = new SolidColorBrush(Color.FromRgb(0x62, 0x72, 0xA4));
    private static readonly Brush ArchiveBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x6C));
    private static readonly Brush ExecutableBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x79, 0xC6));
    private static readonly Brush ImageBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xFA, 0x8C));

    static FileIconColorConverter()
    {
        ScriptBrush.Freeze();
        ConfigBrush.Freeze();
        DocumentBrush.Freeze();
        ArchiveBrush.Freeze();
        ExecutableBrush.Freeze();
        ImageBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string name = value?.ToString() ?? "";
        string ext = Path.GetExtension(name).ToLowerInvariant();

        return ext switch
        {
            ".ps1" or ".bat" or ".cmd" or ".sh" => ScriptBrush,
            ".json" or ".xml" or ".yaml" or ".yml" or ".conf" or ".cfg" or ".ini" => ConfigBrush,
            ".log" or ".txt" or ".md" => DocumentBrush,
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => ArchiveBrush,
            ".exe" or ".msi" or ".dll" => ExecutableBrush,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" => ImageBrush,
            _ => DocumentBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
