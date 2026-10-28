using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanDraftsAndPfDecisionsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pf_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    suggested_exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    final_exercise_id = table.Column<Guid>(type: "uuid", nullable: true),
                    final_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pf_decisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: true),
                    goal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    approved_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_drafts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pf_decisions_athlete_id",
                table: "pf_decisions",
                column: "athlete_id");

            migrationBuilder.CreateIndex(
                name: "IX_pf_decisions_pf_id",
                table: "pf_decisions",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_pf_decisions_source_id",
                table: "pf_decisions",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_drafts_athlete_id",
                table: "plan_drafts",
                column: "athlete_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_drafts_category_id",
                table: "plan_drafts",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_drafts_pf_id",
                table: "plan_drafts",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pf_decisions");

            migrationBuilder.DropTable(
                name: "plan_drafts");
        }
    }
}
