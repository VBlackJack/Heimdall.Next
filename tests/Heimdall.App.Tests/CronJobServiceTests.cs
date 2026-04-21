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
using Heimdall.Core.CronJob;

namespace Heimdall.App.Tests;

public sealed class CronJobServiceTests
{
    private const string Csv = """
"TaskName","Status","Next Run Time","Last Run Time","Last Result"
"\TaskA","Ready","4/18/2026 12:00:00","4/17/2026 12:00:00","0"
"\TaskB","Running","N/A","N/A","267011"
""";

    [Fact]
    public async Task LoadWindowsTasksAsync_ParsesRunnerOutput()
    {
        var service = new CronJobService(_ => Task.FromResult(Csv));

        var tasks = await service.LoadWindowsTasksAsync(CancellationToken.None);

        Assert.Equal(2, tasks.Count);
        Assert.Equal(@"\TaskA", tasks[0].Name);
        Assert.Equal("Running", tasks[1].Status);
    }

    [Fact]
    public async Task LoadWindowsTasksAsync_EmptyOutput_ReturnsEmpty()
    {
        var service = new CronJobService(_ => Task.FromResult(string.Empty));

        var tasks = await service.LoadWindowsTasksAsync(CancellationToken.None);

        Assert.Empty(tasks);
    }

    [Fact]
    public async Task LoadWindowsTasksAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new CronJobService(ct =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(string.Empty);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadWindowsTasksAsync(cts.Token));
    }

    [Fact]
    public async Task LoadWindowsTasksAsync_Exception_Propagates()
    {
        var service = new CronJobService(_ => throw new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadWindowsTasksAsync(CancellationToken.None));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void Constructor_NullRunner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CronJobService(null!));
    }
}
