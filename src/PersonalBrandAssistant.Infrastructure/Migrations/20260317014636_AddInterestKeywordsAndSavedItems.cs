using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInterestKeywordsAndSavedItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterestKeywords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Keyword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    MatchCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestKeywords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedTrendItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrendItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedTrendItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedTrendItems_TrendItems_TrendItemId",
                        column: x => x.TrendItemId,
                        principalTable: "TrendItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterestKeywords_Keyword",
                table: "InterestKeywords",
                column: "Keyword",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedTrendItems_TrendItemId",
                table: "SavedTrendItems",
                column: "TrendItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterestKeywords");

            migrationBuilder.DropTable(
                name: "SavedTrendItems");
        }
    }
}
