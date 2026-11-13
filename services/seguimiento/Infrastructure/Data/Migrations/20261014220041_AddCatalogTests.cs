using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_tests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    motor_capacity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    measurement_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    difficulty_level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_tests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_tests_pf_id",
                table: "catalog_tests",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_tests_pf_id_active",
                table: "catalog_tests",
                columns: new[] { "pf_id", "active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_tests");
        }
    }
}
