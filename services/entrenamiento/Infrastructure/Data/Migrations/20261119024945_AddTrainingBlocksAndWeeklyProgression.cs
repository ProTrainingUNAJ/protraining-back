using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingBlocksAndWeeklyProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Crear la tabla de bloques
            migrationBuilder.CreateTable(
                name: "training_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    training_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_blocks", x => x.id);
                    table.ForeignKey(
                        name: "FK_training_blocks_training_sessions_training_session_id",
                        column: x => x.training_session_id,
                        principalTable: "training_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_training_blocks_pf_id",
                table: "training_blocks",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_training_blocks_training_session_id_order_index",
                table: "training_blocks",
                columns: new[] { "training_session_id", "order_index" },
                unique: true,
                filter: "deleted_at IS NULL");

            // 2. Modificar columnas y tipos en prescribed_exercises
            migrationBuilder.AlterColumn<string>(
                name: "reps",
                table: "prescribed_exercises",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "intensity_type",
                table: "prescribed_exercises",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "prescribed_exercises",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_rir",
                table: "prescribed_exercises",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "target_rpe",
                table: "prescribed_exercises",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            // 3. Agregar training_block_id temporalmente como nullable para permitir backfill
            migrationBuilder.AddColumn<Guid>(
                name: "training_block_id",
                table: "prescribed_exercises",
                type: "uuid",
                nullable: true);

            // 4. Migración de datos sin pérdida (KAN-585):
            // Crear bloque 'Principal' por cada sesión existente y reasignar los ejercicios
            migrationBuilder.Sql(@"
                INSERT INTO training_blocks (id, training_session_id, title, order_index, pf_id, created_at, deleted_at)
                SELECT gen_random_uuid(), id, 'Principal', 1, pf_id, created_at, deleted_at
                FROM training_sessions;

                UPDATE prescribed_exercises pe
                SET training_block_id = tb.id
                FROM training_blocks tb
                WHERE tb.training_session_id = pe.training_session_id;
            ");

            // 5. Establecer training_block_id como NOT NULL y agregar FK e índice
            migrationBuilder.AlterColumn<Guid>(
                name: "training_block_id",
                table: "prescribed_exercises",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_prescribed_exercises_training_block_id_order_index",
                table: "prescribed_exercises",
                columns: new[] { "training_block_id", "order_index" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_prescribed_exercises_training_blocks_training_block_id",
                table: "prescribed_exercises",
                column: "training_block_id",
                principalTable: "training_blocks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // 6. Hacer training_session_id nullable
            migrationBuilder.DropForeignKey(
                name: "FK_prescribed_exercises_training_sessions_training_session_id",
                table: "prescribed_exercises");

            migrationBuilder.AlterColumn<Guid>(
                name: "training_session_id",
                table: "prescribed_exercises",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_prescribed_exercises_training_sessions_training_session_id",
                table: "prescribed_exercises",
                column: "training_session_id",
                principalTable: "training_sessions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_prescribed_exercises_training_blocks_training_block_id",
                table: "prescribed_exercises");

            migrationBuilder.DropForeignKey(
                name: "FK_prescribed_exercises_training_sessions_training_session_id",
                table: "prescribed_exercises");

            // Restaurar training_session_id en prescribed_exercises antes de borrar bloques
            migrationBuilder.Sql(@"
                UPDATE prescribed_exercises pe
                SET training_session_id = tb.training_session_id
                FROM training_blocks tb
                WHERE tb.id = pe.training_block_id AND pe.training_session_id IS NULL;
            ");

            migrationBuilder.DropTable(
                name: "training_blocks");

            migrationBuilder.DropIndex(
                name: "IX_prescribed_exercises_training_block_id_order_index",
                table: "prescribed_exercises");

            migrationBuilder.DropColumn(
                name: "intensity_type",
                table: "prescribed_exercises");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "prescribed_exercises");

            migrationBuilder.DropColumn(
                name: "target_rir",
                table: "prescribed_exercises");

            migrationBuilder.DropColumn(
                name: "target_rpe",
                table: "prescribed_exercises");

            migrationBuilder.DropColumn(
                name: "training_block_id",
                table: "prescribed_exercises");

            migrationBuilder.AlterColumn<Guid>(
                name: "training_session_id",
                table: "prescribed_exercises",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "reps",
                table: "prescribed_exercises",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddForeignKey(
                name: "FK_prescribed_exercises_training_sessions_training_session_id",
                table: "prescribed_exercises",
                column: "training_session_id",
                principalTable: "training_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
