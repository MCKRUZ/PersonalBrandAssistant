using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlatformCredentials_Platform",
                table: "PlatformCredentials");

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "PlatformCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChannelMetricSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    VideoId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VideoTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metrics = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMetricSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformCredentials_Platform_Purpose",
                table: "PlatformCredentials",
                columns: new[] { "Platform", "Purpose" },
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMetricSnapshots_Platform_SnapshotDate",
                table: "ChannelMetricSnapshots",
                columns: new[] { "Platform", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMetricSnapshots_Platform_SnapshotDate_Scope_VideoId",
                table: "ChannelMetricSnapshots",
                columns: new[] { "Platform", "SnapshotDate", "Scope", "VideoId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelMetricSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_PlatformCredentials_Platform_Purpose",
                table: "PlatformCredentials");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "PlatformCredentials");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformCredentials_Platform",
                table: "PlatformCredentials",
                column: "Platform",
                unique: true,
                filter: "\"IsActive\" = true");
        }
    }
}
