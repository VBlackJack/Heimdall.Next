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
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TwinShell.Persistence;
using TwinShell.Persistence.Schema;

namespace TwinShell.Infrastructure.Tests;

public sealed class TwinShellSchemaUpgradeTests
{
    private static readonly string[] PublicIdTables =
    [
        "Actions",
        "CommandBatches",
        "CustomCategories",
        "CommandTemplates"
    ];

    [Fact]
    public async Task UpgradeAsync_LegacyDatabase_AddsPublicIdsAndIndexes()
    {
        await using TempTwinShellDatabase database = new TempTwinShellDatabase();
        await database.CreateLegacySchemaAsync();

        await SchemaUpgrader.UpgradeAsync(database.Context, TwinShellSchema.Steps);

        int userVersion = await ReadUserVersionAsync(database.Context);
        userVersion.Should().Be(1);

        foreach (string tableName in PublicIdTables)
        {
            bool hasPublicIdColumn = await TableHasColumnAsync(database.Context, tableName, "PublicId");
            bool hasPublicIdIndex = await IndexExistsAsync(database.Context, "IX_" + tableName + "_PublicId");
            IReadOnlyList<string> publicIds = await ReadPublicIdsAsync(database.Context, tableName);

            hasPublicIdColumn.Should().BeTrue();
            hasPublicIdIndex.Should().BeTrue();
            publicIds.Should().HaveCount(2);
            publicIds.Should().OnlyContain(publicId => !string.IsNullOrWhiteSpace(publicId));
            publicIds.Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public async Task UpgradeAsync_FreshDatabase_MarksSchemaVersionOne()
    {
        await using TempTwinShellDatabase database = new TempTwinShellDatabase();
        await database.Context.Database.EnsureCreatedAsync();

        await SchemaUpgrader.UpgradeAsync(database.Context, TwinShellSchema.Steps);

        int userVersion = await ReadUserVersionAsync(database.Context);
        userVersion.Should().Be(1);

        foreach (string tableName in PublicIdTables)
        {
            bool hasPublicIdColumn = await TableHasColumnAsync(database.Context, tableName, "PublicId");
            bool hasPublicIdIndex = await IndexExistsAsync(database.Context, "IX_" + tableName + "_PublicId");

            hasPublicIdColumn.Should().BeTrue();
            hasPublicIdIndex.Should().BeTrue();
        }
    }

    [Fact]
    public async Task UpgradeAsync_WhenRunTwice_LeavesVersionAndPublicIdsUnchanged()
    {
        await using TempTwinShellDatabase database = new TempTwinShellDatabase();
        await database.CreateLegacySchemaAsync();

        await SchemaUpgrader.UpgradeAsync(database.Context, TwinShellSchema.Steps);
        Dictionary<string, IReadOnlyList<string>> publicIdsByTable = new Dictionary<string, IReadOnlyList<string>>();

        foreach (string tableName in PublicIdTables)
        {
            IReadOnlyList<string> publicIds = await ReadPublicIdsAsync(database.Context, tableName);
            publicIdsByTable.Add(tableName, publicIds);
        }

        await SchemaUpgrader.UpgradeAsync(database.Context, TwinShellSchema.Steps);

        int userVersion = await ReadUserVersionAsync(database.Context);
        userVersion.Should().Be(1);

        foreach (string tableName in PublicIdTables)
        {
            IReadOnlyList<string> publicIds = await ReadPublicIdsAsync(database.Context, tableName);
            publicIds.Should().Equal(publicIdsByTable[tableName]);
        }
    }

    [Fact]
    public async Task UpgradeAsync_WhenSecondTableUpdateFails_RollsBackWholePublicIdStep()
    {
        await using TempTwinShellDatabase database = new TempTwinShellDatabase();
        await database.CreateLegacySchemaAsync();

        // A trigger makes the production v1 step fail after ALTER TABLE on the second
        // table without changing the step implementation or relying on random UUIDs.
        await ExecuteNonQueryAsync(
            database.Context,
            "CREATE TRIGGER CommandBatches_ForcePublicIdFailure AFTER UPDATE ON CommandBatches "
            + "BEGIN SELECT RAISE(FAIL, 'forced publicid failure'); END");

        Func<Task> act = async () => await SchemaUpgrader.UpgradeAsync(
            database.Context,
            TwinShellSchema.Steps);

        await act.Should().ThrowAsync<Exception>().WithMessage("*forced publicid failure*");

        int userVersion = await ReadUserVersionAsync(database.Context);
        userVersion.Should().Be(0);

        foreach (string tableName in PublicIdTables)
        {
            bool hasPublicIdColumn = await TableHasColumnAsync(database.Context, tableName, "PublicId");
            bool hasPublicIdIndex = await IndexExistsAsync(database.Context, "IX_" + tableName + "_PublicId");

            hasPublicIdColumn.Should().BeFalse();
            hasPublicIdIndex.Should().BeFalse();
        }
    }

    [Fact]
    public async Task BootstrapperInitializationPath_FreshDatabase_ReachesVersionOne()
    {
        await using TempTwinShellDatabase database = new TempTwinShellDatabase();
        ServiceCollection services = new ServiceCollection();
        services.AddDbContext<TwinShellDbContext>(options =>
            options.UseSqlite(database.ConnectionString));

        // TwinShellBootstrapper owns an AppData database path; this exercises the same
        // scoped DI context plus EnsureCreated/Upgrade sequence against a temp database.
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        TwinShellDbContext context = scope.ServiceProvider.GetRequiredService<TwinShellDbContext>();

        await context.Database.EnsureCreatedAsync();
        await SchemaUpgrader.UpgradeAsync(context, TwinShellSchema.Steps);

        int userVersion = await ReadUserVersionAsync(context);
        userVersion.Should().Be(1);
    }

    private static async Task<int> ReadUserVersionAsync(TwinShellDbContext context)
    {
        DbConnection connection = context.Database.GetDbConnection();
        bool openedConnection = connection.State != ConnectionState.Open;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync();
            }

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";

            object? result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> TableHasColumnAsync(
        TwinShellDbContext context,
        string tableName,
        string columnName)
    {
        DbConnection connection = context.Database.GetDbConnection();
        bool openedConnection = connection.State != ConnectionState.Open;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync();
            }

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('" + tableName + "') WHERE name = $columnName";
            DbParameter columnNameParameter = command.CreateParameter();
            columnNameParameter.ParameterName = "$columnName";
            columnNameParameter.Value = columnName;
            command.Parameters.Add(columnNameParameter);

            object? result = await command.ExecuteScalarAsync();
            int columnCount = Convert.ToInt32(result, CultureInfo.InvariantCulture);
            return columnCount > 0;
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> IndexExistsAsync(
        TwinShellDbContext context,
        string indexName)
    {
        DbConnection connection = context.Database.GetDbConnection();
        bool openedConnection = connection.State != ConnectionState.Open;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync();
            }

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $indexName";
            DbParameter indexNameParameter = command.CreateParameter();
            indexNameParameter.ParameterName = "$indexName";
            indexNameParameter.Value = indexName;
            command.Parameters.Add(indexNameParameter);

            object? result = await command.ExecuteScalarAsync();
            int indexCount = Convert.ToInt32(result, CultureInfo.InvariantCulture);
            return indexCount > 0;
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<IReadOnlyList<string>> ReadPublicIdsAsync(
        TwinShellDbContext context,
        string tableName)
    {
        DbConnection connection = context.Database.GetDbConnection();
        bool openedConnection = connection.State != ConnectionState.Open;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync();
            }

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT PublicId FROM " + tableName + " ORDER BY Id";
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            List<string> publicIds = new List<string>();

            while (await reader.ReadAsync())
            {
                publicIds.Add(reader.GetString(0));
            }

            return publicIds;
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task ExecuteNonQueryAsync(
        TwinShellDbContext context,
        string commandText)
    {
        DbConnection connection = context.Database.GetDbConnection();
        bool openedConnection = connection.State != ConnectionState.Open;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync();
            }

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = commandText;

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed class TempTwinShellDatabase : IAsyncDisposable
    {
        private readonly string _rootPath;

        internal TempTwinShellDatabase()
        {
            _rootPath = Path.Combine(
                Path.GetTempPath(),
                "heimdall_twinshell_schema_live_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);

            string databasePath = Path.Combine(_rootPath, "twinshell.db");
            SqliteConnectionStringBuilder connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            };

            ConnectionString = connectionString.ToString();
            DbContextOptions<TwinShellDbContext> options = new DbContextOptionsBuilder<TwinShellDbContext>()
                .UseSqlite(ConnectionString)
                .Options;

            Context = new TwinShellDbContext(options);
        }

        internal TwinShellDbContext Context { get; }

        internal string ConnectionString { get; }

        internal async Task CreateLegacySchemaAsync()
        {
            foreach (string tableName in PublicIdTables)
            {
                await ExecuteNonQueryAsync(
                    Context,
                    "CREATE TABLE " + tableName + " (Id TEXT NOT NULL PRIMARY KEY)");
                await ExecuteNonQueryAsync(
                    Context,
                    "INSERT INTO " + tableName + " (Id) VALUES ('" + tableName + "-1')");
                await ExecuteNonQueryAsync(
                    Context,
                    "INSERT INTO " + tableName + " (Id) VALUES ('" + tableName + "-2')");
            }

            await ExecuteNonQueryAsync(Context, "PRAGMA user_version = 0");
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
