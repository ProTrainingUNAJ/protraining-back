using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_exercise_id",
                table: "exercises",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "variant_label",
                table: "exercises",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercises_parent_exercise_id",
                table: "exercises",
                column: "parent_exercise_id");

            migrationBuilder.AddForeignKey(
                name: "FK_exercises_exercises_parent_exercise_id",
                table: "exercises",
                column: "parent_exercise_id",
                principalTable: "exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_exercises_exercises_parent_exercise_id",
                table: "exercises");

            migrationBuilder.DropIndex(
                name: "IX_exercises_parent_exercise_id",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "parent_exercise_id",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "variant_label",
                table: "exercises");
        }
    }
}
