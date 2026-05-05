<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# CI flaky tests — `Category=CIUnstable`

A small set of tests that are stable on developer machines turn red
intermittently on the GitHub Actions Windows runner. They are tagged
`[Trait("Category", "CIUnstable")]` and excluded from the bloquant CI
test pass via `dotnet test --filter "Category!=CIUnstable"`.

A second non-blocking step in `.github/workflows/ci.yml` runs the same
tests with `continue-on-error: true` so that the signal stays visible in
the workflow summary without failing the build.

## Why these tests are flaky on the runner only

Two distinct root causes share the same symptom (`TaskCanceledException`,
`OperationCanceledException`, or `WaitUntil` timeouts):

1. **Named-pipe handshake latency** — `OpenSshPipeAgentTests` create a
   per-test named pipe and race a server-side `WaitForConnectionAsync`
   against the client-side connect. On the GitHub Actions runner, the
   handshake routinely exceeds 10 seconds even with generous
   `availabilityTimeoutMs` and server-side
   `CancellationTokenSource(TimeSpan.FromSeconds(10))`. Suspect: Defender
   / runner I/O contention scanning the pipe.
2. **WPF + UIAutomation binding propagation latency** — `Pilots/*SmokeTests`
   wait for a value to propagate through a `Binding` / `INotifyPropertyChanged`
   chain. On a slow runner, the propagation outlasts even a 10-second
   `WaitHelpers.DefaultTimeout`. Bumping the timeout further only delays
   the failure window and slows down genuinely hung tests.

## Currently tagged `CIUnstable`

| Test class | File |
|---|---|
| `OpenSshPipeAgentTests` (3 of 4 tests) | `tests/Heimdall.Ssh.Tests/OpenSshPipeAgentTests.cs` |
| `HmacGeneratorSmokeTests` (whole class) | `tests/Heimdall.App.UiTests/Pilots/HmacGeneratorSmokeTests.cs` |
| `TextDiffSmokeTests` (whole class) | `tests/Heimdall.App.UiTests/Pilots/TextDiffSmokeTests.cs` |
| `HashGeneratorSmokeTests` (whole class) | `tests/Heimdall.App.UiTests/Pilots/HashGeneratorSmokeTests.cs` |
| `DnsLookupViewModelTests.CancelCommand_UserCancellation_ClearsStatusWithoutError` | `tests/Heimdall.App.Tests/DnsLookupViewModelTests.cs` |

`OpenSshPipeAgentTests.IsAvailable_NoServer_ReturnsFalse` is intentionally
NOT tagged: it is a negative-path test that asserts a 25 ms
availability probe fires when no server is listening, and that path is
not affected by the runner latency.

## Running locally

`Test.bat` and `dotnet test Heimdall.slnx` (without filter) run the full
suite, tagged tests included. Expect them to pass.

To reproduce the CI behavior locally:

```powershell
dotnet test Heimdall.slnx --filter "Category!=CIUnstable"
dotnet test Heimdall.slnx --filter "Category=CIUnstable"
```

## When to remove a tag

Lift the `CIUnstable` trait once one of the following is true:

- The runner image (or its Defender exclusion list) is updated and the
  tests pass three CI runs in a row without retries.
- The test is rewritten to no longer depend on cross-process I/O timing
  (e.g. by replacing the named pipe with an in-memory transport, or by
  driving the WPF binding update synchronously from the test thread).
- The test is deleted as obsolete.

Removing the trait without one of those changes will re-introduce the
intermittent CI redness.
