using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <summary>
    /// Columns take the screens' names too: RoleId, LevelId and CompanyId (were JobId, JobLevelId and
    /// LegalEntityId), with the proposed/new variants on promotions and revisions. Only names change.
    /// </summary>
    public partial class ScreenNamesForColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The tenant policy names these columns' tables; it is rebuilt at the end.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessUnit_Company_LegalEntityId",
                schema: "org",
                table: "BusinessUnit");

            migrationBuilder.DropForeignKey(
                name: "FK_CostCenter_Company_LegalEntityId",
                schema: "org",
                table: "CostCenter");

            migrationBuilder.DropForeignKey(
                name: "FK_Designation_Level_JobLevelId",
                schema: "org",
                table: "Designation");

            migrationBuilder.DropForeignKey(
                name: "FK_Designation_Role_JobId",
                schema: "org",
                table: "Designation");

            migrationBuilder.DropForeignKey(
                name: "FK_Location_Company_LegalEntityId",
                schema: "org",
                table: "Location");

            migrationBuilder.DropForeignKey(
                name: "FK_Role_Level_JobLevelId",
                schema: "org",
                table: "Role");

            migrationBuilder.RenameColumn(
                name: "JobId",
                schema: "wfp",
                table: "WorkforcePlanLine",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "JobLevelId",
                schema: "comp",
                table: "SalaryBand",
                newName: "LevelId");

            migrationBuilder.RenameColumn(
                name: "JobLevelId",
                schema: "org",
                table: "Role",
                newName: "LevelId");

            migrationBuilder.RenameIndex(
                name: "IX_Role_JobLevelId",
                schema: "org",
                table: "Role",
                newName: "IX_Role_LevelId");

            migrationBuilder.RenameColumn(
                name: "JobId",
                schema: "rec",
                table: "Requisition",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "ProposedJobLevelId",
                schema: "car",
                table: "Promotion",
                newName: "ProposedLevelId");

            migrationBuilder.RenameColumn(
                name: "ProposedJobId",
                schema: "car",
                table: "Promotion",
                newName: "ProposedRoleId");

            migrationBuilder.RenameColumn(
                name: "LegalEntityId",
                schema: "pay",
                table: "PayrollRun",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "JobLevelId",
                schema: "rec",
                table: "Offer",
                newName: "LevelId");

            migrationBuilder.RenameColumn(
                name: "JobLevelId",
                schema: "sep",
                table: "NoticeRule",
                newName: "LevelId");

            migrationBuilder.RenameColumn(
                name: "JobId",
                schema: "wfp",
                table: "ManpowerRequest",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "LegalEntityId",
                schema: "org",
                table: "Location",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_Location_LegalEntityId",
                schema: "org",
                table: "Location",
                newName: "IX_Location_CompanyId");

            migrationBuilder.RenameColumn(
                name: "LegalEntityId",
                schema: "emp",
                table: "EmploymentRecord",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "JobLevelId",
                schema: "emp",
                table: "EmploymentRecord",
                newName: "LevelId");

            migrationBuilder.RenameColumn(
                name: "JobId",
                schema: "emp",
                table: "EmploymentRecord",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "LegalEntityId",
                schema: "emp",
                table: "Employee",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "JobLevelId",
                schema: "emp",
                table: "Employee",
                newName: "LevelId");

            migrationBuilder.RenameColumn(
                name: "JobId",
                schema: "emp",
                table: "Employee",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "JobLevelId",
                schema: "org",
                table: "Designation",
                newName: "LevelId");

            migrationBuilder.RenameColumn(
                name: "JobId",
                schema: "org",
                table: "Designation",
                newName: "RoleId");

            migrationBuilder.RenameIndex(
                name: "IX_Designation_JobLevelId",
                schema: "org",
                table: "Designation",
                newName: "IX_Designation_LevelId");

            migrationBuilder.RenameIndex(
                name: "IX_Designation_JobId",
                schema: "org",
                table: "Designation",
                newName: "IX_Designation_RoleId");

            migrationBuilder.RenameColumn(
                name: "LegalEntityId",
                schema: "org",
                table: "CostCenter",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_CostCenter_LegalEntityId",
                schema: "org",
                table: "CostCenter",
                newName: "IX_CostCenter_CompanyId");

            migrationBuilder.RenameColumn(
                name: "LegalEntityId",
                schema: "cmp",
                table: "ComplianceRegistration",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "NewJobLevelId",
                schema: "comp",
                table: "CompensationRevision",
                newName: "NewLevelId");

            migrationBuilder.RenameColumn(
                name: "LegalEntityId",
                schema: "org",
                table: "BusinessUnit",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessUnit_LegalEntityId",
                schema: "org",
                table: "BusinessUnit",
                newName: "IX_BusinessUnit_CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessUnit_Company_CompanyId",
                schema: "org",
                table: "BusinessUnit",
                column: "CompanyId",
                principalSchema: "org",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CostCenter_Company_CompanyId",
                schema: "org",
                table: "CostCenter",
                column: "CompanyId",
                principalSchema: "org",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Designation_Level_LevelId",
                schema: "org",
                table: "Designation",
                column: "LevelId",
                principalSchema: "org",
                principalTable: "Level",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Designation_Role_RoleId",
                schema: "org",
                table: "Designation",
                column: "RoleId",
                principalSchema: "org",
                principalTable: "Role",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Location_Company_CompanyId",
                schema: "org",
                table: "Location",
                column: "CompanyId",
                principalSchema: "org",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Role_Level_LevelId",
                schema: "org",
                table: "Role",
                column: "LevelId",
                principalSchema: "org",
                principalTable: "Level",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Career paths keep their steps as JSON; their keys follow the columns.
            migrationBuilder.Sql(@"UPDATE car.Path SET StepsJson = REPLACE(REPLACE(StepsJson, '""jobLevelId"":', '""levelId"":'), '""jobId"":', '""roleId"":') WHERE StepsJson LIKE '%""job%';");

            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessUnit_Company_CompanyId",
                schema: "org",
                table: "BusinessUnit");

            migrationBuilder.DropForeignKey(
                name: "FK_CostCenter_Company_CompanyId",
                schema: "org",
                table: "CostCenter");

            migrationBuilder.DropForeignKey(
                name: "FK_Designation_Level_LevelId",
                schema: "org",
                table: "Designation");

            migrationBuilder.DropForeignKey(
                name: "FK_Designation_Role_RoleId",
                schema: "org",
                table: "Designation");

            migrationBuilder.DropForeignKey(
                name: "FK_Location_Company_CompanyId",
                schema: "org",
                table: "Location");

            migrationBuilder.DropForeignKey(
                name: "FK_Role_Level_LevelId",
                schema: "org",
                table: "Role");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                schema: "wfp",
                table: "WorkforcePlanLine",
                newName: "JobId");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "comp",
                table: "SalaryBand",
                newName: "JobLevelId");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "org",
                table: "Role",
                newName: "JobLevelId");

            migrationBuilder.RenameIndex(
                name: "IX_Role_LevelId",
                schema: "org",
                table: "Role",
                newName: "IX_Role_JobLevelId");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                schema: "rec",
                table: "Requisition",
                newName: "JobId");

            migrationBuilder.RenameColumn(
                name: "ProposedRoleId",
                schema: "car",
                table: "Promotion",
                newName: "ProposedJobId");

            migrationBuilder.RenameColumn(
                name: "ProposedLevelId",
                schema: "car",
                table: "Promotion",
                newName: "ProposedJobLevelId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                schema: "pay",
                table: "PayrollRun",
                newName: "LegalEntityId");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "rec",
                table: "Offer",
                newName: "JobLevelId");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "sep",
                table: "NoticeRule",
                newName: "JobLevelId");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                schema: "wfp",
                table: "ManpowerRequest",
                newName: "JobId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                schema: "org",
                table: "Location",
                newName: "LegalEntityId");

            migrationBuilder.RenameIndex(
                name: "IX_Location_CompanyId",
                schema: "org",
                table: "Location",
                newName: "IX_Location_LegalEntityId");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                schema: "emp",
                table: "EmploymentRecord",
                newName: "JobId");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "emp",
                table: "EmploymentRecord",
                newName: "JobLevelId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                schema: "emp",
                table: "EmploymentRecord",
                newName: "LegalEntityId");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                schema: "emp",
                table: "Employee",
                newName: "JobId");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "emp",
                table: "Employee",
                newName: "JobLevelId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                schema: "emp",
                table: "Employee",
                newName: "LegalEntityId");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                schema: "org",
                table: "Designation",
                newName: "JobId");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "org",
                table: "Designation",
                newName: "JobLevelId");

            migrationBuilder.RenameIndex(
                name: "IX_Designation_RoleId",
                schema: "org",
                table: "Designation",
                newName: "IX_Designation_JobId");

            migrationBuilder.RenameIndex(
                name: "IX_Designation_LevelId",
                schema: "org",
                table: "Designation",
                newName: "IX_Designation_JobLevelId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                schema: "org",
                table: "CostCenter",
                newName: "LegalEntityId");

            migrationBuilder.RenameIndex(
                name: "IX_CostCenter_CompanyId",
                schema: "org",
                table: "CostCenter",
                newName: "IX_CostCenter_LegalEntityId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                schema: "cmp",
                table: "ComplianceRegistration",
                newName: "LegalEntityId");

            migrationBuilder.RenameColumn(
                name: "NewLevelId",
                schema: "comp",
                table: "CompensationRevision",
                newName: "NewJobLevelId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                schema: "org",
                table: "BusinessUnit",
                newName: "LegalEntityId");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessUnit_CompanyId",
                schema: "org",
                table: "BusinessUnit",
                newName: "IX_BusinessUnit_LegalEntityId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessUnit_Company_LegalEntityId",
                schema: "org",
                table: "BusinessUnit",
                column: "LegalEntityId",
                principalSchema: "org",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CostCenter_Company_LegalEntityId",
                schema: "org",
                table: "CostCenter",
                column: "LegalEntityId",
                principalSchema: "org",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Designation_Level_JobLevelId",
                schema: "org",
                table: "Designation",
                column: "JobLevelId",
                principalSchema: "org",
                principalTable: "Level",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Designation_Role_JobId",
                schema: "org",
                table: "Designation",
                column: "JobId",
                principalSchema: "org",
                principalTable: "Role",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Location_Company_LegalEntityId",
                schema: "org",
                table: "Location",
                column: "LegalEntityId",
                principalSchema: "org",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Role_Level_JobLevelId",
                schema: "org",
                table: "Role",
                column: "JobLevelId",
                principalSchema: "org",
                principalTable: "Level",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(@"UPDATE car.Path SET StepsJson = REPLACE(REPLACE(StepsJson, '""levelId"":', '""jobLevelId"":'), '""roleId"":', '""jobId"":') WHERE StepsJson LIKE '%Id"":%';");

            migrationBuilder.SyncTenantRowLevelSecurity(new RoleDesignationsAndScreenTableNames().TargetModel);
        }
    }
}
