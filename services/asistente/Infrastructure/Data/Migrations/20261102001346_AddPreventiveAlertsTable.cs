using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPreventiveAlertsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preventive_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    suggestion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    suggested_adjustment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    suggested_adjustment_percentage = table.Column<int>(type: "integer", nullable: true),
                    suggested_adjustment_payload = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    applied_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preventive_alerts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_preventive_alerts_athlete_id",
                table: "preventive_alerts",
                column: "athlete_id");

            migrationBuilder.CreateIndex(
                name: "IX_preventive_alerts_pf_id",
                table: "preventive_alerts",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_preventive_alerts_pf_status_generated",
                table: "preventive_alerts",
                columns: new[] { "pf_id", "status", "generated_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preventive_alerts");
        }
    }
}
