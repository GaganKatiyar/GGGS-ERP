using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SalaryBandLevelIndexName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "UX_SalaryBand_TenantId_Grade_Currency_From",
                schema: "comp",
                table: "SalaryBand",
                newName: "UX_SalaryBand_TenantId_Level_Currency_From");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "UX_SalaryBand_TenantId_Level_Currency_From",
                schema: "comp",
                table: "SalaryBand",
                newName: "UX_SalaryBand_TenantId_Grade_Currency_From");
        }
    }
}
