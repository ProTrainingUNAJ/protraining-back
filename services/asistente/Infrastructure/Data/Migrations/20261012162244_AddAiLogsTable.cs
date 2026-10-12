using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiLogsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: true),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    response = table.Column<string>(type: "text", nullable: true),
                    latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    total_tokens = table.Column<int>(type: "integer", nullable: true),
                    is_valid = table.Column<bool>(type: "boolean", nullable: false),
                    validation_result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_logs_athlete_id",
                table: "ai_logs",
                column: "athlete_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_logs_created_at",
                table: "ai_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_ai_logs_pf_id",
                table: "ai_logs",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_logs_provider",
                table: "ai_logs",
                column: "provider");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_logs");
        }
    }
}
