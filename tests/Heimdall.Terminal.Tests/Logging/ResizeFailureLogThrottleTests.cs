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

using System.Collections.Concurrent;
using FluentAssertions;
using Heimdall.Terminal.Logging;

namespace Heimdall.Terminal.Tests.Logging;

public sealed class ResizeFailureLogThrottleTests
{
    private const int DefaultHResult = unchecked((int)0x80004005);
    private const int AlternateHResult = unchecked((int)0x80070057);
    private const int ParallelTaskCount = 8;
    private const int ParallelCallsPerTask = 200;
    private const int TotalParallelCallCount = ParallelTaskCount * ParallelCallsPerTask;

    [Fact]
    public void RecordFailure_FirstFailure_ReturnsLogCurrent()
    {
        ResizeFailureLogThrottle throttle = new ResizeFailureLogThrottle();

        ResizeFailureLogDecision decision = throttle.RecordFailure(MakeException("resize failed"));

        decision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
        decision.PreviousRepeatCount.Should().Be(0);
    }

    [Fact]
    public void RecordFailure_TwoIdenticalFailures_ReturnsLogCurrentThenSkip()
    {
        ResizeFailureLogThrottle throttle = new ResizeFailureLogThrottle();

        ResizeFailureLogDecision firstDecision = throttle.RecordFailure(MakeException("resize failed"));
        ResizeFailureLogDecision secondDecision = throttle.RecordFailure(MakeException("resize failed"));

        firstDecision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
        secondDecision.Action.Should().Be(ResizeFailureLogAction.Skip);
    }

    [Fact]
    public void RecordFailure_DifferentFailureAfterRepeats_ReturnsSummaryThenCurrent()
    {
        ResizeFailureLogThrottle throttle = new ResizeFailureLogThrottle();

        ResizeFailureLogDecision firstDecision = throttle.RecordFailure(MakeException("resize failed"));
        ResizeFailureLogDecision secondDecision = throttle.RecordFailure(MakeException("resize failed"));
        ResizeFailureLogDecision thirdDecision = throttle.RecordFailure(MakeException("resize failed"));
        ResizeFailureLogDecision fourthDecision = throttle.RecordFailure(MakeException("resize changed"));

        firstDecision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
        secondDecision.Action.Should().Be(ResizeFailureLogAction.Skip);
        thirdDecision.Action.Should().Be(ResizeFailureLogAction.Skip);
        fourthDecision.Action.Should().Be(ResizeFailureLogAction.LogRepeatSummaryThenCurrent);
        fourthDecision.PreviousRepeatCount.Should().Be(2);
    }

    [Fact]
    public void RecordFailure_SameMessageWithDifferentExceptionType_DoesNotDeduplicate()
    {
        ResizeFailureLogThrottle throttle = new ResizeFailureLogThrottle();

        ResizeFailureLogDecision firstDecision = throttle.RecordFailure(MakeException("resize failed"));
        ResizeFailureLogDecision secondDecision = throttle.RecordFailure(
            new AlternateResizeException("resize failed", DefaultHResult));

        firstDecision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
        secondDecision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
        secondDecision.PreviousRepeatCount.Should().Be(0);
    }

    [Fact]
    public void RecordFailure_SameTypeAndMessageWithDifferentHResult_DoesNotDeduplicate()
    {
        ResizeFailureLogThrottle throttle = new ResizeFailureLogThrottle();

        ResizeFailureLogDecision firstDecision = throttle.RecordFailure(MakeException("resize failed"));
        ResizeFailureLogDecision secondDecision = throttle.RecordFailure(
            MakeException("resize failed", AlternateHResult));

        firstDecision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
        secondDecision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
    }

    [Fact]
    public void RecordFailure_AfterSummaryThenCurrent_IdenticalFollowUpSkips()
    {
        ResizeFailureLogThrottle throttle = new ResizeFailureLogThrottle();

        throttle.RecordFailure(MakeException("resize failed"));
        throttle.RecordFailure(MakeException("resize failed"));
        ResizeFailureLogDecision changedDecision = throttle.RecordFailure(MakeException("resize changed"));
        ResizeFailureLogDecision followUpDecision = throttle.RecordFailure(MakeException("resize changed"));

        changedDecision.Action.Should().Be(ResizeFailureLogAction.LogRepeatSummaryThenCurrent);
        changedDecision.PreviousRepeatCount.Should().Be(1);
        followUpDecision.Action.Should().Be(ResizeFailureLogAction.Skip);
    }

    [Fact]
    public void Reset_AfterLogCurrent_MakesNextIdenticalFailureLogCurrent()
    {
        ResizeFailureLogThrottle throttle = new ResizeFailureLogThrottle();

        ResizeFailureLogDecision firstDecision = throttle.RecordFailure(MakeException("resize failed"));
        throttle.Reset();
        ResizeFailureLogDecision secondDecision = throttle.RecordFailure(MakeException("resize failed"));

        firstDecision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
        secondDecision.Action.Should().Be(ResizeFailureLogAction.LogCurrent);
    }

    [Fact]
    public async Task RecordFailure_ConcurrentIdenticalFailures_RecordsEveryCallWithoutThrowing()
    {
        ResizeFailureLogThrottle throttle = new ResizeFailureLogThrottle();
        ConcurrentBag<ResizeFailureLogAction> actions = new ConcurrentBag<ResizeFailureLogAction>();
        Exception exception = MakeException("resize failed");
        Task[] tasks = new Task[ParallelTaskCount];

        for (int taskIndex = 0; taskIndex < ParallelTaskCount; taskIndex++)
        {
            tasks[taskIndex] = Task.Run(() =>
            {
                for (int callIndex = 0; callIndex < ParallelCallsPerTask; callIndex++)
                {
                    ResizeFailureLogDecision decision = throttle.RecordFailure(exception);
                    actions.Add(decision.Action);
                }
            });
        }

        Func<Task> act = async () => await Task.WhenAll(tasks);

        await act.Should().NotThrowAsync();
        actions.Should().HaveCount(TotalParallelCallCount);
        actions.Count(action =>
                action is ResizeFailureLogAction.LogCurrent
                    or ResizeFailureLogAction.LogRepeatSummaryThenCurrent
                    or ResizeFailureLogAction.Skip)
            .Should()
            .Be(TotalParallelCallCount);
        actions.Count(action => action == ResizeFailureLogAction.LogCurrent)
            .Should()
            .Be(1);
        actions.Count(action => action == ResizeFailureLogAction.LogRepeatSummaryThenCurrent)
            .Should()
            .Be(0);
        actions.Count(action => action == ResizeFailureLogAction.Skip)
            .Should()
            .Be(TotalParallelCallCount - 1);
    }

    private static Exception MakeException(
        string message,
        int hResult = DefaultHResult)
    {
        return new ResizeTestException(message, hResult);
    }

    private sealed class ResizeTestException : InvalidOperationException
    {
        public ResizeTestException(string message, int hResult)
            : base(message)
        {
            HResult = hResult;
        }
    }

    private sealed class AlternateResizeException : Exception
    {
        public AlternateResizeException(string message, int hResult)
            : base(message)
        {
            HResult = hResult;
        }
    }
}
