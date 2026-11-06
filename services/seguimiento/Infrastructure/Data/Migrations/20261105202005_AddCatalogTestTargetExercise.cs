using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogTestTargetExercise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "exercise_id",
                table: "catalog_tests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_exercise_name",
                table: "catalog_tests",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exercise_id",
                table: "catalog_tests");

            migrationBuilder.DropColumn(
                name: "target_exercise_name",
                table: "catalog_tests");
        }
    }
}
