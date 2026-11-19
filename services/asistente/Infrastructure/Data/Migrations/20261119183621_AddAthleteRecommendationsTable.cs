using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAthleteRecommendationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "athlete_recommendations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    recommendation_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    metrics_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_athlete_recommendations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_athlete_recommendations_athlete_status",
                table: "athlete_recommendations",
                columns: new[] { "athlete_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_athlete_recommendations_category_status",
                table: "athlete_recommendations",
                columns: new[] { "category_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_athlete_recommendations_pf_id",
                table: "athlete_recommendations",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "athlete_recommendations");
        }
    }
}
