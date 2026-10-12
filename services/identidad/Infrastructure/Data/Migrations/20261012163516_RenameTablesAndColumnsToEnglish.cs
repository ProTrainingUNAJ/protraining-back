using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameTablesAndColumnsToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_roles_claims_roles_RoleId",
                schema: "db_identity",
                table: "roles_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_usuarios_claims_usuarios_UserId",
                schema: "db_identity",
                table: "usuarios_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_usuarios_logins_usuarios_UserId",
                schema: "db_identity",
                table: "usuarios_logins");

            migrationBuilder.DropForeignKey(
                name: "FK_usuarios_roles_roles_RoleId",
                schema: "db_identity",
                table: "usuarios_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_usuarios_roles_usuarios_UserId",
                schema: "db_identity",
                table: "usuarios_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_usuarios_tokens_usuarios_UserId",
                schema: "db_identity",
                table: "usuarios_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_usuarios_tokens",
                schema: "db_identity",
                table: "usuarios_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_usuarios_roles",
                schema: "db_identity",
                table: "usuarios_roles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_usuarios_logins",
                schema: "db_identity",
                table: "usuarios_logins");

            migrationBuilder.DropPrimaryKey(
                name: "PK_usuarios_claims",
                schema: "db_identity",
                table: "usuarios_claims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_usuarios",
                schema: "db_identity",
                table: "usuarios");

            migrationBuilder.DropPrimaryKey(
                name: "PK_roles_claims",
                schema: "db_identity",
                table: "roles_claims");

            migrationBuilder.RenameTable(
                name: "usuarios_tokens",
                schema: "db_identity",
                newName: "user_tokens",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "usuarios_roles",
                schema: "db_identity",
                newName: "user_roles",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "usuarios_logins",
                schema: "db_identity",
                newName: "user_logins",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "usuarios_claims",
                schema: "db_identity",
                newName: "user_claims",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "usuarios",
                schema: "db_identity",
                newName: "users",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "roles_claims",
                schema: "db_identity",
                newName: "role_claims",
                newSchema: "db_identity");

            migrationBuilder.RenameIndex(
                name: "IX_usuarios_roles_RoleId",
                schema: "db_identity",
                table: "user_roles",
                newName: "IX_user_roles_RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_usuarios_logins_UserId",
                schema: "db_identity",
                table: "user_logins",
                newName: "IX_user_logins_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_usuarios_claims_UserId",
                schema: "db_identity",
                table: "user_claims",
                newName: "IX_user_claims_UserId");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "db_identity",
                table: "users",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                schema: "db_identity",
                table: "users",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "FullName",
                schema: "db_identity",
                table: "users",
                newName: "full_name");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "db_identity",
                table: "users",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_roles_claims_RoleId",
                schema: "db_identity",
                table: "role_claims",
                newName: "IX_role_claims_RoleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_tokens",
                schema: "db_identity",
                table: "user_tokens",
                columns: new[] { "UserId", "LoginProvider", "Name" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_roles",
                schema: "db_identity",
                table: "user_roles",
                columns: new[] { "UserId", "RoleId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_logins",
                schema: "db_identity",
                table: "user_logins",
                columns: new[] { "LoginProvider", "ProviderKey" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_claims",
                schema: "db_identity",
                table: "user_claims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                schema: "db_identity",
                table: "users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_role_claims",
                schema: "db_identity",
                table: "role_claims",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_role_claims_roles_RoleId",
                schema: "db_identity",
                table: "role_claims",
                column: "RoleId",
                principalSchema: "db_identity",
                principalTable: "roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_claims_users_UserId",
                schema: "db_identity",
                table: "user_claims",
                column: "UserId",
                principalSchema: "db_identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_logins_users_UserId",
                schema: "db_identity",
                table: "user_logins",
                column: "UserId",
                principalSchema: "db_identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_roles_RoleId",
                schema: "db_identity",
                table: "user_roles",
                column: "RoleId",
                principalSchema: "db_identity",
                principalTable: "roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_users_UserId",
                schema: "db_identity",
                table: "user_roles",
                column: "UserId",
                principalSchema: "db_identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_tokens_users_UserId",
                schema: "db_identity",
                table: "user_tokens",
                column: "UserId",
                principalSchema: "db_identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_role_claims_roles_RoleId",
                schema: "db_identity",
                table: "role_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_user_claims_users_UserId",
                schema: "db_identity",
                table: "user_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_user_logins_users_UserId",
                schema: "db_identity",
                table: "user_logins");

            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_roles_RoleId",
                schema: "db_identity",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_users_UserId",
                schema: "db_identity",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_tokens_users_UserId",
                schema: "db_identity",
                table: "user_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                schema: "db_identity",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_tokens",
                schema: "db_identity",
                table: "user_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_roles",
                schema: "db_identity",
                table: "user_roles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_logins",
                schema: "db_identity",
                table: "user_logins");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_claims",
                schema: "db_identity",
                table: "user_claims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_role_claims",
                schema: "db_identity",
                table: "role_claims");

            migrationBuilder.RenameTable(
                name: "users",
                schema: "db_identity",
                newName: "usuarios",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "user_tokens",
                schema: "db_identity",
                newName: "usuarios_tokens",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "user_roles",
                schema: "db_identity",
                newName: "usuarios_roles",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "user_logins",
                schema: "db_identity",
                newName: "usuarios_logins",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "user_claims",
                schema: "db_identity",
                newName: "usuarios_claims",
                newSchema: "db_identity");

            migrationBuilder.RenameTable(
                name: "role_claims",
                schema: "db_identity",
                newName: "roles_claims",
                newSchema: "db_identity");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "db_identity",
                table: "usuarios",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "is_active",
                schema: "db_identity",
                table: "usuarios",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "full_name",
                schema: "db_identity",
                table: "usuarios",
                newName: "FullName");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "db_identity",
                table: "usuarios",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_user_roles_RoleId",
                schema: "db_identity",
                table: "usuarios_roles",
                newName: "IX_usuarios_roles_RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_user_logins_UserId",
                schema: "db_identity",
                table: "usuarios_logins",
                newName: "IX_usuarios_logins_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_user_claims_UserId",
                schema: "db_identity",
                table: "usuarios_claims",
                newName: "IX_usuarios_claims_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_role_claims_RoleId",
                schema: "db_identity",
                table: "roles_claims",
                newName: "IX_roles_claims_RoleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_usuarios",
                schema: "db_identity",
                table: "usuarios",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_usuarios_tokens",
                schema: "db_identity",
                table: "usuarios_tokens",
                columns: new[] { "UserId", "LoginProvider", "Name" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_usuarios_roles",
                schema: "db_identity",
                table: "usuarios_roles",
                columns: new[] { "UserId", "RoleId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_usuarios_logins",
                schema: "db_identity",
                table: "usuarios_logins",
                columns: new[] { "LoginProvider", "ProviderKey" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_usuarios_claims",
                schema: "db_identity",
                table: "usuarios_claims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_roles_claims",
                schema: "db_identity",
                table: "roles_claims",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_roles_claims_roles_RoleId",
                schema: "db_identity",
                table: "roles_claims",
                column: "RoleId",
                principalSchema: "db_identity",
                principalTable: "roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_usuarios_claims_usuarios_UserId",
                schema: "db_identity",
                table: "usuarios_claims",
                column: "UserId",
                principalSchema: "db_identity",
                principalTable: "usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_usuarios_logins_usuarios_UserId",
                schema: "db_identity",
                table: "usuarios_logins",
                column: "UserId",
                principalSchema: "db_identity",
                principalTable: "usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_usuarios_roles_roles_RoleId",
                schema: "db_identity",
                table: "usuarios_roles",
                column: "RoleId",
                principalSchema: "db_identity",
                principalTable: "roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_usuarios_roles_usuarios_UserId",
                schema: "db_identity",
                table: "usuarios_roles",
                column: "UserId",
                principalSchema: "db_identity",
                principalTable: "usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_usuarios_tokens_usuarios_UserId",
                schema: "db_identity",
                table: "usuarios_tokens",
                column: "UserId",
                principalSchema: "db_identity",
                principalTable: "usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
