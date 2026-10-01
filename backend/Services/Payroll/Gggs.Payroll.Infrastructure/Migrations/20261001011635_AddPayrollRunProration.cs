using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRunProration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LopDays",
                schema: "pay",
                table: "PayrollRunEmployee",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidDays",
                schema: "pay",
                table: "PayrollRunEmployee",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDays",
                schema: "pay",
                table: "PayrollRunEmployee",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LopDays",
                schema: "pay",
                table: "PayrollRunEmployee");

            migrationBuilder.DropColumn(
                name: "PaidDays",
                schema: "pay",
                table: "PayrollRunEmployee");

            migrationBuilder.DropColumn(
                name: "TotalDays",
                schema: "pay",
                table: "PayrollRunEmployee");
        }
    }
}
