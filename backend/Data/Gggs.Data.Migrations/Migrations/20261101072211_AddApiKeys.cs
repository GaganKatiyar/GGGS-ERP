using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKey",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SecretHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ServiceUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_ApiKey", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_ApiKey_User_ServiceUserId",
                        column: x => x.ServiceUserId,
                        principalSchema: "iam",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKey_ServiceUserId",
                schema: "iam",
                table: "ApiKey",
                column: "ServiceUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ApiKey_TenantId_Id",
                schema: "iam",
                table: "ApiKey",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_ApiKey_TenantId_Prefix",
                schema: "iam",
                table: "ApiKey",
                columns: new[] { "TenantId", "Prefix" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // New tenant-owned tables (iam): the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names these tables, so it goes first; it is restored below without them.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "ApiKey",
                schema: "iam");

            // The policy as it stood before these tables existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddLeavePlanYearEnd().TargetModel);
        }
    }
}
