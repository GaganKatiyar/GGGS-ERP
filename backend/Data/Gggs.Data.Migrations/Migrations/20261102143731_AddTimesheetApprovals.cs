using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheetApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerEmployeeId",
                schema: "wf",
                table: "WorkflowInstance",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalId",
                schema: "ts",
                table: "TimesheetEntry",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TimesheetApproval",
                schema: "ts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimesheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BillableHours = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DecidedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
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
                    table.PrimaryKey("PK_TimesheetApproval", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_TimesheetApproval_Timesheet_TimesheetId",
                        column: x => x.TimesheetId,
                        principalSchema: "ts",
                        principalTable: "Timesheet",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetApproval_TenantId_Owner_Status",
                schema: "ts",
                table: "TimesheetApproval",
                columns: new[] { "TenantId", "OwnerEmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetApproval_TenantId_Project_Status",
                schema: "ts",
                table: "TimesheetApproval",
                columns: new[] { "TenantId", "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetApproval_TimesheetId",
                schema: "ts",
                table: "TimesheetApproval",
                column: "TimesheetId");

            migrationBuilder.CreateIndex(
                name: "UX_TimesheetApproval_TenantId_Id",
                schema: "ts",
                table: "TimesheetApproval",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TimesheetApproval_TenantId_Timesheet_Project",
                schema: "ts",
                table: "TimesheetApproval",
                columns: new[] { "TenantId", "TimesheetId", "ProjectId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // A new tenant-owned table (ts): the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names this table, so it goes first; it is restored below without it.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "TimesheetApproval",
                schema: "ts");

            migrationBuilder.DropColumn(
                name: "OwnerEmployeeId",
                schema: "wf",
                table: "WorkflowInstance");

            migrationBuilder.DropColumn(
                name: "ApprovalId",
                schema: "ts",
                table: "TimesheetEntry");

            // The policy as it stood before this table existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddDefaultProjects().TargetModel);
        }
    }
}
