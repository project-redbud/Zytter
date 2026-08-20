using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zytter.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPlacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlacementsLeft",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlacementsLeft",
                table: "Accounts");
        }
    }
}
