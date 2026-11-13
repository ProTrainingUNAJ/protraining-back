using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseImagesAndMuscles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "images",
                table: "exercises",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "primary_muscles",
                table: "exercises",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "secondary_muscles",
                table: "exercises",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "images",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "primary_muscles",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "secondary_muscles",
                table: "exercises");
        }
    }
}
