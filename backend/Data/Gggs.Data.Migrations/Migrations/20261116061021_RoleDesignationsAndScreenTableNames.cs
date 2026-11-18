using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <summary>
    /// Tables take the names the screens use - Company, Role and Level (were LegalEntity, Job and
    /// JobLevel) - and a designation can name the role it is a level of ("Business Analyst L2"). Only
    /// names change; no data moves.
    /// </summary>
    public partial class RoleDesignationsAndScreenTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The tenant policy names these tables; it is rebuilt at the end under the new names.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessUnit_LegalEntity_LegalEntityId",
                schema: "org",
                table: "BusinessUnit");

            migrationBuilder.DropForeignKey(
                name: "FK_CostCenter_LegalEntity_LegalEntityId",
                schema: "org",
                table: "CostCenter");

            migrationBuilder.DropForeignKey(
                name: "FK_Designation_JobLevel_JobLevelId",
                schema: "org",
                table: "Designation");

            migrationBuilder.DropForeignKey(
                name: "FK_Job_Department_DepartmentId",
                schema: "org",
                table: "Job");

            migrationBuilder.DropForeignKey(
                name: "FK_Job_JobLevel_JobLevelId",
                schema: "org",
                table: "Job");

            migrationBuilder.DropForeignKey(
                name: "FK_Location_LegalEntity_LegalEntityId",
                schema: "org",
                table: "Location");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LegalEntity",
                schema: "org",
                table: "LegalEntity");

            migrationBuilder.DropPrimaryKey(
                name: "PK_JobLevel",
                schema: "org",
                table: "JobLevel");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Job",
                schema: "org",
                table: "Job");

            migrationBuilder.RenameTable(
                name: "LegalEntity",
                schema: "org",
                newName: "Company",
                newSchema: "org");

            migrationBuilder.RenameTable(
                name: "JobLevel",
                schema: "org",
                newName: "Level",
                newSchema: "org");

            migrationBuilder.RenameTable(
                name: "Job",
                schema: "org",
                newName: "Role",
                newSchema: "org");

            migrationBuilder.RenameIndex(
                name: "UX_LegalEntity_TenantId_Id",
                schema: "org",
                table: "Company",
                newName: "UX_Company_TenantId_Id");

            migrationBuilder.RenameIndex(
                name: "UX_LegalEntity_TenantId_Code",
                schema: "org",
                table: "Company",
                newName: "UX_Company_TenantId_Code");

            migrationBuilder.RenameIndex(
                name: "UX_JobLevel_TenantId_Id",
                schema: "org",
                table: "Level",
                newName: "UX_Level_TenantId_Id");

            migrationBuilder.RenameIndex(
                name: "UX_JobLevel_TenantId_Code",
                schema: "org",
                table: "Level",
                newName: "UX_Level_TenantId_Code");

            migrationBuilder.RenameIndex(
                name: "IX_JobLevel_TenantId_Rank",
                schema: "org",
                table: "Level",
                newName: "IX_Level_TenantId_Rank");

            migrationBuilder.RenameIndex(
                name: "UX_Job_TenantId_Id",
                schema: "org",
                table: "Role",
                newName: "UX_Role_TenantId_Id");

            migrationBuilder.RenameIndex(
                name: "UX_Job_TenantId_Code",
                schema: "org",
                table: "Role",
                newName: "UX_Role_TenantId_Code");

            migrationBuilder.RenameIndex(
                name: "IX_Job_JobLevelId",
                schema: "org",
                table: "Role",
                newName: "IX_Role_JobLevelId");

            migrationBuilder.RenameIndex(
                name: "IX_Job_DepartmentId",
                schema: "org",
                table: "Role",
                newName: "IX_Role_DepartmentId");

            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                schema: "org",
                table: "Designation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Company",
                schema: "org",
                table: "Company",
                column: "Id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Level",
                schema: "org",
                table: "Level",
                column: "Id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Role",
                schema: "org",
                table: "Role",
                column: "Id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.CreateIndex(
                name: "IX_Designation_JobId",
                schema: "org",
                table: "Designation",
                column: "JobId");

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
                name: "FK_Role_Department_DepartmentId",
                schema: "org",
                table: "Role",
                column: "DepartmentId",
                principalSchema: "org",
                principalTable: "Department",
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

            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "FK_Role_Department_DepartmentId",
                schema: "org",
                table: "Role");

            migrationBuilder.DropForeignKey(
                name: "FK_Role_Level_JobLevelId",
                schema: "org",
                table: "Role");

            migrationBuilder.DropIndex(
                name: "IX_Designation_JobId",
                schema: "org",
                table: "Designation");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Role",
                schema: "org",
                table: "Role");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Level",
                schema: "org",
                table: "Level");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Company",
                schema: "org",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "JobId",
                schema: "org",
                table: "Designation");

            migrationBuilder.RenameTable(
                name: "Role",
                schema: "org",
                newName: "Job",
                newSchema: "org");

            migrationBuilder.RenameTable(
                name: "Level",
                schema: "org",
                newName: "JobLevel",
                newSchema: "org");

            migrationBuilder.RenameTable(
                name: "Company",
                schema: "org",
                newName: "LegalEntity",
                newSchema: "org");

            migrationBuilder.RenameIndex(
                name: "UX_Role_TenantId_Id",
                schema: "org",
                table: "Job",
                newName: "UX_Job_TenantId_Id");

            migrationBuilder.RenameIndex(
                name: "UX_Role_TenantId_Code",
                schema: "org",
                table: "Job",
                newName: "UX_Job_TenantId_Code");

            migrationBuilder.RenameIndex(
                name: "IX_Role_JobLevelId",
                schema: "org",
                table: "Job",
                newName: "IX_Job_JobLevelId");

            migrationBuilder.RenameIndex(
                name: "IX_Role_DepartmentId",
                schema: "org",
                table: "Job",
                newName: "IX_Job_DepartmentId");

            migrationBuilder.RenameIndex(
                name: "UX_Level_TenantId_Id",
                schema: "org",
                table: "JobLevel",
                newName: "UX_JobLevel_TenantId_Id");

            migrationBuilder.RenameIndex(
                name: "UX_Level_TenantId_Code",
                schema: "org",
                table: "JobLevel",
                newName: "UX_JobLevel_TenantId_Code");

            migrationBuilder.RenameIndex(
                name: "IX_Level_TenantId_Rank",
                schema: "org",
                table: "JobLevel",
                newName: "IX_JobLevel_TenantId_Rank");

            migrationBuilder.RenameIndex(
                name: "UX_Company_TenantId_Id",
                schema: "org",
                table: "LegalEntity",
                newName: "UX_LegalEntity_TenantId_Id");

            migrationBuilder.RenameIndex(
                name: "UX_Company_TenantId_Code",
                schema: "org",
                table: "LegalEntity",
                newName: "UX_LegalEntity_TenantId_Code");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Job",
                schema: "org",
                table: "Job",
                column: "Id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_JobLevel",
                schema: "org",
                table: "JobLevel",
                column: "Id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_LegalEntity",
                schema: "org",
                table: "LegalEntity",
                column: "Id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessUnit_LegalEntity_LegalEntityId",
                schema: "org",
                table: "BusinessUnit",
                column: "LegalEntityId",
                principalSchema: "org",
                principalTable: "LegalEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CostCenter_LegalEntity_LegalEntityId",
                schema: "org",
                table: "CostCenter",
                column: "LegalEntityId",
                principalSchema: "org",
                principalTable: "LegalEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Designation_JobLevel_JobLevelId",
                schema: "org",
                table: "Designation",
                column: "JobLevelId",
                principalSchema: "org",
                principalTable: "JobLevel",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Job_Department_DepartmentId",
                schema: "org",
                table: "Job",
                column: "DepartmentId",
                principalSchema: "org",
                principalTable: "Department",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Job_JobLevel_JobLevelId",
                schema: "org",
                table: "Job",
                column: "JobLevelId",
                principalSchema: "org",
                principalTable: "JobLevel",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Location_LegalEntity_LegalEntityId",
                schema: "org",
                table: "Location",
                column: "LegalEntityId",
                principalSchema: "org",
                principalTable: "LegalEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.SyncTenantRowLevelSecurity(new SimplifyOrgStructure().TargetModel);
        }
    }
}
