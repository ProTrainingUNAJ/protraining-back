using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountIdToPendingPurges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "pending_purges",
                type: "uuid",
                nullable: true);

            // Backfill: las purgas pendientes creadas antes de esta migración no guardaron el AccountId.
            // Se completa desde el atleta (el registro sigue en la base hasta que se purga) para que
            // el job pueda borrar también la cuenta en Identidad. Se ignora el borrado lógico a propósito.
            migrationBuilder.Sql(@"
                UPDATE pending_purges AS p
                SET account_id = a.account_id
                FROM athletes AS a
                WHERE a.id = p.athlete_id
                  AND p.account_id IS NULL
                  AND a.account_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "account_id",
                table: "pending_purges");
        }
    }
}
