/*
 * Copyright 2025 Julien Bombled
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

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Unit of Work interface for managing database transactions.
/// Enables atomic operations across multiple repository calls.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Begins a new database transaction
    /// </summary>
    Task BeginTransactionAsync();

    /// <summary>
    /// Commits the current transaction, persisting all changes
    /// </summary>
    Task CommitTransactionAsync();

    /// <summary>
    /// Rolls back the current transaction, discarding all changes
    /// </summary>
    Task RollbackTransactionAsync();

    /// <summary>
    /// Indicates whether a transaction is currently active
    /// </summary>
    bool HasActiveTransaction { get; }
}
