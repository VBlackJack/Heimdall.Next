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

namespace Heimdall.App.Tests;

public sealed class RdpDisconnectTeardownSequenceTests
{
    [Fact]
    public void Execute_PerformsCanonicalStepsInOrder()
    {
        var target = new RecordingTarget();

        RdpDisconnectTeardownSequence.Execute(
            target,
            DisconnectReason.UserAction,
            _ => { },
            _ => { });

        Assert.Equal(
            [
                "Visibility=Collapsed",
                "Child=null",
                "Disconnect",
                "DetachEventSink",
                "Dispose"
            ],
            target.Steps);
    }

    [Fact]
    public void Execute_ContinuesWhenOneStepThrows()
    {
        var target = new RecordingTarget(throwOnDisconnect: true);
        var warnings = new List<string>();

        RdpDisconnectTeardownSequence.Execute(
            target,
            DisconnectReason.ReconnectInitiated,
            _ => { },
            warnings.Add);

        Assert.Equal(
            [
                "Visibility=Collapsed",
                "Child=null",
                "Disconnect",
                "DetachEventSink",
                "Dispose"
            ],
            target.Steps);
        Assert.Single(warnings);
        Assert.Contains("step=Disconnect", warnings[0], StringComparison.Ordinal);
    }

    private sealed class RecordingTarget(bool throwOnDisconnect = false) : IRdpDisconnectTeardownTarget
    {
        public List<string> Steps { get; } = [];

        public string TeardownTargetName => nameof(RecordingTarget);

        public void CollapseHost() => Steps.Add("Visibility=Collapsed");

        public void ClearHostChild() => Steps.Add("Child=null");

        public void Disconnect()
        {
            Steps.Add("Disconnect");
            if (throwOnDisconnect)
            {
                throw new InvalidOperationException("disconnect failed");
            }
        }

        public void DetachEventSink() => Steps.Add("DetachEventSink");

        public void DisposeHost() => Steps.Add("Dispose");
    }
}
