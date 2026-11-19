using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundCheckResultsAndRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BgvDefaultChecksJson",
                schema: "rec",
                table: "RecruitmentSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "BgvHireRule",
                schema: "rec",
                table: "RecruitmentSettings",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "BgvStartOnOfferAccepted",
                schema: "rec",
                table: "RecruitmentSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ItemsJson",
                schema: "rec",
                table: "BackgroundCheck",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportsJson",
                schema: "rec",
                table: "BackgroundCheck",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BgvDefaultChecksJson",
                schema: "rec",
                table: "RecruitmentSettings");

            migrationBuilder.DropColumn(
                name: "BgvHireRule",
                schema: "rec",
                table: "RecruitmentSettings");

            migrationBuilder.DropColumn(
                name: "BgvStartOnOfferAccepted",
                schema: "rec",
                table: "RecruitmentSettings");

            migrationBuilder.DropColumn(
                name: "ItemsJson",
                schema: "rec",
                table: "BackgroundCheck");

            migrationBuilder.DropColumn(
                name: "ReportsJson",
                schema: "rec",
                table: "BackgroundCheck");
        }
    }
}
