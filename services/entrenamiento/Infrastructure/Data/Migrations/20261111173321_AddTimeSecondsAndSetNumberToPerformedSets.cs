using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeSecondsAndSetNumberToPerformedSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "target_distance_meters",
                table: "prescribed_exercises",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "target_time_seconds",
                table: "prescribed_exercises",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "set_number",
                table: "performed_sets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "time_seconds",
                table: "performed_sets",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "target_distance_meters",
                table: "prescribed_exercises");

            migrationBuilder.DropColumn(
                name: "target_time_seconds",
                table: "prescribed_exercises");

            migrationBuilder.DropColumn(
                name: "set_number",
                table: "performed_sets");

            migrationBuilder.DropColumn(
                name: "time_seconds",
                table: "performed_sets");
        }
    }
}
