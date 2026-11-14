using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftPunchOutRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing shifts keep requiring a punch out, so nothing changes until HR turns it off.
            migrationBuilder.AddColumn<bool>(
                name: "RequiresPunchOut",
                schema: "shf",
                table: "Shift",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresPunchOut",
                schema: "shf",
                table: "Shift");
        }
    }
}
