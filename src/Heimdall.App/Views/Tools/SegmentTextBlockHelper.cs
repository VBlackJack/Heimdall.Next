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
using System.Windows.Documents;
using System.Windows.Media;
using Heimdall.Core.Matching;

namespace Heimdall.App.Views.Tools;

public static class SegmentTextBlockHelper
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached(
            "Segments",
            typeof(IReadOnlyList<WordSegment>),
            typeof(SegmentTextBlockHelper),
            new PropertyMetadata(null, OnSegmentsOrBrushChanged));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.RegisterAttached(
            "HighlightBrush",
            typeof(Brush),
            typeof(SegmentTextBlockHelper),
            new PropertyMetadata(null, OnSegmentsOrBrushChanged));

    public static IReadOnlyList<WordSegment>? GetSegments(DependencyObject element)
        => (IReadOnlyList<WordSegment>?)element.GetValue(SegmentsProperty);

    public static void SetSegments(DependencyObject element, IReadOnlyList<WordSegment>? value)
        => element.SetValue(SegmentsProperty, value);

    public static Brush? GetHighlightBrush(DependencyObject element)
        => (Brush?)element.GetValue(HighlightBrushProperty);

    public static void SetHighlightBrush(DependencyObject element, Brush? value)
        => element.SetValue(HighlightBrushProperty, value);

    private static void OnSegmentsOrBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();
        var segments = GetSegments(d);
        if (segments is null)
        {
            return;
        }

        var highlightBrush = GetHighlightBrush(d);
        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.IsChanged && highlightBrush is not null)
            {
                run.Background = highlightBrush;
            }

            textBlock.Inlines.Add(run);
        }
    }
}
