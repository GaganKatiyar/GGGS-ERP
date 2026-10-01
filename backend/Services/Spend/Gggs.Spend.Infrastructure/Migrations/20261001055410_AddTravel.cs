using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Spend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTravel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TravelRequestId",
                schema: "exp",
                table: "ExpenseClaim",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TravelAdvance",
                schema: "exp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TravelRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    DisbursedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
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
                    table.PrimaryKey("PK_TravelAdvance", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TravelRequest",
                schema: "exp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FromCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ToCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DecidedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
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
                    table.PrimaryKey("PK_TravelRequest", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TravelSettlement",
                schema: "exp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TravelRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdvanceTotal = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    ExpenseTotal = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    NetPayable = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    NetRecoverable = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    SettledDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
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
                    table.PrimaryKey("PK_TravelSettlement", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaim_TenantId_TravelRequestId",
                schema: "exp",
                table: "ExpenseClaim",
                columns: new[] { "TenantId", "TravelRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_TravelAdvance_TenantId_TravelRequestId",
                schema: "exp",
                table: "TravelAdvance",
                columns: new[] { "TenantId", "TravelRequestId" });

            migrationBuilder.CreateIndex(
                name: "UX_TravelAdvance_TenantId_Id",
                schema: "exp",
                table: "TravelAdvance",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_TravelRequest_TenantId_EmployeeId",
                schema: "exp",
                table: "TravelRequest",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "UX_TravelRequest_TenantId_Id",
                schema: "exp",
                table: "TravelRequest",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TravelRequest_TenantId_RequestNo",
                schema: "exp",
                table: "TravelRequest",
                columns: new[] { "TenantId", "RequestNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TravelSettlement_TenantId_Id",
                schema: "exp",
                table: "TravelSettlement",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TravelSettlement_TenantId_TravelRequestId",
                schema: "exp",
                table: "TravelSettlement",
                columns: new[] { "TenantId", "TravelRequestId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TravelAdvance",
                schema: "exp");

            migrationBuilder.DropTable(
                name: "TravelRequest",
                schema: "exp");

            migrationBuilder.DropTable(
                name: "TravelSettlement",
                schema: "exp");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseClaim_TenantId_TravelRequestId",
                schema: "exp",
                table: "ExpenseClaim");

            migrationBuilder.DropColumn(
                name: "TravelRequestId",
                schema: "exp",
                table: "ExpenseClaim");
        }
    }
}
