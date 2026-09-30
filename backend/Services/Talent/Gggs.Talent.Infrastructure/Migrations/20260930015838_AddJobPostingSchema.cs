using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Talent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPostingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobPosting",
                schema: "rec",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DescriptionHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PublishedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ClosesDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
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
                    table.PrimaryKey("PK_JobPosting", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobPosting_TenantId_RequisitionId",
                schema: "rec",
                table: "JobPosting",
                columns: new[] { "TenantId", "RequisitionId" });

            migrationBuilder.CreateIndex(
                name: "UX_JobPosting_TenantId_Id",
                schema: "rec",
                table: "JobPosting",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_JobPosting_TenantId_Slug",
                schema: "rec",
                table: "JobPosting",
                columns: new[] { "TenantId", "Slug" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobPosting",
                schema: "rec");
        }
    }
}
