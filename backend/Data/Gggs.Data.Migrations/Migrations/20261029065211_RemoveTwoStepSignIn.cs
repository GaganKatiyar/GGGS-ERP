using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTwoStepSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                schema: "iam",
                table: "User");

            migrationBuilder.DropColumn(
                name: "MfaEnrolledDate",
                schema: "iam",
                table: "User");

            migrationBuilder.DropColumn(
                name: "MfaLastStep",
                schema: "iam",
                table: "User");

            migrationBuilder.DropColumn(
                name: "MfaPendingSecret",
                schema: "iam",
                table: "User");

            migrationBuilder.DropColumn(
                name: "MfaRecoveryCodes",
                schema: "iam",
                table: "User");

            migrationBuilder.DropColumn(
                name: "MfaSecret",
                schema: "iam",
                table: "User");

            migrationBuilder.DropColumn(
                name: "MfaRequirement",
                schema: "iam",
                table: "PasswordPolicy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                schema: "iam",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaEnrolledDate",
                schema: "iam",
                table: "User",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MfaLastStep",
                schema: "iam",
                table: "User",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "MfaPendingSecret",
                schema: "iam",
                table: "User",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaRecoveryCodes",
                schema: "iam",
                table: "User",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecret",
                schema: "iam",
                table: "User",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "MfaRequirement",
                schema: "iam",
                table: "PasswordPolicy",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);
        }
    }
}
