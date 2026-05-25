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

public sealed class SudoUploadCommandsTests
{
    [Fact]
    public void Build_ProducesTwoSeparateCommands_NoLogicalAnd()
    {
        (string write, string cleanup) = SudoUploadCommands.Build(
            "/tmp/.heimdall_upload_xyz",
            "/etc/hosts");

        Assert.DoesNotContain("&&", write);
        Assert.DoesNotContain(";", write);
        Assert.DoesNotContain("&&", cleanup);
        Assert.Equal("cp -- '/tmp/.heimdall_upload_xyz' '/etc/hosts'", write);
        Assert.Equal("rm -f '/tmp/.heimdall_upload_xyz'", cleanup);
    }

    [Fact]
    public void Build_EscapesPathsContainingSingleQuotes()
    {
        (string write, string cleanup) = SudoUploadCommands.Build(
            "/tmp/o'reilly",
            "/var/log/oh's.log");

        Assert.Contains(@"'/tmp/o'\''reilly'", write);
        Assert.Contains(@"'/var/log/oh'\''s.log'", write);
        Assert.Contains(@"'/tmp/o'\''reilly'", cleanup);
    }

    [Fact]
    public void Build_ThrowsForNullOrWhitespaceTempPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => SudoUploadCommands.Build(null!, "/etc/hosts"));
        Assert.ThrowsAny<ArgumentException>(() => SudoUploadCommands.Build(string.Empty, "/etc/hosts"));
        Assert.ThrowsAny<ArgumentException>(() => SudoUploadCommands.Build(" ", "/etc/hosts"));
    }

    [Fact]
    public void Build_ThrowsForNullOrWhitespaceTargetPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => SudoUploadCommands.Build("/tmp/x", null!));
        Assert.ThrowsAny<ArgumentException>(() => SudoUploadCommands.Build("/tmp/x", string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => SudoUploadCommands.Build("/tmp/x", " "));
    }
}
