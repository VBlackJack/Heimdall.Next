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

namespace Heimdall.App.Services;

/// <summary>
/// Abstraction over the WPF UI dispatcher for ViewModel-layer coordination code.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Executes the action on the UI thread synchronously.
    /// </summary>
    void Invoke(Action action);

    /// <summary>
    /// Executes the function on the UI thread synchronously and returns its result.
    /// </summary>
    T Invoke<T>(Func<T> func);

    /// <summary>
    /// Executes the action on the UI thread asynchronously.
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Returns whether the current thread already has UI access.
    /// </summary>
    bool CheckAccess();
}
