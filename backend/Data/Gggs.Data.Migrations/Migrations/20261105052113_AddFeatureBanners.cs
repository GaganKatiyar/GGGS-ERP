using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureBanners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureBanner",
                schema: "eng",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    ActionLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ActionLink = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SecondaryLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SecondaryLink = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Position = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    AudienceDepartmentIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AudienceLocationIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_FeatureBanner", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureBanner_TenantId_Published_StartsOn",
                schema: "eng",
                table: "FeatureBanner",
                columns: new[] { "TenantId", "IsPublished", "StartsOn" });

            migrationBuilder.CreateIndex(
                name: "UX_FeatureBanner_TenantId_Id",
                schema: "eng",
                table: "FeatureBanner",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // A new tenant-owned table: the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names this table, so it goes first; it is restored below without it.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "FeatureBanner",
                schema: "eng");

            migrationBuilder.SyncTenantRowLevelSecurity(new AddPortfolioOutcomesCodeLinks().TargetModel);
        }
    }
}
