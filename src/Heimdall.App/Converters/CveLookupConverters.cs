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
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Discovery;

namespace Heimdall.App.Converters;

/// <summary>
/// Maps CVE severities to themed brushes.
/// </summary>
public sealed class CveSeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CveSeverity severity)
        {
            return DependencyProperty.UnsetValue;
        }

        var resourceKey = severity switch
        {
            CveSeverity.Critical => "ErrorBrush",
            CveSeverity.High => "WarningBrush",
            CveSeverity.Medium => "WarningTextBrush",
            CveSeverity.Low => "SuccessBrush",
            _ => "SuccessBrush",
        };

        return Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a search state to visibility using the target state as converter parameter.
/// </summary>
public sealed class CveSearchStateToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CveSearchState state || parameter is not string target)
        {
            return Visibility.Collapsed;
        }

        return string.Equals(state.ToString(), target, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
