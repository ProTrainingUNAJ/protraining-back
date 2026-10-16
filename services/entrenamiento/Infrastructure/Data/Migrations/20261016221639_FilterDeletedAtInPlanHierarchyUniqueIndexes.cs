using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FilterDeletedAtInPlanHierarchyUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_training_weeks_training_plan_id_week_number",
                table: "training_weeks");

            migrationBuilder.DropIndex(
                name: "IX_training_sessions_training_week_id_order_index",
                table: "training_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_training_weeks_training_plan_id_week_number",
                table: "training_weeks",
                columns: new[] { "training_plan_id", "week_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_training_sessions_training_week_id_order_index",
                table: "training_sessions",
                columns: new[] { "training_week_id", "order_index" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_training_weeks_training_plan_id_week_number",
                table: "training_weeks");

            migrationBuilder.DropIndex(
                name: "IX_training_sessions_training_week_id_order_index",
                table: "training_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_training_weeks_training_plan_id_week_number",
                table: "training_weeks",
                columns: new[] { "training_plan_id", "week_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_training_sessions_training_week_id_order_index",
                table: "training_sessions",
                columns: new[] { "training_week_id", "order_index" },
                unique: true);
        }
    }
}
