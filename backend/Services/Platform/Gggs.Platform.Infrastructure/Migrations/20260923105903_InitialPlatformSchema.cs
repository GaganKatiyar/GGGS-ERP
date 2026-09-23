using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatformSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "InboxMessage",
                schema: "messaging",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessage", x => new { x.MessageId, x.ConsumerName });
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CausationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PublishedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "Plan",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Edition = table.Column<byte>(type: "tinyint", nullable: false),
                    MaxEmployees = table.Column<int>(type: "int", nullable: true),
                    IsPubliclyAvailable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    FeatureCodes = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenant",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    IsolationTier = table.Column<byte>(type: "tinyint", nullable: false),
                    DataRegion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CountryCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    DefaultTimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    DefaultLocale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ActivatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    SuspendedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    SuspensionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PurgeEligibleDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSetting",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValueJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_TenantSetting", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "Subscription",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TrialEndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SeatLimit = table.Column<int>(type: "int", nullable: true),
                    BillingReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    FeatureOverrides = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscription_Plan_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "platform",
                        principalTable: "Plan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscription_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "platform",
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantDatabase",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConnectionSecretName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShardName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantDatabase", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantDatabase_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "platform",
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessage_ProcessedDate",
                schema: "messaging",
                table: "InboxMessage",
                column: "ProcessedDate");

            migrationBuilder.CreateIndex(
                name: "CIX_OutboxMessage_OccurredDate",
                schema: "messaging",
                table: "OutboxMessage",
                columns: new[] { "OccurredDate", "Id" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_Pending",
                schema: "messaging",
                table: "OutboxMessage",
                columns: new[] { "Status", "NextAttemptDate", "OccurredDate" },
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_TenantId_PublishedDate",
                schema: "messaging",
                table: "OutboxMessage",
                columns: new[] { "TenantId", "PublishedDate" });

            migrationBuilder.CreateIndex(
                name: "UX_Plan_Code",
                schema: "platform",
                table: "Plan",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscription_PlanId",
                schema: "platform",
                table: "Subscription",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "UX_Subscription_TenantId_Live",
                schema: "platform",
                table: "Subscription",
                column: "TenantId",
                unique: true,
                filter: "[Status] IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_Tenant_Status",
                schema: "platform",
                table: "Tenant",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Tenant_Code",
                schema: "platform",
                table: "Tenant",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TenantDatabase_TenantId_ServiceName",
                schema: "platform",
                table: "TenantDatabase",
                columns: new[] { "TenantId", "ServiceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TenantSetting_TenantId_Id",
                schema: "platform",
                table: "TenantSetting",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TenantSetting_TenantId_Module_Key",
                schema: "platform",
                table: "TenantSetting",
                columns: new[] { "TenantId", "Module", "Key" },
                unique: true,
                filter: "[IsDeleted] = 0");
            // Only TenantSetting is tenant-owned here. Tenant, Plan, Subscription and
            // TenantDatabase are deliberately platform-owned: this database is the registry that
            // resolves a tenant, so filtering it by tenant would require a tenant to read it.
            migrationBuilder.EnableTenantRowLevelSecurity(
                new RowLevelSecurity.TenantTable("platform", "TenantSetting"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema-bound, so the policy goes before the tables it references.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "InboxMessage",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "OutboxMessage",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "Subscription",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantDatabase",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantSetting",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Plan",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Tenant",
                schema: "platform");
        }
    }
}
