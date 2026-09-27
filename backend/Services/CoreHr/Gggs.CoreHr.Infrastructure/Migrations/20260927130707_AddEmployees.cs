using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.CoreHr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "emp");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Grade_TenantId_Id",
                schema: "org",
                table: "Grade",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Designation_TenantId_Id",
                schema: "org",
                table: "Designation",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "Employee",
                schema: "emp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(303)", maxLength: 303, nullable: false),
                    WorkEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    DateOfJoining = table.Column<DateOnly>(type: "date", nullable: false),
                    OriginalHireDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ConfirmationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExitDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Employee", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("AK_Employee_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "EmploymentRecord",
                schema: "emp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesignationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReportingManagerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmploymentType = table.Column<byte>(type: "tinyint", nullable: false),
                    ProbationEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChangeType = table.Column<byte>(type: "tinyint", nullable: false),
                    ChangeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_EmploymentRecord", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_EmploymentRecord_Branch_TenantId_BranchId",
                        columns: x => new { x.TenantId, x.BranchId },
                        principalSchema: "org",
                        principalTable: "Branch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmploymentRecord_CostCenter_TenantId_CostCenterId",
                        columns: x => new { x.TenantId, x.CostCenterId },
                        principalSchema: "org",
                        principalTable: "CostCenter",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmploymentRecord_Department_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalSchema: "org",
                        principalTable: "Department",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmploymentRecord_Designation_TenantId_DesignationId",
                        columns: x => new { x.TenantId, x.DesignationId },
                        principalSchema: "org",
                        principalTable: "Designation",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmploymentRecord_Employee_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalSchema: "emp",
                        principalTable: "Employee",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmploymentRecord_Employee_TenantId_ReportingManagerId",
                        columns: x => new { x.TenantId, x.ReportingManagerId },
                        principalSchema: "emp",
                        principalTable: "Employee",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmploymentRecord_Grade_TenantId_GradeId",
                        columns: x => new { x.TenantId, x.GradeId },
                        principalSchema: "org",
                        principalTable: "Grade",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmploymentRecord_LegalEntity_TenantId_LegalEntityId",
                        columns: x => new { x.TenantId, x.LegalEntityId },
                        principalSchema: "org",
                        principalTable: "LegalEntity",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Employee_TenantId_EmployeeCode",
                schema: "emp",
                table: "Employee",
                columns: new[] { "TenantId", "EmployeeCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Employee_TenantId_Id",
                schema: "emp",
                table: "Employee",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_Employee_TenantId_UserId",
                schema: "emp",
                table: "Employee",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Employee_TenantId_WorkEmail",
                schema: "emp",
                table: "Employee",
                columns: new[] { "TenantId", "WorkEmail" },
                unique: true,
                filter: "[WorkEmail] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecord_TenantId_BranchId",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecord_TenantId_CostCenterId",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "CostCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecord_TenantId_DepartmentId",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "DepartmentId" },
                filter: "[IsCurrent] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecord_TenantId_DesignationId",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "DesignationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecord_TenantId_EmployeeId_Timeline",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecord_TenantId_GradeId",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "GradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecord_TenantId_LegalEntityId",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecord_TenantId_ReportingManagerId",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "ReportingManagerId" },
                filter: "[IsCurrent] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_EmploymentRecord_TenantId_EmployeeId_Current",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "[IsCurrent] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_EmploymentRecord_TenantId_Id",
                schema: "emp",
                table: "EmploymentRecord",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmploymentRecord",
                schema: "emp");

            migrationBuilder.DropTable(
                name: "Employee",
                schema: "emp");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Grade_TenantId_Id",
                schema: "org",
                table: "Grade");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Designation_TenantId_Id",
                schema: "org",
                table: "Designation");
        }
    }
}
