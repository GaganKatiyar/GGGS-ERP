using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EmailLogRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                schema: "ntf",
                table: "EmailMessage",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailLogSetting",
                schema: "ntf",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeepSentDays = table.Column<int>(type: "int", nullable: false),
                    KeepProblemDays = table.Column<int>(type: "int", nullable: false),
                    AlertOnFailure = table.Column<bool>(type: "bit", nullable: false),
                    AlertedUpTo = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
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
                    table.PrimaryKey("PK_EmailLogSetting", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessage_TenantId_NextAttempt",
                schema: "ntf",
                table: "EmailMessage",
                columns: new[] { "TenantId", "NextAttemptAt" },
                filter: "[NextAttemptAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_EmailLogSetting_TenantId",
                schema: "ntf",
                table: "EmailLogSetting",
                column: "TenantId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_EmailLogSetting_TenantId_Id",
                schema: "ntf",
                table: "EmailLogSetting",
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
                name: "EmailLogSetting",
                schema: "ntf");

            migrationBuilder.SyncTenantRowLevelSecurity(new HireDrafts().TargetModel);

            migrationBuilder.DropIndex(
                name: "IX_EmailMessage_TenantId_NextAttempt",
                schema: "ntf",
                table: "EmailMessage");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "ntf",
                table: "EmailMessage");
        }
    }
}
