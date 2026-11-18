using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CostCenterPolicyByEngagementType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_LeaveCostCenterAssignment_TenantId_CostCenter",
                schema: "lv",
                table: "LeaveCostCenterAssignment");

            migrationBuilder.AddColumn<string>(
                name: "EmploymentTypeCode",
                schema: "lv",
                table: "LeaveCostCenterAssignment",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_LeaveCostCenterAssignment_TenantId_CostCenter_Type",
                schema: "lv",
                table: "LeaveCostCenterAssignment",
                columns: new[] { "TenantId", "CostCenterId", "EmploymentTypeCode" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_LeaveCostCenterAssignment_TenantId_CostCenter_Type",
                schema: "lv",
                table: "LeaveCostCenterAssignment");

            // Back to one policy per cost center: the per-engagement-type rows have nowhere to go.
            migrationBuilder.Sql("DELETE FROM lv.LeaveCostCenterAssignment WHERE EmploymentTypeCode IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "EmploymentTypeCode",
                schema: "lv",
                table: "LeaveCostCenterAssignment");

            migrationBuilder.CreateIndex(
                name: "UX_LeaveCostCenterAssignment_TenantId_CostCenter",
                schema: "lv",
                table: "LeaveCostCenterAssignment",
                columns: new[] { "TenantId", "CostCenterId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
