using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseRestrictionsAndSubstitutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exercise_restrictions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone = table.Column<string>(type: "text", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_pain_level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_restrictions", x => x.id);
                    table.ForeignKey(
                        name: "FK_exercise_restrictions_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exercise_substitutes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restriction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    substitute_exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_substitutes", x => x.id);
                    table.ForeignKey(
                        name: "FK_exercise_substitutes_exercise_restrictions_restriction_id",
                        column: x => x.restriction_id,
                        principalTable: "exercise_restrictions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exercise_substitutes_exercises_substitute_exercise_id",
                        column: x => x.substitute_exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_restrictions_exercise_id",
                table: "exercise_restrictions",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_restrictions_zone",
                table: "exercise_restrictions",
                column: "zone");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_restrictions_zone_exercise_id",
                table: "exercise_restrictions",
                columns: new[] { "zone", "exercise_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_substitutes_restriction_id_substitute_exercise_id",
                table: "exercise_substitutes",
                columns: new[] { "restriction_id", "substitute_exercise_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_substitutes_substitute_exercise_id",
                table: "exercise_substitutes",
                column: "substitute_exercise_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_substitutes");

            migrationBuilder.DropTable(
                name: "exercise_restrictions");
        }
    }
}
