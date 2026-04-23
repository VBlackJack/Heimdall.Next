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

using System.Reflection;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed class TaskSchedulerServiceTests
{
    [Fact]
    public async Task FakeUiDispatcher_InvokeAsyncFuncTask_CompletesAfterInnerTaskAndTracksOverload()
    {
        var dispatcher = new FakeUiDispatcher();
        var innerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var dispatchTask = dispatcher.InvokeAsync(async () =>
        {
            innerStarted.TrySetResult();
            await allowCompletion.Task;
        });

        await innerStarted.Task;

        Assert.False(dispatchTask.IsCompleted);
        Assert.Equal(1, dispatcher.InvokeAsyncFuncCalls);
        Assert.Equal(0, dispatcher.InvokeAsyncCalls);

        allowCompletion.TrySetResult();
        await dispatchTask;
    }

    [Fact]
    public async Task OnTickAsync_DispatchesViaUiDispatcherFuncTaskOverload()
    {
        var dispatcher = new FakeUiDispatcher();
        using var scheduler = CreateScheduler(
            dispatcher,
            CreateDueTask(),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);

        await InvokeOnTickAsync(scheduler);

        Assert.Equal(2, dispatcher.InvokeAsyncFuncCalls);
        Assert.Equal(0, dispatcher.InvokeAsyncCalls);
    }

    [Fact]
    public async Task OnTickAsync_ReleasesTickGuardOnlyAfterDispatchCompletes()
    {
        var dispatcher = new FakeUiDispatcher();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCallbackCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackInvocations = 0;
        var persistInvocations = 0;

        using var scheduler = CreateScheduler(
            dispatcher,
            CreateDueTask(),
            async _ =>
            {
                Interlocked.Increment(ref callbackInvocations);
                callbackStarted.TrySetResult();
                await allowCallbackCompletion.Task;
            },
            () =>
            {
                Interlocked.Increment(ref persistInvocations);
                return Task.CompletedTask;
            });

        var firstTick = InvokeOnTickAsync(scheduler);
        await callbackStarted.Task;

        var secondTick = InvokeOnTickAsync(scheduler);
        await secondTick;

        Assert.False(firstTick.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref callbackInvocations));
        Assert.Equal(0, Volatile.Read(ref persistInvocations));
        Assert.Equal(1, dispatcher.InvokeAsyncFuncCalls);

        allowCallbackCompletion.TrySetResult();
        await firstTick;

        Assert.Equal(1, Volatile.Read(ref callbackInvocations));
        Assert.Equal(1, Volatile.Read(ref persistInvocations));
        Assert.Equal(2, dispatcher.InvokeAsyncFuncCalls);
    }

    private static TaskSchedulerService CreateScheduler(
        FakeUiDispatcher dispatcher,
        ScheduledTaskDto task,
        Func<ScheduledTaskDto, Task> taskDueCallback,
        Func<Task> persistCallback)
    {
        return new TaskSchedulerService(dispatcher)
        {
            TasksProvider = () => [task],
            TaskDueCallback = taskDueCallback,
            PersistCallback = persistCallback
        };
    }

    private static ScheduledTaskDto CreateDueTask()
    {
        return new ScheduledTaskDto
        {
            Id = Guid.NewGuid().ToString("N"),
            ServerId = "srv-1",
            ServerName = "alpha",
            ConnectionType = "SSH",
            Enabled = true,
            ScheduleType = nameof(ScheduleType.Interval),
            IntervalMinutes = 60,
            NextRun = DateTime.Now.AddMinutes(-1)
        };
    }

    private static Task InvokeOnTickAsync(TaskSchedulerService scheduler)
    {
        var method = typeof(TaskSchedulerService).GetMethod("OnTickAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(scheduler, []) as Task
            ?? throw new InvalidOperationException("OnTickAsync did not return a Task.");
    }
}
