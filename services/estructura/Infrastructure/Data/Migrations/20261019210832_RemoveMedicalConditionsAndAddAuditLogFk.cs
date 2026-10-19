using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMedicalConditionsAndAddAuditLogFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "medical_conditions",
                table: "athletes");

            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.AddForeignKey(
                    name: "FK_medical_record_audit_logs_athletes_athlete_id",
                    table: "medical_record_audit_logs",
                    column: "athlete_id",
                    principalTable: "athletes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_medical_record_audit_logs_athletes_athlete_id",
                    table: "medical_record_audit_logs");
            }

            migrationBuilder.AddColumn<string>(
                name: "medical_conditions",
                table: "athletes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }
    }
}
