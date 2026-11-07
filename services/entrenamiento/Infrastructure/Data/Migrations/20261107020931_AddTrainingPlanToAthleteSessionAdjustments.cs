using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingPlanToAthleteSessionAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "training_plan_id",
                table: "athlete_session_adjustments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_athlete_session_adjustments_plan_athlete",
                table: "athlete_session_adjustments",
                columns: new[] { "training_plan_id", "athlete_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_athlete_session_adjustments_plan_athlete",
                table: "athlete_session_adjustments");

            migrationBuilder.DropColumn(
                name: "training_plan_id",
                table: "athlete_session_adjustments");
        }
    }
}
