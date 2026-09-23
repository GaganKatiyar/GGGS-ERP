using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.EnsureSchema(
                name: "iam");

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
                name: "PasswordPolicy",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinimumLength = table.Column<int>(type: "int", nullable: false),
                    MaximumLength = table.Column<int>(type: "int", nullable: false),
                    RequireComplexity = table.Column<bool>(type: "bit", nullable: false),
                    RejectBreachedPasswords = table.Column<bool>(type: "bit", nullable: false),
                    HistoryCount = table.Column<int>(type: "int", nullable: false),
                    MaximumAgeDays = table.Column<int>(type: "int", nullable: false),
                    LockoutThreshold = table.Column<int>(type: "int", nullable: false),
                    LockoutDuration = table.Column<TimeSpan>(type: "time", nullable: false),
                    IdleTimeout = table.Column<TimeSpan>(type: "time", nullable: false),
                    AbsoluteSessionLifetime = table.Column<TimeSpan>(type: "time", nullable: false),
                    MfaRequirement = table.Column<byte>(type: "tinyint", nullable: false),
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
                    table.PrimaryKey("PK_PasswordPolicy", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "Role",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ParentRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Permissions = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "User",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserType = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PasswordChangedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    MfaEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PermissionVersion = table.Column<int>(type: "int", nullable: false),
                    PreferredLocale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    ExternalLogins = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PasswordHistory = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "UserRole",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeType = table.Column<byte>(type: "tinyint", nullable: true),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_UserRole", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
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
                name: "UX_PasswordPolicy_TenantId",
                schema: "iam",
                table: "PasswordPolicy",
                column: "TenantId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_PasswordPolicy_TenantId_Id",
                schema: "iam",
                table: "PasswordPolicy",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_Role_TenantId_Id",
                schema: "iam",
                table: "Role",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_Role_TenantId_NormalizedName",
                schema: "iam",
                table: "Role",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_User_TenantId_EmployeeId",
                schema: "iam",
                table: "User",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "[EmployeeId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_User_TenantId_Id",
                schema: "iam",
                table: "User",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_User_TenantId_NormalizedEmail",
                schema: "iam",
                table: "User",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_TenantId_RoleId",
                schema: "iam",
                table: "UserRole",
                columns: new[] { "TenantId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_TenantId_UserId",
                schema: "iam",
                table: "UserRole",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "UX_UserRole_Assignment",
                schema: "iam",
                table: "UserRole",
                columns: new[] { "TenantId", "UserId", "RoleId", "ScopeType", "ScopeId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_UserRole_TenantId_Id",
                schema: "iam",
                table: "UserRole",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);
            // Every table here is tenant-owned, including User: one person working for two
            // customers has two accounts, and neither tenant may see the other's.
            migrationBuilder.EnableTenantRowLevelSecurity(
                new RowLevelSecurity.TenantTable("iam", "User"),
                new RowLevelSecurity.TenantTable("iam", "Role"),
                new RowLevelSecurity.TenantTable("iam", "UserRole"),
                new RowLevelSecurity.TenantTable("iam", "PasswordPolicy"));
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
                name: "PasswordPolicy",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "Role",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "User",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "UserRole",
                schema: "iam");
        }
    }
}
