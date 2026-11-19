using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAthleteVariantsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "athlete_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    substitute_exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_athlete_variants", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_athlete_variants_athlete_original_status",
                table: "athlete_variants",
                columns: new[] { "athlete_id", "original_exercise_id", "is_approved", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_athlete_variants_pf_id",
                table: "athlete_variants",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "athlete_variants");
        }
    }
}
