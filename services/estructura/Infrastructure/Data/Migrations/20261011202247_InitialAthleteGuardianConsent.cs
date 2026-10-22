using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    public partial class InitialAthleteGuardianConsent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "athletes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_minor = table.Column<bool>(type: "boolean", nullable: false),
                    emergency_contact = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    medical_conditions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    borrado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_athletes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "digital_consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_signed = table.Column<bool>(type: "boolean", nullable: false),
                    consent_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_minor = table.Column<bool>(type: "boolean", nullable: false),
                    guardian_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    guardian_email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    guardian_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    allows_physical_training = table.Column<bool>(type: "boolean", nullable: false),
                    allows_habits_recommendations = table.Column<bool>(type: "boolean", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    borrado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_digital_consents", x => x.id);
                    table.ForeignKey(
                        name: "FK_digital_consents_athletes_athlete_id",
                        column: x => x.athlete_id,
                        principalTable: "athletes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_athletes_pf_id",
                table: "athletes",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_digital_consents_athlete_id",
                table: "digital_consents",
                column: "athlete_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_digital_consents_pf_id",
                table: "digital_consents",
                column: "pf_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "digital_consents");

            migrationBuilder.DropTable(
                name: "athletes");
        }
    }
}
