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
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Heimdall.App.Converters;

/// <summary>
/// Converts a file name to a <see cref="Brush"/> based on its extension category.
/// Resolves theme-aware brushes (FileScriptBrush, FileConfigBrush, etc.) from application resources.
/// Used alongside <see cref="FileIconConverter"/> for color-coded file type icons.
/// </summary>
public sealed class FileIconColorConverter : IValueConverter
{
    private static readonly Brush FallbackBrush = Brushes.Gray;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string name = value?.ToString() ?? "";
        string ext = Path.GetExtension(name).ToLowerInvariant();

        string resourceKey = ext switch
        {
            ".ps1" or ".bat" or ".cmd" or ".sh" => "FileScriptBrush",
            ".json" or ".xml" or ".yaml" or ".yml" or ".conf" or ".cfg" or ".ini" => "FileConfigBrush",
            ".log" or ".txt" or ".md" => "FileDocumentBrush",
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "FileArchiveBrush",
            ".exe" or ".msi" or ".dll" => "FileExecutableBrush",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" => "FileImageBrush",
            _ => "FileDocumentBrush"
        };

        return Application.Current?.TryFindResource(resourceKey) as Brush ?? FallbackBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
