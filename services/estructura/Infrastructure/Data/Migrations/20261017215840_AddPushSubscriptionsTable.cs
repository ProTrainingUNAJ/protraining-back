using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPushSubscriptionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    athlete_id = table.Column<Guid>(type: "uuid", nullable: true),
                    endpoint = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    auth = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    last_delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    pf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_athlete_id",
                table: "push_subscriptions",
                column: "athlete_id");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_pf_id",
                table: "push_subscriptions",
                column: "pf_id");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_user_id",
                table: "push_subscriptions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscriptions");
        }
    }
}
