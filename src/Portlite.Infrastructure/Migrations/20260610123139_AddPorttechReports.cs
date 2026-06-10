using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portlite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPorttechReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PorttechReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubPortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TechnicalDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PorttechReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PorttechReports_SubPortfolios_SubPortfolioId",
                        column: x => x.SubPortfolioId,
                        principalTable: "SubPortfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PorttechReports_SubPortfolioId",
                table: "PorttechReports",
                column: "SubPortfolioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PorttechReports");
        }
    }
}
