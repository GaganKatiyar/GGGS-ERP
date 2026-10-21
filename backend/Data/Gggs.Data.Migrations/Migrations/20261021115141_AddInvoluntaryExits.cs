using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoluntaryExits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExitProposalLeadDays",
                schema: "sep",
                table: "Policy",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ShowCauseReplyDays",
                schema: "sep",
                table: "Policy",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ContractEndDate",
                schema: "emp",
                table: "Employee",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomineeContact",
                schema: "sep",
                table: "Case",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomineeName",
                schema: "sep",
                table: "Case",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomineeRelation",
                schema: "sep",
                table: "Case",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayInLieuDays",
                schema: "sep",
                table: "Case",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Proposed",
                schema: "sep",
                table: "Case",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AbsenceAlert",
                schema: "sep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstAbsentDay = table.Column<DateOnly>(type: "date", nullable: false),
                    LastWorkedDay = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkingDaysAbsent = table.Column<int>(type: "int", nullable: false),
                    DetectedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    NoticesSent = table.Column<int>(type: "int", nullable: false),
                    LastNoticeOn = table.Column<DateOnly>(type: "date", nullable: true),
                    RespondBy = table.Column<DateOnly>(type: "date", nullable: true),
                    LastLetterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SeparationCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_AbsenceAlert", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "ShowCause",
                schema: "sep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Allegation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReplyDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IssuedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NoticeLetterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reply = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RepliedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EnquiryOfficerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EnquiryFindings = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EnquiryCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DecidedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SeparationCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_ShowCause", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceAlert_TenantId_Employee",
                schema: "sep",
                table: "AbsenceAlert",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceAlert_TenantId_Status",
                schema: "sep",
                table: "AbsenceAlert",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_AbsenceAlert_TenantId_Id",
                schema: "sep",
                table: "AbsenceAlert",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_ShowCause_TenantId_Employee",
                schema: "sep",
                table: "ShowCause",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShowCause_TenantId_Status",
                schema: "sep",
                table: "ShowCause",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ShowCause_TenantId_Id",
                schema: "sep",
                table: "ShowCause",
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
                name: "AbsenceAlert",
                schema: "sep");

            migrationBuilder.DropTable(
                name: "ShowCause",
                schema: "sep");

            migrationBuilder.DropColumn(
                name: "ExitProposalLeadDays",
                schema: "sep",
                table: "Policy");

            migrationBuilder.DropColumn(
                name: "ShowCauseReplyDays",
                schema: "sep",
                table: "Policy");

            migrationBuilder.DropColumn(
                name: "ContractEndDate",
                schema: "emp",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "NomineeContact",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "NomineeName",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "NomineeRelation",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "PayInLieuDays",
                schema: "sep",
                table: "Case");

            migrationBuilder.DropColumn(
                name: "Proposed",
                schema: "sep",
                table: "Case");

            // The policy as it stood before these tables existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddSeparationOffboarding().TargetModel);
        }
    }
}
