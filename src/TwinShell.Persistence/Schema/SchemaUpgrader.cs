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

using System.Data;
using System.Data.Common;
using System.Globalization;
using Heimdall.Core.Logging;
using Microsoft.EntityFrameworkCore;

namespace TwinShell.Persistence.Schema;

public static class SchemaUpgrader
{
    public static async Task UpgradeAsync(
        TwinShellDbContext context,
        IReadOnlyList<SchemaStep> steps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(steps);

        List<SchemaStep> orderedSteps = ValidateAndOrderSteps(steps);
        DbConnection connection = context.Database.GetDbConnection();
        bool openedConnection = connection.State != ConnectionState.Open;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            int currentVersion = await ReadUserVersionAsync(connection, cancellationToken);

            foreach (SchemaStep step in orderedSteps)
            {
                if (step.Version <= currentVersion)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

                try
                {
                    await step.ApplyAsync(connection, transaction, cancellationToken);
                    await SetUserVersionAsync(connection, transaction, step.Version, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    currentVersion = step.Version;

                    FileLogger.Info($"[TwinShell] schema upgraded to v{step.Version} ({step.Name})");
                }
                catch (Exception ex)
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception rollbackException)
                    {
                        FileLogger.Error(
                            $"[TwinShell] schema rollback failed for v{step.Version} ({step.Name})",
                            rollbackException);
                    }

                    FileLogger.Error(
                        $"[TwinShell] schema upgrade to v{step.Version} ({step.Name}) failed",
                        ex);
                    throw;
                }
            }
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static List<SchemaStep> ValidateAndOrderSteps(IReadOnlyList<SchemaStep> steps)
    {
        List<SchemaStep> orderedSteps = steps
            .OrderBy(static step => step.Version)
            .ToList();
        int previousVersion = 0;

        foreach (SchemaStep step in orderedSteps)
        {
            if (step.Version < 1)
            {
                throw new ArgumentException("Schema step versions must be greater than or equal to 1.", nameof(steps));
            }

            if (step.Version <= previousVersion)
            {
                throw new ArgumentException("Schema step versions must be unique.", nameof(steps));
            }

            previousVersion = step.Version;
        }

        return orderedSteps;
    }

    private static async Task<int> ReadUserVersionAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task SetUserVersionAsync(
        DbConnection connection,
        DbTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = " + version.ToString(CultureInfo.InvariantCulture);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
