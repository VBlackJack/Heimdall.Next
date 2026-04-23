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

using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Session;

namespace Heimdall.App.Tests;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public void ClearPostConnectStateOnUiThread_UsesInjectedDispatcher()
    {
        var dispatcher = new FakeUiDispatcher();
        var coordinator = SessionCoordinator.CreateForTests(dispatcher);
        var tab = new SessionTabViewModel();
        tab.SetPostConnectState(true, "1/2", "Running");

        coordinator.ClearPostConnectStateOnUiThread(tab);

        Assert.Equal(1, dispatcher.InvokeCalls);
        Assert.False(tab.IsPostConnectRunning);
        Assert.Equal(string.Empty, tab.PostConnectProgressText);
        Assert.Equal(string.Empty, tab.PostConnectTooltip);
    }
}
