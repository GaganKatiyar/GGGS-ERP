using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectSlaPolicy",
                schema: "prj",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlwaysOn = table.Column<bool>(type: "bit", nullable: false),
                    DayStart = table.Column<TimeOnly>(type: "time", nullable: false),
                    DayEnd = table.Column<TimeOnly>(type: "time", nullable: false),
                    WorkingDaysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PausedColumnIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MonthlyCreditCapPercent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_ProjectSlaPolicy", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TicketSla",
                schema: "prj",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    ResponseMinutes = table.Column<int>(type: "int", nullable: false),
                    ResolutionMinutes = table.Column<int>(type: "int", nullable: false),
                    CreditPercent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ResponseDueAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ResolutionDueAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PausedSince = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PausedMinutes = table.Column<int>(type: "int", nullable: false),
                    ResponseBreachedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ResolutionBreachedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ExcludedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExcludedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_TicketSla", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectSlaPolicy_TenantId_Id",
                schema: "prj",
                table: "ProjectSlaPolicy",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_ProjectSlaPolicy_TenantId_Project",
                schema: "prj",
                table: "ProjectSlaPolicy",
                columns: new[] { "TenantId", "ProjectId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TicketSla_TenantId_Project",
                schema: "prj",
                table: "TicketSla",
                columns: new[] { "TenantId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketSla_TenantId_Sweep",
                schema: "prj",
                table: "TicketSla",
                columns: new[] { "TenantId", "ResolvedAt", "ResolutionDueAt" });

            migrationBuilder.CreateIndex(
                name: "UX_TicketSla_TenantId_Id",
                schema: "prj",
                table: "TicketSla",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TicketSla_TenantId_Task",
                schema: "prj",
                table: "TicketSla",
                columns: new[] { "TenantId", "TaskId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "ProjectSlaPolicy",
                schema: "prj");

            migrationBuilder.DropTable(
                name: "TicketSla",
                schema: "prj");

            migrationBuilder.SyncTenantRowLevelSecurity(new AddProjectBilling().TargetModel);
        }
    }
}
