using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Spend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                schema: "exp",
                table: "TravelRequest",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                schema: "exp",
                table: "ExpenseClaim",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                schema: "exp",
                table: "TravelRequest");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                schema: "exp",
                table: "ExpenseClaim");
        }
    }
}
