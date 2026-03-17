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

using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Models;

namespace Heimdall.App.Views;

/// <summary>
/// Hosts one or two session panes in a split layout with a <see cref="GridSplitter"/>.
/// When <see cref="IsSplit"/> is false, only the primary content fills the area.
/// When true, two panes are shown side-by-side (vertical) or stacked (horizontal).
/// </summary>
public partial class SplitPaneHost : UserControl
{
    public static readonly DependencyProperty PrimaryContentProperty =
        DependencyProperty.Register(nameof(PrimaryContent), typeof(object), typeof(SplitPaneHost),
            new PropertyMetadata(null, OnPrimaryContentChanged));

    public static readonly DependencyProperty SecondaryContentProperty =
        DependencyProperty.Register(nameof(SecondaryContent), typeof(object), typeof(SplitPaneHost),
            new PropertyMetadata(null, OnSecondaryContentChanged));

    public static readonly DependencyProperty IsSplitProperty =
        DependencyProperty.Register(nameof(IsSplit), typeof(bool), typeof(SplitPaneHost),
            new PropertyMetadata(false, OnLayoutChanged));

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(SplitOrientation), typeof(SplitPaneHost),
            new PropertyMetadata(SplitOrientation.Vertical, OnLayoutChanged));

    public object? PrimaryContent
    {
        get => GetValue(PrimaryContentProperty);
        set => SetValue(PrimaryContentProperty, value);
    }

    public object? SecondaryContent
    {
        get => GetValue(SecondaryContentProperty);
        set => SetValue(SecondaryContentProperty, value);
    }

    public bool IsSplit
    {
        get => (bool)GetValue(IsSplitProperty);
        set => SetValue(IsSplitProperty, value);
    }

    public SplitOrientation Orientation
    {
        get => (SplitOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public SplitPaneHost()
    {
        InitializeComponent();
        ApplyLayout();
    }

    private static void OnPrimaryContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SplitPaneHost host)
        {
            host.PrimaryPresenter.Content = e.NewValue;
        }
    }

    private static void OnSecondaryContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SplitPaneHost host)
        {
            host.SecondaryPresenter.Content = e.NewValue;
        }
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SplitPaneHost host)
        {
            host.ApplyLayout();
        }
    }

    private void ApplyLayout()
    {
        // Sync both presenters with current DP values (critical for TabControl tab switching)
        PrimaryPresenter.Content = PrimaryContent;
        SecondaryPresenter.Content = SecondaryContent;

        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        if (!IsSplit)
        {
            // Single pane: primary fills everything
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(PrimaryPresenter, 0);
            Grid.SetColumn(PrimaryPresenter, 0);
            Grid.SetRowSpan(PrimaryPresenter, 1);
            Grid.SetColumnSpan(PrimaryPresenter, 1);

            Splitter.Visibility = Visibility.Collapsed;
            SecondaryPresenter.Visibility = Visibility.Collapsed;
            return;
        }

        SecondaryPresenter.Content = SecondaryContent;
        SecondaryPresenter.Visibility = Visibility.Visible;
        Splitter.Visibility = Visibility.Visible;

        if (Orientation == SplitOrientation.Horizontal)
        {
            // Stacked: top/bottom
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(PrimaryPresenter, 0);
            Grid.SetColumn(PrimaryPresenter, 0);
            Grid.SetRowSpan(PrimaryPresenter, 1);
            Grid.SetColumnSpan(PrimaryPresenter, 1);

            Grid.SetRow(Splitter, 1);
            Grid.SetColumn(Splitter, 0);
            Splitter.Height = 4;
            Splitter.Width = double.NaN;
            Splitter.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            Splitter.VerticalAlignment = VerticalAlignment.Center;
            Splitter.ResizeDirection = GridResizeDirection.Rows;

            Grid.SetRow(SecondaryPresenter, 2);
            Grid.SetColumn(SecondaryPresenter, 0);
            Grid.SetRowSpan(SecondaryPresenter, 1);
            Grid.SetColumnSpan(SecondaryPresenter, 1);
        }
        else
        {
            // Side-by-side: left/right
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(PrimaryPresenter, 0);
            Grid.SetColumn(PrimaryPresenter, 0);
            Grid.SetRowSpan(PrimaryPresenter, 1);
            Grid.SetColumnSpan(PrimaryPresenter, 1);

            Grid.SetRow(Splitter, 0);
            Grid.SetColumn(Splitter, 1);
            Splitter.Width = 4;
            Splitter.Height = double.NaN;
            Splitter.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            Splitter.VerticalAlignment = VerticalAlignment.Stretch;
            Splitter.ResizeDirection = GridResizeDirection.Columns;

            Grid.SetRow(SecondaryPresenter, 0);
            Grid.SetColumn(SecondaryPresenter, 2);
            Grid.SetRowSpan(SecondaryPresenter, 1);
            Grid.SetColumnSpan(SecondaryPresenter, 1);
        }
    }
}
