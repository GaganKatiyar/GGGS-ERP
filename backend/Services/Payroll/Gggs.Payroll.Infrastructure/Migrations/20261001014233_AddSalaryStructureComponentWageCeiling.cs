using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryStructureComponentWageCeiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WageCeiling",
                schema: "pay",
                table: "SalaryStructureComponent",
                type: "decimal(12,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WageCeiling",
                schema: "pay",
                table: "SalaryStructureComponent");
        }
    }
}
