using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEngagementModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyRate",
                schema: "prj",
                table: "ProjectTeamMember",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptanceEvidence",
                schema: "prj",
                table: "ProjectMilestone",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedBy",
                schema: "prj",
                table: "ProjectMilestone",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AcceptedOn",
                schema: "prj",
                table: "ProjectMilestone",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAcceptance",
                schema: "prj",
                table: "ProjectMilestone",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CapAlertedPercent",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "IncludedHoursPerMonth",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NotToExceed",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OverageRate",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PoNumber",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PoValue",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RolloverPercent",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BaselineEndDate",
                schema: "prj",
                table: "Project",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaselineHours",
                schema: "prj",
                table: "Project",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BaselinedOn",
                schema: "prj",
                table: "Project",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectTemplate",
                schema: "prj",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BillingType = table.Column<byte>(type: "tinyint", nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_ProjectTemplate", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTemplate_TenantId_Code",
                schema: "prj",
                table: "ProjectTemplate",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTemplate_TenantId_Id",
                schema: "prj",
                table: "ProjectTemplate",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // A new tenant-owned table (prj): the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names this table, so it goes first; it is restored below without it.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "ProjectTemplate",
                schema: "prj");

            migrationBuilder.DropColumn(
                name: "MonthlyRate",
                schema: "prj",
                table: "ProjectTeamMember");

            migrationBuilder.DropColumn(
                name: "AcceptanceEvidence",
                schema: "prj",
                table: "ProjectMilestone");

            migrationBuilder.DropColumn(
                name: "AcceptedBy",
                schema: "prj",
                table: "ProjectMilestone");

            migrationBuilder.DropColumn(
                name: "AcceptedOn",
                schema: "prj",
                table: "ProjectMilestone");

            migrationBuilder.DropColumn(
                name: "RequiresAcceptance",
                schema: "prj",
                table: "ProjectMilestone");

            migrationBuilder.DropColumn(
                name: "CapAlertedPercent",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "IncludedHoursPerMonth",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "NotToExceed",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "OverageRate",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "PoNumber",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "PoValue",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "RolloverPercent",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "BaselineEndDate",
                schema: "prj",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "BaselineHours",
                schema: "prj",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "BaselinedOn",
                schema: "prj",
                table: "Project");

            // The policy as it stood before this table existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddTimesheetApprovals().TargetModel);
        }
    }
}
