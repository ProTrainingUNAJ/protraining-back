using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyLoadTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_loads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateTime>(type: "date", nullable: false),
                    gym_minutes = table.Column<int>(type: "integer", nullable: false),
                    gym_rpe = table.Column<int>(type: "integer", nullable: true),
                    sport_training_minutes = table.Column<int>(type: "integer", nullable: false),
                    sport_training_rpe = table.Column<int>(type: "integer", nullable: true),
                    match1_minutes = table.Column<int>(type: "integer", nullable: false),
                    match1_rpe = table.Column<int>(type: "integer", nullable: true),
                    match2_minutes = table.Column<int>(type: "integer", nullable: false),
                    match2_rpe = table.Column<int>(type: "integer", nullable: true),
                    individual_work_minutes = table.Column<int>(type: "integer", nullable: false),
                    individual_work_rpe = table.Column<int>(type: "integer", nullable: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_loads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_loads_athlete_id_date",
                table: "daily_loads",
                columns: new[] { "athlete_id", "date" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_daily_loads_pf_id",
                table: "daily_loads",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_loads");
        }
    }
}
