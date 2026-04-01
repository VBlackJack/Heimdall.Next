using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace TwinShell.Persistence.Migrations;

/// <summary>
/// Adds PublicId (Guid) columns to entities for GitOps synchronization support.
/// </summary>
public partial class AddPublicIdToEntities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add PublicId column to Actions table
        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "Actions",
            type: "TEXT",
            nullable: false,
            defaultValueSql: "lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))");

        migrationBuilder.CreateIndex(
            name: "IX_Actions_PublicId",
            table: "Actions",
            column: "PublicId",
            unique: true);

        // Add PublicId column to CommandBatches table
        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "CommandBatches",
            type: "TEXT",
            nullable: false,
            defaultValueSql: "lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))");

        migrationBuilder.CreateIndex(
            name: "IX_CommandBatches_PublicId",
            table: "CommandBatches",
            column: "PublicId",
            unique: true);

        // Add PublicId column to CustomCategories table
        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "CustomCategories",
            type: "TEXT",
            nullable: false,
            defaultValueSql: "lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))");

        migrationBuilder.CreateIndex(
            name: "IX_CustomCategories_PublicId",
            table: "CustomCategories",
            column: "PublicId",
            unique: true);

        // Add PublicId column to CommandTemplates table
        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "CommandTemplates",
            type: "TEXT",
            nullable: false,
            defaultValueSql: "lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))");

        migrationBuilder.CreateIndex(
            name: "IX_CommandTemplates_PublicId",
            table: "CommandTemplates",
            column: "PublicId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Actions_PublicId",
            table: "Actions");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "Actions");

        migrationBuilder.DropIndex(
            name: "IX_CommandBatches_PublicId",
            table: "CommandBatches");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "CommandBatches");

        migrationBuilder.DropIndex(
            name: "IX_CustomCategories_PublicId",
            table: "CustomCategories");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "CustomCategories");

        migrationBuilder.DropIndex(
            name: "IX_CommandTemplates_PublicId",
            table: "CommandTemplates");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "CommandTemplates");
    }
}
