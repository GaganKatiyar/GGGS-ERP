using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireFromEveryone",
                schema: "ts",
                table: "TimesheetSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OpenToTeam",
                schema: "prj",
                table: "ProjectTask",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "UX_Project_TenantId_DefaultDepartment",
                schema: "prj",
                table: "Project",
                columns: new[] { "TenantId", "DepartmentId" },
                unique: true,
                filter: "[Type] = 10 AND [Status] IN (1, 2) AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Project_TenantId_DefaultDepartment",
                schema: "prj",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "RequireFromEveryone",
                schema: "ts",
                table: "TimesheetSettings");

            migrationBuilder.DropColumn(
                name: "OpenToTeam",
                schema: "prj",
                table: "ProjectTask");
        }
    }
}
