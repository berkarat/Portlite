using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portlite.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviousCloseToPriceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PreviousClose",
                table: "PriceSnapshots",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousClose",
                table: "PriceSnapshots");
        }
    }
}
