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

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Attached property that allows binding a collection of <see cref="Inline"/> elements
/// to a <see cref="TextBlock"/>, since <see cref="TextBlock.Inlines"/> is not a dependency property.
/// </summary>
public static class InlineBindingHelper
{
    public static readonly DependencyProperty InlineCollectionProperty =
        DependencyProperty.RegisterAttached(
            "InlineCollection",
            typeof(IEnumerable<Inline>),
            typeof(InlineBindingHelper),
            new PropertyMetadata(null, OnInlineCollectionChanged));

    public static void SetInlineCollection(DependencyObject element, IEnumerable<Inline>? value)
    {
        element.SetValue(InlineCollectionProperty, value);
    }

    public static IEnumerable<Inline>? GetInlineCollection(DependencyObject element)
    {
        return (IEnumerable<Inline>?)element.GetValue(InlineCollectionProperty);
    }

    private static void OnInlineCollectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;

        textBlock.Inlines.Clear();

        if (e.NewValue is IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                textBlock.Inlines.Add(inline);
            }
        }
    }
}
