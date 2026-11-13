using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePurgasPendientesToPendingPurges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_purgas_pendientes",
                table: "purgas_pendientes");

            migrationBuilder.RenameTable(
                name: "purgas_pendientes",
                newName: "pending_purges");

            migrationBuilder.RenameIndex(
                name: "IX_purgas_pendientes_status",
                table: "pending_purges",
                newName: "IX_pending_purges_status");

            migrationBuilder.RenameIndex(
                name: "IX_purgas_pendientes_pf_id",
                table: "pending_purges",
                newName: "IX_pending_purges_pf_id");

            migrationBuilder.RenameIndex(
                name: "IX_purgas_pendientes_athlete_id",
                table: "pending_purges",
                newName: "IX_pending_purges_athlete_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_pending_purges",
                table: "pending_purges",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_pending_purges",
                table: "pending_purges");

            migrationBuilder.RenameTable(
                name: "pending_purges",
                newName: "purgas_pendientes");

            migrationBuilder.RenameIndex(
                name: "IX_pending_purges_status",
                table: "purgas_pendientes",
                newName: "IX_purgas_pendientes_status");

            migrationBuilder.RenameIndex(
                name: "IX_pending_purges_pf_id",
                table: "purgas_pendientes",
                newName: "IX_purgas_pendientes_pf_id");

            migrationBuilder.RenameIndex(
                name: "IX_pending_purges_athlete_id",
                table: "purgas_pendientes",
                newName: "IX_purgas_pendientes_athlete_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_purgas_pendientes",
                table: "purgas_pendientes",
                column: "id");
        }
    }
}
