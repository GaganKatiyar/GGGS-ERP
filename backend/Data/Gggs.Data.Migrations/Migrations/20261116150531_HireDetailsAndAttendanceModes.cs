using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class HireDetailsAndAttendanceModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PassportCiphertext",
                schema: "emp",
                table: "Employee",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PassportExpiry",
                schema: "emp",
                table: "Employee",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportHint",
                schema: "emp",
                table: "Employee",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousExperienceYears",
                schema: "emp",
                table: "Employee",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceProfile",
                schema: "att",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Modes = table.Column<byte>(type: "tinyint", nullable: false),
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
                    table.PrimaryKey("PK_AttendanceProfile", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceProfile_TenantId_Employee",
                schema: "att",
                table: "AttendanceProfile",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceProfile_TenantId_Id",
                schema: "att",
                table: "AttendanceProfile",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // A new tenant-owned table: the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names this table, so it goes first; it is restored below without it.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "AttendanceProfile",
                schema: "att");

            migrationBuilder.SyncTenantRowLevelSecurity(new AddCelebrationWishes().TargetModel);

            migrationBuilder.DropColumn(
                name: "PassportCiphertext",
                schema: "emp",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "PassportExpiry",
                schema: "emp",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "PassportHint",
                schema: "emp",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "PreviousExperienceYears",
                schema: "emp",
                table: "Employee");
        }
    }
}
