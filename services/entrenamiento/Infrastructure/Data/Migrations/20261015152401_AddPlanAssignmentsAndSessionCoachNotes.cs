using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanAssignmentsAndSessionCoachNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_training_weeks_training_plan_id",
                table: "training_weeks");

            migrationBuilder.DropIndex(
                name: "IX_training_sessions_training_week_id",
                table: "training_sessions");

            migrationBuilder.DropColumn(
                name: "athlete_id",
                table: "training_plans");

            migrationBuilder.AddColumn<string>(
                name: "coach_notes",
                table: "training_sessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "day_of_week",
                table: "training_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "objective",
                table: "training_plans",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.CreateTable(
                name: "plan_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    training_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_assignments_training_plans_training_plan_id",
                        column: x => x.training_plan_id,
                        principalTable: "training_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_training_weeks_training_plan_id_week_number",
                table: "training_weeks",
                columns: new[] { "training_plan_id", "week_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_training_sessions_training_week_id_order_index",
                table: "training_sessions",
                columns: new[] { "training_week_id", "order_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plan_assignments_athlete_id",
                table: "plan_assignments",
                column: "athlete_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_assignments_category_id",
                table: "plan_assignments",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_assignments_pf_id",
                table: "plan_assignments",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_assignments_training_plan_id",
                table: "plan_assignments",
                column: "training_plan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_assignments");

            migrationBuilder.DropIndex(
                name: "IX_training_weeks_training_plan_id_week_number",
                table: "training_weeks");

            migrationBuilder.DropIndex(
                name: "IX_training_sessions_training_week_id_order_index",
                table: "training_sessions");

            migrationBuilder.DropColumn(
                name: "coach_notes",
                table: "training_sessions");

            migrationBuilder.DropColumn(
                name: "day_of_week",
                table: "training_sessions");

            migrationBuilder.AlterColumn<string>(
                name: "objective",
                table: "training_plans",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "athlete_id",
                table: "training_plans",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_training_weeks_training_plan_id",
                table: "training_weeks",
                column: "training_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_training_sessions_training_week_id",
                table: "training_sessions",
                column: "training_week_id");
        }
    }
}
