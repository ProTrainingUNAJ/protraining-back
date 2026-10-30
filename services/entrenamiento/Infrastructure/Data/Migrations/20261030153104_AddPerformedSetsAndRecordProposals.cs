using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformedSetsAndRecordProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "performed_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    reps = table.Column<int>(type: "integer", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_performed_sets", x => x.id);
                    table.ForeignKey(
                        name: "FK_performed_sets_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_performed_sets_training_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "training_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "record_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_weight = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    proposed_weight = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    performed_weight = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    performed_reps = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_proposals", x => x.id);
                    table.ForeignKey(
                        name: "FK_record_proposals_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_performed_sets_athlete_id_date",
                table: "performed_sets",
                columns: new[] { "athlete_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_performed_sets_exercise_id",
                table: "performed_sets",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "IX_performed_sets_pf_id",
                table: "performed_sets",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_performed_sets_session_id",
                table: "performed_sets",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_record_proposals_athlete_id_exercise_id_status",
                table: "record_proposals",
                columns: new[] { "athlete_id", "exercise_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_record_proposals_exercise_id",
                table: "record_proposals",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "IX_record_proposals_pf_id",
                table: "record_proposals",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "performed_sets");

            migrationBuilder.DropTable(
                name: "record_proposals");
        }
    }
}
