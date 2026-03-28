using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrendSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "TrendSources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedUrl",
                table: "TrendSources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "TrendItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrendSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RelevanceFilterEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RelevanceScoreThreshold = table.Column<float>(type: "real", nullable: false),
                    MaxSuggestionsPerCycle = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrendSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrendSettings");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "TrendSources");

            migrationBuilder.DropColumn(
                name: "FeedUrl",
                table: "TrendSources");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "TrendItems");
        }
    }
}
