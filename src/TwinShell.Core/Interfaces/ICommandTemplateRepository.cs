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

using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Repository interface for command template persistence
/// </summary>
public interface ICommandTemplateRepository
{
    /// <summary>
    /// Gets all command templates
    /// </summary>
    Task<IEnumerable<CommandTemplate>> GetAllAsync();

    /// <summary>
    /// Gets a template by its internal ID
    /// </summary>
    Task<CommandTemplate?> GetByIdAsync(string id);

    /// <summary>
    /// Gets a template by its public ID (for GitOps sync)
    /// </summary>
    Task<CommandTemplate?> GetByPublicIdAsync(Guid publicId);

    /// <summary>
    /// Gets the internal ID for a given public ID
    /// </summary>
    Task<string?> GetIdByPublicIdAsync(Guid publicId);

    /// <summary>
    /// Adds a new template
    /// </summary>
    Task AddAsync(CommandTemplate template);

    /// <summary>
    /// Updates an existing template
    /// </summary>
    Task UpdateAsync(CommandTemplate template);

    /// <summary>
    /// Deletes a template by its internal ID
    /// </summary>
    Task DeleteAsync(string id);

    /// <summary>
    /// Checks if a template exists by internal ID
    /// </summary>
    Task<bool> ExistsAsync(string id);

    /// <summary>
    /// Gets the total count of templates
    /// </summary>
    Task<int> CountAsync();
}
