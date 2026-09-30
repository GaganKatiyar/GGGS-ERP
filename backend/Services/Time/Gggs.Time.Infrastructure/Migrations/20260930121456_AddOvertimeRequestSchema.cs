using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Time.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeRequestSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OvertimeRequest",
                schema: "time",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OvertimeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedMinutes = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ApprovedMinutes = table.Column<int>(type: "int", nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_OvertimeRequest", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRequest_TenantId_EmployeeId_OvertimeDate",
                schema: "time",
                table: "OvertimeRequest",
                columns: new[] { "TenantId", "EmployeeId", "OvertimeDate" });

            migrationBuilder.CreateIndex(
                name: "UX_OvertimeRequest_TenantId_Id",
                schema: "time",
                table: "OvertimeRequest",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OvertimeRequest",
                schema: "time");
        }
    }
}
