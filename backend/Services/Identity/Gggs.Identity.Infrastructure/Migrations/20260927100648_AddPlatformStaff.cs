using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformRole",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    PermissionCodes = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRole", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformRoleAssignment",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformStaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRoleAssignment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformStaff",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PasswordChangedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PermissionVersion = table.Column<int>(type: "int", nullable: false),
                    InvitationTokenHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InvitationTokenExpiresDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    PasswordHistory = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformStaff", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_PlatformRole_NormalizedName",
                schema: "iam",
                table: "PlatformRole",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRoleAssignment_PlatformRoleId",
                schema: "iam",
                table: "PlatformRoleAssignment",
                column: "PlatformRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRoleAssignment_PlatformStaffId",
                schema: "iam",
                table: "PlatformRoleAssignment",
                column: "PlatformStaffId");

            migrationBuilder.CreateIndex(
                name: "UX_PlatformRoleAssignment_Assignment",
                schema: "iam",
                table: "PlatformRoleAssignment",
                columns: new[] { "PlatformStaffId", "PlatformRoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlatformStaff_NormalizedEmail",
                schema: "iam",
                table: "PlatformStaff",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformRole",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "PlatformRoleAssignment",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "PlatformStaff",
                schema: "iam");
        }
    }
}
