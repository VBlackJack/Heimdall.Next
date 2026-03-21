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

using Heimdall.Core.Localization;

namespace Heimdall.Core.Models;

/// <summary>
/// Contract for built-in tool views. Implemented by all tool UserControls
/// so the <see cref="ToolDescriptor"/> registry can create and initialize them
/// without a giant switch statement.
/// </summary>
public interface IToolView : IDisposable
{
    /// <summary>
    /// Initializes the tool with optional server context and localization.
    /// </summary>
    /// <param name="context">Optional context carrying target host/port from the selected server.</param>
    /// <param name="localizer">Localization manager for i18n strings.</param>
    void Initialize(ToolContext? context, LocalizationManager? localizer);
}
