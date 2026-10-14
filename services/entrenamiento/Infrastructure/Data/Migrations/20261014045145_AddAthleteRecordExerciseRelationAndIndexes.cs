using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAthleteRecordExerciseRelationAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_athlete_records_athlete_id_exercise_id",
                table: "athlete_records",
                columns: new[] { "athlete_id", "exercise_id" },
                unique: true,
                filter: "is_active = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_athlete_records_exercise_id",
                table: "athlete_records",
                column: "exercise_id");

            migrationBuilder.AddForeignKey(
                name: "FK_athlete_records_exercises_exercise_id",
                table: "athlete_records",
                column: "exercise_id",
                principalTable: "exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_athlete_records_exercises_exercise_id",
                table: "athlete_records");

            migrationBuilder.DropIndex(
                name: "IX_athlete_records_athlete_id_exercise_id",
                table: "athlete_records");

            migrationBuilder.DropIndex(
                name: "IX_athlete_records_exercise_id",
                table: "athlete_records");
        }
    }
}
