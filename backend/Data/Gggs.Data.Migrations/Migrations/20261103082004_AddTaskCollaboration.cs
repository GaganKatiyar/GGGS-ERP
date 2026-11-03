using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCollaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BaselineDueDate",
                schema: "prj",
                table: "ProjectTask",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BaselineStartDate",
                schema: "prj",
                table: "ProjectTask",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalKey",
                schema: "prj",
                table: "ProjectTask",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskActivity",
                schema: "prj",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    At = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_TaskActivity", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TaskComment",
                schema: "prj",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
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
                    table.PrimaryKey("PK_TaskComment", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TaskSavedView",
                schema: "prj",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FiltersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_TaskSavedView", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TaskWatcher",
                schema: "prj",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_TaskWatcher", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTask_TenantId_Project_ExternalKey",
                schema: "prj",
                table: "ProjectTask",
                columns: new[] { "TenantId", "ProjectId", "ExternalKey" },
                unique: true,
                filter: "[ExternalKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivity_TenantId_Task_At",
                schema: "prj",
                table: "TaskActivity",
                columns: new[] { "TenantId", "TaskId", "At" });

            migrationBuilder.CreateIndex(
                name: "UX_TaskActivity_TenantId_Id",
                schema: "prj",
                table: "TaskActivity",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskComment_TenantId_Task",
                schema: "prj",
                table: "TaskComment",
                columns: new[] { "TenantId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "UX_TaskComment_TenantId_Id",
                schema: "prj",
                table: "TaskComment",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSavedView_TenantId_Project_Owner",
                schema: "prj",
                table: "TaskSavedView",
                columns: new[] { "TenantId", "ProjectId", "OwnerEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "UX_TaskSavedView_TenantId_Id",
                schema: "prj",
                table: "TaskSavedView",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TaskWatcher_TenantId_Id",
                schema: "prj",
                table: "TaskWatcher",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_TaskWatcher_TenantId_Task_Employee",
                schema: "prj",
                table: "TaskWatcher",
                columns: new[] { "TenantId", "TaskId", "EmployeeId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "TaskActivity",
                schema: "prj");

            migrationBuilder.DropTable(
                name: "TaskComment",
                schema: "prj");

            migrationBuilder.DropTable(
                name: "TaskSavedView",
                schema: "prj");

            migrationBuilder.DropTable(
                name: "TaskWatcher",
                schema: "prj");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTask_TenantId_Project_ExternalKey",
                schema: "prj",
                table: "ProjectTask");

            migrationBuilder.DropColumn(
                name: "BaselineDueDate",
                schema: "prj",
                table: "ProjectTask");

            migrationBuilder.DropColumn(
                name: "BaselineStartDate",
                schema: "prj",
                table: "ProjectTask");

            migrationBuilder.DropColumn(
                name: "ExternalKey",
                schema: "prj",
                table: "ProjectTask");

            migrationBuilder.SyncTenantRowLevelSecurity(new AddServiceLevels().TargetModel);
        }
    }
}
