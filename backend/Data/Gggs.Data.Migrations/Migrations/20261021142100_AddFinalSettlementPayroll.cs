using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalSettlementPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayrollRunId",
                schema: "sep",
                table: "Settlement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                schema: "pay",
                table: "PayrollRun",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Kind",
                schema: "pay",
                table: "PayrollRun",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1); // Existing runs are regular monthly runs.

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastWorkingDay",
                schema: "pay",
                table: "PayrollRun",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SalaryExcluded",
                schema: "pay",
                table: "PayrollRun",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                schema: "pay",
                table: "PayrollRun",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SettlementOverdueNotifiedOn",
                schema: "sep",
                table: "Case",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRun_TenantId_Source",
                schema: "pay",
                table: "PayrollRun",
                columns: new[] { "TenantId", "SourceId" });

            // New tenant-owned tables (pay, sep): the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names these tables, so it goes first; it is restored below without them.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropIndex(
                name: "IX_PayrollRun_TenantId_Source",
                schema: "pay",
                table: "PayrollRun");

            migrationBuilder.DropColumn(
                name: "PayrollRunId",
                schema: "sep",
                table: "Settlement");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                schema: "pay",
                table: "PayrollRun");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "pay",
                table: "PayrollRun");

            migrationBuilder.DropColumn(
                name: "LastWorkingDay",
                schema: "pay",
                table: "PayrollRun");

            migrationBuilder.DropColumn(
                name: "SalaryExcluded",
                schema: "pay",
                table: "PayrollRun");

            migrationBuilder.DropColumn(
                name: "SourceId",
                schema: "pay",
                table: "PayrollRun");

            migrationBuilder.DropColumn(
                name: "SettlementOverdueNotifiedOn",
                schema: "sep",
                table: "Case");

            // The policy as it stood before these tables existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddInvoluntaryExits().TargetModel);
        }
    }
}
