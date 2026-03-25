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

using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;
using WpfBinding = System.Windows.Data.Binding;
using WpfBindingMode = System.Windows.Data.BindingMode;

namespace Heimdall.App.Localization;

/// <summary>
/// WPF markup extension that resolves i18n keys at runtime via <see cref="LocalizationSource"/>.
/// <para>
/// Usage in XAML: <c>{loc:Translate Key=StatusReady}</c> or <c>{loc:Translate StatusReady}</c>.
/// </para>
/// <para>
/// Works with any dependency property: <c>Text</c>, <c>Content</c>, <c>ToolTip</c>,
/// <c>Header</c>, <c>AutomationProperties.Name</c>, <c>Window.Title</c>, etc.
/// </para>
/// <para>
/// In the WPF designer, displays the raw key in brackets (e.g., <c>[StatusReady]</c>)
/// for visual debugging.
/// </para>
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    /// <summary>
    /// Initializes a new instance with no key (set <see cref="Key"/> separately).
    /// </summary>
    public TranslateExtension() { }

    /// <summary>
    /// Initializes a new instance with the specified locale key.
    /// Enables the shorthand syntax: <c>{loc:Translate StatusReady}</c>.
    /// </summary>
    public TranslateExtension(string key) => Key = key;

    /// <summary>
    /// The i18n key to resolve (e.g., "StatusReady", "BtnConnect").
    /// </summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        // In the WPF designer, show the raw key for visual debugging
        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            return $"[{Key}]";
        }

        var binding = new WpfBinding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = WpfBindingMode.OneWay
        };

        // If the target is a DependencyProperty, return a live binding that
        // auto-updates on locale change. Otherwise, return the resolved string.
        if (serviceProvider.GetService(typeof(IProvideValueTarget))
                is IProvideValueTarget { TargetProperty: DependencyProperty })
        {
            return binding.ProvideValue(serviceProvider);
        }

        return LocalizationSource.Instance[Key];
    }
}
