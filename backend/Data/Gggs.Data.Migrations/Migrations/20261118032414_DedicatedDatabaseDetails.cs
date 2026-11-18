using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Platform-owned table (no tenant row-level security). The model also held a work-in-progress
    /// lv.LeaveCostCenterAssignment entity from another change when this was generated; it was taken
    /// out of this migration, its designer and the snapshot, so that change's own migration creates it.
    /// </remarks>
    public partial class DedicatedDatabaseDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DedicatedDatabase",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Server = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Authentication = table.Column<byte>(type: "tinyint", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PasswordCiphertext = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Encryption = table.Column<byte>(type: "tinyint", nullable: false),
                    PasswordChangedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PasswordChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastTestedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "bit", nullable: true),
                    LastTestResult = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DedicatedDatabase", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DedicatedDatabase_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "platform",
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "A dedicated database's connection details, kept by the platform console. The password is ciphertext only.");

            migrationBuilder.CreateIndex(
                name: "UX_DedicatedDatabase_TenantId",
                schema: "platform",
                table: "DedicatedDatabase",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DedicatedDatabase",
                schema: "platform");
        }
    }
}
