using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GardenLeave",
                schema: "sep",
                table: "Case",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                schema: "sep",
                table: "Case",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Regretted",
                schema: "sep",
                table: "Case",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReleaseWithBuyout",
                schema: "sep",
                table: "Case",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedReleaseDay",
                schema: "sep",
                table: "Case",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetentionNote",
                schema: "sep",
                table: "Case",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryReasonCode",
                schema: "sep",
                table: "Case",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CaseEvent",
                schema: "sep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
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
                    table.PrimaryKey("PK_CaseEvent", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_CaseEvent_Case_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "sep",
                        principalTable: "Case",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoticeRule",
                schema: "sep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmploymentTypeCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    OnProbation = table.Column<bool>(type: "bit", nullable: true),
                    NoticeDays = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_NoticeRule", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "Policy",
                schema: "sep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultNoticeDays = table.Column<int>(type: "int", nullable: false),
                    ProbationNoticeDays = table.Column<int>(type: "int", nullable: false),
                    BuyoutAllowed = table.Column<bool>(type: "bit", nullable: false),
                    GardenLeaveAllowed = table.Column<bool>(type: "bit", nullable: false),
                    BlockedLeaveTypeCodes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EncashableLeaveTypeCodes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EncashmentCapDays = table.Column<int>(type: "int", nullable: true),
                    RetirementAge = table.Column<int>(type: "int", nullable: false),
                    AbscondingDays = table.Column<int>(type: "int", nullable: false),
                    SettlementDueWorkingDays = table.Column<int>(type: "int", nullable: false),
                    GratuityDueDays = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Policy", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseEvent_CaseId",
                schema: "sep",
                table: "CaseEvent",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseEvent_TenantId_Case",
                schema: "sep",
                table: "CaseEvent",
                columns: new[] { "TenantId", "CaseId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CaseEvent_TenantId_Id",
                schema: "sep",
                table: "CaseEvent",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticeRule_TenantId",
                schema: "sep",
                table: "NoticeRule",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_NoticeRule_TenantId_Id",
                schema: "sep",
                table: "NoticeRule",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_Policy_TenantId",
                schema: "sep",
                table: "Policy",
                column: "TenantId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Policy_TenantId_Id",
                schema: "sep",
                table: "Policy",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // New tenant-owned tables (sep): the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names these tables, so it goes first; it is restored below without them.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "CaseEvent",
                schema: "sep");

            migrationBuilder.DropTable(
                name: "NoticeRule",
                schema: "sep");

            migrationBuilder.DropTable(
                name: "Policy",
                schema: "sep");

            migrationBuilder.DropColumn(
                name: "GardenLeave",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "Regretted",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "ReleaseWithBuyout",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "RequestedReleaseDay",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "RetentionNote",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "SecondaryReasonCode",
                schema: "sep",
                table: "Case");

            // The policy as it stood before these tables existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddSecurity().TargetModel);
        }
    }
}
