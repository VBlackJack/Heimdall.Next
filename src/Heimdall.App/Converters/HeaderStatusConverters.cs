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
/// Maps a <see cref="HeaderCheckStatus"/> to the corresponding status brush.
/// </summary>
public sealed class HeaderStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HeaderCheckStatus status)
        {
            return DependencyProperty.UnsetValue;
        }

        var resourceKey = status switch
        {
            HeaderCheckStatus.Pass => "SuccessTextBrush",
            HeaderCheckStatus.Warn => "WarningTextBrush",
            _ => "ErrorTextBrush",
        };

        return Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="HeaderCheckStatus"/> to the corresponding glyph.
/// </summary>
public sealed class HeaderStatusToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HeaderCheckStatus status)
        {
            return string.Empty;
        }

        return status switch
        {
            HeaderCheckStatus.Pass => "\u2713",
            HeaderCheckStatus.Warn => "\u26A0",
            _ => "\u2717",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="HeaderCheckStatus"/> to a localized status text.
/// </summary>
public sealed class HeaderStatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HeaderCheckStatus status)
        {
            return string.Empty;
        }

        var key = status switch
        {
            HeaderCheckStatus.Pass => "ToolHttpHeadersStatusPass",
            HeaderCheckStatus.Warn => "ToolHttpHeadersStatusWarn",
            _ => "ToolHttpHeadersStatusFail",
        };

        return LocalizationSource.Instance[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps an <see cref="HttpGrade"/> to the corresponding grade banner brush.
/// </summary>
public sealed class HttpGradeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HttpGrade grade)
        {
            return DependencyProperty.UnsetValue;
        }

        var resourceKey = grade switch
        {
            HttpGrade.APlus or HttpGrade.A or HttpGrade.BPlus or HttpGrade.B => "SuccessBrush",
            HttpGrade.C => "WarningBrush",
            _ => "ErrorBrush",
        };

        return Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
