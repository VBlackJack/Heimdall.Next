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
using Heimdall.App.ViewModels.Tools;

namespace Heimdall.App.Tests;

public sealed class UlidGeneratorViewModelTests
{
    [Fact]
    public void Ctor_DoesNotGenerate()
    {
        var service = new QueueService("A");
        _ = new UlidGeneratorViewModel(service);

        Assert.Equal(0, service.GenerateCallCount);
    }

    [Fact]
    public void Initialize_GeneratesInitialSingleResult()
    {
        var service = new QueueService("01ARZ3NDEKTSV4RRFFQ69G5FAV");
        var vm = new UlidGeneratorViewModel(service);

        vm.Initialize(null);

        Assert.Equal("01ARZ3NDEKTSV4RRFFQ69G5FAV", vm.SingleResult);
        Assert.Equal(1, service.GenerateCallCount);
    }

    [Fact]
    public void Initialize_SetsBatchCountToDefault()
    {
        var vm = new UlidGeneratorViewModel(new QueueService("A"));
        vm.Initialize(null);

        Assert.Equal("10", vm.BatchCountText);
    }

    [Fact]
    public void GenerateCommand_UpdatesSingleResult()
    {
        var vm = CreateInitializedViewModel(new QueueService("A", "B"));

        vm.GenerateCommand.Execute(null);

        Assert.Equal("B", vm.SingleResult);
    }

    [Fact]
    public void GenerateCommand_ProducesDifferentValuesOnConsecutiveCalls()
    {
        var vm = CreateInitializedViewModel(new QueueService("A", "B", "C"));

        vm.GenerateCommand.Execute(null);
        var second = vm.SingleResult;
        vm.GenerateCommand.Execute(null);

        Assert.NotEqual(second, vm.SingleResult);
    }

    [Fact]
    public void GenerateBatchCommand_ProducesExpectedLineCount()
    {
        var vm = CreateInitializedViewModel(new QueueService("A", "B", "C", "D", "E", "F"));
        vm.BatchCountText = "5";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal(5, SplitLines(vm.BatchResults).Length);
    }

    [Fact]
    public void GenerateBatchCommand_ClampsBelowMinimum()
    {
        var vm = CreateInitializedViewModel(new QueueService("A", "B"));
        vm.BatchCountText = "0";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal("1", vm.BatchCountText);
        Assert.Single(SplitLines(vm.BatchResults));
    }

    [Fact]
    public void GenerateBatchCommand_ClampsAboveMaximum()
    {
        var vm = CreateInitializedViewModel(new QueueService(Enumerable.Range(0, 101).Select(i => i.ToString()).ToArray()));
        vm.BatchCountText = "500";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal("100", vm.BatchCountText);
        Assert.Equal(100, SplitLines(vm.BatchResults).Length);
    }

    [Fact]
    public void GenerateBatchCommand_ParseFailureFallsBackToMinimum()
    {
        var vm = CreateInitializedViewModel(new QueueService("A", "B"));
        vm.BatchCountText = "abc";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal("1", vm.BatchCountText);
        Assert.Single(SplitLines(vm.BatchResults));
    }

    [Fact]
    public void BatchSeparator_UsesNewlinesWithoutTrailingNewline()
    {
        var vm = CreateInitializedViewModel(new QueueService("A", "B", "C"));
        vm.BatchCountText = "2";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal("B" + Environment.NewLine + "C", vm.BatchResults);
        Assert.False(vm.BatchResults.EndsWith(Environment.NewLine, StringComparison.Ordinal));
    }

    private static UlidGeneratorViewModel CreateInitializedViewModel(IUlidGeneratorToolService service)
    {
        var vm = new UlidGeneratorViewModel(service);
        vm.Initialize(null);
        vm.MarkInitialized();
        return vm;
    }

    private static string[] SplitLines(string value)
        => value.Split([Environment.NewLine], StringSplitOptions.None);

    private sealed class QueueService(params string[] values) : IUlidGeneratorToolService
    {
        private readonly Queue<string> _values = new(values);

        public int GenerateCallCount { get; private set; }

        public string Generate()
        {
            GenerateCallCount++;
            return _values.Count > 0 ? _values.Dequeue() : Guid.NewGuid().ToString("N");
        }
    }
}
