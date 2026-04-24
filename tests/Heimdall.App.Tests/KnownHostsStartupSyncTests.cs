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
using Heimdall.Core.Configuration;
using Heimdall.Core.Ssh;
using System.IO;

namespace Heimdall.App.Tests;

public sealed class KnownHostsStartupSyncTests
{
    [Fact]
    public void StartIfEnabled_DefaultFalse_DoesNotScheduleImport()
    {
        var scheduled = 0;
        var sync = new KnownHostsStartupSync(
            _ => throw new InvalidOperationException("import should not run"),
            _ => scheduled++);

        var started = sync.StartIfEnabled(new AppSettings());

        Assert.False(started);
        Assert.Equal(0, scheduled);
    }

    [Fact]
    public void StartIfEnabled_True_SchedulesImportWithoutRunningInline()
    {
        var importCalled = false;
        Action? captured = null;
        var sync = new KnownHostsStartupSync(
            path =>
            {
                importCalled = true;
                Assert.Equal("mem://known_hosts", path);
                return new KnownHostsImportReport(Imported: 1, Matched: 0, Conflicts: []);
            },
            action => captured = action);

        var started = sync.StartIfEnabled(
            new AppSettings { SyncKnownHostsAtStartup = true },
            "mem://known_hosts");

        Assert.True(started);
        Assert.NotNull(captured);
        Assert.False(importCalled);
        captured!();
        Assert.True(importCalled);
    }

    [Fact]
    public void StartIfEnabled_ImportThrows_DoesNotPropagateToCaller()
    {
        var sync = new KnownHostsStartupSync(
            _ => throw new IOException("simulated import failure"),
            action => action());

        var started = sync.StartIfEnabled(new AppSettings { SyncKnownHostsAtStartup = true });

        Assert.True(started);
    }
}
