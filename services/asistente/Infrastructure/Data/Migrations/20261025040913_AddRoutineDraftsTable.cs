using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutineDraftsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "routine_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    proposed_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    justification_summary = table.Column<string>(type: "text", nullable: false),
                    provider_used = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    exercises_json = table.Column<string>(type: "text", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routine_drafts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_routine_drafts_athlete_id",
                table: "routine_drafts",
                column: "athlete_id");

            migrationBuilder.CreateIndex(
                name: "IX_routine_drafts_created_at",
                table: "routine_drafts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_routine_drafts_pf_id",
                table: "routine_drafts",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routine_drafts");
        }
    }
}
