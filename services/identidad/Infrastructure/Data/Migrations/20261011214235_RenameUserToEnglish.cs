using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameUserToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NombreCompleto",
                schema: "db_identity",
                table: "usuarios",
                newName: "FullName");

            migrationBuilder.RenameColumn(
                name: "CreadoEn",
                schema: "db_identity",
                table: "usuarios",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "ActualizadoEn",
                schema: "db_identity",
                table: "usuarios",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "Activo",
                schema: "db_identity",
                table: "usuarios",
                newName: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "db_identity",
                table: "usuarios",
                newName: "ActualizadoEn");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                schema: "db_identity",
                table: "usuarios",
                newName: "Activo");

            migrationBuilder.RenameColumn(
                name: "FullName",
                schema: "db_identity",
                table: "usuarios",
                newName: "NombreCompleto");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "db_identity",
                table: "usuarios",
                newName: "CreadoEn");
        }
    }
}
