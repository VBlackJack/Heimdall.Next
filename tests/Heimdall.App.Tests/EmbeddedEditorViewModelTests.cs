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

using Heimdall.App.ViewModels;

namespace Heimdall.App.Tests;

public sealed class EmbeddedEditorViewModelTests
{
    [Fact]
    public async Task SaveAsync_RemoteFile_LeavesModifiedUntilSaveIsConfirmed()
    {
        EmbeddedEditorViewModel viewModel = new();
        viewModel.LoadContent("remote.txt");
        viewModel.NotifyTextChanged();

        bool result = await viewModel.SaveAsync("changed");

        Assert.True(result);
        Assert.True(viewModel.IsModified);
    }

    [Fact]
    public async Task ConfirmRemoteSaved_RemoteFile_ClearsModifiedState()
    {
        EmbeddedEditorViewModel viewModel = new();
        viewModel.LoadContent("remote.txt");
        viewModel.NotifyTextChanged();
        await viewModel.SaveAsync("changed");

        viewModel.ConfirmRemoteSaved();

        Assert.False(viewModel.IsModified);
    }
}
