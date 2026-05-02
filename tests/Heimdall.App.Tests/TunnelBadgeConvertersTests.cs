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
using System.Windows.Media;
using Heimdall.App.Converters;
using Heimdall.App.ViewModels.Tunnels;

namespace Heimdall.App.Tests;

public sealed class TunnelBadgeConvertersTests
{
    private readonly TunnelBadgeVisibilityConverter _visibilityConverter = new();

    [Fact]
    public void Visibility_HiddenStateAndPanelClosed_ReturnsCollapsed()
    {
        var result = ConvertVisibility(TunnelBadgeState.Hidden, isPanelOpen: false);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Visibility_HealthyStateAndPanelClosed_ReturnsVisible()
    {
        var result = ConvertVisibility(TunnelBadgeState.Healthy, isPanelOpen: false);

        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Visibility_UnhealthyStateAndPanelClosed_ReturnsVisible()
    {
        var result = ConvertVisibility(TunnelBadgeState.Unhealthy, isPanelOpen: false);

        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Visibility_HealthyStateAndPanelOpen_ReturnsCollapsed()
    {
        var result = ConvertVisibility(TunnelBadgeState.Healthy, isPanelOpen: true);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Visibility_UnhealthyStateAndPanelOpen_ReturnsCollapsed()
    {
        var result = ConvertVisibility(TunnelBadgeState.Unhealthy, isPanelOpen: true);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Visibility_NullOrUnexpectedInputs_ReturnsCollapsed()
    {
        Assert.Equal(Visibility.Collapsed, _visibilityConverter.Convert([], typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, _visibilityConverter.Convert([null!, false], typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, _visibilityConverter.Convert([TunnelBadgeState.Healthy, "false"], typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Brush_Healthy_ReturnsSuccessBrush()
    {
        var successBrush = Brushes.Green;
        var converter = CreateBrushConverter(("SuccessBrush", successBrush));

        var result = converter.Convert(TunnelBadgeState.Healthy, typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Same(successBrush, result);
    }

    [Fact]
    public void Brush_Unhealthy_ReturnsWarningBrush()
    {
        var warningBrush = Brushes.Gold;
        var converter = CreateBrushConverter(("WarningBrush", warningBrush));

        var result = converter.Convert(TunnelBadgeState.Unhealthy, typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Same(warningBrush, result);
    }

    [Fact]
    public void Brush_Hidden_ReturnsTransparentOrNull()
    {
        var converter = CreateBrushConverter();

        var result = converter.Convert(TunnelBadgeState.Hidden, typeof(Brush), null, CultureInfo.InvariantCulture);

        AssertTransparentOrNull(result);
    }

    [Fact]
    public void Brush_NullOrUnexpectedInput_ReturnsTransparentOrNull()
    {
        var converter = CreateBrushConverter();

        AssertTransparentOrNull(converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture));
        AssertTransparentOrNull(converter.Convert("Healthy", typeof(Brush), null, CultureInfo.InvariantCulture));
    }

    private Visibility ConvertVisibility(TunnelBadgeState state, bool isPanelOpen)
        => (Visibility)_visibilityConverter.Convert([state, isPanelOpen], typeof(Visibility), null, CultureInfo.InvariantCulture);

    private static TunnelBadgeStateToBrushConverter CreateBrushConverter(params (string Key, Brush Brush)[] resources)
    {
        var map = resources.ToDictionary(item => item.Key, item => item.Brush, StringComparer.Ordinal);
        return new TunnelBadgeStateToBrushConverter(key => map.TryGetValue(key, out var brush) ? brush : null);
    }

    private static void AssertTransparentOrNull(object? value)
    {
        if (value is null)
        {
            return;
        }

        Assert.Same(Brushes.Transparent, Assert.IsAssignableFrom<Brush>(value));
    }
}
