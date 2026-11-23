using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectionReasonAndUniqueIndexToAthleteSessionAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_athlete_session_adjustments_session_athlete_date",
                table: "athlete_session_adjustments");

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "athlete_session_adjustments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            // Migrar motivos de rechazo previos almacenados en 'note' hacia 'rejection_reason'.
            migrationBuilder.Sql(@"
                UPDATE athlete_session_adjustments
                SET rejection_reason = note,
                    note = NULL
                WHERE status = 'Rejected'
                  AND rejection_reason IS NULL
                  AND note IS NOT NULL;
            ");

            // Deduplicar eventuales registros activos repetidos antes de aplicar el índice único.
            migrationBuilder.Sql(@"
                UPDATE athlete_session_adjustments
                SET deleted_at = CURRENT_TIMESTAMP
                WHERE id IN (
                    SELECT id
                    FROM (
                        SELECT id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY training_session_id, athlete_id, date
                                   ORDER BY updated_at DESC, created_at DESC, id DESC
                               ) AS rn
                        FROM athlete_session_adjustments
                        WHERE deleted_at IS NULL
                    ) ranked
                    WHERE rn > 1
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_athlete_session_adjustments_session_athlete_date",
                table: "athlete_session_adjustments",
                columns: new[] { "training_session_id", "athlete_id", "date" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_athlete_session_adjustments_session_athlete_date",
                table: "athlete_session_adjustments");

            // Restaurar motivos de rechazo hacia 'note' antes de eliminar 'rejection_reason' para rollback simétrico.
            migrationBuilder.Sql(@"
                UPDATE athlete_session_adjustments
                SET note = rejection_reason
                WHERE status = 'Rejected'
                  AND note IS NULL
                  AND rejection_reason IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "athlete_session_adjustments");

            migrationBuilder.CreateIndex(
                name: "IX_athlete_session_adjustments_session_athlete_date",
                table: "athlete_session_adjustments",
                columns: new[] { "training_session_id", "athlete_id", "date" });
        }
    }
}
