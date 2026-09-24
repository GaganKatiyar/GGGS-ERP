using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InvitationTokenExpiresDate",
                schema: "iam",
                table: "User",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvitationTokenHash",
                schema: "iam",
                table: "User",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvitationTokenExpiresDate",
                schema: "iam",
                table: "User");

            migrationBuilder.DropColumn(
                name: "InvitationTokenHash",
                schema: "iam",
                table: "User");
        }
    }
}
