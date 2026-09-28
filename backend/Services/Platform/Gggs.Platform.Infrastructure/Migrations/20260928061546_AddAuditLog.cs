using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Service = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedFieldsCsv = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                },
                comment: "Append-only. Fed exclusively by AuditEntryConsumer; nothing else writes here.");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TenantId_EntityType_EntityId_OccurredDate",
                schema: "platform",
                table: "AuditLog",
                columns: new[] { "TenantId", "EntityType", "EntityId", "OccurredDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TenantId_OccurredDate",
                schema: "platform",
                table: "AuditLog",
                columns: new[] { "TenantId", "OccurredDate" });

            migrationBuilder.CreateIndex(
                name: "UX_AuditLog_TenantId_Id",
                schema: "platform",
                table: "AuditLog",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "platform");
        }
    }
}
