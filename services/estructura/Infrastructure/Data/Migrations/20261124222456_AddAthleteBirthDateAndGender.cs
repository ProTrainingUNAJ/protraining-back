using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAthleteBirthDateAndGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "birth_date",
                table: "athletes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "athletes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "birth_date",
                table: "athletes");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "athletes");
        }
    }
}
