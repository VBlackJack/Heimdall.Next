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

namespace Heimdall.App.Theming;

/// <summary>
/// Shared attached visual state for controls that can accept a drag-drop item.
/// </summary>
public static class DropTargetVisualState
{
    public static readonly DependencyProperty IsDropTargetProperty =
        DependencyProperty.RegisterAttached(
            "IsDropTarget",
            typeof(bool),
            typeof(DropTargetVisualState),
            new FrameworkPropertyMetadata(false));

    public static void SetIsDropTarget(DependencyObject element, bool value)
    {
        element.SetValue(IsDropTargetProperty, value);
    }

    public static bool GetIsDropTarget(DependencyObject element)
    {
        return (bool)element.GetValue(IsDropTargetProperty);
    }
}
