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

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for creating connected animations between views.
/// Captures source element state and animates to destination position.
/// </summary>
public interface IConnectedAnimationService
{
    /// <summary>
    /// Prepares an element for connected animation by capturing its visual state.
    /// </summary>
    /// <param name="key">Unique key to identify this animation.</param>
    /// <param name="sourceElement">The source element to capture.</param>
    void PrepareToAnimate(string key, object sourceElement);

    /// <summary>
    /// Tries to start a connected animation to the destination element.
    /// </summary>
    /// <param name="key">The key used in PrepareToAnimate.</param>
    /// <param name="destinationElement">The destination element.</param>
    /// <returns>True if animation started successfully.</returns>
    bool TryStartAnimation(string key, object destinationElement);

    /// <summary>
    /// Cancels a prepared animation.
    /// </summary>
    /// <param name="key">The key of the animation to cancel.</param>
    void Cancel(string key);

    /// <summary>
    /// Gets or sets the default animation duration.
    /// </summary>
    TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets whether connected animations are enabled.
    /// </summary>
    bool IsEnabled { get; set; }
}
