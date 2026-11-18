using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PlatformConsole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RenewsOn",
                schema: "platform",
                table: "Subscription",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformAuditEntry",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAuditEntry", x => x.Id);
                },
                comment: "Append-only. What platform staff did in the console; written only by PlatformAuditWriter.");

            migrationBuilder.CreateTable(
                name: "TenantUsage",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeasuredDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Employees = table.Column<int>(type: "int", nullable: false),
                    Users = table.Column<int>(type: "int", nullable: false),
                    SignedInToday = table.Column<int>(type: "int", nullable: false),
                    LastSignIn = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    EmailsSentToday = table.Column<int>(type: "int", nullable: false),
                    EmailsFailedToday = table.Column<int>(type: "int", nullable: false),
                    EmailsLoggedToday = table.Column<int>(type: "int", nullable: false),
                    EmailChannel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SignIn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantUsage_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "platform",
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditEntry_OccurredDate",
                schema: "platform",
                table: "PlatformAuditEntry",
                column: "OccurredDate");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditEntry_TenantId_OccurredDate",
                schema: "platform",
                table: "PlatformAuditEntry",
                columns: new[] { "TenantId", "OccurredDate" });

            migrationBuilder.CreateIndex(
                name: "UX_TenantUsage_TenantId",
                schema: "platform",
                table: "TenantUsage",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformAuditEntry",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantUsage",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "RenewsOn",
                schema: "platform",
                table: "Subscription");
        }
    }
}
