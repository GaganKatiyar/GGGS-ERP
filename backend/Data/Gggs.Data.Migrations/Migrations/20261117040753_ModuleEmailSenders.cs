using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ModuleEmailSenders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SenderKey",
                schema: "ntf",
                table: "NotificationTemplate",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMasked",
                schema: "ntf",
                table: "EmailMessage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ModuleKey",
                schema: "ntf",
                table: "EmailMessage",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentAs",
                schema: "ntf",
                table: "EmailMessage",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailSender",
                schema: "ntf",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FromName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReplyTo = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Bcc = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Signature = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BrandColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_EmailSender", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessage_TenantId_Module",
                schema: "ntf",
                table: "EmailMessage",
                columns: new[] { "TenantId", "ModuleKey", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "UX_EmailSender_TenantId_Id",
                schema: "ntf",
                table: "EmailSender",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_EmailSender_TenantId_Module",
                schema: "ntf",
                table: "EmailSender",
                columns: new[] { "TenantId", "ModuleKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // Emails already on record get their module, as EmailModules.KeyFor would give it, for the Email log's filter.
            migrationBuilder.Sql("""
                UPDATE ntf.EmailMessage SET ModuleKey =
                    CASE LEFT(EventCode, CHARINDEX('.', EventCode + '.') - 1)
                        WHEN 'identity' THEN 'identity'
                        WHEN 'onboarding' THEN 'onboarding'
                        WHEN 'recruitment' THEN 'recruitment'
                        WHEN 'performance' THEN 'appraisal'
                        WHEN 'career' THEN 'appraisal'
                        WHEN 'reward' THEN 'reward'
                        WHEN 'engagement' THEN 'engagement'
                        WHEN 'payroll' THEN 'payroll'
                        WHEN 'compensation' THEN 'payroll'
                        WHEN 'loan' THEN 'payroll'
                        WHEN 'expense' THEN 'payroll'
                        WHEN 'benefit' THEN 'payroll'
                        WHEN 'timesheet' THEN 'time'
                        WHEN 'leave' THEN 'time'
                        WHEN 'attendance' THEN 'time'
                        WHEN 'workflow' THEN 'approvals'
                        WHEN 'separation' THEN 'exits'
                        ELSE 'other'
                    END
                WHERE ModuleKey IS NULL;

                -- Their stored copy has the secret masked, so they must never be sent again.
                UPDATE ntf.EmailMessage SET IsMasked = 1
                WHERE EventCode IN ('identity.account-created', 'identity.password-reset-requested');
                """);

            // A new tenant-owned table: the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names this table, so it goes first; it is restored below without it.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "EmailSender",
                schema: "ntf");

            migrationBuilder.SyncTenantRowLevelSecurity(new HireDetailsAndAttendanceModes().TargetModel);

            migrationBuilder.DropIndex(
                name: "IX_EmailMessage_TenantId_Module",
                schema: "ntf",
                table: "EmailMessage");

            migrationBuilder.DropColumn(
                name: "SenderKey",
                schema: "ntf",
                table: "NotificationTemplate");

            migrationBuilder.DropColumn(
                name: "IsMasked",
                schema: "ntf",
                table: "EmailMessage");

            migrationBuilder.DropColumn(
                name: "ModuleKey",
                schema: "ntf",
                table: "EmailMessage");

            migrationBuilder.DropColumn(
                name: "SentAs",
                schema: "ntf",
                table: "EmailMessage");
        }
    }
}
