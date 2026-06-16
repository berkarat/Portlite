using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portlite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "Assets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Theme",
                table: "Assets");
        }
    }
}
