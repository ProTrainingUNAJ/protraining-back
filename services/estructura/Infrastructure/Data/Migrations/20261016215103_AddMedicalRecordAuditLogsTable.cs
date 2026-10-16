using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalRecordAuditLogsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medical_record_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accessed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    justification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_record_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medical_record_audit_logs_athlete_id",
                table: "medical_record_audit_logs",
                column: "athlete_id");

            migrationBuilder.CreateIndex(
                name: "IX_medical_record_audit_logs_athlete_id_created_at",
                table: "medical_record_audit_logs",
                columns: new[] { "athlete_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_record_audit_logs_pf_id",
                table: "medical_record_audit_logs",
                column: "pf_id");

            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(
                    """
                    CREATE TRIGGER trg_medical_record_audit_logs_no_update
                    BEFORE UPDATE ON medical_record_audit_logs
                    BEGIN
                        SELECT RAISE(ABORT, 'Los registros de auditoría médica son inmutables y de solo inserción (append-only): no se permite ejecutar UPDATE sobre medical_record_audit_logs.');
                    END;

                    CREATE TRIGGER trg_medical_record_audit_logs_no_delete
                    BEFORE DELETE ON medical_record_audit_logs
                    BEGIN
                        SELECT RAISE(ABORT, 'Los registros de auditoría médica son inmutables y de solo inserción (append-only): no se permite ejecutar DELETE sobre medical_record_audit_logs.');
                    END;
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    CREATE OR REPLACE FUNCTION prevent_medical_record_audit_logs_modification()
                    RETURNS TRIGGER AS $$
                    BEGIN
                        RAISE EXCEPTION 'Los registros de auditoría médica son inmutables y de solo inserción (append-only): no se permite ejecutar % sobre la tabla %.', TG_OP, TG_TABLE_NAME
                            USING ERRCODE = 'integrity_constraint_violation';
                    END;
                    $$ LANGUAGE plpgsql;

                    CREATE TRIGGER trg_medical_record_audit_logs_append_only
                    BEFORE UPDATE OR DELETE ON medical_record_audit_logs
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_medical_record_audit_logs_modification();

                    CREATE TRIGGER trg_medical_record_audit_logs_no_truncate
                    BEFORE TRUNCATE ON medical_record_audit_logs
                    FOR EACH STATEMENT
                    EXECUTE FUNCTION prevent_medical_record_audit_logs_modification();
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(
                    """
                    DROP TRIGGER IF EXISTS trg_medical_record_audit_logs_no_update;
                    DROP TRIGGER IF EXISTS trg_medical_record_audit_logs_no_delete;
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    DROP TRIGGER IF EXISTS trg_medical_record_audit_logs_append_only ON medical_record_audit_logs;
                    DROP TRIGGER IF EXISTS trg_medical_record_audit_logs_no_truncate ON medical_record_audit_logs;
                    DROP FUNCTION IF EXISTS prevent_medical_record_audit_logs_modification();
                    """);
            }

            migrationBuilder.DropTable(
                name: "medical_record_audit_logs");
        }
    }
}
