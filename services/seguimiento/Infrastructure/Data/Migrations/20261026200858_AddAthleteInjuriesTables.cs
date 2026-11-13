using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAthleteInjuriesTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "athlete_injuries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    injury_date = table.Column<DateTime>(type: "date", nullable: false),
                    estimated_return_date = table.Column<DateTime>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cleared_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_athlete_injuries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "athlete_injury_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    injury_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_athlete_injury_status_changes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_athlete_injuries_athlete_id_status",
                table: "athlete_injuries",
                columns: new[] { "athlete_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_athlete_injuries_pf_id",
                table: "athlete_injuries",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_athlete_injury_status_changes_injury_id",
                table: "athlete_injury_status_changes",
                column: "injury_id");

            migrationBuilder.CreateIndex(
                name: "IX_athlete_injury_status_changes_pf_id",
                table: "athlete_injury_status_changes",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "athlete_injuries");

            migrationBuilder.DropTable(
                name: "athlete_injury_status_changes");
        }
    }
}
