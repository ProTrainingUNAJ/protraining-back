using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldTestSessionsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "field_test_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_test_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "field_test_session_tests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_test_session_tests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "field_test_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateTime>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_test_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_field_test_results_pf_id",
                table: "field_test_results",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_field_test_results_session_id_catalog_test_id_athlete_id_at~",
                table: "field_test_results",
                columns: new[] { "session_id", "catalog_test_id", "athlete_id", "attempt" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_field_test_session_tests_pf_id",
                table: "field_test_session_tests",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_field_test_session_tests_session_id",
                table: "field_test_session_tests",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_field_test_sessions_category_id",
                table: "field_test_sessions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_field_test_sessions_pf_id",
                table: "field_test_sessions",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_test_results");

            migrationBuilder.DropTable(
                name: "field_test_session_tests");

            migrationBuilder.DropTable(
                name: "field_test_sessions");
        }
    }
}
