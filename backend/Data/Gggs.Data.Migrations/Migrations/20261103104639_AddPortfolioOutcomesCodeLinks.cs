using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioOutcomesCodeLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TargetUnits",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                schema: "pfin",
                table: "ProjectFinanceProfile",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectOutcomeUnits",
                schema: "pfin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AcceptedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_ProjectOutcomeUnits", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TaskCodeLink",
                schema: "prj",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Author = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
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
                    table.PrimaryKey("PK_TaskCodeLink", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectOutcomeUnits_TenantId_Project_Date",
                schema: "pfin",
                table: "ProjectOutcomeUnits",
                columns: new[] { "TenantId", "ProjectId", "AcceptedOn" });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectOutcomeUnits_TenantId_Id",
                schema: "pfin",
                table: "ProjectOutcomeUnits",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TaskCodeLink_TenantId_Id",
                schema: "prj",
                table: "TaskCodeLink",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TaskCodeLink_TenantId_Task_External",
                schema: "prj",
                table: "TaskCodeLink",
                columns: new[] { "TenantId", "TaskId", "Provider", "Kind", "ExternalId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "ProjectOutcomeUnits",
                schema: "pfin");

            migrationBuilder.DropTable(
                name: "TaskCodeLink",
                schema: "prj");

            migrationBuilder.DropColumn(
                name: "TargetUnits",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "UnitName",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                schema: "pfin",
                table: "ProjectFinanceProfile");

            migrationBuilder.SyncTenantRowLevelSecurity(new AddTaskCollaboration().TargetModel);
        }
    }
}
