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

using Heimdall.Sftp;

namespace Heimdall.Sftp.Tests;

public sealed class RemoteFileEditorUploadTrackingTests
{
    [Fact]
    public void ShouldReplaceTrackedUpload_NullCurrent_ReturnsTrue()
    {
        bool result = RemoteFileEditor.ShouldReplaceTrackedUpload(null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldReplaceTrackedUpload_CompletedCurrent_ReturnsTrue()
    {
        Task completed = Task.CompletedTask;

        bool result = RemoteFileEditor.ShouldReplaceTrackedUpload(completed);

        Assert.True(result);
    }

    [Fact]
    public void ShouldReplaceTrackedUpload_RunningCurrent_ReturnsFalse()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        bool result = RemoteFileEditor.ShouldReplaceTrackedUpload(completion.Task);

        Assert.False(result);
    }
}
