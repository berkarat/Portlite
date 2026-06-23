using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portlite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionCostOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PositionCostOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubPortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetSymbol = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AverageCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionCostOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionCostOverrides_Assets_AssetSymbol",
                        column: x => x.AssetSymbol,
                        principalTable: "Assets",
                        principalColumn: "Symbol",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PositionCostOverrides_SubPortfolios_SubPortfolioId",
                        column: x => x.SubPortfolioId,
                        principalTable: "SubPortfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PositionCostOverrides_AssetSymbol",
                table: "PositionCostOverrides",
                column: "AssetSymbol");

            migrationBuilder.CreateIndex(
                name: "IX_PositionCostOverrides_SubPortfolioId_AssetSymbol",
                table: "PositionCostOverrides",
                columns: new[] { "SubPortfolioId", "AssetSymbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionCostOverrides");
        }
    }
}
