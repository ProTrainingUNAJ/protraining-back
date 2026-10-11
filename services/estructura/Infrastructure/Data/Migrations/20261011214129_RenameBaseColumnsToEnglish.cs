using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameBaseColumnsToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "creado_en",
                table: "digital_consents",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "borrado_en",
                table: "digital_consents",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "creado_en",
                table: "athletes",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "borrado_en",
                table: "athletes",
                newName: "deleted_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "digital_consents",
                newName: "borrado_en");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "digital_consents",
                newName: "creado_en");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "athletes",
                newName: "borrado_en");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "athletes",
                newName: "creado_en");
        }
    }
}
