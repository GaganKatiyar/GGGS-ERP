using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInsight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ins");

            migrationBuilder.CreateTable(
                name: "ImportRun",
                schema: "ins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandlerKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Valid = table.Column<int>(type: "int", nullable: false),
                    Imported = table.Column<int>(type: "int", nullable: false),
                    Committed = table.Column<bool>(type: "bit", nullable: false),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_ImportRun", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationConnection",
                schema: "ins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdapterKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastTestedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "bit", nullable: true),
                    LastMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_IntegrationConnection", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "ReportSubscription",
                schema: "ins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Frequency = table.Column<byte>(type: "tinyint", nullable: false),
                    LastSentOn = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_ReportSubscription", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationRun",
                schema: "ins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Records = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_IntegrationRun", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_IntegrationRun_IntegrationConnection_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "ins",
                        principalTable: "IntegrationConnection",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRun_TenantId_Created",
                schema: "ins",
                table: "ImportRun",
                columns: new[] { "TenantId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "UX_ImportRun_TenantId_Id",
                schema: "ins",
                table: "ImportRun",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationConnection_TenantId_Id",
                schema: "ins",
                table: "IntegrationConnection",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationConnection_TenantId_Name",
                schema: "ins",
                table: "IntegrationConnection",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRun_ConnectionId",
                schema: "ins",
                table: "IntegrationRun",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRun_TenantId_Connection_Created",
                schema: "ins",
                table: "IntegrationRun",
                columns: new[] { "TenantId", "ConnectionId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationRun_TenantId_Id",
                schema: "ins",
                table: "IntegrationRun",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_ReportSubscription_TenantId_Id",
                schema: "ins",
                table: "ReportSubscription",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_ReportSubscription_TenantId_User_Area",
                schema: "ins",
                table: "ReportSubscription",
                columns: new[] { "TenantId", "UserId", "Area" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // New tenant-owned tables (ins): the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names these tables, so it goes first; it is restored below without them.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "ImportRun",
                schema: "ins");

            migrationBuilder.DropTable(
                name: "IntegrationRun",
                schema: "ins");

            migrationBuilder.DropTable(
                name: "ReportSubscription",
                schema: "ins");

            migrationBuilder.DropTable(
                name: "IntegrationConnection",
                schema: "ins");

            // The policy as it stood before these tables existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddExperience().TargetModel);
        }
    }
}
