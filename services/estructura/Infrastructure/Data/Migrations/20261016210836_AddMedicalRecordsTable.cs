using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalRecordsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medical_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medical_history = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    surgeries = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pathologies = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    has_physical_fitness_certificate = table.Column<bool>(type: "boolean", nullable: false),
                    physical_fitness_issued_at = table.Column<DateOnly>(type: "date", nullable: true),
                    physical_fitness_expires_at = table.Column<DateOnly>(type: "date", nullable: true),
                    physical_fitness_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_medical_records_athletes_athlete_id",
                        column: x => x.athlete_id,
                        principalTable: "athletes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medical_records_athlete_id",
                table: "medical_records",
                column: "athlete_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medical_records_pf_id",
                table: "medical_records",
                column: "pf_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medical_records");
        }
    }
}
