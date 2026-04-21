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
using Heimdall.Core.Security;

namespace Heimdall.App.Converters;

/// <summary>
/// Maps DNS check statuses to themed brushes.
/// </summary>
public sealed class DnsCheckStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DnsCheckStatus status)
        {
            return DependencyProperty.UnsetValue;
        }

        var key = status switch
        {
            DnsCheckStatus.Pass => "SuccessTextBrush",
            DnsCheckStatus.Warn => "WarningTextBrush",
            _ => "ErrorTextBrush",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps DNS check statuses to glyphs.
/// </summary>
public sealed class DnsCheckStatusToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DnsCheckStatus status
            ? DnsSecurityEvaluationEngine.StatusToIcon(status)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps DNS check statuses to localized labels.
/// </summary>
public sealed class DnsCheckStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DnsCheckStatus status)
        {
            return string.Empty;
        }

        return LocalizationSource.Instance[DnsSecurityEvaluationEngine.StatusToLabelKey(status)];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps DNS summary states to themed banner brushes.
/// </summary>
public sealed class DnsSummaryStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DnsSummaryStatus status)
        {
            return DependencyProperty.UnsetValue;
        }

        var key = status switch
        {
            DnsSummaryStatus.AllPass => "SuccessBrush",
            DnsSummaryStatus.Good => "SuccessTextBrush",
            DnsSummaryStatus.Partial => "WarningBrush",
            _ => "ErrorBrush",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
