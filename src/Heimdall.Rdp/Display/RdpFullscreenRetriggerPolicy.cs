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

namespace Heimdall.Rdp.Display;

/// <summary>
/// Decides whether a runtime fullscreen toggle should re-trigger the display resolver.
/// </summary>
public static class RdpFullscreenRetriggerPolicy
{
    /// <summary>
    /// Returns <c>true</c> only when the session is connected and the post-connect
    /// stabilization window has fully elapsed.
    /// </summary>
    public static bool ShouldRetrigger(
        bool isConnected,
        TimeSpan sinceConnected,
        TimeSpan stabilizationWindow)
    {
        if (!isConnected || sinceConnected < TimeSpan.Zero)
        {
            return false;
        }

        return sinceConnected >= stabilizationWindow;
    }
}
