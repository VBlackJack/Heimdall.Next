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

using System.Data.Common;
using System.Globalization;

namespace TwinShell.Persistence.Schema;

public static class TwinShellSchema
{
    private const string PublicIdColumnName = "PublicId";

    private const string UuidSql =
        "lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))";

    private static readonly string[] PublicIdTables =
    [
        "Actions",
        "CommandBatches",
        "CustomCategories",
        "CommandTemplates"
    ];

    private static readonly IReadOnlyList<SchemaStep> SchemaSteps = new List<SchemaStep>
    {
        new SchemaStep(1, "GitOps PublicId columns", ApplyPublicIdAsync)
    }.AsReadOnly();

    public static IReadOnlyList<SchemaStep> Steps => SchemaSteps;

    private static async Task ApplyPublicIdAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (string tableName in PublicIdTables)
        {
            await AddPublicIdColumnIfNotExistsAsync(
                connection,
                transaction,
                tableName,
                cancellationToken);
        }
    }

    private static async Task AddPublicIdColumnIfNotExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        EnsureKnownPublicIdTable(tableName);

        bool exists = await PublicIdColumnExistsAsync(
            connection,
            transaction,
            tableName,
            cancellationToken);

        if (exists)
        {
            return;
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "ALTER TABLE " + tableName + " ADD COLUMN " + PublicIdColumnName + " TEXT NOT NULL DEFAULT ''",
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "UPDATE " + tableName + " SET " + PublicIdColumnName + " = " + UuidSql,
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_" + tableName + "_" + PublicIdColumnName
            + " ON " + tableName + "(" + PublicIdColumnName + ")",
            cancellationToken);
    }

    private static void EnsureKnownPublicIdTable(string tableName)
    {
        if (!PublicIdTables.Contains(tableName, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid table name: " + tableName, nameof(tableName));
        }
    }

    private static async Task<bool> PublicIdColumnExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('" + tableName + "') WHERE name = '"
            + PublicIdColumnName + "'";

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        int existingColumnCount = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        return existingColumnCount > 0;
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
