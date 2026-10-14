using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "check_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateTime>(type: "date", nullable: false),
                    traffic_light = table.Column<int>(type: "integer", nullable: false),
                    traffic_light_reason = table.Column<string>(type: "text", nullable: true),
                    sleep_hours = table.Column<double>(type: "double precision", nullable: false),
                    sleep_quality = table.Column<int>(type: "integer", nullable: false),
                    muscle_soreness = table.Column<int>(type: "integer", nullable: false),
                    fatigue_level = table.Column<int>(type: "integer", nullable: false),
                    mood_rate = table.Column<int>(type: "integer", nullable: false),
                    stress_level = table.Column<int>(type: "integer", nullable: false),
                    is_sick = table.Column<bool>(type: "boolean", nullable: false),
                    effort_rate = table.Column<int>(type: "integer", nullable: true),
                    pain_zones = table.Column<string>(type: "text", nullable: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_check_ins", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_check_ins_athlete_id_date",
                table: "check_ins",
                columns: new[] { "athlete_id", "date" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_check_ins_pf_id",
                table: "check_ins",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "check_ins");
        }
    }
}
