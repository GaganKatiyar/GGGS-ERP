using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Organisations, plans, subscriptions, platform staff and the console's records moved to the
    /// platform console's own database, NaBPlatform (repository GGGS-ERP-PLATFORM). Copy them first with
    /// GGGS-ERP-PLATFORM/scripts/migrate-from-peopleos.sql: this drops them here. Also adds two nullable
    /// CV columns on rec.Candidate that were in the model at the time (another change, agreed).
    /// </remarks>
    public partial class MovePlatformDataOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DedicatedDatabase",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "PlatformAuditEntry",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "PlatformRole",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "PlatformRoleAssignment",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "PlatformStaff",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "Subscription",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantDatabase",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantUsage",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Plan",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Tenant",
                schema: "platform");

            migrationBuilder.AddColumn<string>(
                name: "CvFileName",
                schema: "rec",
                table: "Candidate",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CvStorageKey",
                schema: "rec",
                table: "Candidate",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CvFileName",
                schema: "rec",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "CvStorageKey",
                schema: "rec",
                table: "Candidate");

            migrationBuilder.CreateTable(
                name: "Plan",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Edition = table.Column<byte>(type: "tinyint", nullable: false),
                    FeatureCodes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPubliclyAvailable = table.Column<bool>(type: "bit", nullable: false),
                    MaxEmployees = table.Column<int>(type: "int", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformAuditEntry",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OccurredDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TenantCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAuditEntry", x => x.Id);
                },
                comment: "Append-only. What platform staff did in the console; written only by PlatformAuditWriter.");

            migrationBuilder.CreateTable(
                name: "PlatformRole",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PermissionCodes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRole", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformRoleAssignment",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PlatformRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformStaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRoleAssignment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformStaff",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    InvitationTokenExpiresDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    InvitationTokenHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LockoutEndDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordChangedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PermissionVersion = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    PasswordHistory = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformStaff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenant",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CountryCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DataRegion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    DefaultLocale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DefaultTimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsolationTier = table.Column<byte>(type: "tinyint", nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PurgeEligibleDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    SuspendedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    SuspensionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DedicatedDatabase",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Authentication = table.Column<byte>(type: "tinyint", nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Encryption = table.Column<byte>(type: "tinyint", nullable: false),
                    LastTestResult = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "bit", nullable: true),
                    LastTestedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PasswordChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PasswordChangedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PasswordCiphertext = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Server = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "Subscription",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RenewsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    SeatLimit = table.Column<int>(type: "int", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrialEndsOn = table.Column<DateOnly>(type: "date", nullable: true),
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
                    ConnectionSecretName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    SchemaVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServiceName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShardName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "TenantUsage",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailChannel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmailsFailedToday = table.Column<int>(type: "int", nullable: false),
                    EmailsLoggedToday = table.Column<int>(type: "int", nullable: false),
                    EmailsSentToday = table.Column<int>(type: "int", nullable: false),
                    Employees = table.Column<int>(type: "int", nullable: false),
                    LastSignIn = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    MeasuredDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SignIn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SignedInToday = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Users = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantUsage_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "platform",
                        principalTable: "Tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_DedicatedDatabase_TenantId",
                schema: "platform",
                table: "DedicatedDatabase",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Plan_Code",
                schema: "platform",
                table: "Plan",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditEntry_OccurredDate",
                schema: "platform",
                table: "PlatformAuditEntry",
                column: "OccurredDate");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditEntry_TenantId_OccurredDate",
                schema: "platform",
                table: "PlatformAuditEntry",
                columns: new[] { "TenantId", "OccurredDate" });

            migrationBuilder.CreateIndex(
                name: "UX_PlatformRole_NormalizedName",
                schema: "iam",
                table: "PlatformRole",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRoleAssignment_PlatformRoleId",
                schema: "iam",
                table: "PlatformRoleAssignment",
                column: "PlatformRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRoleAssignment_PlatformStaffId",
                schema: "iam",
                table: "PlatformRoleAssignment",
                column: "PlatformStaffId");

            migrationBuilder.CreateIndex(
                name: "UX_PlatformRoleAssignment_Assignment",
                schema: "iam",
                table: "PlatformRoleAssignment",
                columns: new[] { "PlatformStaffId", "PlatformRoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlatformStaff_NormalizedEmail",
                schema: "iam",
                table: "PlatformStaff",
                column: "NormalizedEmail",
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
                name: "UX_TenantUsage_TenantId",
                schema: "platform",
                table: "TenantUsage",
                column: "TenantId",
                unique: true);
        }
    }
}
