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
using System.Reflection;
using System.Runtime.CompilerServices;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Sidebar;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class SidebarViewModelTests
{
    [Fact]
    public void OnFavoritesChanged_OnUiThread_RunsInline()
    {
        var dispatcher = new FakeUiDispatcher(checkAccess: true);
        using var viewModel = CreateViewModel(dispatcher);

        InvokeOnFavoritesChanged(viewModel, "PING");

        Assert.Equal(0, dispatcher.InvokeAsyncCalls);
    }

    [Fact]
    public void OnFavoritesChanged_OffUiThread_PostsToDispatcher()
    {
        var dispatcher = new FakeUiDispatcher(checkAccess: false);
        using var viewModel = CreateViewModel(dispatcher);

        InvokeOnFavoritesChanged(viewModel, "PING");

        Assert.Equal(1, dispatcher.InvokeAsyncCalls);
    }

    private static SidebarViewModel CreateViewModel(FakeUiDispatcher dispatcher)
    {
        var main = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        return new SidebarViewModel(
            main,
            new LocalizationManager(),
            null!,
            new ToolsTabPopulationService(new ToolRegistry()),
            new StubToolContextProvider(),
            dispatcher);
    }

    private static void InvokeOnFavoritesChanged(SidebarViewModel viewModel, string toolId)
    {
        var method = typeof(SidebarViewModel).GetMethod("OnFavoritesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewModel, [toolId]);
    }

    private sealed class StubToolContextProvider : IToolContextProvider
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public string? TargetHost => null;

        public bool HasTarget => false;

        public string ContextLabel => "Context";

        public string ContextTooltip => "Context";

        public string ContextBrushKey => "TextDisabledBrush";

        public void SetSelectedServer(ServerItemViewModel? server)
        {
        }

        public void Dispose()
        {
        }
    }
}
