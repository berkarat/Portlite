using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portlite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubPortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioAnalyses_SubPortfolios_SubPortfolioId",
                        column: x => x.SubPortfolioId,
                        principalTable: "SubPortfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAnalyses_SubPortfolioId_ContentHash",
                table: "PortfolioAnalyses",
                columns: new[] { "SubPortfolioId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAnalyses_SubPortfolioId_GeneratedAt",
                table: "PortfolioAnalyses",
                columns: new[] { "SubPortfolioId", "GeneratedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioAnalyses");
        }
    }
}
