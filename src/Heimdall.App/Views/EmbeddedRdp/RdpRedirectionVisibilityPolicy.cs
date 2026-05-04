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

namespace Heimdall.App.Views.EmbeddedRdp;

/// <summary>
/// Static rules for hiding disabled redirection indicators in the embedded
/// RDP toolbar. The default policy collapses disabled redirections and
/// surfaces them through a "+N" expand badge; the
/// <c>AppSettings.RdpRedirectionIndicatorsAlwaysExpanded</c> opt-in keeps
/// the legacy behavior (everything visible).
///
/// Pure helpers, kept stateless for unit-testing without WPF.
/// </summary>
internal static class RdpRedirectionVisibilityPolicy
{
    /// <summary>
    /// Decides whether a redirection icon should be visible given the
    /// active state, the global setting, and any per-session expand override.
    /// </summary>
    public static bool IsIndicatorVisible(
        bool isActive,
        bool alwaysExpanded,
        bool sessionExpandedOverride)
        => isActive || alwaysExpanded || sessionExpandedOverride;

    /// <summary>
    /// Decides whether the "+N disabled" expand badge should be shown.
    /// Hidden when the global setting forces full expansion, when the user
    /// already opted-in to expand for this session, or when nothing is
    /// disabled.
    /// </summary>
    public static bool ShouldShowExpandBadge(
        int disabledCount,
        bool alwaysExpanded,
        bool sessionExpandedOverride)
        => !alwaysExpanded && !sessionExpandedOverride && disabledCount > 0;

    /// <summary>
    /// Counts disabled (<c>false</c>) entries in the supplied state sequence.
    /// </summary>
    public static int CountDisabled(IEnumerable<bool> isActiveStates)
    {
        ArgumentNullException.ThrowIfNull(isActiveStates);
        var count = 0;
        foreach (var active in isActiveStates)
        {
            if (!active)
            {
                count++;
            }
        }
        return count;
    }
}
