using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingPlanHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "training_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    objective = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training_weeks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_number = table.Column<int>(type: "integer", nullable: false),
                    training_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_weeks", x => x.id);
                    table.ForeignKey(
                        name: "FK_training_weeks_training_plans_training_plan_id",
                        column: x => x.training_plan_id,
                        principalTable: "training_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "training_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    training_week_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_training_sessions_training_weeks_training_week_id",
                        column: x => x.training_week_id,
                        principalTable: "training_weeks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prescribed_exercises",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    sets = table.Column<int>(type: "integer", nullable: false),
                    reps = table.Column<int>(type: "integer", nullable: false),
                    target_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    training_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prescribed_exercises", x => x.id);
                    table.ForeignKey(
                        name: "FK_prescribed_exercises_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prescribed_exercises_training_sessions_training_session_id",
                        column: x => x.training_session_id,
                        principalTable: "training_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_prescribed_exercises_exercise_id",
                table: "prescribed_exercises",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "IX_prescribed_exercises_pf_id",
                table: "prescribed_exercises",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_prescribed_exercises_training_session_id",
                table: "prescribed_exercises",
                column: "training_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_training_plans_pf_id",
                table: "training_plans",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_training_sessions_pf_id",
                table: "training_sessions",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_training_sessions_training_week_id",
                table: "training_sessions",
                column: "training_week_id");

            migrationBuilder.CreateIndex(
                name: "IX_training_weeks_pf_id",
                table: "training_weeks",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_training_weeks_training_plan_id",
                table: "training_weeks",
                column: "training_plan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prescribed_exercises");

            migrationBuilder.DropTable(
                name: "training_sessions");

            migrationBuilder.DropTable(
                name: "training_weeks");

            migrationBuilder.DropTable(
                name: "training_plans");
        }
    }
}
