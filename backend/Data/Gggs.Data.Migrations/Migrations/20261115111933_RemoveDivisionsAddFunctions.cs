using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <summary>
    /// Removes divisions: a department now sits directly in a business unit, and a function is a
    /// department inside a department. Data is carried over before anything is dropped - each
    /// department takes its division's business unit, functions take their department's, and an
    /// employee with a division but no business unit gets the division's.
    /// </summary>
    public partial class RemoveDivisionsAddFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Department_Division_DivisionId",
                schema: "org",
                table: "Department");

            // The column is renamed below, so it must hold business unit ids, not division ids.
            migrationBuilder.Sql("""
                UPDATE d SET d.DivisionId = v.BusinessUnitId
                FROM org.Department d
                LEFT JOIN org.Division v ON v.Id = d.DivisionId;
                """);

            migrationBuilder.Sql("""
                UPDATE e SET e.BusinessUnitId = v.BusinessUnitId
                FROM emp.Employee e
                JOIN org.Division v ON v.Id = e.DivisionId
                WHERE e.BusinessUnitId IS NULL AND v.BusinessUnitId IS NOT NULL;

                UPDATE r SET r.BusinessUnitId = v.BusinessUnitId
                FROM emp.EmploymentRecord r
                JOIN org.Division v ON v.Id = r.DivisionId
                WHERE r.BusinessUnitId IS NULL AND v.BusinessUnitId IS NOT NULL;
                """);

            // The tenant policy names the Division table, so it goes first; it is rebuilt below without it.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "Division",
                schema: "org");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                schema: "emp",
                table: "EmploymentRecord");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                schema: "emp",
                table: "Employee");

            migrationBuilder.RenameColumn(
                name: "DivisionId",
                schema: "org",
                table: "Department",
                newName: "BusinessUnitId");

            migrationBuilder.RenameIndex(
                name: "IX_Department_DivisionId",
                schema: "org",
                table: "Department",
                newName: "IX_Department_BusinessUnitId");

            // A function sits in its department's business unit.
            migrationBuilder.Sql("""
                UPDATE c SET c.BusinessUnitId = p.BusinessUnitId
                FROM org.Department c
                JOIN org.Department p ON p.Id = c.ParentDepartmentId;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Department_BusinessUnit_BusinessUnitId",
                schema: "org",
                table: "Department",
                column: "BusinessUnitId",
                principalSchema: "org",
                principalTable: "BusinessUnit",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropForeignKey(
                name: "FK_Department_BusinessUnit_BusinessUnitId",
                schema: "org",
                table: "Department");

            migrationBuilder.RenameColumn(
                name: "BusinessUnitId",
                schema: "org",
                table: "Department",
                newName: "DivisionId");

            migrationBuilder.RenameIndex(
                name: "IX_Department_BusinessUnitId",
                schema: "org",
                table: "Department",
                newName: "IX_Department_DivisionId");

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                schema: "emp",
                table: "EmploymentRecord",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                schema: "emp",
                table: "Employee",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Division",
                schema: "org",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    HeadEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Division", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_Division_BusinessUnit_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalSchema: "org",
                        principalTable: "BusinessUnit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Division_BusinessUnitId",
                schema: "org",
                table: "Division",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "UX_Division_TenantId_Code",
                schema: "org",
                table: "Division",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Division_TenantId_Id",
                schema: "org",
                table: "Division",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // The column holds business unit ids now; divisions are not coming back with their data.
            migrationBuilder.Sql("UPDATE org.Department SET DivisionId = NULL;");

            migrationBuilder.AddForeignKey(
                name: "FK_Department_Division_DivisionId",
                schema: "org",
                table: "Department",
                column: "DivisionId",
                principalSchema: "org",
                principalTable: "Division",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.SyncTenantRowLevelSecurity(new AddShiftPunchOutRequired().TargetModel);
        }
    }
}
