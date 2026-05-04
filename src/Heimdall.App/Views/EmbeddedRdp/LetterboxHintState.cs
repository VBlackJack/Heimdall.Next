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

using Heimdall.Core.Configuration;

namespace Heimdall.App.Views.EmbeddedRdp;

internal sealed class LetterboxHintState
{
    private RdpResolutionMode? _lastResolutionMode;
    private bool? _lastUsesFixedLocalResolution;
    private bool _shown;

    public bool ShouldShow(
        RdpResolutionMode resolutionMode,
        bool usesFixedLocalResolution,
        bool isLetterboxActive)
    {
        Observe(resolutionMode, usesFixedLocalResolution);

        if (!isLetterboxActive || _shown)
        {
            return false;
        }

        _shown = true;
        return true;
    }

    public void Observe(RdpResolutionMode resolutionMode, bool usesFixedLocalResolution)
    {
        if (_lastResolutionMode == resolutionMode
            && _lastUsesFixedLocalResolution == usesFixedLocalResolution)
        {
            return;
        }

        _lastResolutionMode = resolutionMode;
        _lastUsesFixedLocalResolution = usesFixedLocalResolution;
        _shown = false;
    }
}
