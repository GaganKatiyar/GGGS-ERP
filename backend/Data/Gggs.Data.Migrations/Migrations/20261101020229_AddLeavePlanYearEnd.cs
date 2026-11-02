using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddLeavePlanYearEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GrantsItsDays",
                schema: "lv",
                table: "LeaveRequest",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CarryForwardExpiryMonths",
                schema: "lv",
                table: "LeavePlanItem",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "CreditTiming",
                schema: "lv",
                table: "LeavePlanItem",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<decimal>(
                name: "DocumentRequiredAboveDays",
                schema: "lv",
                table: "LeavePlanItem",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EncashAtYearEnd",
                schema: "lv",
                table: "LeavePlanItem",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "GrantPerOccurrence",
                schema: "lv",
                table: "LeavePlanItem",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JoiningCutoffDay",
                schema: "lv",
                table: "LeavePlanItem",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxEncashDays",
                schema: "lv",
                table: "LeavePlanItem",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxOccurrencesInEmployment",
                schema: "lv",
                table: "LeavePlanItem",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProrateOnJoining",
                schema: "lv",
                table: "LeavePlanItem",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "LeaveRequestAttachment",
                schema: "lv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequestAttachment", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_LeaveRequestAttachment_LeaveRequest_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "lv",
                        principalTable: "LeaveRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestAttachment_RequestId",
                schema: "lv",
                table: "LeaveRequestAttachment",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestAttachment_TenantId_Request",
                schema: "lv",
                table: "LeaveRequestAttachment",
                columns: new[] { "TenantId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaveRequestAttachment_TenantId_Id",
                schema: "lv",
                table: "LeaveRequestAttachment",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // New tenant-owned tables (lv): the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names these tables, so it goes first; it is restored below without them.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "LeaveRequestAttachment",
                schema: "lv");

            migrationBuilder.DropColumn(
                name: "GrantsItsDays",
                schema: "lv",
                table: "LeaveRequest");

            migrationBuilder.DropColumn(
                name: "CarryForwardExpiryMonths",
                schema: "lv",
                table: "LeavePlanItem");

            migrationBuilder.DropColumn(
                name: "CreditTiming",
                schema: "lv",
                table: "LeavePlanItem");

            migrationBuilder.DropColumn(
                name: "DocumentRequiredAboveDays",
                schema: "lv",
                table: "LeavePlanItem");

            migrationBuilder.DropColumn(
                name: "EncashAtYearEnd",
                schema: "lv",
                table: "LeavePlanItem");

            migrationBuilder.DropColumn(
                name: "GrantPerOccurrence",
                schema: "lv",
                table: "LeavePlanItem");

            migrationBuilder.DropColumn(
                name: "JoiningCutoffDay",
                schema: "lv",
                table: "LeavePlanItem");

            migrationBuilder.DropColumn(
                name: "MaxEncashDays",
                schema: "lv",
                table: "LeavePlanItem");

            migrationBuilder.DropColumn(
                name: "MaxOccurrencesInEmployment",
                schema: "lv",
                table: "LeavePlanItem");

            migrationBuilder.DropColumn(
                name: "ProrateOnJoining",
                schema: "lv",
                table: "LeavePlanItem");

            // The policy as it stood before these tables existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddLeavePlans().TargetModel);
        }
    }
}
