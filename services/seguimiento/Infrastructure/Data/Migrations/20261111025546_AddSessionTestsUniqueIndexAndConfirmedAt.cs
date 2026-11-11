using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTestsUniqueIndexAndConfirmedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KAN-568: si la tabla tuviera duplicados activos (session_id, catalog_test_id)
            // de datos anteriores al índice, se soft-deatean conservando la fila de menor id.
            // Sin esto, el CreateIndex de abajo fallaría; si no hay duplicados, es no-op.
            migrationBuilder.Sql(
                """
                UPDATE field_test_session_tests t SET deleted_at = NOW()
                WHERE t.deleted_at IS NULL AND EXISTS (
                    SELECT 1 FROM field_test_session_tests d
                    WHERE d.deleted_at IS NULL
                      AND d.session_id = t.session_id
                      AND d.catalog_test_id = t.catalog_test_id
                      AND d.id < t.id);
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "confirmed_at",
                table: "field_test_results",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_field_test_session_tests_session_id_catalog_test_id",
                table: "field_test_session_tests",
                columns: new[] { "session_id", "catalog_test_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_field_test_session_tests_session_id_catalog_test_id",
                table: "field_test_session_tests");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                table: "field_test_results");
        }
    }
}
