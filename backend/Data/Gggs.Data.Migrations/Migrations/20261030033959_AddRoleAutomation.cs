using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_UserRole_Assignment",
                schema: "iam",
                table: "UserRole");

            migrationBuilder.AddColumn<bool>(
                name: "IsAutomatic",
                schema: "iam",
                table: "UserRole",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "AutoAssign",
                schema: "iam",
                table: "Role",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateIndex(
                name: "UX_UserRole_Assignment",
                schema: "iam",
                table: "UserRole",
                columns: new[] { "TenantId", "UserId", "RoleId", "ScopeType", "ScopeId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ValidTo] IS NULL");

            // Existing tenants' built-in Manager and Department Head roles follow the org chart from
            // now on, as new tenants' do (RoleTemplates). The nightly sync makes the grants.
            migrationBuilder.Sql("UPDATE [iam].[Role] SET [AutoAssign] = 1 WHERE [TemplateCode] = N'TEAM_LEAD' AND [IsDeleted] = 0;");
            migrationBuilder.Sql("UPDATE [iam].[Role] SET [AutoAssign] = 2 WHERE [TemplateCode] = N'DEPARTMENT_MANAGER' AND [IsDeleted] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_UserRole_Assignment",
                schema: "iam",
                table: "UserRole");

            migrationBuilder.DropColumn(
                name: "IsAutomatic",
                schema: "iam",
                table: "UserRole");

            migrationBuilder.DropColumn(
                name: "AutoAssign",
                schema: "iam",
                table: "Role");

            migrationBuilder.CreateIndex(
                name: "UX_UserRole_Assignment",
                schema: "iam",
                table: "UserRole",
                columns: new[] { "TenantId", "UserId", "RoleId", "ScopeType", "ScopeId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
